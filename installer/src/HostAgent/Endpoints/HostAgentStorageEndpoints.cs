// HostAgent/Endpoints/HostAgentStorageEndpoints.cs

using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Storage;
using HostAgent.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HostAgent.Endpoints;

public sealed class HostAgentStorageEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent");

        group.MapGet("/runtime-stacks/{slugOrId}/storage",
            async (
                HttpContext httpContext,
                string slugOrId,
                RuntimeStackStorageService storageService,
                CancellationToken ct) =>
            {
                var authorizationResult =
                    HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                if (authorizationResult is not null)
                {
                    return authorizationResult;
                }

                try
                {
                    var result = await storageService.InspectAsync(
                        slugOrId,
                        ct);

                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new HostAgentErrorResponse(
                        Error: "invalid_storage_request",
                        Detail: ex.Message));
                }
            });
    }
}
