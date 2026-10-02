using System.Security.Claims;
using Carter;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;
using Modules.Operator.Migrations;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationFinalPackageRecipientEndpointContractTests
{
    [Fact]
    public async Task Final_recipient_route_is_migration_keyed_step_up_gated_and_no_store()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-final-recipient-endpoint-{Guid.NewGuid():N}.db");
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton<IAuthorizationService, AllowAllAuthorizationService>();
            builder.Services.AddDataProtection();
            builder.Services.AddDbContext<MemDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath}"));
            builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
            builder.Services.AddScoped<MigrationSessionProjectionService>();
            builder.Services.AddScoped<MigrationSessionInventoryService>();
            builder.Services.AddScoped<MigrationSessionLifecycleService>();
            builder.Services.AddScoped<MigrationSourceRequestService>();
            builder.Services.AddScoped<MigrationFinalPackageRecipientService>();
            builder.Services.AddScoped<MigrationFinalPackageUploadService>();
            builder.Services.AddScoped<IAgeKeyPairGenerator, StubAgeKeyPairGenerator>();
            await using var application = builder.Build();

            var module = new MigrationSessionEndpoints();
            Assert.IsAssignableFrom<ICarterModule>(module);
            module.AddRoutes(application);

            var endpoint = ((IEndpointRouteBuilder)application).DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Single(candidate =>
                    candidate.RoutePattern.RawText ==
                    "/api/operator/migrations/sessions/{migrationId}/package-revisions/final-recipient");

            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                authorization => authorization.Policy ==
                    MemOperatorPolicies.MigrationIntakeOperate);
            Assert.Contains(
                HttpMethods.Post,
                endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);

            await using var scope = application.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
            await db.Database.EnsureCreatedAsync();
            var context = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                User = CreatePlatformOwnerPrincipal(),
            };
            context.Request.Method = HttpMethods.Post;
            context.Request.Path =
                "/api/operator/migrations/sessions/mig_missing/package-revisions/final-recipient";
            context.Request.RouteValues["migrationId"] = "mig_missing";
            context.Response.Body = new MemoryStream();

            await endpoint.RequestDelegate!(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
            Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public void Final_recipient_browser_contract_exposes_no_private_identity_or_server_identifier()
    {
        var propertyNames = typeof(MigrationFinalPackageRecipientDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("ProtectedAgeIdentity", propertyNames);
        Assert.DoesNotContain("HostPath", propertyNames);
        Assert.DoesNotContain("DockerId", propertyNames);
        Assert.DoesNotContain("ImageId", propertyNames);
        Assert.DoesNotContain("CandidateArtifactId", propertyNames);
        Assert.DoesNotContain("StagingRunId", propertyNames);
    }

    private static ClaimsPrincipal CreatePlatformOwnerPrincipal() =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner),
        ],
        IdentityConstants.ApplicationScheme));

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

    private sealed class StubAgeKeyPairGenerator : IAgeKeyPairGenerator
    {
        public Task<AgeKeyPair> GenerateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AgeKeyPair(
                "age1qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq",
                "AGE-SECRET-KEY-ENDPOINT-TEST"));
    }
}
