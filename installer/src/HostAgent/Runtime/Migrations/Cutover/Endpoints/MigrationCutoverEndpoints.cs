using Carter;
using HostAgent.Commands;
using HostAgent.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Modules.Auth.Identity;

namespace HostAgent.Runtime.Migrations.Cutover.Endpoints;

public sealed class MigrationCutoverEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/operator/migrations/sessions/{migrationId}/cutover")
            .WithTags("Operator / Migrations / Cutover")
            .RequireAuthorization(MemOperatorPolicies.MigrationIntakeOperate);

        group.MapGet("", StateAsync);
        group.MapPost("/candidate", PrepareCandidateAsync);
        group.MapPost("/preview", PreviewAsync);
        group.MapPost("/confirmation", ConfirmAsync);
        group.MapGet("/readiness", ReadinessAsync);
        group.MapPost("/execution", ExecuteAsync);
        group.MapGet("/post-cutover", PostCutoverAsync);
    }

    private static async Task<IResult> StateAsync(
        HttpContext httpContext,
        string migrationId,
        MigrationCutoverFacadeService service,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        return await ExecuteReadAsync(() => service.GetStateAsync(migrationId, ct));
    }

    private static async Task<IResult> PrepareCandidateAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] PrepareMigrationCutoverCandidateRequest? request,
        MigrationCutoverFacadeService service,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        var stepUp = await HostAgentEndpointOperatorGuard.ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
            httpContext,
            authorizationService);
        if (stepUp is not null)
        {
            return stepUp;
        }

        return await ExecuteWriteAsync(() => service.PrepareCandidateAsync(
            migrationId,
            request ?? new PrepareMigrationCutoverCandidateRequest(null),
            ct));
    }

    private static async Task<IResult> PreviewAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] CreateMigrationCutoverPreviewRequest? request,
        MigrationCutoverFacadeService service,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        if (request is null)
        {
            return Results.BadRequest(new HostAgentErrorResponse(
                "migration_cutover_preview_body_required",
                "Provide a JSON request body. Target stack, restore mode, and intended hosts are optional."));
        }

        return await ExecuteWriteAsync(() => service.CreatePreviewAsync(migrationId, request, ct));
    }

    private static async Task<IResult> ConfirmAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] ConfirmMigrationCutoverRequest? request,
        MigrationCutoverFacadeService service,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        if (request is null)
        {
            return Results.BadRequest(new HostAgentErrorResponse(
                "migration_cutover_confirmation_body_required",
                "Provide operator confirmation and all required acknowledgement fields."));
        }

        return await ExecuteWriteAsync(() => service.ConfirmAsync(migrationId, request, ct));
    }

    private static async Task<IResult> ReadinessAsync(
        HttpContext httpContext,
        string migrationId,
        MigrationCutoverFacadeService service,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        return await ExecuteReadAsync(() => service.GetReadinessAsync(migrationId, ct));
    }

    private static async Task<IResult> ExecuteAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] ExecuteMigrationCutoverRequest? request,
        MigrationCutoverFacadeService service,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        var stepUp = await HostAgentEndpointOperatorGuard.ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
            httpContext,
            authorizationService);
        if (stepUp is not null)
        {
            return stepUp;
        }

        if (request is null)
        {
            return Results.BadRequest(new HostAgentErrorResponse(
                "migration_cutover_execution_body_required",
                "Provide execution intent and all required acknowledgement fields."));
        }

        return await ExecuteWriteAsync(() => service.ExecuteAsync(migrationId, request, ct));
    }

    private static async Task<IResult> PostCutoverAsync(
        HttpContext httpContext,
        string migrationId,
        MigrationCutoverFacadeService service,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        return await ExecuteReadAsync(() => service.GetPostCutoverAsync(migrationId, ct));
    }

    private static async Task<IResult> ExecuteReadAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (FileNotFoundException ex)
        {
            return Results.NotFound(new HostAgentErrorResponse("migration_cutover_source_not_found", ex.Message));
        }
        catch (InvalidDataException ex)
        {
            return Results.Json(new HostAgentErrorResponse("migration_cutover_evidence_invalid", ex.Message), statusCode: StatusCodes.Status409Conflict);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(new HostAgentErrorResponse("migration_cutover_not_ready", ex.Message), statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static Task<IResult> ExecuteWriteAsync<T>(Func<Task<T>> action) => ExecuteReadAsync(action);

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
