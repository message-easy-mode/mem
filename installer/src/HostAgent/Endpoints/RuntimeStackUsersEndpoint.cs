using Carter;
using HostAgent.Matrix.Users;
using HostAgent.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace HostAgent.Endpoints;

public sealed class RuntimeStackUsersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/internal/host-agent/runtime-stacks/{slugOrId}/users",
                async (
                    string slugOrId,
                    HttpContext httpContext,
                    [FromServices] RuntimeStackUserService userService,
                    CancellationToken ct) =>
                {
                    var auth = Authorize(httpContext);

                    if (!auth.Authorized)
                    {
                        return Results.Json(
                            new
                            {
                                source = "control-plane",
                                status = "unauthorized",
                                slug = slugOrId,
                                users = Array.Empty<object>(),
                                detail = auth.Detail
                            },
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    var result = await userService.ListAsync(slugOrId, ct);

                    return result is null
                        ? Results.NotFound(new
                        {
                            source = "control-plane",
                            status = "not_found",
                            slug = slugOrId,
                            users = Array.Empty<object>(),
                            detail = $"Runtime stack '{slugOrId}' was not found."
                        })
                        : Results.Ok(result);
                })
            .WithName("ListHostAgentRuntimeStackUsers")
            .WithTags("HostAgent");

        app.MapPost(
                "/internal/host-agent/runtime-stacks/{slugOrId}/users/synchronize",
                async (
                    string slugOrId,
                    HttpContext httpContext,
                    [FromServices] RuntimeStackUserService userService,
                    CancellationToken ct) =>
                {
                    var auth = Authorize(httpContext);

                    if (!auth.Authorized)
                    {
                        return Results.Json(
                            new { status = "unauthorized", detail = auth.Detail },
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    try
                    {
                        var result = await userService.SynchronizeAsync(slugOrId, ct);

                        return result is null
                            ? Results.NotFound(new
                            {
                                source = "control-plane",
                                status = "not_found",
                                slug = slugOrId,
                                users = Array.Empty<object>(),
                                detail = $"Runtime stack '{slugOrId}' was not found."
                            })
                            : Results.Ok(result);
                    }
                    catch (RuntimeStackUserInventoryException ex)
                    {
                        return InventoryUnavailable(ex);
                    }
                })
            .WithName("SynchronizeHostAgentRuntimeStackUsers")
            .WithTags("HostAgent");

        app.MapPost(
                "/internal/host-agent/runtime-stacks/{slugOrId}/users/first-admin",
                async (
                    string slugOrId,
                    CreateRuntimeStackUserRequest body,
                    HttpContext httpContext,
                    [FromServices] RuntimeStackUserService userService,
                    CancellationToken ct) =>
                {
                    var auth = Authorize(httpContext);

                    if (!auth.Authorized)
                    {
                        return Results.Json(
                            new { status = "unauthorized", detail = auth.Detail },
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    try
                    {
                        var result = await userService.CreateFirstAdminAsync(slugOrId, body, ct);
                        return Results.Ok(result);
                    }
                    catch (RuntimeStackUserConflictException ex)
                    {
                        return Conflict(ex);
                    }
                    catch (RuntimeStackUserInventoryException ex)
                    {
                        return InventoryUnavailable(ex);
                    }
                })
            .WithName("CreateHostAgentRuntimeStackFirstAdmin")
            .WithTags("HostAgent");

        app.MapPost(
                "/internal/host-agent/runtime-stacks/{slugOrId}/users",
                async (
                    string slugOrId,
                    CreateRuntimeStackUserRequest body,
                    HttpContext httpContext,
                    [FromServices] RuntimeStackUserService userService,
                    CancellationToken ct) =>
                {
                    var auth = Authorize(httpContext);

                    if (!auth.Authorized)
                    {
                        return Results.Json(
                            new { status = "unauthorized", detail = auth.Detail },
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    try
                    {
                        var result = await userService.CreateUserAsync(slugOrId, body, ct);
                        return Results.Ok(result);
                    }
                    catch (RuntimeStackUserConflictException ex)
                    {
                        return Conflict(ex);
                    }
                    catch (RuntimeStackUserInventoryException ex)
                    {
                        return InventoryUnavailable(ex);
                    }
                })
            .WithName("CreateHostAgentRuntimeStackUser")
            .WithTags("HostAgent");


        app.MapPost(
                "/internal/host-agent/runtime-stacks/{slugOrId}/users/admin-authority",
                async (
                    string slugOrId,
                    [FromBody] ImportMatrixAdminAuthorityRequest body,
                    HttpContext httpContext,
                    CancellationToken ct) =>
                {
                    httpContext.Response.Headers.CacheControl = "no-store";
                    var authorizationService = httpContext.RequestServices
                        .GetRequiredService<IAuthorizationService>();
                    var denied = await HostAgentEndpointOperatorGuard
                        .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                            httpContext,
                            authorizationService);

                    if (denied is not null)
                    {
                        return denied;
                    }

                    var userService = httpContext.RequestServices
                        .GetRequiredService<RuntimeStackUserService>();

                    try
                    {
                        return Results.Ok(await userService.ImportAdminAuthorityAsync(
                            slugOrId,
                            body,
                            ct));
                    }
                    catch (MatrixAdminAuthorityException ex)
                    {
                        return AuthorityFailure(ex);
                    }
                })
            .WithName("SetHostAgentRuntimeStackMatrixAdminAuthority")
            .WithTags("HostAgent");

        app.MapPost(
                "/internal/host-agent/runtime-stacks/{slugOrId}/users/{userId:guid}/password",
                async (
                    string slugOrId,
                    Guid userId,
                    [FromBody] ResetRuntimeStackUserPasswordRequest body,
                    HttpContext httpContext,
                    CancellationToken ct) =>
                {
                    httpContext.Response.Headers.CacheControl = "no-store";
                    var authorizationService = httpContext.RequestServices
                        .GetRequiredService<IAuthorizationService>();
                    var denied = await HostAgentEndpointOperatorGuard
                        .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                            httpContext,
                            authorizationService);

                    if (denied is not null)
                    {
                        return denied;
                    }

                    var userService = httpContext.RequestServices
                        .GetRequiredService<RuntimeStackUserService>();

                    try
                    {
                        return Results.Ok(await userService.ResetPasswordAsync(
                            slugOrId,
                            userId,
                            body,
                            ct));
                    }
                    catch (MatrixAdminAuthorityException ex)
                    {
                        return AuthorityFailure(ex);
                    }
                })
            .WithName("ResetHostAgentRuntimeStackMatrixUserPassword")
            .WithTags("HostAgent");

        app.MapPost(
                "/internal/host-agent/runtime-stacks/{slugOrId}/users/{userId:guid}/deactivate",
                async (
                    string slugOrId,
                    Guid userId,
                    [FromBody] DeactivateRuntimeStackUserRequest body,
                    HttpContext httpContext,
                    CancellationToken ct) =>
                {
                    httpContext.Response.Headers.CacheControl = "no-store";
                    var authorizationService = httpContext.RequestServices
                        .GetRequiredService<IAuthorizationService>();
                    var denied = await HostAgentEndpointOperatorGuard
                        .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                            httpContext,
                            authorizationService);

                    if (denied is not null)
                    {
                        return denied;
                    }

                    var userService = httpContext.RequestServices
                        .GetRequiredService<RuntimeStackUserService>();

                    try
                    {
                        return Results.Ok(await userService.DeactivateUserAsync(
                            slugOrId,
                            userId,
                            body,
                            ct));
                    }
                    catch (MatrixAdminAuthorityException ex)
                    {
                        return AuthorityFailure(ex);
                    }
                    catch (RuntimeStackUserInventoryException ex)
                    {
                        return InventoryUnavailable(ex);
                    }
                })
            .WithName("DeactivateHostAgentRuntimeStackMatrixUser")
            .WithTags("HostAgent");

        app.MapPost(
                "/internal/host-agent/runtime-stacks/{slugOrId}/users/{userId:guid}/reactivate",
                async (
                    string slugOrId,
                    Guid userId,
                    [FromBody] ReactivateRuntimeStackUserRequest body,
                    HttpContext httpContext,
                    CancellationToken ct) =>
                {
                    httpContext.Response.Headers.CacheControl = "no-store";
                    var authorizationService = httpContext.RequestServices
                        .GetRequiredService<IAuthorizationService>();
                    var denied = await HostAgentEndpointOperatorGuard
                        .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                            httpContext,
                            authorizationService);

                    if (denied is not null)
                    {
                        return denied;
                    }

                    var userService = httpContext.RequestServices
                        .GetRequiredService<RuntimeStackUserService>();

                    try
                    {
                        return Results.Ok(await userService.ReactivateUserAsync(
                            slugOrId,
                            userId,
                            body,
                            ct));
                    }
                    catch (MatrixAdminAuthorityException ex)
                    {
                        return AuthorityFailure(ex);
                    }
                    catch (RuntimeStackUserInventoryException ex)
                    {
                        return InventoryUnavailable(ex);
                    }
                })
            .WithName("ReactivateHostAgentRuntimeStackMatrixUser")
            .WithTags("HostAgent");
    }


    private static IResult AuthorityFailure(MatrixAdminAuthorityException ex)
    {
        var statusCode = ex.FailureKind switch
        {
            MatrixAdminAuthorityFailureKind.Required => StatusCodes.Status409Conflict,
            MatrixAdminAuthorityFailureKind.InvalidRequest => StatusCodes.Status400BadRequest,
            MatrixAdminAuthorityFailureKind.Rejected => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status503ServiceUnavailable
        };

        return Results.Json(
            new
            {
                source = "control-plane",
                status = "matrix_admin_authority_error",
                code = ex.Code,
                detail = ex.SafeDetail
            },
            statusCode: statusCode);
    }

    private static IResult Conflict(RuntimeStackUserConflictException ex) =>
        Results.Json(
            new
            {
                source = "control-plane",
                status = "conflict",
                code = ex.Code,
                detail = ex.SafeDetail
            },
            statusCode: StatusCodes.Status409Conflict);

    private static IResult InventoryUnavailable(RuntimeStackUserInventoryException ex) =>
        Results.Json(
            new
            {
                source = "control-plane",
                status = "inventory_unavailable",
                code = ex.Code,
                detail = ex.SafeDetail
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);

    private static RuntimeStackUsersAuthResult Authorize(HttpContext httpContext)
    {
        return HostAgentEndpointOperatorGuard.HasCurrentControlPlaneSession(httpContext.User)
            ? new RuntimeStackUsersAuthResult(Authorized: true, Detail: null)
            : new RuntimeStackUsersAuthResult(
                Authorized: false,
                Detail: "A valid installer unlock session is required.");
    }
}

public sealed record RuntimeStackUsersAuthResult(
    bool Authorized,
    string? Detail);
