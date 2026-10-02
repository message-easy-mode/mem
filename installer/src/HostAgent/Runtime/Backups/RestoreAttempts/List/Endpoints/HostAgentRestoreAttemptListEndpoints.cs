using Carter;
using HostAgent.Commands;
using HostAgent.Security;

namespace HostAgent.Runtime.Backups.RestoreAttempts.List.Endpoints;

/// <summary>
/// Read-only canonical Restore Attempt inventory. This endpoint is intentionally
/// separate from the retired validation-ledger route while the web client moves
/// to the durable /restores contract in the following slice.
/// </summary>
public sealed class HostAgentRestoreAttemptListEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Restores");

        group.MapGet(
                "/backups/restores",
                async (
                    HttpContext httpContext,
                    int? page,
                    int? pageSize,
                    string? search,
                    string? status,
                    string? targetStack,
                    string? sortBy,
                    string? sortDirection,
                    RestoreAttemptListService restoreAttempts,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var response = await restoreAttempts.ListAsync(
                            new RestoreAttemptListQuery(
                                Page: page ?? 1,
                                PageSize: pageSize ?? 10,
                                Search: search,
                                Status: status,
                                TargetStack: targetStack,
                                SortBy: sortBy,
                                SortDirection: sortDirection),
                            ct);

                        return Results.Ok(response);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return Results.StatusCode(499);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_restore_attempt_list_request",
                            Detail: ex.Message));
                    }
                })
            .Produces<RestoreAttemptListResponse>();
    }
}
