using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modules.Auth.Identity;
using Modules.Operator.Npm;

namespace Api.IntegrationTests.Setup;

public sealed class NpmInstalledCredentialEndpointContractTests
{
    [Fact]
    public async Task STARTUP_NPM_BOOTSTRAP_01D_all_routes_require_platform_owner_authority()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();

        await using var app = builder.Build();
        new NpmCredentialManagementEndpoints().AddRoutes(app);

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/api/operator/npm",
                StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(3, routes.Length);
        Assert.All(routes, route =>
        {
            var authorization = route.Metadata.GetOrderedMetadata<IAuthorizeData>();
            Assert.Contains(
                authorization,
                data => string.Equals(
                    data.Policy,
                    MemOperatorPolicies.ManagePlatform,
                    StringComparison.Ordinal));
        });
    }

    [Theory]
    [InlineData("POST", "/api/operator/npm/credential/reveal", null)]
    [InlineData("PUT", "/api/operator/npm/credential", "{\"email\":\"replacement@example.test\",\"password\":\"replacement-test-secret\"}")]
    public async Task STARTUP_NPM_BOOTSTRAP_01D_high_risk_credential_routes_require_recent_step_up_and_no_store(
        string method,
        string path,
        string? body)
    {
        var authorization = new DenyRecentStepUpAuthorizationService();
        var builder = WebApplication.CreateBuilder();
        builder.Services.Replace(
            ServiceDescriptor.Singleton<IAuthorizationService>(authorization));
        builder.Services.AddSingleton(new NpmCredentialManagementService(
            db: null!,
            credentialService: null!,
            runtimeService: null!,
            apiBaseUrlResolver: null!,
            apiClient: null!,
            tokenProvider: null!,
            authorityResolver: null!,
            audit: null!,
            timeProvider: TimeProvider.System));

        await using var app = builder.Build();
        new NpmCredentialManagementEndpoints().AddRoutes(app);

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                string.Equals(candidate.RoutePattern.RawText, path, StringComparison.Ordinal) &&
                candidate.Metadata.OfType<HttpMethodMetadata>()
                    .Any(metadata => metadata.HttpMethods.Contains(method)));

        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
            User = CreatePlatformOwnerPrincipal()
        };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        if (body is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = bytes.Length;
            context.Request.Body = new MemoryStream(bytes);
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new CanHaveBodyRequestFeature());
        }

        await (endpoint.RequestDelegate
            ?? throw new InvalidOperationException("NPM credential endpoint has no request delegate."))(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());
        Assert.Equal(MemOperatorPolicies.RecentStepUp, authorization.LastPolicyName);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("step_up_required", document.RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("replacement-test-secret", document.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    private static ClaimsPrincipal CreatePlatformOwnerPrincipal() =>
        new(
            new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
                    new Claim(ClaimTypes.Name, "owner.npm-settings-test"),
                    new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
                ],
                IdentityConstants.ApplicationScheme));

    private sealed class DenyRecentStepUpAuthorizationService : IAuthorizationService
    {
        public string? LastPolicyName { get; private set; }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName)
        {
            LastPolicyName = policyName;
            return Task.FromResult(AuthorizationResult.Failed());
        }
    }

    private sealed class CanHaveBodyRequestFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
