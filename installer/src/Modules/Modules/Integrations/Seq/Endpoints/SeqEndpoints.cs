using Microsoft.Extensions.DependencyInjection;
using Carter;
using Microsoft.AspNetCore.Authorization;
using Modules.Auth.Identity;
using Modules.Integrations.Seq.Contracts;
using Modules.Integrations.Seq.Services;

namespace Modules.Integrations.Seq.Endpoints;

public sealed class SeqEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/operator/services/seq")
            .WithTags("Seq")
            .RequireAuthorization(MemOperatorPolicies.ManagePlatform);

        group.MapPost("/plan", (
            SeqPlanRequest request,
            SeqRuntimeService service,
            HttpContext httpContext) =>
        {
            SetNoStore(httpContext);
            return Execute(() => service.Plan(request));
        });

        group.MapGet("/", async (
            SeqRuntimeService service,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            SetNoStore(httpContext);
            return Results.Ok(await service.GetStatusAsync(ct));
        });

        var options = app.ServiceProvider
            .GetRequiredService<SeqDiagnosticsOptions>();

        if (!options.ManagementEnabled)
        {
            MapDisabledMutationRoutes(group);
            return;
        }

        group.MapPost("/start", async (
            SeqRuntimeService service,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            SetNoStore(httpContext);
            return await ExecuteAsync(() => service.StartAsync(ct));
        });

        group.MapPost("/deploy", async (
            SeqDeployRequest request,
            SeqRuntimeService service,
            HttpContext httpContext,
            IAuthorizationService authorization,
            CancellationToken ct) =>
        {
            SetNoStore(httpContext);
            var stepUp = await RequireRecentStepUpAsync(
                httpContext,
                authorization);
            if (stepUp is not null)
            {
                return stepUp;
            }

            return await ExecuteAsync(() => service.DeployAsync(request, ct));
        });

        group.MapPost("/stop", async (
            SeqRuntimeService service,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            SetNoStore(httpContext);
            return await ExecuteAsync(() => service.StopAsync(ct));
        });

        group.MapPost("/remove", async (
            SeqRuntimeService service,
            HttpContext httpContext,
            IAuthorizationService authorization,
            CancellationToken ct) =>
        {
            SetNoStore(httpContext);
            var stepUp = await RequireRecentStepUpAsync(
                httpContext,
                authorization);
            if (stepUp is not null)
            {
                return stepUp;
            }

            return await ExecuteAsync(() => service.RemoveAsync(ct));
        });
    }

    private static void MapDisabledMutationRoutes(RouteGroupBuilder group)
    {
        group.MapPost("/start", SeqManagementDisabled);
        group.MapPost("/deploy", SeqManagementDisabled);
        group.MapPost("/stop", SeqManagementDisabled);
        group.MapPost("/remove", SeqManagementDisabled);
    }

    private static IResult SeqManagementDisabled(HttpContext httpContext)
    {
        SetNoStore(httpContext);
        return Problem(
            "seq_management_disabled",
            StatusCodes.Status409Conflict,
            "Seq management is disabled.",
            "MEM's local diagnostic recorder remains active. Enable and configure optional Seq management before using this endpoint.");
    }

    private static IResult Execute<T>(Func<T> action)
    {
        try
        {
            return Results.Ok(action());
        }
        catch (SeqOperationException exception)
        {
            return Problem(
                exception.Code,
                exception.StatusCode,
                "Seq operation could not continue.",
                exception.Message);
        }
    }

    private static async Task<IResult> ExecuteAsync(
        Func<Task<Core.Runtime.RuntimeActionResponse>> action)
    {
        try
        {
            var result = await action();
            return result.Success
                ? Results.Ok(result)
                : Problem(
                    "seq_operation_failed",
                    StatusCodes.Status503ServiceUnavailable,
                    "Seq operation failed.",
                    result.Message);
        }
        catch (SeqOperationException exception)
        {
            return Problem(
                exception.Code,
                exception.StatusCode,
                "Seq operation could not continue.",
                exception.Message);
        }
    }

    private static async Task<IResult?> RequireRecentStepUpAsync(
        HttpContext httpContext,
        IAuthorizationService authorization)
    {
        var result = await authorization.AuthorizeAsync(
            httpContext.User,
            resource: null,
            policyName: MemOperatorPolicies.RecentStepUp);

        return result.Succeeded
            ? null
            : Results.Json(
                new { status = "step_up_required" },
                statusCode: StatusCodes.Status403Forbidden);
    }

    private static IResult Problem(
        string code,
        int statusCode,
        string title,
        string detail) =>
        Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code
            });

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
