using Api.Logging;
using Microsoft.AspNetCore.Http;
using Serilog.Events;

namespace Api.IntegrationTests.Diagnostics;

public sealed class MemRequestLoggingTests
{
    [Theory]
    [InlineData(200, LogEventLevel.Information)]
    [InlineData(301, LogEventLevel.Information)]
    [InlineData(400, LogEventLevel.Warning)]
    [InlineData(404, LogEventLevel.Warning)]
    [InlineData(500, LogEventLevel.Error)]
    [InlineData(503, LogEventLevel.Error)]
    public void Request_completion_level_reflects_http_outcome(
        int statusCode,
        LogEventLevel expected)
    {
        var context = new DefaultHttpContext();
        context.Response.StatusCode = statusCode;

        Assert.Equal(
            expected,
            MemRequestLogging.GetLevel(context, 1.5, exception: null));
    }

    [Fact]
    public void Unhandled_request_exception_is_always_an_error()
    {
        var context = new DefaultHttpContext();
        context.Response.StatusCode = StatusCodes.Status200OK;

        Assert.Equal(
            LogEventLevel.Error,
            MemRequestLogging.GetLevel(
                context,
                1.5,
                new InvalidOperationException("test")));
    }

    [Fact]
    public void Health_request_success_is_debug_noise_not_operator_activity()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/health/ready";
        context.Response.StatusCode = StatusCodes.Status200OK;

        Assert.Equal(
            LogEventLevel.Debug,
            MemRequestLogging.GetLevel(context, 1.5, exception: null));
    }

    [Fact]
    public void Safe_caller_correlation_id_is_preserved()
    {
        var context = new DefaultHttpContext();
        var options = new MemLoggingOptions();
        context.Request.Headers[options.CorrelationHeaderName] = "migration:abc-123";

        Assert.Equal(
            "migration:abc-123",
            MemRequestLogging.ResolveCorrelationId(context, options));
    }

    [Theory]
    [InlineData("contains a space")]
    [InlineData("semicolon;value")]
    [InlineData("../filesystem/path")]
    public void Unsafe_correlation_values_are_not_reflected(string supplied)
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "safe-request-id"
        };
        var options = new MemLoggingOptions();
        context.Request.Headers[options.CorrelationHeaderName] = supplied;

        var resolved = MemRequestLogging.ResolveCorrelationId(context, options);

        Assert.Equal("safe-request-id", resolved);
        Assert.NotEqual(supplied, resolved);
    }

    [Fact]
    public void Excessively_long_correlation_value_is_rejected()
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "safe-request-id"
        };
        var options = new MemLoggingOptions();
        context.Request.Headers[options.CorrelationHeaderName] = new string('a', 129);

        Assert.Equal(
            "safe-request-id",
            MemRequestLogging.ResolveCorrelationId(context, options));
    }
}
