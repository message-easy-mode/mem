using System.Security.Claims;
using Carter;
using HostAgent.Endpoints;
using HostAgent.Runtime.Coturn;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;

namespace Api.IntegrationTests.Runtime;

public sealed class HostAgentCoturnEndpointContractTests
{
    [Theory]
    [InlineData("/internal/host-agent/platform/coturn", "GET")]
    [InlineData("/internal/host-agent/platform/coturn/ensure", "POST")]
    [InlineData("/internal/host-agent/platform/coturn/install", "POST")]
    [InlineData("/internal/host-agent/platform/coturn/maintenance", "POST")]
    [InlineData("/internal/host-agent/platform/coturn/maintenance/active", "GET")]
    [InlineData("/internal/host-agent/platform/coturn/startup-supervision", "GET")]
    [InlineData("/internal/host-agent/platform/coturn/check", "POST")]
    [InlineData("/internal/host-agent/platform/coturn/check/latest", "GET")]
    [InlineData("/internal/host-agent/platform/coturn/logs", "GET")]
    public async Task Coturn_routes_use_the_expected_narrow_http_contract(
        string route,
        string expectedMethod)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<CoturnRuntimeService>();
        builder.Services.AddScoped<CoturnPlatformInstallOperationService>();
        builder.Services.AddScoped<CoturnPlatformMaintenanceOperationService>();
        builder.Services.AddSingleton<CoturnStartupSupervisionState>();
        builder.Services.AddAuthorization();

        await using var application = builder.Build();

        var module = new HostAgentCoturnEndpoints();
        Assert.IsAssignableFrom<ICarterModule>(module);
        module.AddRoutes(application);

        var endpoint = FindEndpoint(application, route);
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();

        Assert.NotNull(methods);
        Assert.Equal(expectedMethod, Assert.Single(methods!.HttpMethods));
    }

    [Fact]
    public async Task Restart_verify_requires_current_owner_session_but_not_recent_step_up()
    {
        var authorization = new RecordingAuthorizationService(AuthorizationResult.Failed());
        var context = CreatePlatformOwnerContext();

        var result = await HostAgentCoturnEndpoints.ValidateMaintenanceAuthorizationAsync(
            context,
            new CoturnPlatformMaintenanceRequest(
                CoturnPlatformMaintenanceActions.RestartVerify),
            authorization);

        Assert.Null(result);
        Assert.Equal(0, authorization.CallCount);
        Assert.Null(authorization.LastPolicyName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("8.8.8.8")]
    public async Task Repair_including_apply_or_clear_external_ip_retains_recent_step_up_authorization(string? externalIp)
    {
        var authorization = new RecordingAuthorizationService(AuthorizationResult.Failed());
        var context = CreatePlatformOwnerContext();

        var result = await HostAgentCoturnEndpoints.ValidateMaintenanceAuthorizationAsync(
            context,
            new CoturnPlatformMaintenanceRequest(
                CoturnPlatformMaintenanceActions.Repair, ExternalIp: externalIp),
            authorization);

        Assert.NotNull(result);
        Assert.Equal(1, authorization.CallCount);
        Assert.Equal(MemOperatorPolicies.RecentStepUp, authorization.LastPolicyName);
    }

    [Fact]
    public async Task Unknown_maintenance_action_fails_closed_to_recent_step_up()
    {
        var authorization = new RecordingAuthorizationService(AuthorizationResult.Failed());
        var context = CreatePlatformOwnerContext();

        var result = await HostAgentCoturnEndpoints.ValidateMaintenanceAuthorizationAsync(
            context,
            new CoturnPlatformMaintenanceRequest("unexpected-action"),
            authorization);

        Assert.NotNull(result);
        Assert.Equal(1, authorization.CallCount);
        Assert.Equal(MemOperatorPolicies.RecentStepUp, authorization.LastPolicyName);
    }

    [Fact]
    public async Task Restart_verify_rejects_anonymous_sessions_before_authorization_policy()
    {
        var authorization = new RecordingAuthorizationService(AuthorizationResult.Success());
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var result = await HostAgentCoturnEndpoints.ValidateMaintenanceAuthorizationAsync(
            context,
            new CoturnPlatformMaintenanceRequest(
                CoturnPlatformMaintenanceActions.RestartVerify),
            authorization);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, statusResult.StatusCode);
        Assert.Equal(0, authorization.CallCount);
    }

    private static DefaultHttpContext CreatePlatformOwnerContext()
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
                new Claim(ClaimTypes.Name, "owner"),
                new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
            },
            IdentityConstants.ApplicationScheme);

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
    }

    private sealed class RecordingAuthorizationService(AuthorizationResult result)
        : IAuthorizationService
    {
        public int CallCount { get; private set; }
        public string? LastPolicyName { get; private set; }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
        {
            CallCount++;
            return Task.FromResult(result);
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName)
        {
            CallCount++;
            LastPolicyName = policyName;
            return Task.FromResult(result);
        }
    }

    private static RouteEndpoint FindEndpoint(
        WebApplication application,
        string route) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => string.Equals(
                candidate.RoutePattern.RawText,
                route,
                StringComparison.Ordinal));
}
