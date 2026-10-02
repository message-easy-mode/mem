using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.Contracts;
using HostAgent.Runtime.Backups.Workspace.PrivateTest;
using HostAgent.Security;

namespace HostAgent.Runtime.Backups.Workspace.Endpoints;

public sealed class HostAgentRestoreWorkspaceEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Restore Workspace");

        group.MapGet(
                "/backups/restores/{restoreSessionId}/workspace",
                async (
                    HttpContext httpContext,
                    string restoreSessionId,
                    RestoreWorkspaceService restoreWorkspaceService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);
                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var workspace = await restoreWorkspaceService.GetAsync(
                            restoreSessionId,
                            ct);

                        return workspace is null
                            ? Results.NotFound(new HostAgentErrorResponse(
                                Error: "restore_attempt_not_found",
                                Detail: $"Restore attempt '{restoreSessionId}' was not found.",
                                Message: HostAgentStructuredMessages.RestoreAttemptNotFound(
                                    restoreSessionId)))
                            : Results.Ok(workspace);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_restore_workspace_request",
                            Detail: ex.Message,
                            Message: HostAgentStructuredMessages.RestoreWorkspaceRequestInvalid(
                                restoreSessionId)));
                    }
                })
            .Produces<RestoreWorkspaceResponse>();

        group.MapPost(
                "/backups/restores/{restoreSessionId}/private-test",
                async (
                    HttpContext httpContext,
                    string restoreSessionId,
                    RestoreWorkspacePrivateTestService privateTestService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);
                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await privateTestService.RunAsync(
                            restoreSessionId,
                            ct);
                        return Results.Ok(result);
                    }
                    catch (RestoreAttemptConflictException ex)
                    {
                        return Results.Conflict(ex.Conflict);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "restore_private_test_not_available",
                            Detail: ex.Message,
                            Message: HostAgentStructuredMessages.RestorePrivateTestNotAvailable(
                                restoreSessionId)));
                    }
                })
            .Produces<RestoreWorkspacePrivateTestActionResponse>();

        group.MapPost(
                "/backups/restores/{restoreSessionId}/complete-handover",
                async (
                    HttpContext httpContext,
                    string restoreSessionId,
                    bool? acknowledgeCompletion,
                    RestoreAttemptCoordinator restoreAttemptCoordinator,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);
                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    if (acknowledgeCompletion != true)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "restore_handover_acknowledgement_required",
                            Detail: "Completing restore handover requires acknowledgeCompletion=true.",
                            Message: HostAgentStructuredMessages.RestoreHandoverAcknowledgementRequired(
                                restoreSessionId)));
                    }

                    try
                    {
                        var attempt = await restoreAttemptCoordinator.CompleteHandoverAsync(
                            restoreSessionId,
                            ct);
                        return Results.Ok(attempt);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "restore_handover_not_available",
                            Detail: ex.Message,
                            Message: HostAgentStructuredMessages.RestoreHandoverNotAvailable(
                                restoreSessionId)));
                    }
                })
            .Produces<RestoreAttemptSnapshot>();
    }
}
