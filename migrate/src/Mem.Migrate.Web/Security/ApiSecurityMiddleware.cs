using System.Text.Json;

namespace Mem.Migrate.Web.Security;

internal sealed class ApiSecurityMiddleware
{
    private static readonly JsonSerializerOptions ProblemJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly PathString[] AnonymousPaths =
    [
        new("/api/health"),
        new("/api/access/login"),
        new("/api/access/session")
    ];

    private readonly RequestDelegate next;

    public ApiSecurityMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    public async Task InvokeAsync(HttpContext context, SourceAssistantSessionStore sessionStore)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
            AnonymousPaths.Any(path => context.Request.Path.Equals(path)))
        {
            await next(context);
            return;
        }

        context.Request.Cookies.TryGetValue(SourceAssistantSessionStore.CookieName, out var cookieToken);
        if (!sessionStore.TryGet(cookieToken, out var session) || session is null)
        {
            if (IsBrowserNavigation(context.Request))
            {
                context.Response.Redirect("/?session=expired");
                return;
            }

            await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, "Authentication required", "Sign in with the access code printed by the Source Assistant process.");
            return;
        }

        context.Items[SourceAssistantSessionStore.ContextItemName] = session;

        if (IsStateChanging(context.Request.Method))
        {
            if (string.Equals(context.Request.Headers["Sec-Fetch-Site"].ToString(), "cross-site", StringComparison.OrdinalIgnoreCase))
            {
                await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "Cross-site request rejected", "State-changing Source Assistant requests must originate from the same site.");
                return;
            }

            if (!context.Request.Headers.TryGetValue("X-MEM-CSRF", out var csrfHeader) ||
                !string.Equals(csrfHeader.ToString(), session.CsrfToken, StringComparison.Ordinal))
            {
                await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "CSRF validation failed", "Refresh the Source Assistant and try again.");
                return;
            }
        }

        await next(context);
    }

    private static bool IsBrowserNavigation(HttpRequest request) =>
        HttpMethods.IsGet(request.Method) &&
        string.Equals(request.Headers["Sec-Fetch-Mode"].ToString(), "navigate", StringComparison.OrdinalIgnoreCase);

    private static bool IsStateChanging(string method) =>
        !HttpMethods.IsGet(method) &&
        !HttpMethods.IsHead(method) &&
        !HttpMethods.IsOptions(method) &&
        !HttpMethods.IsTrace(method);

    private static async Task WriteProblemAsync(HttpContext context, int status, string title, string detail)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            new ApiProblemDetails("about:blank", title, status, detail),
            ProblemJsonOptions,
            cancellationToken: context.RequestAborted);
    }

    private sealed record ApiProblemDetails(string Type, string Title, int Status, string Detail);
}
