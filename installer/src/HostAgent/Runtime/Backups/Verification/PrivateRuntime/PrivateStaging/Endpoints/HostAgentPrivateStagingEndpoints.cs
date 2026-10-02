using System.Diagnostics;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using HostAgent.Commands;
using HostAgent.Security;
using HostAgent.Runtime.Backups.Workspace.PrivateTest;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging.Endpoints;

/// <summary>
/// Private staging is created only by the canonical Restore Workspace. This
/// low-level endpoint remains for explicit destruction of a retained runtime.
/// </summary>
public sealed class HostAgentPrivateStagingEndpoints : ICarterModule
{
    private static readonly TimeSpan DiagnosticTimeout = TimeSpan.FromSeconds(20);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Verification / Private Runtime / Private Staging");

        group.MapPost(
                "/backups/verification/private-runtime/private-staging/{stagingId}/destroy",
                async (
                    HttpContext httpContext,
                    string stagingId,
                    PrivateStagingService stagingService,
                    MemDbContext db,
                    PrivateStagingDestroyOperationLifetime destroyLifetime,
                    RestorePrivateTestRetirementAuditService retirementAudit,
                    IMemDiagnosticEventWriter diagnostics,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    var retirementAccepted = false;

                    try
                    {
                        // This Restore-only path must not bypass Migration review even
                        // when a retained history report is missing or mislabeled.
                        bool migrationOwned;
                        try
                        {
                            migrationOwned = await db.MigrationStagingRuns.AsNoTracking()
                                .AnyAsync(run => run.PrivateRuntimeStagingId == stagingId, ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch (Exception)
                        {
                            return Results.Json(new HostAgentErrorResponse(
                                Error: "staging_ownership_unavailable",
                                Detail: "Current staging ownership could not be established. No retirement was accepted."),
                                statusCode: StatusCodes.Status503ServiceUnavailable);
                        }
                        if (migrationOwned)
                            return Results.Json(new HostAgentErrorResponse(
                                Error: "migration_staging_review_required",
                                Detail: "Review retirement in the owning Migration workspace or Services."),
                                statusCode: StatusCodes.Status409Conflict);

                        // Before acceptance the initiating request may still govern
                        // validation/read-only preparation. Once a retained staging
                        // record is resolved, retirement becomes a bounded server-owned
                        // destructive mutation.
                        var existing = await stagingService.GetAsync(
                            stagingId,
                            ct);

                        if (existing is null)
                        {
                            return Results.NotFound(new HostAgentErrorResponse(
                                Error: "restore_staging_run_not_found",
                                Detail: $"Restore Staging run '{stagingId}' was not found."));
                        }

                        if (existing.SourceKind == "migration-candidate")
                        {
                            return Results.Json(new HostAgentErrorResponse(
                                Error: "migration_staging_review_required",
                                Detail: "Review retirement in the owning Migration workspace or Services."),
                                statusCode: StatusCodes.Status409Conflict);
                        }

                        var auditContext = await retirementAudit.ResolveAsync(
                            stagingId,
                            ct);

                        if (PrivateStagingService.IsDestroyComplete(existing))
                        {
                            if (auditContext is not null)
                            {
                                using var auditCancellation = new CancellationTokenSource(DiagnosticTimeout);
                                await retirementAudit.RecordDestroyedIfMissingAsync(
                                    auditContext,
                                    existing.Destroy,
                                    auditCancellation.Token);
                            }

                            return Results.Ok(existing);
                        }

                        using var operationLifetime = destroyLifetime.BeginAfterAcceptance(
                            stagingId,
                            ct);
                        retirementAccepted = true;

                        if (auditContext is not null)
                        {
                            await retirementAudit.RecordDestroyRequestedAsync(
                                auditContext,
                                operationLifetime.CancellationToken);
                        }

                        var result = await stagingService.DestroyAsync(
                            stagingId,
                            operationLifetime.CancellationToken);

                        if (auditContext is not null)
                        {
                            using var auditCancellation = new CancellationTokenSource(DiagnosticTimeout);
                            await retirementAudit.RecordDestroyedIfMissingAsync(
                                auditContext,
                                result.Destroy,
                                auditCancellation.Token);
                        }

                        return Results.Ok(result);
                    }
                    catch (OperationCanceledException) when (!retirementAccepted && ct.IsCancellationRequested)
                    {
                        // The browser/request still owns read-only validation before
                        // retirement is accepted. Do not manufacture a cleanup incident
                        // when no destructive mutation began.
                        throw;
                    }
                    catch (PrivateStagingDestroyIncompleteException ex)
                    {
                        await WriteDestroyFailureDiagnosticAsync(
                            httpContext,
                            diagnostics,
                            stagingId,
                            ex,
                            eventCode: "restore.private-test.retirement.failed",
                            observedStatus: ex.Result.Status,
                            warningCount: ex.Result.Destroy?.Warnings.Count ?? 0,
                            suggestedAction: "Open the Restore Workspace, review the retained private-test state and Diagnostics, then retry retirement. MEM will safely reconcile already-removed disposable resources.");

                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "restore_private_test_retirement_failed",
                                Detail: "MEM could not remove every disposable private-test resource. Review Diagnostics and retry retirement."),
                            statusCode: StatusCodes.Status500InternalServerError);
                    }
                    catch (OperationCanceledException ex)
                    {
                        await WriteDestroyFailureDiagnosticAsync(
                            httpContext,
                            diagnostics,
                            stagingId,
                            ex,
                            eventCode: "restore.private-test.retirement.interrupted",
                            observedStatus: "interrupted",
                            warningCount: 0,
                            suggestedAction: "Review the Restore Workspace after the Control Plane is available. Retirement is idempotent and can be retried safely.");

                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "restore_private_test_retirement_interrupted",
                                Detail: "Private-test retirement was interrupted before MEM could confirm terminal cleanup. Review the workspace before retrying."),
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_restore_staging_destroy_request",
                            Detail: ex.Message));
                    }
                    catch (FileNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "restore_staging_run_not_found",
                            Detail: ex.Message));
                    }
                })
            .Produces<PrivateStagingRunResult>();
    }

    private static async Task WriteDestroyFailureDiagnosticAsync(
        HttpContext httpContext,
        IMemDiagnosticEventWriter diagnostics,
        string stagingId,
        Exception exception,
        string eventCode,
        string observedStatus,
        int warningCount,
        string suggestedAction)
    {
        using var diagnosticCancellation = new CancellationTokenSource(DiagnosticTimeout);

        var activity = Activity.Current;
        try
        {
            await diagnostics.WriteAsync(
                new MemDiagnosticWriteRequest(
                    Severity: MemDiagnosticSeverities.Error,
                    EventCode: eventCode,
                    Source: "host-agent.restore",
                    Feature: "restore",
                    Stage: "private-test-retirement",
                    Message: "MEM could not safely complete Private Restore Test retirement.",
                    CreateIncident: true,
                    Resource: new MemDiagnosticResource(
                        Kind: "restore-private-staging",
                        Id: stagingId,
                        DisplayName: "Private Restore Test staging runtime"),
                    Expected: new Dictionary<string, string?>
                    {
                        ["stagingRuntimeStatus"] = "destroyed"
                    },
                    Observed: new Dictionary<string, string?>
                    {
                        ["stagingRuntimeStatus"] = observedStatus,
                        ["warningCount"] = warningCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    },
                    Details: new Dictionary<string, string?>
                    {
                        ["stagingId"] = stagingId,
                        ["requestAbortCancelsMutation"] = "false"
                    },
                    Exception: exception,
                    SuggestedAction: suggestedAction,
                    Retryable: true,
                    Context: new MemDiagnosticContext(
                        TraceId: activity?.TraceId.ToString(),
                        SpanId: activity?.SpanId.ToString(),
                        RequestId: httpContext.TraceIdentifier,
                        CorrelationId: activity?.TraceId.ToString())),
                diagnosticCancellation.Token);
        }
        catch (Exception diagnosticFailure) when (
            diagnosticFailure is not StackOverflowException and not OutOfMemoryException)
        {
            // Failure diagnostics must never replace the primary retirement outcome.
        }
    }
}
