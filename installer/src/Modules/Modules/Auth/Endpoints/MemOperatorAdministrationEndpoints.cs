using Carter;
using Infrastructure.Data.Entities.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Contracts;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;

namespace Modules.Auth.Endpoints;

/// <summary>
/// Platform Owner-only local operator inventory and lifecycle endpoints. Server
/// policy remains the authority: browser navigation and disabled controls are
/// convenience only and never replace endpoint authorization or lifecycle
/// guards.
/// </summary>
public sealed class MemOperatorAdministrationEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/security/operators")
            .WithTags("MEM Operator Administration")
            .RequireAuthorization(MemOperatorPolicies.ManagePlatform);

        group.MapGet("", async (
            HttpContext httpContext,
            [FromServices] UserManager<MemOperator> userManager,
            [FromServices] IMemOperatorDirectoryService directory,
            CancellationToken ct) =>
        {
            var currentOperator = await GetCurrentOperatorAsync(httpContext, userManager);

            if (currentOperator is null)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await directory.ListAsync(currentOperator.Id, ct));
        });

        group.MapPost("", async (
            [FromBody] CreatePendingMemOperatorRequest request,
            HttpContext httpContext,
            [FromServices] IAuthorizationService authorizationService,
            CancellationToken ct) =>
        {
            var stepUpRequired = await RequireRecentStepUpAsync(httpContext, authorizationService);
            if (stepUpRequired is not null)
            {
                return stepUpRequired;
            }

            // Resolve identity and lifecycle services only after the current
            // browser session has satisfied the RecentStepUp policy. Besides
            // avoiding unnecessary work for denied requests, this prevents a
            // direct policy-refusal route from depending on unrelated services.
            var userManager = httpContext.RequestServices
                .GetRequiredService<UserManager<MemOperator>>();
            var lifecycle = httpContext.RequestServices
                .GetRequiredService<IMemOperatorLifecycleService>();

            var currentOperator = await GetCurrentOperatorAsync(httpContext, userManager);

            if (currentOperator is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var created = await lifecycle.CreatePendingAsync(
                    currentOperator.Id,
                    request,
                    httpContext.TraceIdentifier,
                    ct);

                return Results.Created(
                    $"/api/security/operators/{created.OperatorId:D}",
                    created);
            }
            catch (OperatorAdministrationException exception)
            {
                return ToProblem(exception);
            }
        });

        group.MapPut("/{operatorId:guid}/enabled", async (
            Guid operatorId,
            [FromBody] SetMemOperatorEnabledRequest request,
            HttpContext httpContext,
            [FromServices] IAuthorizationService authorizationService,
            CancellationToken ct) =>
        {
            var stepUpRequired = await RequireRecentStepUpAsync(httpContext, authorizationService);
            if (stepUpRequired is not null)
            {
                return stepUpRequired;
            }

            var userManager = httpContext.RequestServices
                .GetRequiredService<UserManager<MemOperator>>();
            var lifecycle = httpContext.RequestServices
                .GetRequiredService<IMemOperatorLifecycleService>();

            var currentOperator = await GetCurrentOperatorAsync(httpContext, userManager);

            if (currentOperator is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await lifecycle.SetEnabledAsync(
                    currentOperator.Id,
                    operatorId,
                    request,
                    httpContext.TraceIdentifier,
                    ct));
            }
            catch (OperatorAdministrationException exception)
            {
                return ToProblem(exception);
            }
        });

        group.MapPut("/{operatorId:guid}/roles", async (
            Guid operatorId,
            [FromBody] SetMemOperatorRolesRequest request,
            HttpContext httpContext,
            [FromServices] IAuthorizationService authorizationService,
            CancellationToken ct) =>
        {
            var stepUpRequired = await RequireRecentStepUpAsync(httpContext, authorizationService);
            if (stepUpRequired is not null)
            {
                return stepUpRequired;
            }

            var userManager = httpContext.RequestServices
                .GetRequiredService<UserManager<MemOperator>>();
            var lifecycle = httpContext.RequestServices
                .GetRequiredService<IMemOperatorLifecycleService>();

            var currentOperator = await GetCurrentOperatorAsync(httpContext, userManager);

            if (currentOperator is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await lifecycle.SetRolesAsync(
                    currentOperator.Id,
                    operatorId,
                    request,
                    httpContext.TraceIdentifier,
                    ct));
            }
            catch (OperatorAdministrationException exception)
            {
                return ToProblem(exception);
            }
        });

        group.MapPost("/{operatorId:guid}/revoke-sessions", async (
            Guid operatorId,
            HttpContext httpContext,
            [FromServices] IAuthorizationService authorizationService,
            CancellationToken ct) =>
        {
            var stepUpRequired = await RequireRecentStepUpAsync(httpContext, authorizationService);
            if (stepUpRequired is not null)
            {
                return stepUpRequired;
            }

            var userManager = httpContext.RequestServices
                .GetRequiredService<UserManager<MemOperator>>();
            var lifecycle = httpContext.RequestServices
                .GetRequiredService<IMemOperatorLifecycleService>();

            var currentOperator = await GetCurrentOperatorAsync(httpContext, userManager);

            if (currentOperator is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await lifecycle.RevokeSessionsAsync(
                    currentOperator.Id,
                    operatorId,
                    httpContext.TraceIdentifier,
                    ct));
            }
            catch (OperatorAdministrationException exception)
            {
                return ToProblem(exception);
            }
        });


        group.MapPost("/{operatorId:guid}/enrollment-grants", async (
            Guid operatorId,
            HttpContext httpContext,
            [FromServices] IAuthorizationService authorizationService,
            CancellationToken ct) =>
        {
            var stepUpRequired = await RequireRecentStepUpAsync(httpContext, authorizationService);
            if (stepUpRequired is not null)
            {
                return stepUpRequired;
            }

            var userManager = httpContext.RequestServices
                .GetRequiredService<UserManager<MemOperator>>();
            var enrollment = httpContext.RequestServices
                .GetRequiredService<IMemOperatorEnrollmentService>();

            var currentOperator = await GetCurrentOperatorAsync(httpContext, userManager);

            if (currentOperator is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var issued = await enrollment.IssueAsync(
                    currentOperator.Id,
                    operatorId,
                    httpContext.TraceIdentifier,
                    ct);

                // The code is capability-bearing and shown to the Platform Owner
                // exactly once. Prevent HTTP intermediaries/browser caches from
                // retaining this response.
                httpContext.Response.Headers.CacheControl = "no-store";
                httpContext.Response.Headers.Pragma = "no-cache";

                return Results.Ok(issued);
            }
            catch (OperatorEnrollmentException exception)
            {
                return ToProblem(exception);
            }
        });
    }

    /// <summary>
    /// The group policy establishes that the caller is a Platform Owner. Each
    /// mutation below additionally requires a fresh, server-side step-up grant
    /// tied to this exact browser session when the server-owned high-risk
    /// identity-verification policy is Required. A stable, non-secret body lets
    /// the browser invoke the shared verifier and retry only the action it
    /// already confirmed.
    /// </summary>
    private static async Task<IResult?> RequireRecentStepUpAsync(
        HttpContext httpContext,
        IAuthorizationService authorizationService)
    {
        var settings = httpContext.RequestServices.GetService<IMemSecuritySettingsService>();
        if (settings is not null)
        {
            var snapshot = await settings.GetEffectiveAsync(httpContext.RequestAborted);
            if (!snapshot.RequireHighRiskStepUp)
            {
                return null;
            }
        }

        var result = await authorizationService.AuthorizeAsync(
            httpContext.User,
            resource: null,
            policyName: MemOperatorPolicies.RecentStepUp);

        if (result.Succeeded)
        {
            return null;
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";

        return Results.Json(
            new { status = "step_up_required" },
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static Task<MemOperator?> GetCurrentOperatorAsync(
        HttpContext httpContext,
        UserManager<MemOperator> userManager)
    {
        return userManager.GetUserAsync(httpContext.User);
    }

    private static IResult ToProblem(OperatorAdministrationException exception)
    {
        var statusCode = exception.Kind switch
        {
            OperatorAdministrationFailureKind.Validation =>
                StatusCodes.Status422UnprocessableEntity,
            OperatorAdministrationFailureKind.NotFound =>
                StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict
        };

        return Results.Json(
            new
            {
                status = exception.Code
            },
            statusCode: statusCode);
    }

    private static IResult ToProblem(OperatorEnrollmentException exception)
    {
        var statusCode = exception.Kind switch
        {
            OperatorEnrollmentFailureKind.Validation =>
                StatusCodes.Status422UnprocessableEntity,
            OperatorEnrollmentFailureKind.NotFound =>
                StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict
        };

        return Results.Json(
            new
            {
                status = exception.Code
            },
            statusCode: statusCode);
    }
}
