using Carter;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modules.Integrations.Npm.Services;

namespace HostAgent.Endpoints;

public sealed class HostAgentStatusEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/internal/host-agent/status",
                async (
                    HttpContext httpContext,
                    DockerClient docker,
                    NpmReadinessService npmReadinessService,
                    CancellationToken ct) =>
                {
                    if (!HostAgentEndpointOperatorGuard.HasCurrentControlPlaneSession(httpContext.User))
                    {
                        return Results.Json(
                            new HostAgentStatusResponse(
                                Source: "control-plane",
                                HostAgent: "unauthorized",
                                DockerReachable: false,
                                RuntimeNetworkName: null,
                                NpmReady: false,
                                Status: "unauthorized",
                                Detail: "A valid installer unlock session is required."),
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    var dockerReachable = false;
                    var runtimeNetworkName = "mem-gateway";
                    string? detail = null;

                    try
                    {
                        await docker.System.PingAsync(ct);

                        dockerReachable = true;

                        // Optional but useful: confirm the expected gateway network exists.
                        await docker.Networks.InspectNetworkAsync(
                            runtimeNetworkName,
                            ct);
                    }
                    catch (Exception ex)
                    {
                        detail = ex.Message;
                    }

                    var npmReady = false;

                    try
                    {
                        var npm = await npmReadinessService.GetReadinessAsync(ct);

                        npmReady =
                            npm.ContainerExists &&
                            npm.ContainerRunning &&
                            npm.AdminUiReachable &&
                            npm.Initialized &&
                            npm.ApiAuthenticated &&
                            npm.CertificateApiReachable &&
                            string.Equals(
                                npm.RecommendedAction,
                                "ready",
                                StringComparison.OrdinalIgnoreCase);
                    }
                    catch (Exception ex)
                    {
                        detail = string.IsNullOrWhiteSpace(detail)
                            ? ex.Message
                            : $"{detail} | NPM readiness failed: {ex.Message}";
                    }

                    var ready = dockerReachable && npmReady;

                    return Results.Ok(new HostAgentStatusResponse(
                        Source: "control-plane",
                        HostAgent: "ready",
                        DockerReachable: dockerReachable,
                        RuntimeNetworkName: runtimeNetworkName,
                        NpmReady: npmReady,
                        Status: ready ? "ready" : "degraded",
                        Detail: detail));
                })
            .WithName("GetHostAgentStatus")
            .WithTags("HostAgent");
    }
}

public sealed record HostAgentStatusResponse(
    string Source,
    string HostAgent,
    bool DockerReachable,
    string? RuntimeNetworkName,
    bool NpmReady,
    string Status,
    string? Detail);
