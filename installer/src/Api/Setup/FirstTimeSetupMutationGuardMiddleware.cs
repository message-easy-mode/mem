using Modules.Setup.Lifecycle;
using Shared.Exceptions;

namespace Api.Setup;

public sealed class FirstTimeSetupMutationGuardMiddleware(
    RequestDelegate next,
    ILogger<FirstTimeSetupMutationGuardMiddleware> logger)
{
    private const string SetupApiPrefix = "/api/setup";
    public const string ProblemCode = "setup_first_time_locked";

    public async Task InvokeAsync(
        HttpContext context,
        FirstTimeSetupLockService lockService)
    {
        if (!IsFirstTimeSetupMutation(context.Request) ||
            IsSetupHandoffCompletion(context.Request) ||
            IsSetupSupportReportGeneration(context.Request))
        {
            await next(context);
            return;
        }

        var state = await lockService.GetStateAsync(context.RequestAborted);
        if (!state.Locked)
        {
            await next(context);
            return;
        }

        logger.LogWarning(
            "Blocked first-time setup mutation after setup lockout. Method={Method} Path={Path} Reason={ReasonCode}",
            context.Request.Method,
            context.Request.Path.Value,
            state.ReasonCode);

        throw new MemProblemException(
            StatusCodes.Status409Conflict,
            ProblemCode,
            "First-time setup is no longer available",
            "This MEM Control Plane is already established. Use the operator dashboard, Diagnostics, or an explicit maintenance workflow instead of first-time setup.",
            createIncident: false,
            retryable: false,
            suggestedAction: "Open the operator dashboard and use the normal operator or maintenance surface.",
            feature: "setup",
            stage: "first-time-lockout");
    }

    private static bool IsSetupHandoffCompletion(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
        {
            return false;
        }

        if (!request.Path.StartsWithSegments(
                new PathString("/api/setup/install-runs"),
                out var remaining))
        {
            return false;
        }

        var segments = remaining.Value?
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return segments is { Length: 3 } &&
               Guid.TryParse(segments[0], out _) &&
               string.Equals(segments[1], "handoff", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(segments[2], "complete", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSetupSupportReportGeneration(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
        {
            return false;
        }

        if (string.Equals(
                request.Path.Value,
                "/api/setup/support-report",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!request.Path.StartsWithSegments(
                new PathString("/api/setup/installations"),
                out var remaining))
        {
            return false;
        }

        var segments = remaining.Value?
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments is { Length: 2 } &&
               Guid.TryParse(segments[0], out _) &&
               string.Equals(segments[1], "support-report", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFirstTimeSetupMutation(HttpRequest request)
    {
        if (!request.Path.StartsWithSegments(new PathString(SetupApiPrefix), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !HttpMethods.IsGet(request.Method) &&
               !HttpMethods.IsHead(request.Method) &&
               !HttpMethods.IsOptions(request.Method);
    }
}
