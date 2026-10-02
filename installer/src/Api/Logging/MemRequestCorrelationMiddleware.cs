using System.Diagnostics;
using Serilog.Context;

namespace Api.Logging;

public sealed class MemRequestCorrelationMiddleware(
    RequestDelegate next,
    MemLoggingOptions options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        Activity? fallbackActivity = null;
        if (Activity.Current is null)
        {
            fallbackActivity = new Activity("MEM HTTP request");
            fallbackActivity.SetIdFormat(ActivityIdFormat.W3C);
            fallbackActivity.Start();
        }

        try
        {
            var correlationId = MemRequestLogging.ResolveCorrelationId(context, options);
            var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
            var spanId = Activity.Current?.SpanId.ToString();

            context.Items[MemRequestLogging.CorrelationItemKey] = correlationId;
            context.Response.Headers[options.CorrelationHeaderName] = correlationId;

            using (LogContext.PushProperty("CorrelationId", correlationId))
            using (LogContext.PushProperty("TraceId", traceId))
            using (LogContext.PushProperty("SpanId", spanId))
            using (LogContext.PushProperty("RequestId", context.TraceIdentifier))
            {
                await next(context);
            }
        }
        finally
        {
            fallbackActivity?.Stop();
        }
    }
}
