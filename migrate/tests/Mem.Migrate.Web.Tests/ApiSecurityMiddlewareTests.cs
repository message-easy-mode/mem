using Mem.Migrate.Web.Security;
using Microsoft.AspNetCore.Http;

namespace Mem.Migrate.Web.Tests;

public sealed class ApiSecurityMiddlewareTests
{
    [Fact]
    public async Task Protected_api_rejects_anonymous_request()
    {
        var invoked = false;
        var middleware = new ApiSecurityMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });
        var context = NewContext("GET", "/api/preflight");
        var store = new SourceAssistantSessionStore(TimeProvider.System, TimeSpan.FromMinutes(30), TimeSpan.FromHours(4));

        await middleware.InvokeAsync(context, store);

        Assert.False(invoked);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }


    [Fact]
    public async Task Expired_browser_navigation_redirects_to_the_sign_in_surface()
    {
        var middleware = new ApiSecurityMiddleware(_ => Task.CompletedTask);
        var context = NewContext("GET", "/api/workflows/source-01/package/download");
        context.Request.Headers["Sec-Fetch-Mode"] = "navigate";
        var store = new SourceAssistantSessionStore(
            TimeProvider.System,
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(4));

        await middleware.InvokeAsync(context, store);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/?session=expired", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task State_change_requires_matching_csrf_header()
    {
        var invoked = false;
        var middleware = new ApiSecurityMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });
        var store = new SourceAssistantSessionStore(TimeProvider.System, TimeSpan.FromMinutes(30), TimeSpan.FromHours(4));
        var session = store.Create();
        var context = NewContext("POST", "/api/access/logout");
        context.Request.Headers["Cookie"] = $"{SourceAssistantSessionStore.CookieName}={session.CookieToken}";

        await middleware.InvokeAsync(context, store);

        Assert.False(invoked);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        context = NewContext("POST", "/api/access/logout");
        context.Request.Headers["Cookie"] = $"{SourceAssistantSessionStore.CookieName}={session.CookieToken}";
        context.Request.Headers["X-MEM-CSRF"] = session.CsrfToken;

        await middleware.InvokeAsync(context, store);

        Assert.True(invoked);
    }

    private static DefaultHttpContext NewContext(string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }
}
