using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;
using Modules.Operator.Diagnostics.Services;
using Modules.Setup.SupportReports;
using Shared.Exceptions;

namespace Api.IntegrationTests.Setup;

public sealed class SetupInstallationSupportReportEndpointContractTests
{
    [Fact]
    public async Task STARTUP_INSTALL_REL_01F_routes_are_authenticated_and_bounded()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(new DiagnosticsApiOptions());
        builder.Services.AddSingleton(new SetupInstallationSupportReportService(
            db: null!,
            diagnosticsOptions: null!,
            eventReader: null!,
            diagnosticHealthReader: null!,
            textRedactor: null!,
            dockerEvidenceReader: null!,
            runtimeContext: null!,
            configuration: null!,
            sizeLimiter: null!,
            timeProvider: TimeProvider.System,
            logger: null!));
        builder.Services.AddSingleton(new SetupInstallationSupportReportFormatter());

        await using var app = builder.Build();
        new SetupInstallationSupportReportEndpoints().AddRoutes(app);

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains(
                "support-report",
                StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(2, routes.Length);
        Assert.All(routes, route =>
        {
            var authorization = route.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .Where(metadata => metadata is not null)
                .ToArray();

            Assert.NotEmpty(authorization);

            var authenticationSchemes = authorization
                .SelectMany(metadata =>
                    (metadata.AuthenticationSchemes ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToHashSet(StringComparer.Ordinal);

            Assert.Contains(
                CookieAuthenticationDefaults.AuthenticationScheme,
                authenticationSchemes);
            Assert.Contains(
                IdentityConstants.ApplicationScheme,
                authenticationSchemes);
            Assert.Contains(
                route.Metadata.OfType<HttpMethodMetadata>(),
                metadata => metadata.HttpMethods.Contains("POST"));
        });
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01F_named_operator_without_platform_owner_is_denied_with_no_store()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(new DiagnosticsApiOptions());
        builder.Services.AddSingleton(new SetupInstallationSupportReportService(
            db: null!,
            diagnosticsOptions: null!,
            eventReader: null!,
            diagnosticHealthReader: null!,
            textRedactor: null!,
            dockerEvidenceReader: null!,
            runtimeContext: null!,
            configuration: null!,
            sizeLimiter: null!,
            timeProvider: TimeProvider.System,
            logger: null!));
        builder.Services.AddSingleton(new SetupInstallationSupportReportFormatter());

        await using var app = builder.Build();
        new SetupInstallationSupportReportEndpoints().AddRoutes(app);

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => string.Equals(
                candidate.RoutePattern.RawText,
                "/api/setup/support-report",
                StringComparison.Ordinal));

        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.Role, MemOperatorRoles.Operator)],
                    IdentityConstants.ApplicationScheme))
        };
        context.Request.Method = "POST";
        context.Request.Path = "/api/setup/support-report";
        context.Response.Body = new MemoryStream();

        var exception = await Assert.ThrowsAsync<MemProblemException>(async () =>
            await (endpoint.RequestDelegate
                ?? throw new InvalidOperationException("Support report endpoint has no request delegate."))(context));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.StatusCode);
        Assert.Equal("setup_support_report_forbidden", exception.Code);
        Assert.Contains("no-store", context.Response.Headers.CacheControl.ToString(), StringComparison.Ordinal);
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());
    }
}
