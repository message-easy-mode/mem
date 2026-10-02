using System.Security.Claims;
using System.Text;
using Carter;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using HostAgent.Runtime.Migrations.Staging;
using HostAgent.Runtime.Migrations.Staging.Endpoints;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationStagingEndpointContractTests
{
    [Fact]
    public async Task Staging_routes_require_the_migration_operate_policy()
    {
        await using var fixture = await Fixture.CreateAsync();

        var endpoints = GetStagingEndpoints(fixture.Application);
        Assert.Equal(2, endpoints.Length);
        Assert.All(endpoints, endpoint =>
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                authorization => string.Equals(
                    authorization.Policy,
                    MemOperatorPolicies.MigrationIntakeOperate,
                    StringComparison.Ordinal)));

        Assert.Contains(endpoints, endpoint => HasMethod(endpoint, HttpMethods.Get));
        Assert.Single(endpoints.Where(endpoint => HasMethod(endpoint, HttpMethods.Post)));
    }

    [Theory]
    [InlineData("GET", "/api/operator/migrations/sessions/{migrationId}/staging-runs", null)]
    [InlineData("POST", "/api/operator/migrations/sessions/{migrationId}/staging-runs", "{}")]
    public async Task Staging_responses_are_no_store(
        string method,
        string routePattern,
        string? body)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var scope = fixture.Application.Services.CreateAsyncScope();

        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = CreatePlatformOwnerPrincipal()
        };
        context.Request.Method = method;
        context.Request.Path = "/api/operator/migrations/sessions/mig_missing/staging-runs";
        context.Request.RouteValues["migrationId"] = "mig_missing";
        context.Response.Body = new MemoryStream();

        if (routePattern.Contains("{stagingRunId}", StringComparison.Ordinal))
        {
            context.Request.Path += "/mstg_missing/destroy";
            context.Request.RouteValues["stagingRunId"] = "mstg_missing";
        }

        if (body is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = bytes.Length;
            context.Request.Body = new MemoryStream(bytes);
        }

        var endpoint = GetStagingEndpoints(fixture.Application)
            .Single(candidate =>
                string.Equals(
                    candidate.RoutePattern.RawText?.TrimEnd('/'),
                    routePattern.TrimEnd('/'),
                    StringComparison.Ordinal) &&
                HasMethod(candidate, method));
        var requestDelegate = endpoint.RequestDelegate
            ?? throw new InvalidOperationException("Staging route does not have a request delegate.");

        await requestDelegate(context);

        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());
        Assert.Contains(
            context.Response.StatusCode,
            new[]
            {
                StatusCodes.Status200OK,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict
            });
    }

    private static RouteEndpoint[] GetStagingEndpoints(WebApplication application) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint =>
                endpoint.RoutePattern.RawText?.StartsWith(
                    "/api/operator/migrations/sessions/{migrationId}/staging-runs",
                    StringComparison.Ordinal) == true)
            .ToArray();

    private static bool HasMethod(RouteEndpoint endpoint, string method) =>
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) == true;

    private static ClaimsPrincipal CreatePlatformOwnerPrincipal() =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
        ],
        authenticationType: IdentityConstants.ApplicationScheme));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private Fixture(WebApplication application, string databasePath)
        {
            Application = application;
            _databasePath = databasePath;
        }

        public WebApplication Application { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-migration-staging-endpoint-{Guid.NewGuid():N}.db");

            var builder = WebApplication.CreateBuilder();
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton<IAuthorizationService, AllowAllAuthorizationService>();
            builder.Services.AddDbContext<MemDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath}"));
            builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
            builder.Services.AddScoped<IMigrationPrivateStagingRunner, NoopRunner>();
            builder.Services.AddSingleton<MigrationStagingOperationLifetime>();
            builder.Services.AddScoped<MigrationStagingOrchestrator>();

            var application = builder.Build();
            var module = new MigrationStagingEndpoints();
            Assert.IsAssignableFrom<ICarterModule>(module);
            module.AddRoutes(application);

            await using (var scope = application.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
                await db.Database.EnsureCreatedAsync();
            }

            return new Fixture(application, databasePath);
        }

        public async ValueTask DisposeAsync()
        {
            await Application.DisposeAsync();
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
    }

    private sealed class AllowAllAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName) =>
            Task.FromResult(AuthorizationResult.Success());
    }

    private sealed class NoopRunner : IMigrationPrivateStagingRunner
    {
        public Task<PrivateStagingRunResult> CreateAsync(
            MigrationIntakeEntity intake,
            MigrationPackageRevisionEntity packageRevision,
            MigrationCandidateArtifactEntity candidate,
            string runId,
            string? slug,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The endpoint contract fixture does not execute staging.");

        public Task<PrivateStagingRunResult> DestroyAsync(
            string id,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The endpoint contract fixture does not execute staging destruction.");
    }
}
