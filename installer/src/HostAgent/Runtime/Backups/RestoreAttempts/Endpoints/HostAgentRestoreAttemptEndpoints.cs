using HostAgent.Commands;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.Contracts;
using HostAgent.Security;

namespace HostAgent.Runtime.Backups.RestoreAttempts.Endpoints;

/// <summary>
/// Canonical durable restore-attempt operations. These endpoints use a restore session id as the stable workspace key; they do not accept validation receipts.
/// </summary>
public sealed class HostAgentRestoreAttemptEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Restores");

        group.MapGet("/backups/restores/{restoreSessionId}/attempt",
            async (
                HttpContext httpContext,
                string restoreSessionId,
                RestoreAttemptCoordinator restoreAttemptCoordinator,
                CancellationToken ct) =>
            {
                var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                if (authResult is not null)
                {
                    return authResult;
                }

                try
                {
                    var attempt = await restoreAttemptCoordinator.GetByRestoreSessionIdAsync(
                        restoreSessionId,
                        ct);

                    return attempt is null
                        ? Results.NotFound(new HostAgentErrorResponse(
                            Error: "restore_attempt_not_found",
                            Detail: $"Restore attempt '{restoreSessionId}' was not found.",
                            Message: HostAgentStructuredMessages.RestoreAttemptNotFound(
                                restoreSessionId)))
                        : Results.Ok(attempt);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new HostAgentErrorResponse(
                        Error: "invalid_restore_attempt_request",
                        Detail: ex.Message,
                        Message: HostAgentStructuredMessages.RestoreAttemptRequestInvalid(
                            restoreSessionId)));
                }
            })
            .Produces<RestoreAttemptSnapshot>();

        group.MapPost("/backups/restores/{restoreSessionId}/cancel",
            async (
                HttpContext httpContext,
                string restoreSessionId,
                bool? acknowledgeCancel,
                RestoreAttemptCoordinator restoreAttemptCoordinator,
                CancellationToken ct) =>
            {
                var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                if (authResult is not null)
                {
                    return authResult;
                }

                if (acknowledgeCancel != true)
                {
                    return Results.BadRequest(new HostAgentErrorResponse(
                        Error: "restore_cancel_acknowledgement_required",
                        Detail: "Cancelling a restore requires acknowledgeCancel=true.",
                        Message: HostAgentStructuredMessages.RestoreCancelAcknowledgementRequired(
                            restoreSessionId)));
                }

                try
                {
                    var attempt = await restoreAttemptCoordinator.CancelAsync(
                        restoreSessionId,
                        ct);
                    return Results.Ok(attempt);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new HostAgentErrorResponse(
                        Error: "restore_cancel_not_available",
                        Detail: ex.Message,
                        Message: HostAgentStructuredMessages.RestoreCancelNotAvailable(
                            restoreSessionId)));
                }
            })
            .Produces<RestoreAttemptSnapshot>();

        group.MapGet("/backups/restores/reconciliation",
            async (
                HttpContext httpContext,
                RestoreAttemptCoordinator restoreAttemptCoordinator,
                CancellationToken ct) =>
            {
                var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                if (authResult is not null)
                {
                    return authResult;
                }

                var report = await restoreAttemptCoordinator.ReconcileCreatingAttemptsAsync(ct);
                return Results.Ok(report);
            })
            .Produces<RestoreAttemptReconciliationReport>();
    }
}
