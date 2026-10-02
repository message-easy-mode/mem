using Carter;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Modules.Auth.Identity;

namespace Modules.Operator.Migrations;

public sealed class MigrationSessionEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/operator/migrations/sessions")
            .WithTags("Migration sessions")
            .RequireAuthorization(MemOperatorPolicies.MigrationIntakeOperate);

        group.MapGet("/inventory", async (
            string? page,
            string? pageSize,
            string? search,
            string? lifecycle,
            string? action,
            string? stage,
            string? targetStack,
            string? sortBy,
            string? sortDirection,
            string? includeArchived,
            HttpContext httpContext,
            MigrationSessionInventoryService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(httpContext);
            var parsed = MigrationSessionInventoryQuery.Parse(
                page,
                pageSize,
                search,
                lifecycle,
                action,
                stage,
                targetStack,
                sortBy,
                sortDirection,
                includeArchived);
            if (!parsed.IsValid)
            {
                return Results.BadRequest(
                    new MigrationSessionInventoryProblemResponse(
                        "migration_session_query_invalid",
                        "One or more Migration Session query values are invalid.",
                        parsed.Errors));
            }

            return Results.Ok(
                await service.ListAsync(
                    parsed.Query!,
                    cancellationToken));
        });

        group.MapGet("/{migrationId}", async (
            string migrationId,
            HttpContext httpContext,
            MigrationSessionProjectionService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(httpContext);
            var session = await service.GetAsync(migrationId, cancellationToken);
            return session is null
                ? Results.NotFound(new MigrationSessionProblemResponse(
                    "migration_session_not_found",
                    "Migration session was not found."))
                : Results.Ok(session);
        });

        group.MapGet("/{migrationId}/lifecycle", async (
            string migrationId,
            HttpContext httpContext,
            MigrationSessionLifecycleService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(httpContext);
            var lifecycle = await service.GetAsync(migrationId, cancellationToken);
            return lifecycle is null
                ? Results.NotFound(new MigrationSessionLifecycleProblemResponse(
                    "migration_session_not_found",
                    "Migration Session was not found."))
                : Results.Ok(lifecycle);
        });

        group.MapPost("/{migrationId}/lifecycle/archive", async (
            string migrationId,
            MigrationSessionLifecycleMutationRequest request,
            HttpContext httpContext,
            MigrationSessionLifecycleService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(httpContext);
            var actor = ResolveOperator(httpContext);
            if (actor is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await service.ArchiveAsync(
                    migrationId,
                    request.ExpectedStateVersion,
                    actor,
                    cancellationToken));
            }
            catch (MigrationSessionLifecycleException exception)
            {
                return LifecycleProblem(exception);
            }
        });

        group.MapPost("/{migrationId}/lifecycle/unarchive", async (
            string migrationId,
            MigrationSessionLifecycleMutationRequest request,
            HttpContext httpContext,
            MigrationSessionLifecycleService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(httpContext);
            try
            {
                return Results.Ok(await service.UnarchiveAsync(
                    migrationId,
                    request.ExpectedStateVersion,
                    cancellationToken));
            }
            catch (MigrationSessionLifecycleException exception)
            {
                return LifecycleProblem(exception);
            }
        });

        group.MapPost("/{migrationId}/lifecycle/cancel", async (
            string migrationId,
            MigrationSessionCancelRequest request,
            HttpContext httpContext,
            IAuthorizationService authorization,
            MigrationSessionLifecycleService service,
            CancellationToken cancellationToken) =>
        {
            var stepUp = await RequireRecentStepUpAsync(httpContext, authorization);
            if (stepUp is not null)
            {
                return stepUp;
            }

            SetNoStore(httpContext);
            var actor = ResolveOperator(httpContext);
            if (actor is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await service.CancelAsync(
                    migrationId,
                    request,
                    actor,
                    cancellationToken));
            }
            catch (MigrationSessionLifecycleException exception)
            {
                return LifecycleProblem(exception);
            }
        });

        group.MapPost("/{migrationId}/lifecycle/delete", async (
            string migrationId,
            MigrationSessionDeleteRequest request,
            HttpContext httpContext,
            IAuthorizationService authorization,
            MigrationSessionLifecycleService service,
            CancellationToken cancellationToken) =>
        {
            var stepUp = await RequireRecentStepUpAsync(httpContext, authorization);
            if (stepUp is not null)
            {
                return stepUp;
            }

            SetNoStore(httpContext);
            var actorOperatorId = ResolveOperatorId(httpContext);
            if (!actorOperatorId.HasValue)
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await service.DeleteAsync(
                    migrationId,
                    request,
                    actorOperatorId.Value,
                    cancellationToken));
            }
            catch (MigrationSessionLifecycleException exception)
            {
                return LifecycleProblem(exception);
            }
        });

        group.MapGet("/{migrationId}/package-revisions/{purpose}/source-request", async (
            string migrationId,
            string purpose,
            HttpContext httpContext,
            MigrationSourceRequestService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var request = await service.CreateAsync(
                    migrationId,
                    purpose,
                    cancellationToken);
                SetNoStore(httpContext);
                return Results.File(
                    request.Contents,
                    contentType: "application/json; charset=utf-8",
                    fileDownloadName: request.FileName);
            }
            catch (MigrationSourceRequestException exception)
            {
                SetNoStore(httpContext);
                return Results.Json(
                    new MigrationSessionProblemResponse(
                        exception.Code,
                        exception.Message),
                    statusCode: exception.StatusCode);
            }
        });

        group.MapPost("/{migrationId}/package-revisions/final-recipient", async (
            string migrationId,
            HttpContext httpContext,
            IAuthorizationService authorization,
            MigrationFinalPackageRecipientService service,
            CancellationToken cancellationToken) =>
        {
            var stepUp = await RequireRecentStepUpAsync(httpContext, authorization);
            if (stepUp is not null)
            {
                return stepUp;
            }

            try
            {
                var recipient = await service.CreateOrResumeAsync(
                    migrationId,
                    cancellationToken);
                SetNoStore(httpContext);
                return Results.Ok(recipient);
            }
            catch (MigrationFinalPackageRecipientException exception)
            {
                SetNoStore(httpContext);
                return Results.Json(
                    new MigrationSessionProblemResponse(
                        exception.Code,
                        exception.Message),
                    statusCode: exception.StatusCode);
            }
        });


        group.MapPost("/{migrationId}/package-revisions/final/package", async (
            string migrationId,
            HttpRequest request,
            HttpContext httpContext,
            IAuthorizationService authorization,
            MigrationFinalPackageUploadService service,
            CancellationToken cancellationToken) =>
        {
            var stepUp = await RequireRecentStepUpAsync(httpContext, authorization);
            if (stepUp is not null)
            {
                return stepUp;
            }

            if (!request.HasFormContentType)
            {
                SetNoStore(httpContext);
                return Results.BadRequest(new MigrationSessionProblemResponse(
                    "final_package_required",
                    "Choose an encrypted final migration package."));
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var package = form.Files.GetFile("package");
            if (package is null)
            {
                SetNoStore(httpContext);
                return Results.BadRequest(new MigrationSessionProblemResponse(
                    "final_package_required",
                    "Choose an encrypted final migration package."));
            }

            try
            {
                var result = await service.UploadAndValidateAsync(
                    migrationId,
                    package,
                    cancellationToken);
                SetNoStore(httpContext);
                return Results.Ok(result);
            }
            catch (MigrationFinalPackageUploadException exception)
            {
                SetNoStore(httpContext);
                return Results.Json(
                    new MigrationSessionProblemResponse(
                        exception.Code,
                        exception.Message),
                    statusCode: exception.StatusCode);
            }
            catch (SecureMigrationIntakeException exception)
            {
                SetNoStore(httpContext);
                return Results.Json(
                    new MigrationSessionProblemResponse(
                        exception.Code,
                        exception.Message),
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        })
        .DisableAntiforgery()
        .WithMetadata(
            new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(
                5L * 1024 * 1024 * 1024),
            new Microsoft.AspNetCore.Mvc.RequestFormLimitsAttribute
            {
                MultipartBodyLengthLimit = 5L * 1024 * 1024 * 1024,
            });
    }

    private static string? ResolveOperator(HttpContext httpContext)
    {
        var actor =
            httpContext.User.Identity?.Name ??
            httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return string.IsNullOrWhiteSpace(actor)
            ? null
            : actor.Trim();
    }

    private static Guid? ResolveOperatorId(HttpContext httpContext)
    {
        var value = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var actorOperatorId)
            ? actorOperatorId
            : null;
    }

    private static IResult LifecycleProblem(
        MigrationSessionLifecycleException exception) =>
        Results.Json(
            new MigrationSessionLifecycleProblemResponse(
                exception.Code,
                exception.Message),
            statusCode: exception.StatusCode);

    private static async Task<IResult?> RequireRecentStepUpAsync(
        HttpContext httpContext,
        IAuthorizationService authorization)
    {
        var result = await authorization.AuthorizeAsync(
            httpContext.User,
            resource: null,
            policyName: MemOperatorPolicies.RecentStepUp);

        if (result.Succeeded)
        {
            return null;
        }

        SetNoStore(httpContext);
        return Results.Json(
            new { status = "step_up_required" },
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
