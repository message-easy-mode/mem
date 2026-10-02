using System.Diagnostics;
using System.Security.Claims;
using Api.Logging;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Routing;
using Shared.Diagnostics;
using Shared.Exceptions;

namespace Api.Diagnostics;

public sealed class MemGlobalExceptionHandler(
    MemExceptionClassifier classifier,
    MemProblemDetailsFactory problemFactory,
    IMemDiagnosticEventWriter diagnosticWriter,
    ILogger<MemGlobalExceptionHandler> logger)
    : IExceptionHandler
{
    private const int ClientClosedRequestStatusCode = 499;
    private static readonly TimeSpan DiagnosticWriteTimeout = TimeSpan.FromSeconds(2);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException &&
            context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = ClientClosedRequestStatusCode;
            logger.LogInformation(
                "HTTP request ended because the client disconnected. {ProblemCode}",
                MemProblemCodes.ClientClosedRequest);
            return true;
        }

        var classification = classifier.Classify(context, exception);
        var incidentId = classification.CreateIncident
            ? $"inc_{Guid.NewGuid():N}"
            : null;
        string? diagnosticWarningCode = null;

        if (classification.CreateIncident)
        {
            diagnosticWarningCode = await TryWriteDiagnosticEventAsync(
                context,
                exception,
                classification,
                incidentId!);
        }

        LogBoundaryFailure(context, exception, classification, incidentId, diagnosticWarningCode);

        if (context.Response.HasStarted)
        {
            logger.LogWarning(
                "MEM could not write Problem Details because the HTTP response had already started. IncidentId={IncidentId}",
                incidentId);
            return false;
        }

        context.Response.Clear();
        context.Response.StatusCode = classification.StatusCode;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";

        var problem = problemFactory.Create(
            context,
            classification,
            incidentId,
            diagnosticWarningCode);

        await context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken.IsCancellationRequested
                ? CancellationToken.None
                : cancellationToken);

        return true;
    }

    private async Task<string?> TryWriteDiagnosticEventAsync(
        HttpContext context,
        Exception exception,
        MemExceptionClassification classification,
        string incidentId)
    {
        var details = BuildDiagnosticDetails(context, classification);
        using var timeout = new CancellationTokenSource(DiagnosticWriteTimeout);

        try
        {
            var result = await diagnosticWriter.WriteAsync(
                new MemDiagnosticWriteRequest(
                    Severity: classification.DiagnosticSeverity,
                    EventCode: classification.DiagnosticEventCode,
                    Source: "control-plane-api",
                    Feature: classification.Feature,
                    Message: classification.Detail,
                    Stage: classification.Stage ?? "request",
                    IncidentId: incidentId,
                    CreateIncident: false,
                    OperationId: classification.OperationId,
                    Resource: classification.Resource,
                    Details: details,
                    Exception: exception,
                    SuggestedAction: classification.SuggestedAction,
                    Retryable: classification.Retryable,
                    Context: MemRequestLogging.CreateDiagnosticContext(context)),
                timeout.Token);

            return result.WarningCode;
        }
        catch (OperationCanceledException)
        {
            logger.LogError(
                "MEM timed out while recording diagnostic incident {IncidentId}",
                incidentId);
            return MemDiagnosticCodes.StoreWriteFailed;
        }
        catch (Exception writerException) when (
            writerException is not StackOverflowException and not OutOfMemoryException)
        {
            logger.LogError(
                writerException,
                "MEM could not record diagnostic incident {IncidentId}",
                incidentId);
            return MemDiagnosticCodes.StoreWriteFailed;
        }
    }

    private void LogBoundaryFailure(
        HttpContext context,
        Exception exception,
        MemExceptionClassification classification,
        string? incidentId,
        string? diagnosticWarningCode)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["ProblemCode"] = classification.Code,
            ["IncidentId"] = incidentId,
            ["TraceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
            ["RequestId"] = context.TraceIdentifier,
            ["CorrelationId"] = MemRequestLogging.GetCorrelationId(context),
            ["OperationId"] = classification.OperationId,
            ["DiagnosticWarningCode"] = diagnosticWarningCode
        });

        if (classification.StatusCode >= StatusCodes.Status500InternalServerError ||
            classification.CreateIncident)
        {
            // ExceptionHandlerMiddleware owns the single raw exception log. This
            // event adds MEM incident context without duplicating the full stack.
            logger.LogError(
                "MEM request failed at the global request boundary. StatusCode={StatusCode} Feature={Feature} ExceptionType={ExceptionType}",
                classification.StatusCode,
                classification.Feature,
                exception.GetType().FullName);
            return;
        }

        if (classification.StatusCode == StatusCodes.Status404NotFound)
        {
            logger.LogInformation(
                "MEM request returned a safe not-found problem. ProblemCode={ProblemCode}",
                classification.Code);
            return;
        }

        logger.LogWarning(
            "MEM request returned a safe client problem. StatusCode={StatusCode} ProblemCode={ProblemCode}",
            classification.StatusCode,
            classification.Code);
    }

    private static IReadOnlyDictionary<string, string?> BuildDiagnosticDetails(
        HttpContext context,
        MemExceptionClassification classification)
    {
        var details = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["httpMethod"] = context.Request.Method,
            ["requestPath"] = context.Request.Path.Value,
            ["routeTemplate"] = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText,
            ["statusCode"] = classification.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["problemCode"] = classification.Code,
            ["operatorUserId"] = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        };

        if (classification.DiagnosticDetails is not null)
        {
            foreach (var pair in classification.DiagnosticDetails)
            {
                details[pair.Key] = pair.Value;
            }
        }

        return details;
    }
}
