using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Api.Diagnostics;
using Api.Logging;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Diagnostics;
using Shared.Exceptions;

namespace Api.IntegrationTests.Diagnostics;

public sealed class MemGlobalExceptionHandlerTests
{
    [Fact]
    public async Task Unexpected_failure_returns_safe_problem_and_records_correlated_incident()
    {
        var writer = new CapturingDiagnosticWriter();
        var logger = new CapturingLogger<MemGlobalExceptionHandler>();
        var handler = CreateHandler(writer, logger);
        var context = CreateContext("/api/operator/migrations/sessions/abc", "POST");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "operator-internal-42")
        ], "test"));

        using var activity = new Activity("test-request").SetIdFormat(ActivityIdFormat.W3C).Start();
        var exception = new InvalidOperationException(
            "password=super-secret failed at /workspace/private/source.cs");

        var handled = await handler.TryHandleAsync(
            context,
            exception,
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());

        using var body = await ReadBodyAsync(context);
        var root = body.RootElement;
        Assert.Equal(MemProblemCodes.UnexpectedFailure, root.GetProperty("code").GetString());
        Assert.Equal("MEM encountered an unexpected failure", root.GetProperty("title").GetString());
        Assert.DoesNotContain("super-secret", root.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("/home/master", root.GetRawText(), StringComparison.Ordinal);

        var incidentId = root.GetProperty("incidentId").GetString();
        Assert.StartsWith("inc_", incidentId, StringComparison.Ordinal);
        Assert.Equal(activity.TraceId.ToString(), root.GetProperty("traceId").GetString());
        Assert.Equal("corr-test-123", root.GetProperty("correlationId").GetString());

        var request = Assert.Single(writer.Requests);
        Assert.Equal(incidentId, request.IncidentId);
        Assert.Equal("api.request.unexpected_failure", request.EventCode);
        Assert.Equal("migrations", request.Feature);
        Assert.Same(exception, request.Exception);
        Assert.Equal("operator-internal-42", request.Details!["operatorUserId"]);
        Assert.Equal("/api/operator/migrations/sessions/abc", request.Details["requestPath"]);
        Assert.Equal(activity.TraceId.ToString(), request.Context!.TraceId);
        Assert.DoesNotContain(
            logger.Entries,
            entry => ReferenceEquals(entry.Exception, exception));
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Error && entry.Exception is null);
    }

    [Fact]
    public async Task Validation_failure_returns_codes_without_creating_an_incident()
    {
        var writer = new CapturingDiagnosticWriter();
        var handler = CreateHandler(writer);
        var context = CreateContext("/api/operator/stacks", "POST");
        var failure = new ValidationFailure("Password", "The supplied secret-value is invalid")
        {
            ErrorCode = "NotEmptyValidator",
            AttemptedValue = "secret-value"
        };

        await handler.TryHandleAsync(
            context,
            new ValidationException([failure]),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        using var body = await ReadBodyAsync(context);
        var root = body.RootElement;
        Assert.Equal(MemProblemCodes.ValidationFailed, root.GetProperty("code").GetString());
        Assert.False(root.TryGetProperty("incidentId", out _));
        Assert.DoesNotContain("secret-value", root.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(
            "notemptyvalidator",
            root.GetProperty("validationErrors")
                .GetProperty("Password")[0]
                .GetString());
        Assert.Empty(writer.Requests);
    }

    [Fact]
    public async Task Not_found_returns_safe_404_without_incident()
    {
        var writer = new CapturingDiagnosticWriter();
        var handler = CreateHandler(writer);
        var context = CreateContext("/api/operator/backups/missing", "GET");

        await handler.TryHandleAsync(
            context,
            new NotFoundException("Backup", "private-source-id"),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        using var body = await ReadBodyAsync(context);
        var root = body.RootElement;
        Assert.Equal(MemProblemCodes.NotFound, root.GetProperty("code").GetString());
        Assert.DoesNotContain("private-source-id", root.GetRawText(), StringComparison.Ordinal);
        Assert.False(root.TryGetProperty("incidentId", out _));
        Assert.Empty(writer.Requests);
    }

    [Fact]
    public async Task Deliberate_conflict_preserves_only_the_declared_safe_contract()
    {
        var writer = new CapturingDiagnosticWriter();
        var handler = CreateHandler(writer);
        var context = CreateContext("/api/operator/migrations/session", "POST");
        var exception = new MemProblemException(
            StatusCodes.Status409Conflict,
            "migration_already_active",
            "Migration already active",
            "Another migration operation is already active for this session.");

        await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        using var body = await ReadBodyAsync(context);
        var root = body.RootElement;
        Assert.Equal("migration_already_active", root.GetProperty("code").GetString());
        Assert.Equal(
            "Another migration operation is already active for this session.",
            root.GetProperty("detail").GetString());
        Assert.False(root.TryGetProperty("incidentId", out _));
        Assert.Empty(writer.Requests);
    }

    [Fact]
    public async Task Wrapped_dependency_failure_is_classified_without_leaking_inner_detail()
    {
        var writer = new CapturingDiagnosticWriter();
        var handler = CreateHandler(writer);
        var context = CreateContext("/api/operator/federation/health", "GET");
        var exception = new InvalidOperationException(
            "wrapper",
            new HttpRequestException("Authorization: Bearer private-token"));

        await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        using var body = await ReadBodyAsync(context);
        var root = body.RootElement;
        Assert.Equal(
            MemProblemCodes.DependencyRequestFailed,
            root.GetProperty("code").GetString());
        Assert.DoesNotContain("private-token", root.GetRawText(), StringComparison.Ordinal);
        Assert.Single(writer.Requests);
        Assert.Equal("federation", writer.Requests[0].Feature);
        Assert.Equal("api.dependency.request_failed", writer.Requests[0].EventCode);
    }

    [Fact]
    public async Task Server_timeout_creates_an_incident()
    {
        var writer = new CapturingDiagnosticWriter();
        var handler = CreateHandler(writer);
        var context = CreateContext("/api/operator/restores/abc", "POST");

        await handler.TryHandleAsync(
            context,
            new TimeoutException("private dependency timed out"),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status504GatewayTimeout, context.Response.StatusCode);
        using var body = await ReadBodyAsync(context);
        var root = body.RootElement;
        Assert.Equal(MemProblemCodes.RequestTimeout, root.GetProperty("code").GetString());
        Assert.StartsWith("inc_", root.GetProperty("incidentId").GetString(), StringComparison.Ordinal);
        Assert.Single(writer.Requests);
    }

    [Fact]
    public async Task Client_disconnect_does_not_create_an_incident_or_response_body()
    {
        using var disconnected = new CancellationTokenSource();
        disconnected.Cancel();

        var writer = new CapturingDiagnosticWriter();
        var handler = CreateHandler(writer);
        var context = CreateContext("/api/operator/stacks", "GET");
        context.RequestAborted = disconnected.Token;

        var handled = await handler.TryHandleAsync(
            context,
            new OperationCanceledException(disconnected.Token),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(0L, context.Response.Body.Length);
        Assert.Empty(writer.Requests);
    }

    [Fact]
    public async Task Diagnostic_writer_failure_does_not_hide_the_safe_problem()
    {
        var handler = CreateHandler(new ThrowingDiagnosticWriter());
        var context = CreateContext("/api/operator/federation/apply", "POST");

        await handler.TryHandleAsync(
            context,
            new InvalidOperationException("secret technical failure"),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        using var body = await ReadBodyAsync(context);
        var root = body.RootElement;
        Assert.StartsWith("inc_", root.GetProperty("incidentId").GetString(), StringComparison.Ordinal);
        Assert.Equal("degraded", root.GetProperty("diagnosticCapture").GetString());
        Assert.Equal(
            MemDiagnosticCodes.StoreWriteFailed,
            root.GetProperty("diagnosticWarningCode").GetString());
        Assert.DoesNotContain("secret technical failure", root.GetRawText(), StringComparison.Ordinal);
    }

    private static MemGlobalExceptionHandler CreateHandler(
        IMemDiagnosticEventWriter writer,
        ILogger<MemGlobalExceptionHandler>? logger = null) =>
        new(
            new MemExceptionClassifier(),
            new MemProblemDetailsFactory(),
            writer,
            logger ?? NullLogger<MemGlobalExceptionHandler>.Instance);

    private static DefaultHttpContext CreateContext(string path, string method)
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "request-test-123"
        };
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        context.Items[MemRequestLogging.CorrelationItemKey] = "corr-test-123";
        return context;
    }

    private static async Task<JsonDocument> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }

    private sealed class CapturingDiagnosticWriter : IMemDiagnosticEventWriter
    {
        public List<MemDiagnosticWriteRequest> Requests { get; } = [];

        public Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(new MemDiagnosticWriteResult(
                Stored: true,
                EventId: $"evt_{Guid.NewGuid():N}",
                IncidentId: request.IncidentId,
                WarningCode: null));
        }
    }

    private sealed class ThrowingDiagnosticWriter : IMemDiagnosticEventWriter
    {
        public Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("diagnostic writer unavailable");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                exception));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception);
}
