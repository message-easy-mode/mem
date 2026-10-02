using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Diagnostics;

public sealed class MemDiagnosticEventFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly MemDiagnosticsOptions _options;
    private readonly MemDiagnosticRedactor _redactor;
    private readonly MemDiagnosticExceptionProjector _exceptionProjector;
    private readonly TimeProvider _timeProvider;

    public MemDiagnosticEventFactory(
        MemDiagnosticsOptions options,
        MemDiagnosticRedactor redactor,
        MemDiagnosticExceptionProjector exceptionProjector,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _exceptionProjector = exceptionProjector ?? throw new ArgumentNullException(nameof(exceptionProjector));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    internal MemDiagnosticEvent Create(MemDiagnosticWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var severity = MemDiagnosticRedactor.NormalizeIdentifier(request.Severity, 32)
            .ToLowerInvariant();
        if (!MemDiagnosticSeverities.IsKnown(severity))
        {
            throw new InvalidOperationException(
                "Unknown diagnostic severity.");
        }

        var eventCode = RequiredIdentifier(
            request.EventCode,
            200,
            "Diagnostic event code is required.");
        var source = RequiredIdentifier(
            request.Source,
            100,
            "Diagnostic source is required.");
        var feature = RequiredIdentifier(
            request.Feature,
            100,
            "Diagnostic feature is required.");
        var stage = OptionalIdentifier(request.Stage, 120);
        var incidentId = ResolveIncidentId(request);
        var context = ResolveContext(request.Context);

        var message = _redactor.RedactText(
            request.Message,
            _options.MaximumDetailCharacters,
            request.ExactSecrets);
        var suggestedAction = _redactor.RedactText(
            request.SuggestedAction,
            _options.MaximumDetailCharacters,
            request.ExactSecrets);
        var resource = _redactor.RedactResource(request.Resource, request.ExactSecrets);
        var expected = _redactor.RedactDictionary(request.Expected, request.ExactSecrets);
        var observed = _redactor.RedactDictionary(request.Observed, request.ExactSecrets);
        var details = _redactor.RedactDictionary(request.Details, request.ExactSecrets);
        var projectedException = _exceptionProjector.Project(
            request.Exception,
            request.ExactSecrets);

        var @event = new MemDiagnosticEvent(
            SchemaVersion: 1,
            EventId: $"evt_{Guid.NewGuid():N}",
            TimestampUtc: _timeProvider.GetUtcNow(),
            Severity: severity,
            EventCode: eventCode,
            Source: source,
            Feature: feature,
            Stage: stage,
            Message: message.Value,
            IncidentId: incidentId,
            TraceId: context.TraceId,
            SpanId: context.SpanId,
            RequestId: context.RequestId,
            CorrelationId: context.CorrelationId,
            OperationId: request.OperationId,
            Resource: resource.Value,
            Expected: expected.Value,
            Observed: observed.Value,
            Details: details.Value,
            Exception: projectedException.Exception,
            SuggestedAction: string.IsNullOrWhiteSpace(suggestedAction.Value)
                ? null
                : suggestedAction.Value,
            Retryable: request.Retryable,
            RedactionsApplied: true,
            Truncated: message.Truncated || suggestedAction.Truncated ||
                       resource.Truncated || expected.Truncated || observed.Truncated ||
                       details.Truncated || projectedException.Truncated);

        return EnsureWithinMaximumSize(@event);
    }

    internal MemDiagnosticEvent CreateFallback(
        MemDiagnosticWriteRequest request,
        Exception creationFailure)
    {
        var eventCode = MemDiagnosticRedactor.NormalizeIdentifier(
            request.EventCode,
            200);
        if (string.IsNullOrWhiteSpace(eventCode))
        {
            eventCode = MemDiagnosticCodes.EventMalformed;
        }

        var incidentId = ResolveIncidentId(request);
        var context = ResolveContext(request.Context);
        var failureType = MemDiagnosticRedactor.NormalizeIdentifier(
            creationFailure.GetType().FullName ?? creationFailure.GetType().Name,
            300);

        return new MemDiagnosticEvent(
            SchemaVersion: 1,
            EventId: $"evt_{Guid.NewGuid():N}",
            TimestampUtc: _timeProvider.GetUtcNow(),
            Severity: MemDiagnosticSeverities.Error,
            EventCode: eventCode,
            Source: "control-plane",
            Feature: "diagnostics",
            Stage: null,
            Message: "MEM could not create the full diagnostic event. A minimal safe event was recorded.",
            IncidentId: incidentId,
            TraceId: context.TraceId,
            SpanId: context.SpanId,
            RequestId: context.RequestId,
            CorrelationId: context.CorrelationId,
            OperationId: request.OperationId,
            Resource: null,
            Expected: null,
            Observed: null,
            Details: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["failureType"] = failureType
            },
            Exception: null,
            SuggestedAction: null,
            Retryable: false,
            RedactionsApplied: true,
            Truncated: true);
    }

    private MemDiagnosticEvent EnsureWithinMaximumSize(MemDiagnosticEvent source)
    {
        if (SerializedSize(source) <= _options.MaximumEventBytes)
        {
            return source;
        }

        var reduced = source with
        {
            Message = Trim(source.Message, 1000),
            SuggestedAction = TrimOptional(source.SuggestedAction, 500),
            Details = null,
            Exception = ReduceException(source.Exception),
            Truncated = true
        };
        if (SerializedSize(reduced) <= _options.MaximumEventBytes)
        {
            return reduced;
        }

        reduced = reduced with
        {
            Expected = ReduceDictionary(reduced.Expected),
            Observed = ReduceDictionary(reduced.Observed),
            Exception = reduced.Exception is null
                ? null
                : reduced.Exception with
                {
                    StackTrace = null,
                    InnerExceptions = Array.Empty<MemDiagnosticException>()
                }
        };
        if (SerializedSize(reduced) <= _options.MaximumEventBytes)
        {
            return reduced;
        }

        var minimal = reduced with
        {
            Resource = reduced.Resource is null
                ? null
                : reduced.Resource with
                {
                    DisplayName = null,
                    StackSlug = null,
                    WorkspacePath = null
                },
            Expected = null,
            Observed = null,
            Exception = null,
            Message = Trim(reduced.Message, 500),
            SuggestedAction = null,
            Truncated = true
        };
        if (SerializedSize(minimal) <= _options.MaximumEventBytes)
        {
            return minimal;
        }

        return minimal with
        {
            Resource = null,
            Message = "The diagnostic event exceeded the safe storage boundary and was reduced.",
            Details = null,
            Truncated = true
        };
    }

    private static MemDiagnosticException? ReduceException(
        MemDiagnosticException? exception)
    {
        if (exception is null)
        {
            return null;
        }

        return exception with
        {
            Message = Trim(exception.Message, 750),
            StackTrace = TrimOptional(exception.StackTrace, 3000),
            InnerExceptions = exception.InnerExceptions
                .Take(2)
                .Select(inner => inner with
                {
                    Message = Trim(inner.Message, 500),
                    StackTrace = null,
                    InnerExceptions = Array.Empty<MemDiagnosticException>()
                })
                .ToArray()
        };
    }

    private static IReadOnlyDictionary<string, string>? ReduceDictionary(
        IReadOnlyDictionary<string, string>? source)
    {
        if (source is null)
        {
            return null;
        }

        return source
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(8)
            .ToDictionary(
                pair => pair.Key,
                pair => Trim(pair.Value, 300),
                StringComparer.Ordinal);
    }

    private static int SerializedSize(MemDiagnosticEvent @event) =>
        JsonSerializer.SerializeToUtf8Bytes(@event, JsonOptions).Length + 1;

    private static MemDiagnosticContext ResolveContext(MemDiagnosticContext? supplied)
    {
        var activity = Activity.Current;
        return new MemDiagnosticContext(
            TraceId: OptionalIdentifier(
                supplied?.TraceId ?? activity?.TraceId.ToString(),
                80),
            SpanId: OptionalIdentifier(
                supplied?.SpanId ?? activity?.SpanId.ToString(),
                40),
            RequestId: OptionalIdentifier(supplied?.RequestId, 160),
            CorrelationId: OptionalIdentifier(supplied?.CorrelationId, 128));
    }

    private static string? ResolveIncidentId(MemDiagnosticWriteRequest request)
    {
        var supplied = OptionalIdentifier(request.IncidentId, 80);
        if (!string.IsNullOrWhiteSpace(supplied))
        {
            return supplied;
        }

        return request.CreateIncident ? $"inc_{Guid.NewGuid():N}" : null;
    }

    private static string RequiredIdentifier(
        string? value,
        int maximumCharacters,
        string error)
    {
        var normalized = MemDiagnosticRedactor.NormalizeIdentifier(
            value,
            maximumCharacters);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException(error);
        }

        return normalized;
    }

    private static string? OptionalIdentifier(string? value, int maximumCharacters)
    {
        var normalized = MemDiagnosticRedactor.NormalizeIdentifier(
            value,
            maximumCharacters);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string Trim(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters ? value : value[..maximumCharacters];

    private static string? TrimOptional(string? value, int maximumCharacters) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Trim(value, maximumCharacters);
}
