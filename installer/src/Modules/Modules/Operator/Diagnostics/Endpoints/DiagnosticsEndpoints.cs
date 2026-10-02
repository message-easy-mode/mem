using System.Security.Cryptography;
using System.Text.Json;
using System.Security.Claims;
using Carter;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Contracts;
using Modules.Operator.Diagnostics.Services;
using Shared.Exceptions;

namespace Modules.Operator.Diagnostics.Endpoints;

public sealed class DiagnosticsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/operator/diagnostics")
            .WithTags("Operator Diagnostics");

        group.MapGet("/overview", async (
            HttpContext context,
            DiagnosticsOverviewService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            return Results.Ok(await service.GetAsync(
                context.User,
                cancellationToken));
        })
        .WithName("GetDiagnosticsOverview")
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus)
        .Produces<DiagnosticsOverviewResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/attention", async (
            HttpContext context,
            DiagnosticsAttentionService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            var limit = ParseAttentionLimit(context.Request.Query["limit"].ToString());
            return Results.Ok(await service.GetAsync(limit, cancellationToken));
        })
        .WithName("GetDiagnosticsAttention")
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus)
        .Produces<DiagnosticsAttentionResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/incidents", async (
            HttpContext context,
            DiagnosticsQueryParser parser,
            DiagnosticsIncidentService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            var query = parser.Parse(context.Request.Query);
            var lifecycle = parser.ParseLifecycleFilter(context.Request.Query);
            return Results.Ok(await service.QueryAsync(
                query,
                lifecycle,
                context.User,
                cancellationToken));
        })
        .WithName("ListDiagnosticIncidents")
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus)
        .Produces<DiagnosticsIncidentPageResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/incidents/{incidentId}", async (
            string incidentId,
            HttpContext context,
            DiagnosticsIncidentService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            return Results.Ok(await service.GetAsync(
                incidentId,
                context.User,
                cancellationToken));
        })
        .WithName("GetDiagnosticIncident")
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus)
        .Produces<DiagnosticsIncidentDetailResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/incidents/{incidentId}/acknowledge", async (
            string incidentId,
            HttpContext context,
            DiagnosticsIncidentActionService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            var operatorId = RequireDurableOperatorId(context.User);
            return Results.Ok(await service.AcknowledgeAsync(
                incidentId,
                context.User,
                operatorId,
                context.Request.Headers["X-Correlation-ID"].ToString(),
                cancellationToken));
        })
        .WithName("AcknowledgeDiagnosticIncident")
        .RequireAuthorization(MemOperatorPolicies.Operate)
        .Produces<DiagnosticsIncidentDetailResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/incidents/{incidentId}/snooze", async (
            string incidentId,
            DiagnosticsIncidentSnoozeRequest request,
            HttpContext context,
            DiagnosticsIncidentActionService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            var operatorId = RequireDurableOperatorId(context.User);
            return Results.Ok(await service.SnoozeAsync(
                incidentId,
                request,
                context.User,
                operatorId,
                context.Request.Headers["X-Correlation-ID"].ToString(),
                cancellationToken));
        })
        .WithName("SnoozeDiagnosticIncident")
        .RequireAuthorization(MemOperatorPolicies.Operate)
        .Accepts<DiagnosticsIncidentSnoozeRequest>("application/json")
        .Produces<DiagnosticsIncidentDetailResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/incidents/{incidentId}/resolve", async (
            string incidentId,
            DiagnosticsIncidentResolveRequest request,
            HttpContext context,
            DiagnosticsIncidentActionService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            var operatorId = RequireDurableOperatorId(context.User);
            return Results.Ok(await service.ResolveAsync(
                incidentId,
                request,
                context.User,
                operatorId,
                context.Request.Headers["X-Correlation-ID"].ToString(),
                cancellationToken));
        })
        .WithName("ResolveDiagnosticIncident")
        .RequireAuthorization(MemOperatorPolicies.Operate)
        .Accepts<DiagnosticsIncidentResolveRequest>("application/json")
        .Produces<DiagnosticsIncidentDetailResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/incidents/{incidentId}/reopen", async (
            string incidentId,
            HttpContext context,
            DiagnosticsIncidentActionService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            var operatorId = RequireDurableOperatorId(context.User);
            return Results.Ok(await service.ReopenAsync(
                incidentId,
                context.User,
                operatorId,
                context.Request.Headers["X-Correlation-ID"].ToString(),
                cancellationToken));
        })
        .WithName("ReopenDiagnosticIncident")
        .RequireAuthorization(MemOperatorPolicies.Operate)
        .Produces<DiagnosticsIncidentDetailResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/events", async (
            HttpContext context,
            DiagnosticsQueryParser parser,
            DiagnosticsEventService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            var query = parser.Parse(context.Request.Query);
            return Results.Ok(await service.QueryAsync(
                query,
                context.User,
                cancellationToken));
        })
        .WithName("ListDiagnosticEvents")
        .RequireAuthorization(MemOperatorPolicies.Operate)
        .Produces<DiagnosticsEventPageResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/events/{eventId}", async (
            string eventId,
            HttpContext context,
            DiagnosticsEventService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            return Results.Ok(await service.GetAsync(
                eventId,
                context.User,
                cancellationToken));
        })
        .WithName("GetDiagnosticEvent")
        .RequireAuthorization(MemOperatorPolicies.Operate)
        .Produces<DiagnosticsEventProjection>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/logging-health", (
            HttpContext context,
            DiagnosticsLoggingHealthService service) =>
        {
            SetNoStore(context);
            return Results.Ok(service.Get(context.User));
        })
        .WithName("GetDiagnosticsLoggingHealth")
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus)
        .Produces<DiagnosticsLoggingHealthResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/seq", async (
            HttpContext context,
            DiagnosticsSeqService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            return Results.Ok(await service.GetOverviewAsync(
                context.User,
                cancellationToken));
        })
        .WithName("GetDiagnosticsSeqOverview")
        .RequireAuthorization(MemOperatorPolicies.Operate)
        .Produces<DiagnosticsSeqOverviewResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/seq/ui-authority", async (
            DiagnosticsSeqUiAuthorityUpdateRequest request,
            HttpContext context,
            DiagnosticsSeqUiAuthorityService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            return Results.Ok(await service.UpdateAsync(
                request,
                context.User,
                cancellationToken));
        })
        .WithName("UpdateDiagnosticsSeqUiAuthority")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Accepts<DiagnosticsSeqUiAuthorityUpdateRequest>("application/json")
        .Produces<DiagnosticsSeqUiAuthorityUpdateResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/seq/bootstrap", async (
            HttpContext context,
            DiagnosticsSeqBootstrapService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            return Results.Ok(await service.GetOverviewAsync(
                context.User,
                cancellationToken));
        })
        .WithName("GetDiagnosticsSeqBootstrapOverview")
        .RequireAuthorization(MemOperatorPolicies.Operate)
        .Produces<DiagnosticsSeqBootstrapOverviewResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/seq/bootstrap/review", async (
            DiagnosticsSeqBootstrapReviewRequest request,
            HttpContext context,
            DiagnosticsSeqBootstrapService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            return Results.Ok(await service.ReviewAsync(
                request,
                cancellationToken));
        })
        .WithName("ReviewDiagnosticsSeqBootstrap")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Produces<DiagnosticsSeqBootstrapReviewResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/seq/bootstrap/execute", async (
            HttpContext context,
            DiagnosticsSeqBootstrapExecutionService service,
            IAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            var stepUp = await RequireRecentStepUpAsync(context, authorization);
            if (stepUp is not null)
            {
                return stepUp;
            }

            try
            {
                var request = await ReadSeqBootstrapExecuteRequestAsync(
                    context.Request,
                    maximumRequestBytes: 2048,
                    cancellationToken);
                var accepted = await service.QueueAsync(
                    request,
                    context.User,
                    cancellationToken);
                return Results.Accepted(
                    $"/api/operator/diagnostics/seq/bootstrap/operations/{accepted.OperationId:D}",
                    accepted);
            }
            catch (SeqOperationException exception)
            {
                return SeqProblem(
                    exception.Code,
                    exception.StatusCode,
                    "Seq setup could not continue.",
                    exception.Message);
            }
        })
        .WithName("ExecuteDiagnosticsSeqBootstrap")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Accepts<DiagnosticsSeqBootstrapExecuteRequest>("application/json")
        .Produces<DiagnosticsSeqBootstrapExecuteResponse>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/seq/bootstrap/operations/{operationId:guid}", async (
            Guid operationId,
            HttpContext context,
            DiagnosticsSeqBootstrapExecutionService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            try
            {
                return Results.Ok(await service.GetOperationAsync(
                    operationId,
                    cancellationToken));
            }
            catch (SeqOperationException exception)
            {
                return SeqProblem(
                    exception.Code,
                    exception.StatusCode,
                    "Seq setup operation is unavailable.",
                    exception.Message);
            }
        })
        .WithName("GetDiagnosticsSeqBootstrapOperation")
        .RequireAuthorization(MemOperatorPolicies.Operate)
        .Produces<DiagnosticsSeqBootstrapOperationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/seq/connect", async (
            HttpContext context,
            DiagnosticsSeqConnectionService service,
            IAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            var stepUp = await RequireRecentStepUpAsync(context, authorization);
            if (stepUp is not null)
            {
                return stepUp;
            }

            try
            {
                var request = await ReadSeqConnectionRequestAsync(
                    context.Request,
                    maximumRequestBytes: 1024,
                    cancellationToken);
                return Results.Ok(await service.ConnectAsync(
                    request,
                    context.User,
                    cancellationToken));
            }
            catch (SeqOperationException exception)
            {
                return SeqProblem(
                    exception.Code,
                    exception.StatusCode,
                    "MEM could not connect to Seq.",
                    exception.Message);
            }
        })
        .WithName("ConnectDiagnosticsSeq")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Accepts<DiagnosticsSeqConnectionRequest>("application/json")
        .Produces<DiagnosticsSeqConnectionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        group.MapGet("/portainer", async (
            HttpContext context,
            DiagnosticsPortainerService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            return Results.Ok(await service.GetOverviewAsync(cancellationToken));
        })
        .WithName("GetDiagnosticsPortainerOverview")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Produces<DiagnosticsPortainerOverviewResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/portainer/incidents/{incidentId}/container", async (
            string incidentId,
            HttpContext context,
            DiagnosticsPortainerService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            var handoff = await service.HandoffIncidentAsync(
                incidentId,
                cancellationToken);
            context.Response.Headers["X-MEM-Portainer-Handoff"] = handoff.Kind;
            return Results.Redirect(handoff.Location);
        })
        .WithName("OpenDiagnosticIncidentContainerInPortainer")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Produces(StatusCodes.Status302Found)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/portainer/resources/{resourceKind}/{resourceId}", async (
            string resourceKind,
            string resourceId,
            HttpContext context,
            DiagnosticsPortainerService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            var handoff = await service.HandoffResourceAsync(
                resourceKind,
                resourceId,
                context.Request.Query["service"].ToString(),
                cancellationToken);
            context.Response.Headers["X-MEM-Portainer-Handoff"] = handoff.Kind;
            return Results.Redirect(handoff.Location);
        })
        .WithName("OpenDiagnosticResourceInPortainer")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Produces(StatusCodes.Status302Found)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/portainer/seq/container", async (
            HttpContext context,
            DiagnosticsPortainerService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            var handoff = await service.HandoffSeqAsync(cancellationToken);
            context.Response.Headers["X-MEM-Portainer-Handoff"] = handoff.Kind;
            return Results.Redirect(handoff.Location);
        })
        .WithName("OpenDiagnosticsSeqContainerInPortainer")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Produces(StatusCodes.Status302Found)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/seq/setup/review", async (
            HttpContext context,
            DiagnosticsSeqService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            return Results.Ok(await service.ReviewSetupAsync(cancellationToken));
        })
        .WithName("ReviewDiagnosticsSeqSetup")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Produces<DiagnosticsSeqSetupReviewResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/seq/runtime/deploy", async (
            HttpContext context,
            DiagnosticsSeqLifecycleService service,
            IAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            var stepUp = await RequireRecentStepUpAsync(context, authorization);
            return stepUp ?? await ExecuteSeqOperationAsync(() =>
                service.DeployAsync(context.User, cancellationToken));
        })
        .WithName("DeployDiagnosticsSeqRuntime")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Produces<DiagnosticsSeqOperationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/seq/runtime/start", async (
            HttpContext context,
            DiagnosticsSeqLifecycleService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            return await ExecuteSeqOperationAsync(() =>
                service.StartAsync(context.User, cancellationToken));
        })
        .WithName("StartDiagnosticsSeqRuntime")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Produces<DiagnosticsSeqOperationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/seq/runtime/stop", async (
            HttpContext context,
            DiagnosticsSeqLifecycleService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            return await ExecuteSeqOperationAsync(() =>
                service.StopAsync(context.User, cancellationToken));
        })
        .WithName("StopDiagnosticsSeqRuntime")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Produces<DiagnosticsSeqOperationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/seq/runtime/restart", async (
            HttpContext context,
            DiagnosticsSeqLifecycleService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            return await ExecuteSeqOperationAsync(() =>
                service.RestartAsync(context.User, cancellationToken));
        })
        .WithName("RestartDiagnosticsSeqRuntime")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Produces<DiagnosticsSeqOperationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/seq/runtime/remove", async (
            HttpContext context,
            DiagnosticsSeqLifecycleService service,
            IAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            var stepUp = await RequireRecentStepUpAsync(context, authorization);
            return stepUp ?? await ExecuteSeqOperationAsync(() =>
                service.RemoveAsync(context.User, cancellationToken));
        })
        .WithName("RemoveDiagnosticsSeqRuntime")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Produces<DiagnosticsSeqOperationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/seq/delivery", async (
            DiagnosticsSeqDeliveryChangeRequest request,
            HttpContext context,
            DiagnosticsSeqLifecycleService service,
            IAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            var stepUp = await RequireRecentStepUpAsync(context, authorization);
            return stepUp ?? await ExecuteSeqOperationAsync(() =>
                service.ChangeDeliveryAsync(
                    context.User,
                    request.Enabled,
                    cancellationToken));
        })
        .WithName("ChangeDiagnosticsSeqDelivery")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Produces<DiagnosticsSeqOperationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/seq/delivery/verify", async (
            HttpContext context,
            DiagnosticsSeqDeliveryVerificationService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            try
            {
                return Results.Ok(await service.VerifyAsync(
                    context.User,
                    cancellationToken));
            }
            catch (SeqOperationException exception)
            {
                return SeqProblem(
                    exception.Code,
                    exception.StatusCode,
                    "Active Seq delivery could not be verified.",
                    exception.Message);
            }
        })
        .WithName("VerifyDiagnosticsSeqDelivery")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Produces<DiagnosticsSeqDeliveryVerificationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/seq/health-check", async (
            HttpContext context,
            DiagnosticsSeqLifecycleService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            return await ExecuteSeqOperationAsync(() =>
                service.CheckHealthAsync(context.User, cancellationToken));
        })
        .WithName("CheckDiagnosticsSeqHealth")
        .RequireAuthorization(MemOperatorPolicies.Operate)
        .Produces<DiagnosticsSeqOperationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/self-test", async (
            HttpContext context,
            DiagnosticsPipelineSelfTestService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            return Results.Ok(await service.RunAsync(cancellationToken));
        })
        .WithName("RunDiagnosticsPipelineSelfTest")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Produces<DiagnosticsPipelineSelfTestResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/incidents/{incidentId}/docker-evidence", async (
            string incidentId,
            HttpContext context,
            DiagnosticsDockerEvidenceService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            return Results.Ok(await service.GetAsync(
                incidentId,
                cancellationToken));
        })
        .WithName("GetDiagnosticIncidentDockerEvidence")
        .RequireAuthorization(MemOperatorPolicies.Operate)
        .Produces<DiagnosticsDockerEvidenceResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/incidents/{incidentId}/docker-evidence/refresh", async (
            string incidentId,
            HttpContext context,
            DiagnosticsDockerEvidenceService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            return Results.Ok(await service.GetAsync(
                incidentId,
                cancellationToken));
        })
        .WithName("RefreshDiagnosticIncidentDockerEvidence")
        .RequireAuthorization(MemOperatorPolicies.Operate)
        .Produces<DiagnosticsDockerEvidenceResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/support-report", async (
            HttpContext context,
            DiagnosticsApiOptions options,
            DiagnosticsSupportReportService service,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            var request = await ReadSupportReportRequestAsync(
                context.Request,
                options.SupportReportMaximumRequestBytes,
                cancellationToken);

            var operatorId = Guid.TryParse(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var parsedOperatorId)
                ? parsedOperatorId
                : (Guid?)null;
            var correlationId = context.Request.Headers["X-Correlation-ID"].ToString();
            var report = await service.GenerateAsync(
                request,
                context.User,
                operatorId,
                correlationId,
                cancellationToken);
            var timestamp = report.GeneratedAtUtc.ToUniversalTime()
                .ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
            context.Response.Headers["Content-Disposition"] =
                $"attachment; filename=\"mem-diagnostics-{report.Incident.IncidentId}-{timestamp}.json\"";

            return Results.Json(
                report,
                contentType: "application/json",
                statusCode: StatusCodes.Status200OK);
        })
        .WithName("GenerateDiagnosticsSupportReport")
        .RequireAuthorization(MemOperatorPolicies.Operate)
        .Accepts<DiagnosticsSupportReportRequest>("application/json")
        .Produces<DiagnosticsSupportReport>(StatusCodes.Status200OK, "application/json")
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status413PayloadTooLarge)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);
    }


    private static Guid RequireDurableOperatorId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(value, out var operatorId) && operatorId != Guid.Empty)
        {
            return operatorId;
        }

        throw new MemProblemException(
            StatusCodes.Status403Forbidden,
            "diagnostics_operator_identity_unavailable",
            "A durable operator identity is required",
            "The current operator session does not contain a durable operator identity for this evidence-management action.",
            feature: "diagnostics");
    }

    private static int ParseAttentionLimit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DiagnosticsAttentionService.MaximumItems;
        }

        if (!int.TryParse(
                value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed is < 1 or > DiagnosticsAttentionService.MaximumItems)
        {
            throw new MemProblemException(
                StatusCodes.Status400BadRequest,
                "diagnostics_attention_limit_invalid",
                "The diagnostics attention limit is invalid",
                $"limit must be between 1 and {DiagnosticsAttentionService.MaximumItems}.",
                feature: "diagnostics");
        }

        return parsed;
    }

    private static async Task<DiagnosticsSeqBootstrapExecuteRequest> ReadSeqBootstrapExecuteRequestAsync(
        HttpRequest request,
        int maximumRequestBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > maximumRequestBytes)
        {
            throw new SeqOperationException(
                "seq_bootstrap_request_too_large",
                StatusCodes.Status413PayloadTooLarge,
                "The Seq setup request exceeded the bounded request-size limit.");
        }

        var buffer = new byte[maximumRequestBytes + 1];
        var total = 0;
        try
        {
            while (total < buffer.Length)
            {
                var read = await request.Body.ReadAsync(
                    buffer.AsMemory(total, buffer.Length - total),
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            if (total > maximumRequestBytes)
            {
                throw new SeqOperationException(
                    "seq_bootstrap_request_too_large",
                    StatusCodes.Status413PayloadTooLarge,
                    "The Seq setup request exceeded the bounded request-size limit.");
            }

            var parsed = JsonSerializer.Deserialize<DiagnosticsSeqBootstrapExecuteRequest>(
                buffer.AsSpan(0, total),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.ReviewId))
            {
                return parsed;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SeqOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or BadHttpRequestException or InvalidOperationException)
        {
            throw new SeqOperationException(
                "seq_bootstrap_request_invalid",
                StatusCodes.Status400BadRequest,
                "The Seq setup request is invalid.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }

        throw new SeqOperationException(
            "seq_bootstrap_request_invalid",
            StatusCodes.Status400BadRequest,
            "The Seq setup request is invalid.");
    }

    private static async Task<DiagnosticsSeqConnectionRequest> ReadSeqConnectionRequestAsync(
        HttpRequest request,
        int maximumRequestBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > maximumRequestBytes)
        {
            throw new SeqOperationException(
                "seq_connection_request_too_large",
                StatusCodes.Status413PayloadTooLarge,
                "The Seq connection request exceeded the bounded request-size limit.");
        }

        var buffer = new byte[maximumRequestBytes + 1];
        var total = 0;
        try
        {
            while (total < buffer.Length)
            {
                var read = await request.Body.ReadAsync(
                    buffer.AsMemory(total, buffer.Length - total),
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            if (total > maximumRequestBytes)
            {
                throw new SeqOperationException(
                    "seq_connection_request_too_large",
                    StatusCodes.Status413PayloadTooLarge,
                    "The Seq connection request exceeded the bounded request-size limit.");
            }

            var parsed = JsonSerializer.Deserialize<DiagnosticsSeqConnectionRequest>(
                buffer.AsSpan(0, total),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (parsed is not null &&
                !string.IsNullOrWhiteSpace(parsed.AdministratorPassword))
            {
                return parsed;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SeqOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or BadHttpRequestException or InvalidOperationException)
        {
            throw new SeqOperationException(
                "seq_connection_request_invalid",
                StatusCodes.Status400BadRequest,
                "The Seq connection request is invalid.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }

        throw new SeqOperationException(
            "seq_connection_request_invalid",
            StatusCodes.Status400BadRequest,
            "The Seq connection request is invalid.");
    }

    private static async Task<DiagnosticsSupportReportRequest> ReadSupportReportRequestAsync(
        HttpRequest request,
        int maximumRequestBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > maximumRequestBytes)
        {
            throw SupportReportRequestTooLarge();
        }

        var buffer = new byte[maximumRequestBytes + 1];
        var total = 0;
        try
        {
            while (total < buffer.Length)
            {
                var read = await request.Body.ReadAsync(
                    buffer.AsMemory(total, buffer.Length - total),
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            if (total > maximumRequestBytes)
            {
                throw SupportReportRequestTooLarge();
            }

            var parsed = JsonSerializer.Deserialize<DiagnosticsSupportReportRequest>(
                buffer.AsSpan(0, total),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.IncidentId))
            {
                return parsed;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MemProblemException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is JsonException or BadHttpRequestException or InvalidOperationException)
        {
            throw InvalidSupportReportRequest(ex);
        }

        throw InvalidSupportReportRequest();
    }

    private static MemProblemException SupportReportRequestTooLarge() =>
        new(
            StatusCodes.Status413PayloadTooLarge,
            "diagnostics_support_report_request_too_large",
            "The support report request is too large",
            "The support report request exceeded the configured request-size limit.",
            feature: "diagnostics");

    private static MemProblemException InvalidSupportReportRequest(Exception? innerException = null) =>
        new(
            StatusCodes.Status400BadRequest,
            "diagnostics_support_report_request_invalid",
            "The support report request is invalid",
            "An incidentId is required to generate a support report.",
            feature: "diagnostics",
            innerException: innerException);

    private static async Task<IResult> ExecuteSeqOperationAsync(
        Func<Task<DiagnosticsSeqOperationResponse>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (SeqOperationException exception)
        {
            return SeqProblem(
                exception.Code,
                exception.StatusCode,
                "Seq operation could not continue.",
                exception.Message);
        }
    }

    internal static async Task<IResult?> RequireRecentStepUpAsync(
        HttpContext context,
        IAuthorizationService authorization)
    {
        var settings = context.RequestServices.GetService<IMemSecuritySettingsService>();
        if (settings is not null)
        {
            var snapshot = await settings.GetEffectiveAsync(context.RequestAborted);
            if (!snapshot.RequireHighRiskStepUp)
            {
                return null;
            }
        }

        var result = await authorization.AuthorizeAsync(
            context.User,
            resource: null,
            policyName: MemOperatorPolicies.RecentStepUp);
        return result.Succeeded
            ? null
            : SeqProblem(
                "step_up_required",
                StatusCodes.Status403Forbidden,
                "Recent identity verification is required.",
                "Verify your identity, then retry only the action you already reviewed.");
    }

    private static IResult SeqProblem(
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

    private static void SetNoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
}
