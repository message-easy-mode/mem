using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.AdvancedCutover.Candidate;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Candidate.Endpoints;

/// <summary>
/// Creates or resumes a private-only Production Restore candidate directly from
/// canonical Backup Catalog material. Public cutover stays unavailable.
/// </summary>
public sealed class HostAgentCatalogProductionCandidateEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Catalog / Production Restore Candidate");

        group.MapPost(
                "/backups/catalog/{catalogEntryId}/production-restore/candidates/recreate-private",
                async (
                    HttpContext httpContext,
                    string catalogEntryId,
                    [FromBody] CatalogProductionCandidateRequest? request,
                    CancellationToken ct) =>
                {
                    // Creating a private Production Restore candidate starts
                    // PostgreSQL, Synapse, and Element containers from backup
                    // material and persists restore/candidate state. It creates
                    // no public route, but it is still a production-restore
                    // operation and therefore requires fresh step-up.
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

                    if (request is null)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "backup_catalog_production_candidate_body_required",
                            Detail: "Provide a JSON request body. targetStackSlug and restoreMode are optional; recreate-production is the default."));
                    }

                    var candidateService = httpContext.RequestServices
                        .GetRequiredService<RuntimeStackBackupProductionCandidateService>();

                    try
                    {
                        var response = await candidateService
                            .CreateRecreateProductionPrivateCandidateFromCatalogAsync(
                                catalogEntryId,
                                request,
                                ct);

                        return Results.Ok(response);
                    }
                    catch (CatalogRestoreSourceNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "backup_catalog_entry_not_found",
                            Detail: ex.Message));
                    }
                    catch (CatalogRestoreSourceUnavailableException ex)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "backup_catalog_entry_not_available",
                                Detail: ex.Message),
                            statusCode: StatusCodes.Status409Conflict);
                    }
                    catch (BackupCatalogPayloadResolutionException ex)
                        when (string.Equals(
                            ex.ErrorCode,
                            "backup_catalog_entry_not_found",
                            StringComparison.Ordinal))
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: ex.ErrorCode,
                            Detail: ex.Message));
                    }
                    catch (BackupCatalogPayloadResolutionException ex)
                        when (ex.ErrorCode is "backup_catalog_payload_not_available"
                            or "backup_catalog_payload_invalid"
                            or "backup_catalog_payload_not_found")
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: ex.ErrorCode,
                                Detail: ex.Message),
                            statusCode: StatusCodes.Status409Conflict);
                    }
                    catch (BackupCatalogPayloadResolutionException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: ex.ErrorCode,
                            Detail: ex.Message));
                    }
                    catch (InvalidDataException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_catalog_production_candidate_payload",
                            Detail: ex.Message));
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_catalog_production_candidate_request",
                            Detail: ex.Message));
                    }
                })
            .Accepts<CatalogProductionCandidateRequest>("application/json")
            .Produces<CatalogProductionCandidateResponse>()
            .Produces<HostAgentErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status409Conflict);
    }
}
