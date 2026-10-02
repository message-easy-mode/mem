using System.Diagnostics;
using Api.Logging;
using Microsoft.AspNetCore.Http;

namespace Api.IntegrationTests.Diagnostics;

public sealed class MemCorrelationMiddlewareTests
{
    [Fact]
    public async Task Valid_correlation_is_returned_and_available_to_downstream_code()
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "request-123"
        };
        context.Request.Headers["X-Correlation-ID"] = "migration:abc-123";

        string? downstreamCorrelation = null;
        string? downstreamTraceId = null;
        var middleware = new MemRequestCorrelationMiddleware(
            next: nextContext =>
            {
                downstreamCorrelation = MemRequestLogging.GetCorrelationId(nextContext);
                downstreamTraceId = Activity.Current?.TraceId.ToString();
                return Task.CompletedTask;
            },
            new MemLoggingOptions());

        await middleware.InvokeAsync(context);

        Assert.Equal("migration:abc-123", downstreamCorrelation);
        Assert.Equal(
            "migration:abc-123",
            context.Response.Headers["X-Correlation-ID"].ToString());
        Assert.False(string.IsNullOrWhiteSpace(downstreamTraceId));
    }

    [Fact]
    public async Task Invalid_correlation_is_replaced_and_not_reflected()
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "safe-request-id"
        };
        context.Request.Headers["X-Correlation-ID"] = "unsafe correlation value";

        string? downstreamCorrelation = null;
        var middleware = new MemRequestCorrelationMiddleware(
            next: nextContext =>
            {
                downstreamCorrelation = MemRequestLogging.GetCorrelationId(nextContext);
                return Task.CompletedTask;
            },
            new MemLoggingOptions());

        await middleware.InvokeAsync(context);

        Assert.NotEqual("unsafe correlation value", downstreamCorrelation);
        Assert.True(MemRequestLogging.IsSafeCorrelationId(downstreamCorrelation));
        Assert.Equal(
            downstreamCorrelation,
            context.Response.Headers["X-Correlation-ID"].ToString());
    }
}
