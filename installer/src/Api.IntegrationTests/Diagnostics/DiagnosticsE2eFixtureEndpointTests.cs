using System.Security.Claims;
using Carter;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modules.Auth.Identity;
using Modules.Operator.Diagnostics.Endpoints;
using Modules.Operator.Diagnostics.Services;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsE2eFixtureEndpointTests
{
    private const string Route = "/api/operator/diagnostics/test-fixture/incident";

    [Fact]
    public async Task Fixture_route_is_absent_in_production_even_when_enabled()
    {
        await using var application = BuildApplication(Environments.Production, enabled: true);

        Assert.Null(FindEndpointOrDefault(application));
    }

    [Fact]
    public async Task Fixture_route_is_absent_in_development_until_explicitly_enabled()
    {
        await using var application = BuildApplication(Environments.Development, enabled: false);

        Assert.Null(FindEndpointOrDefault(application));
    }

    [Fact]
    public async Task Enabled_development_fixture_is_owner_authorized_no_store_and_writes_one_safe_incident()
    {
        var writer = new RecordingWriter();
        await using var application = BuildApplication(
            Environments.Development,
            enabled: true,
            writer);
        await using var scope = application.Services.CreateAsyncScope();
        var endpoint = FindEndpointOrDefault(application)
            ?? throw new InvalidOperationException("Fixture endpoint was not mapped.");

        var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        Assert.Contains(
            authorization,
            metadata => string.Equals(
                metadata.Policy,
                MemOperatorPolicies.ManagePlatform,
                StringComparison.Ordinal));

        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = Principal(MemOperatorRoles.PlatformOwner)
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = Route;
        context.Request.Headers["X-Correlation-ID"] = "diag-release-proof";
        context.Response.Body = new MemoryStream();

        await (endpoint.RequestDelegate
               ?? throw new InvalidOperationException("Fixture endpoint has no request delegate."))(context);

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());
        var request = Assert.Single(writer.Requests);
        Assert.True(request.CreateIncident);
        Assert.Equal("diagnostics.release_proof.failure", request.EventCode);
        Assert.Equal("diagnostics", request.Resource?.Kind);
        Assert.Equal("release-proof", request.Resource?.Id);
        Assert.Equal("/diagnostics/logs", request.Resource?.WorkspacePath);
        Assert.DoesNotContain("password", request.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplication BuildApplication(
        string environmentName,
        bool enabled,
        RecordingWriter? writer = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName
        });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(new DiagnosticsE2eFixtureOptions
        {
            Enabled = enabled
        });
        var recordingWriter = writer ?? new RecordingWriter();
        builder.Services.AddSingleton(recordingWriter);
        builder.Services.AddSingleton<IMemDiagnosticEventWriter>(recordingWriter);

        var application = builder.Build();
        var module = new DiagnosticsE2eFixtureEndpoints();
        Assert.IsAssignableFrom<ICarterModule>(module);
        module.AddRoutes(application);
        return application;
    }

    private static RouteEndpoint? FindEndpointOrDefault(WebApplication application) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SingleOrDefault(candidate =>
                string.Equals(candidate.RoutePattern.RawText, Route, StringComparison.Ordinal) &&
                candidate.Metadata.OfType<HttpMethodMetadata>()
                    .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Post)));

    private static ClaimsPrincipal Principal(string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, role)
        ],
        "test"));

    private sealed class RecordingWriter : IMemDiagnosticEventWriter
    {
        public List<MemDiagnosticWriteRequest> Requests { get; } = [];

        public Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new MemDiagnosticWriteResult(
                Stored: true,
                EventId: "evt_release_proof",
                IncidentId: "inc_release_proof",
                WarningCode: null));
        }
    }
}
