using Carter;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;
using Modules.Operator.Migrations;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationFinalPackageUploadEndpointContractTests
{
    [Fact]
    public async Task Final_package_upload_route_is_session_keyed_post_only_and_size_bounded()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddDataProtection();
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection().Build());
        builder.Services.AddDbContext<MemDbContext>(options =>
            options.UseSqlite("Data Source=:memory:"));
        builder.Services.AddScoped<MigrationSessionProjectionService>();
        builder.Services.AddScoped<MigrationSessionInventoryService>();
        builder.Services.AddScoped<MigrationSessionLifecycleService>();
        builder.Services.AddScoped<MigrationSourceRequestService>();
        builder.Services.AddScoped<MigrationFinalPackageRecipientService>();
        builder.Services.AddScoped<MigrationFinalPackageUploadService>();
        builder.Services.AddScoped<IAgeKeyPairGenerator, StubAgeKeyPairGenerator>();
        builder.Services.AddScoped<IAgePackageDecryptor, StubAgePackageDecryptor>();
        await using var application = builder.Build();

        new MigrationSessionEndpoints().AddRoutes(application);

        var endpoint = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                candidate.RoutePattern.RawText ==
                "/api/operator/migrations/sessions/{migrationId}/package-revisions/final/package");

        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            authorization => authorization.Policy ==
                MemOperatorPolicies.MigrationIntakeOperate);
        var methods = endpoint.Metadata
            .GetMetadata<HttpMethodMetadata>()!
            .HttpMethods;
        Assert.Single(methods);
        Assert.Contains(HttpMethods.Post, methods);
        Assert.NotNull(
            endpoint.Metadata.GetMetadata<
                Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute>());
        Assert.NotNull(
            endpoint.Metadata.GetMetadata<
                Microsoft.AspNetCore.Mvc.RequestFormLimitsAttribute>());
    }

    [Fact]
    public void Final_upload_browser_contract_exposes_no_private_identity_or_server_path()
    {
        var propertyNames = typeof(MigrationFinalPackageUploadDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("ProtectedAgeIdentity", propertyNames);
        Assert.DoesNotContain("AgeIdentity", propertyNames);
        Assert.DoesNotContain("HostPath", propertyNames);
        Assert.DoesNotContain("ArchivePath", propertyNames);
        Assert.DoesNotContain("DockerId", propertyNames);
        Assert.DoesNotContain("ImageId", propertyNames);
        Assert.DoesNotContain("CandidateArtifactId", propertyNames);
        Assert.DoesNotContain("StagingRunId", propertyNames);
    }

    private sealed class StubAgeKeyPairGenerator : IAgeKeyPairGenerator
    {
        public Task<AgeKeyPair> GenerateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AgeKeyPair(
                "age1qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq",
                "AGE-SECRET-KEY-ENDPOINT-TEST"));
    }

    private sealed class StubAgePackageDecryptor : IAgePackageDecryptor
    {
        public Task DecryptAsync(
            string identity,
            string encryptedPath,
            string outputPath,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
