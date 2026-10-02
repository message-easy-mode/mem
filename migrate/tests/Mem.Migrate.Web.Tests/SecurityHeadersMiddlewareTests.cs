using Mem.Migrate.Web.Security;
using Microsoft.AspNetCore.Http;

namespace Mem.Migrate.Web.Tests;

public sealed class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task Adds_restrictive_browser_headers()
    {
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.Contains("default-src 'self'", context.Response.Headers["Content-Security-Policy"].ToString(), StringComparison.Ordinal);
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
        Assert.Equal("no-store", context.Response.Headers["Cache-Control"].ToString());
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"].ToString());
    }
}
