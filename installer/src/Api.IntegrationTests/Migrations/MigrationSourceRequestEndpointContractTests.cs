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

public sealed class MigrationSourceRequestEndpointContractTests
{
    [Fact]
    public async Task Source_request_route_is_session_and_purpose_keyed_get_only()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
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
                "/api/operator/migrations/sessions/{migrationId}/package-revisions/{purpose}/source-request");

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
    public void Source_request_browser_contract_exposes_no_private_identity_or_host_path()
    {
        var propertyNames = typeof(MigrationSourceRequestDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("ProtectedAgeIdentity", propertyNames);
        Assert.DoesNotContain("AgeIdentity", propertyNames);
        Assert.DoesNotContain("HostPath", propertyNames);
        Assert.DoesNotContain("ArchivePath", propertyNames);
        Assert.DoesNotContain("DatabasePassword", propertyNames);
    }
}
