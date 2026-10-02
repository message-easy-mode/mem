using System.Security.Claims;
using System.Text.Json;
using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Migrations.Assurance;
using HostAgent.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Modules.Auth.Identity;

namespace HostAgent.Runtime.Migrations.ProductionAdoption.Endpoints;

public sealed class MigrationProductionAdoptionEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/operator/migrations/sessions/{migrationId}/production-adoption")
            .WithTags("Operator / Migrations / Production adoption")
            .RequireAuthorization(MemOperatorPolicies.MigrationIntakeOperate);

        group.MapGet("", StateAsync);
        group.MapPost("/plan", PrepareAsync);
        group.MapPost("/materialize", MaterializeAsync);
        group.MapPost("/private-server/review", ReviewPrivateServerTargetAsync);
        group.MapPost("/private-server", CreatePrivateServerAsync);
        group.MapPost("/go-live", MakeServerLiveAsync);
        group.MapPost("/cutover/preview", PrepareCutoverPreviewAsync);
        group.MapPost("/cutover/execution", ExecuteCutoverAsync);
        group.MapPost("/verification", VerifyProductionAsync);
        group.MapPost("/rollback/preview", PrepareRollbackPreviewAsync);
        group.MapPost("/rollback/execution", ExecuteRollbackAsync);
        group.MapGet("/rollback/source-handoff", DownloadSourceHandoffAsync);
        group.MapPost("/rollback/source-completion", ImportSourceCompletionAsync);
    }

    private static async Task<IResult> StateAsync(
        HttpContext httpContext,
        string migrationId,
        MigrationProductionAdoptionService service,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        return await ExecuteAsync(() => service.GetStateAsync(migrationId, ct));
    }

    private static async Task<IResult> PrepareAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] PrepareMigrationProductionAdoptionRequest? request,
        MigrationProductionAdoptionService service,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        return await ExecuteAsync(() => service.PrepareAsync(
            migrationId,
            request ?? new PrepareMigrationProductionAdoptionRequest(),
            ct));
    }


    private static async Task<IResult> MaterializeAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] MaterializeMigrationProductionRuntimeRequest? request,
        MigrationProductionRuntimeMaterializationService service,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        SetNoStore(httpContext);

        var stepUp = await HostAgentEndpointOperatorGuard
            .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                httpContext,
                authorizationService);
        if (stepUp is not null)
        {
            return stepUp;
        }

        return await ExecuteAsync(() => service.MaterializeAsync(
            migrationId,
            request ?? new MaterializeMigrationProductionRuntimeRequest(
                Operator: null,
                Note: null,
                ExecutePrivateProductionMaterialization: false,
                AcknowledgeCreatesNormalRuntimeRecords: false,
                AcknowledgeMutatesProductionPostgres: false,
                AcknowledgeStartsProductionContainers: false,
                AcknowledgeNoPublicRoutes: false,
                AcknowledgeNoAutomaticRollback: false),
            ct));
    }

    private static async Task<IResult> ReviewPrivateServerTargetAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] ReviewMigrationPrivateServerTargetRequest? request,
        MigrationPrivateServerTargetReviewService service,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        return await ExecuteAsync(() => service.ReviewAsync(
            migrationId,
            request ?? new ReviewMigrationPrivateServerTargetRequest(),
            ct));
    }

    private static async Task<IResult> CreatePrivateServerAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] CreateMigrationPrivateServerRequest? request,
        MigrationPrivateServerCreationService service,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        SetNoStore(httpContext);

        var stepUp = await HostAgentEndpointOperatorGuard
            .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                httpContext,
                authorizationService);
        if (stepUp is not null)
        {
            return stepUp;
        }

        if (request is null)
        {
            return Results.BadRequest(new HostAgentErrorResponse(
                "migration_private_server_body_required",
                "Provide the target identity and all required private-server confirmations."));
        }

        var operatorIdText = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(operatorIdText, out var operatorId))
        {
            return Results.Json(
                new HostAgentErrorResponse(
                    "named_operator_required",
                    "A named Platform Owner session is required to create the private server."),
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await ExecuteAsync(() => service.CreateAsync(
            migrationId,
            operatorId,
            request,
            ct));
    }

    private static async Task<IResult> MakeServerLiveAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] MakeMigrationServerLiveRequest? request,
        MigrationGoLiveService service,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        SetNoStore(httpContext);

        var stepUp = await HostAgentEndpointOperatorGuard
            .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                httpContext,
                authorizationService);
        if (stepUp is not null)
        {
            return stepUp;
        }

        if (request is null)
        {
            return Results.BadRequest(new HostAgentErrorResponse(
                "migration_go_live_body_required",
                "Confirm the public traffic change, old-server boundary, and live verification."));
        }

        return await ExecuteAsync(() => service.MakeLiveAsync(
            migrationId,
            request,
            ct));
    }

    private static async Task<IResult> PrepareCutoverPreviewAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] PrepareMigrationProductionCutoverRequest? request,
        MigrationProductionCutoverService service,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        return await ExecuteAsync(() => service.PreparePreviewAsync(
            migrationId,
            request ?? new PrepareMigrationProductionCutoverRequest(),
            ct));
    }

    private static async Task<IResult> ExecuteCutoverAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] ExecuteMigrationProductionCutoverRequest? request,
        MigrationProductionCutoverService service,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        SetNoStore(httpContext);

        var stepUp = await HostAgentEndpointOperatorGuard
            .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                httpContext,
                authorizationService);
        if (stepUp is not null)
        {
            return stepUp;
        }

        if (request is null)
        {
            return Results.BadRequest(new HostAgentErrorResponse(
                "migration_production_cutover_body_required",
                "Provide the current preview id, execution intent, and all required acknowledgements."));
        }

        return await ExecuteAsync(() => service.ExecuteAsync(migrationId, request, ct));
    }

    private static async Task<IResult> VerifyProductionAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] RunMigrationProductionVerificationRequest? request,
        MigrationProductionVerificationService service,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        return await ExecuteAsync(() => service.VerifyAsync(
            migrationId,
            request ?? new RunMigrationProductionVerificationRequest(),
            ct));
    }

    private static async Task<IResult> PrepareRollbackPreviewAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] PrepareMigrationProductionRollbackRequest? request,
        MigrationProductionRollbackService service,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        return await ExecuteAsync(() => service.PreparePreviewAsync(
            migrationId,
            request ?? new PrepareMigrationProductionRollbackRequest(),
            ct));
    }

    private static async Task<IResult> ExecuteRollbackAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] ExecuteMigrationProductionRollbackRequest? request,
        MigrationProductionRollbackService service,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        SetNoStore(httpContext);

        var stepUp = await HostAgentEndpointOperatorGuard
            .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                httpContext,
                authorizationService);
        if (stepUp is not null)
        {
            return stepUp;
        }

        if (request is null)
        {
            return Results.BadRequest(new HostAgentErrorResponse(
                "migration_production_rollback_body_required",
                "Provide the current rollback preview id, execution intent, and all required acknowledgements."));
        }

        return await ExecuteAsync(() => service.ExecuteAsync(migrationId, request, ct));
    }

    private static async Task<IResult> ImportSourceCompletionAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] MigrationProductionSourceRestorationCompletionEnvelope? envelope,
        MigrationProductionRollbackCompletionService service,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        SetNoStore(httpContext);

        var stepUp = await HostAgentEndpointOperatorGuard
            .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                httpContext,
                authorizationService);
        if (stepUp is not null)
        {
            return stepUp;
        }

        if (envelope is null)
        {
            return Results.BadRequest(new HostAgentErrorResponse(
                "migration_production_source_completion_body_required",
                "Provide the source-restoration completion envelope generated by mem-migrate."));
        }

        return await ExecuteAsync(() => service.ImportAsync(migrationId, envelope, ct));
    }

    private static async Task<IResult> DownloadSourceHandoffAsync(
        HttpContext httpContext,
        string migrationId,
        MigrationProductionRollbackService service,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        SetNoStore(httpContext);

        var stepUp = await HostAgentEndpointOperatorGuard
            .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                httpContext,
                authorizationService);
        if (stepUp is not null)
        {
            return stepUp;
        }

        try
        {
            var handoff = await service.GetSourceHandoffAsync(migrationId, ct);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                handoff,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            return Results.File(
                bytes,
                "application/json",
                $"{handoff.Payload.HandoffId}.mem-source-restoration-handoff.json");
        }
        catch (FileNotFoundException ex)
        {
            return Results.NotFound(new HostAgentErrorResponse(
                "migration_production_adoption_source_not_found",
                ex.Message));
        }
        catch (InvalidDataException ex)
        {
            return Results.Json(
                new HostAgentErrorResponse("migration_production_adoption_evidence_invalid", ex.Message),
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(
                new HostAgentErrorResponse("migration_production_adoption_not_ready", ex.Message),
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (FileNotFoundException ex)
        {
            return Results.NotFound(new HostAgentErrorResponse(
                "migration_production_adoption_source_not_found",
                ex.Message));
        }
        catch (InvalidDataException ex)
        {
            return Results.Json(
                new HostAgentErrorResponse("migration_production_adoption_evidence_invalid", ex.Message),
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (MigrationProductionAuthorityException ex)
        {
            var statusCode = ex.Kind == MigrationProductionAuthorityFailureKind.InvalidRequest
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status409Conflict;
            return Results.Json(
                new HostAgentErrorResponse(ex.Code, ex.Message),
                statusCode: statusCode);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(
                new HostAgentErrorResponse("migration_production_adoption_not_ready", ex.Message),
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
