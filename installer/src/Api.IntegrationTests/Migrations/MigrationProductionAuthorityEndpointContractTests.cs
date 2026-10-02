using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Migrations.Assurance;
using HostAgent.Runtime.Migrations.Assurance.Endpoints;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationProductionAuthorityEndpointContractTests
{
    [Fact]
    public async Task Production_authority_routes_are_migration_keyed_policy_protected_and_server_select_artifacts()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddDbContext<MemDbContext>(options =>
            options.UseSqlite("Data Source=:memory:"));
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IMemOperatorAuditService>(new NoopAuditService());
        builder.Services.AddScoped<MigrationProductionAuthorityService>();
        await using var application = builder.Build();

        var module = new MigrationProductionAuthorityEndpoints();
        Assert.IsAssignableFrom<ICarterModule>(module);
        module.AddRoutes(application);

        var endpoints = GetEndpoints(application);
        Assert.Equal(2, endpoints.Length);
        Assert.All(endpoints, endpoint =>
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                authorization => authorization.Policy ==
                    MemOperatorPolicies.MigrationIntakeOperate));

        var routes = endpoints.SelectMany(endpoint =>
            (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => $"{method} {Normalize(endpoint.RoutePattern.RawText)}"))
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(
            "GET /api/operator/migrations/sessions/{migrationId}/production-authority",
            routes);
        Assert.Contains(
            "POST /api/operator/migrations/sessions/{migrationId}/production-authority/operator-attested-snapshot",
            routes);

        var requestProperties = typeof(CreateOperatorAttestedSnapshotAuthorityRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();
        Assert.Equal(6, requestProperties.Length);
        Assert.All(requestProperties, property =>
            Assert.True(property.StartsWith("Acknowledge", StringComparison.Ordinal)));
        Assert.DoesNotContain("PackageRevisionId", requestProperties);
        Assert.DoesNotContain("CandidateArtifactId", requestProperties);
        Assert.DoesNotContain("StagingRunId", requestProperties);
        Assert.DoesNotContain("SourceFingerprint", requestProperties);
        Assert.DoesNotContain("SigningKeyIdentitySha256", requestProperties);
    }

    [Fact]
    public async Task Operator_attested_authority_creation_requires_recent_step_up_and_does_not_echo_body()
    {
        var authorization = new RecordingAuthorizationService(
            AuthorizationResult.Failed());
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAuthorizationService>(authorization);
        builder.Services.AddDbContext<MemDbContext>(options =>
            options.UseSqlite("Data Source=:memory:"));
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IMemOperatorAuditService>(new NoopAuditService());
        builder.Services.AddScoped<MigrationProductionAuthorityService>();
        await using var application = builder.Build();
        new MigrationProductionAuthorityEndpoints().AddRoutes(application);

        var endpoint = GetEndpoints(application).Single(candidate =>
            candidate.Metadata.GetMetadata<HttpMethodMetadata>()!
                .HttpMethods.Contains(HttpMethods.Post));
        const string migrationId = "mig_step_up_authority";
        var marker = "must-not-be-echoed";
        var body = Encoding.UTF8.GetBytes(
            $$"""
            {
              "acknowledgeUsersWereInstructedNotToUseSource": true,
              "acknowledgePostCaptureWritesWillNotMigrate": true,
              "acknowledgeSelectedSnapshotBecomesAuthoritative": true,
              "acknowledgeSourceWillBeRetainedUntilVerification": true,
              "acknowledgeNoFormalSourceFreezeEvidence": true,
              "acknowledgeReducedRollbackAssurance": true,
              "{{marker}}": true
            }
            """);

        await using var scope = application.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = CreatePlatformOwnerPrincipal(),
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path =
            $"/api/operator/migrations/sessions/{migrationId}/production-authority/operator-attested-snapshot";
        context.Request.RouteValues["migrationId"] = migrationId;
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        context.Features.Set<IHttpRequestBodyDetectionFeature>(
            new CanHaveBodyRequestFeature());
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
        Assert.DoesNotContain(migrationId, response.Detail ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(marker, response.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    private static ClaimsPrincipal CreatePlatformOwnerPrincipal()
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
                new Claim(ClaimTypes.Name, "owner.authority-test"),
                new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner),
            },
            IdentityConstants.ApplicationScheme);
        return new ClaimsPrincipal(identity);
    }

    private static RouteEndpoint[] GetEndpoints(WebApplication application) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => Normalize(endpoint.RoutePattern.RawText).StartsWith(
                "/api/operator/migrations/sessions/{migrationId}/production-authority",
                StringComparison.Ordinal))
            .ToArray();

    private static string Normalize(string? rawText)
    {
        var route = string.IsNullOrWhiteSpace(rawText) ? "/" : rawText;
        return route.Length > 1 ? route.TrimEnd('/') : route;
    }

    private sealed class NoopAuditService : IMemOperatorAuditService
    {
        public Task WriteAsync(
            MemOperatorAuditEventWrite auditEvent,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingAuthorizationService(
        AuthorizationResult result) : IAuthorizationService
    {
        public string? LastPolicyName { get; private set; }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(result);

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
