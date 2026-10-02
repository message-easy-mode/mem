using Carter;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;
using Modules.Operator.Migrations;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationSessionInventoryEndpointContractTests
{
    [Fact]
    public async Task Inventory_route_is_authorized_get_only()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddDbContext<MemDbContext>(options =>
            options.UseSqlite("Data Source=:memory:"));
        builder.Services.AddScoped<MigrationSessionProjectionService>();
        builder.Services.AddScoped<MigrationSessionInventoryService>();
        builder.Services.AddScoped<MigrationSessionLifecycleService>();
        builder.Services.AddScoped<MigrationSourceRequestService>();
        builder.Services.AddScoped<MigrationFinalPackageRecipientService>();
        builder.Services.AddScoped<MigrationFinalPackageUploadService>();
        await using var application = builder.Build();

        new MigrationSessionEndpoints().AddRoutes(application);

        var endpoint = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                candidate.RoutePattern.RawText ==
                "/api/operator/migrations/sessions/inventory");

        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            authorization => authorization.Policy ==
                MemOperatorPolicies.MigrationIntakeOperate);
        var methods = endpoint.Metadata
            .GetMetadata<HttpMethodMetadata>()!
            .HttpMethods;
        Assert.Single(methods);
        Assert.Contains(HttpMethods.Get, methods);
    }

    [Fact]
    public void Compact_row_contract_exposes_no_secret_or_path_fields()
    {
        var propertyNames = typeof(MigrationSessionInventoryRowDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("ProtectedAgeIdentity", propertyNames);
        Assert.DoesNotContain("AgeRecipient", propertyNames);
        Assert.DoesNotContain("ManifestJson", propertyNames);
        Assert.DoesNotContain("WorkspacePath", propertyNames);
        Assert.DoesNotContain("ArtifactPath", propertyNames);
        Assert.DoesNotContain("EvidenceJson", propertyNames);
        Assert.DoesNotContain("DatabasePassword", propertyNames);
    }
}
