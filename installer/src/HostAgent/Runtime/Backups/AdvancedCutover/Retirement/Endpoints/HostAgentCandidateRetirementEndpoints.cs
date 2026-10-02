using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.AdvancedCutover.Retirement;
using HostAgent.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Retirement.Endpoints;

public sealed class HostAgentCandidateRetirementEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Advanced Cutover / Retirement");

        group.MapGet(
                "/backups/advanced-cutover/retirement/standard-recreate-runs/{recreateId}/assessment",
                async (
                    HttpContext httpContext,
                    string recreateId,
                    string? candidateId,
                    [FromServices] RuntimeStackBackupProductionRecreateCleanupService cleanupService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await cleanupService.AssessAsync(
                            recreateId,
                            candidateId,
                            ct);

                        return Results.Ok(result);
                    }
                    catch (FileNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "production_recreate_run_not_found",
                            Detail: ex.Message));
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_production_recreate_cleanup_request",
                            Detail: ex.Message));
                    }
                })
            .Produces<RuntimeStackBackupProductionRecreateCleanupAssessment>();

        group.MapPost(
                "/backups/advanced-cutover/retirement/standard-recreate-runs/{recreateId}/execute",
                async (
                    HttpContext httpContext,
                    string recreateId,
                    string? candidateId,
                    string? retirementMode,
                    bool? acknowledgeRetireCandidate,
                    CancellationToken ct) =>
                {
                    // Candidate retirement destroys disposable private restore and
                    // cutover resources, then removes the retired candidate's
                    // ingress network. A normal signed-in session is therefore
                    // not sufficient: require fresh password-plus-TOTP step-up
                    // before the Docker-capable cleanup service can be resolved
                    // or invoked.
                    var authorizationService = httpContext.RequestServices
                        .GetRequiredService<IAuthorizationService>();

                    var stepUpRequired = await HostAgentEndpointOperatorGuard
                        .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                            httpContext,
                            authorizationService);

                    if (stepUpRequired is not null)
                    {
                        return stepUpRequired;
                    }

                    var cleanupService = httpContext.RequestServices
                        .GetRequiredService<RuntimeStackBackupProductionRecreateCleanupService>();

                    try
                    {
                        var result = await cleanupService.ExecuteAsync(
                            recreateId,
                            new RuntimeStackBackupProductionRecreateCleanupRequest(
                                CandidateId: candidateId,
                                RetirementMode: retirementMode,
                                AcknowledgeRetireCandidate: acknowledgeRetireCandidate ?? false),
                            ct);

                        return Results.Ok(result);
                    }
                    catch (FileNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "production_recreate_run_not_found",
                            Detail: ex.Message));
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_production_recreate_cleanup_request",
                            Detail: ex.Message));
                    }
                })
            .Produces<RuntimeStackBackupProductionRecreateCleanupResult>();
    }
}