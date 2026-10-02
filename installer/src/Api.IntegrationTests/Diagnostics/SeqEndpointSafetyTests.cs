using System.Text.Json;
using Carter;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;
using Modules.Integrations.Seq.Endpoints;
using Modules.Integrations.Seq.Services;

namespace Api.IntegrationTests.Diagnostics;

public sealed class SeqEndpointSafetyTests
{
    [Theory]
    [InlineData("/api/operator/services/seq/plan")]
    [InlineData("/api/operator/services/seq/")]
    [InlineData("/api/operator/services/seq/start")]
    [InlineData("/api/operator/services/seq/deploy")]
    [InlineData("/api/operator/services/seq/stop")]
    [InlineData("/api/operator/services/seq/remove")]
    public async Task Retained_seq_routes_require_platform_owner_authority(string route)
    {
        await using var application = BuildApplication();
        var endpoint = FindEndpoint(application, route);

        var authorization = endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>();

        Assert.Contains(
            authorization,
            metadata => string.Equals(
                metadata.Policy,
                MemOperatorPolicies.ManagePlatform,
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/api/operator/services/seq/start")]
    [InlineData("/api/operator/services/seq/deploy")]
    [InlineData("/api/operator/services/seq/stop")]
    [InlineData("/api/operator/services/seq/remove")]
    public async Task Seq_mutations_fail_closed_while_optional_management_is_disabled(
        string route)
    {
        await using var application = BuildApplication();
        await using var scope = application.Services.CreateAsyncScope();

        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = route;
        context.Response.Body = new MemoryStream();

        var endpoint = FindEndpoint(application, route);
        var requestDelegate = endpoint.RequestDelegate
            ?? throw new InvalidOperationException("Seq endpoint has no request delegate.");

        await requestDelegate(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(
            "seq_management_disabled",
            body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void Seq_is_optional_and_disabled_by_default()
    {
        var options = new SeqDiagnosticsOptions();

        Assert.False(options.SinkEnabled);
        Assert.False(options.ManagementEnabled);
        Assert.Equal("http://seq:5341", options.IngestionUrl);
        Assert.Equal("http://seq:80", options.HealthUrl);
        Assert.Null(options.UiUrl);
        Assert.False(options.AllowOperationalPull);
    }

    private static WebApplication BuildApplication()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(new SeqDiagnosticsOptions());
        builder.Services.AddScoped<SeqRuntimeService>();

        var application = builder.Build();
        var module = new SeqEndpoints();
        Assert.IsAssignableFrom<ICarterModule>(module);
        module.AddRoutes(application);
        return application;
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
