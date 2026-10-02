using System.Diagnostics;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Serilog;
using Serilog.AspNetCore;
using Serilog.Events;
using Shared.Diagnostics;

namespace Api.Logging;

public static partial class MemRequestLogging
{
    public const string CorrelationItemKey = "mem.correlation-id";
    private const int MaximumCorrelationIdLength = 128;

    public static void Configure(RequestLoggingOptions options)
    {
        options.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.GetLevel = GetLevel;
        options.EnrichDiagnosticContext = EnrichDiagnosticContext;
    }

    public static LogEventLevel GetLevel(
        HttpContext context,
        double elapsedMilliseconds,
        Exception? exception)
    {
        _ = elapsedMilliseconds;

        if (exception is not null || context.Response.StatusCode >= 500)
        {
            return LogEventLevel.Error;
        }

        if (context.Response.StatusCode >= 400)
        {
            return LogEventLevel.Warning;
        }

        if (context.Request.Path.StartsWithSegments("/health"))
        {
            return LogEventLevel.Debug;
        }

        return LogEventLevel.Information;
    }

    public static string ResolveCorrelationId(
        HttpContext context,
        MemLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        if (context.Request.Headers.TryGetValue(options.CorrelationHeaderName, out var values) &&
            values.Count == 1)
        {
            var candidate = values[0]?.Trim();
            if (IsSafeCorrelationId(candidate))
            {
                return candidate!;
            }
        }

        var activityTraceId = Activity.Current?.TraceId.ToString();
        if (IsSafeCorrelationId(activityTraceId))
        {
            return activityTraceId!;
        }

        if (IsSafeCorrelationId(context.TraceIdentifier))
        {
            return context.TraceIdentifier;
        }

        return Guid.NewGuid().ToString("N");
    }

    public static bool IsSafeCorrelationId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumCorrelationIdLength &&
        SafeCorrelationIdPattern().IsMatch(value);

    public static string GetCorrelationId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(CorrelationItemKey, out var stored) &&
            IsSafeCorrelationId(stored?.ToString()))
        {
            return stored!.ToString()!;
        }

        var traceId = Activity.Current?.TraceId.ToString();
        if (IsSafeCorrelationId(traceId))
        {
            return traceId!;
        }

        return IsSafeCorrelationId(context.TraceIdentifier)
            ? context.TraceIdentifier
            : Guid.NewGuid().ToString("N");
    }

    public static MemDiagnosticContext CreateDiagnosticContext(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new MemDiagnosticContext(
            TraceId: Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
            SpanId: Activity.Current?.SpanId.ToString(),
            RequestId: context.TraceIdentifier,
            CorrelationId: GetCorrelationId(context));
    }

    private static void EnrichDiagnosticContext(
        IDiagnosticContext diagnosticContext,
        HttpContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        var spanId = Activity.Current?.SpanId.ToString();
        var correlationId = context.Items.TryGetValue(CorrelationItemKey, out var stored)
            ? stored?.ToString()
            : null;
        var operatorUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var routeTemplate = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;

        diagnosticContext.Set("TraceId", traceId);
        diagnosticContext.Set("SpanId", spanId);
        diagnosticContext.Set("RequestId", context.TraceIdentifier);
        diagnosticContext.Set("CorrelationId", correlationId);
        diagnosticContext.Set("OperatorUserId", operatorUserId);
        diagnosticContext.Set("RouteTemplate", routeTemplate);
        diagnosticContext.Set("EndpointName", context.GetEndpoint()?.DisplayName);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeCorrelationIdPattern();
}
