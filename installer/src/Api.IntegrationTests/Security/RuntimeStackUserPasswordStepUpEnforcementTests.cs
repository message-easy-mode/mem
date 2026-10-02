using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HostAgent.Commands;
using HostAgent.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modules.Auth.Identity;

namespace Api.IntegrationTests.Security;

public sealed class RuntimeStackUserPasswordStepUpEnforcementTests
{
    [Theory]
    [InlineData(
        "/internal/host-agent/runtime-stacks/{slugOrId}/users/admin-authority",
        "/internal/host-agent/runtime-stacks/demo/users/admin-authority",
        "{\"accessToken\":\"must-not-be-read\"}")]
    [InlineData(
        "/internal/host-agent/runtime-stacks/{slugOrId}/users/admin-authority",
        "/internal/host-agent/runtime-stacks/demo/users/admin-authority",
        "{\"matrixUserId\":\"@admin:demo.test\",\"password\":\"must-not-be-read\"}")]
    [InlineData(
        "/internal/host-agent/runtime-stacks/{slugOrId}/users/{userId:guid}/password",
        "/internal/host-agent/runtime-stacks/demo/users/11111111-1111-1111-1111-111111111111/password",
        "{\"newPassword\":\"must-not-be-read\"}")]
    [InlineData(
        "/internal/host-agent/runtime-stacks/{slugOrId}/users/{userId:guid}/deactivate",
        "/internal/host-agent/runtime-stacks/demo/users/11111111-1111-1111-1111-111111111111/deactivate",
        "{\"erase\":false}")]
    [InlineData(
        "/internal/host-agent/runtime-stacks/{slugOrId}/users/{userId:guid}/reactivate",
        "/internal/host-agent/runtime-stacks/demo/users/11111111-1111-1111-1111-111111111111/reactivate",
        "{\"newPassword\":\"must-not-be-read\"}")]
    public async Task Matrix_password_authority_mutations_require_recent_step_up_before_service_resolution(
        string routeTemplate,
        string path,
        string bodyJson)
    {
        var authorization = new RecordingAuthorizationService(AuthorizationResult.Failed());
        var builder = WebApplication.CreateBuilder();
        builder.Services.Replace(ServiceDescriptor.Singleton<IAuthorizationService>(authorization));

        await using var application = builder.Build();
        new RuntimeStackUsersEndpoint().AddRoutes(application);

        var endpoint = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == routeTemplate);

        var body = Encoding.UTF8.GetBytes(bodyJson);
        var context = new DefaultHttpContext
        {
            RequestServices = application.Services,
            User = CreatePlatformOwnerPrincipal()
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;
        context.Request.RouteValues["slugOrId"] = "demo";
        if (routeTemplate.Contains("{userId:guid}", StringComparison.Ordinal))
        {
            context.Request.RouteValues["userId"] = "11111111-1111-1111-1111-111111111111";
        }
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new CanHaveBodyRequestFeature());
        context.Request.Body = new MemoryStream(body);
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal(MemOperatorPolicies.RecentStepUp, authorization.LastPolicyName);

        context.Response.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<HostAgentErrorResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(response);
        Assert.Equal("step_up_required", response!.Error);
        Assert.DoesNotContain("must-not-be-read", response.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    private static ClaimsPrincipal CreatePlatformOwnerPrincipal()
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
            },
            IdentityConstants.ApplicationScheme);
        return new ClaimsPrincipal(identity);
    }

    private sealed class RecordingAuthorizationService(AuthorizationResult result)
        : IAuthorizationService
    {
        public string? LastPolicyName { get; private set; }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
        {
            return Task.FromResult(result);
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName)
        {
            LastPolicyName = policyName;
            return Task.FromResult(result);
        }
    }

    private sealed class CanHaveBodyRequestFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
