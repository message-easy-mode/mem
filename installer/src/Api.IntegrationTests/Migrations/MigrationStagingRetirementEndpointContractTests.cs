using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HostAgent.Runtime.Migrations.Staging.Retirement;
using HostAgent.Runtime.Migrations.Staging.Retirement.Endpoints;
using HostAgent.Runtime.Services.TemporaryStaging;
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

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationStagingRetirementEndpointContractTests
{
    private const string Route = "/api/operator/migrations/sessions/{migrationId}/staging-runs/{stagingRunId}/retirement";

    [Fact]
    public async Task Review_and_acceptance_share_one_session_scoped_route_and_migration_authorization()
    {
        await using var f = await Fixture.CreateAsync(true);
        var endpoints = Endpoints(f.Application);
        Assert.Equal(2, endpoints.Length);
        Assert.All(endpoints, endpoint => Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            rule => rule.Policy == MemOperatorPolicies.MigrationIntakeOperate));
        Assert.Equal(new[] { "GET", "POST" }, endpoints.SelectMany(e => e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods).OrderBy(x=>x).ToArray());
    }

    [Theory]
    [InlineData("GET", true, 404)]
    [InlineData("POST", false, 403)]
    [InlineData("POST", true, 404)]
    public async Task Responses_are_no_store_and_step_up_precedes_admission(string method,bool allowStepUp,int expectedStatus)
    {
        await using var f = await Fixture.CreateAsync(allowStepUp);
        await using var scope = f.Application.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices=scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier,Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role,MemOperatorRoles.PlatformOwner)
            },IdentityConstants.ApplicationScheme)) };
        context.Request.Method=method;
        context.Request.Path="/api/operator/migrations/sessions/mig_missing/staging-runs/mst_missing/retirement";
        context.Request.RouteValues["migrationId"]="mig_missing";
        context.Request.RouteValues["stagingRunId"]="mst_missing";
        context.Response.Body=new MemoryStream();
        if(method=="POST")
        {
            var body=Encoding.UTF8.GetBytes("{\"reviewFingerprint\":\"never-echo-this-request-marker\",\"confirmRetirement\":true}");
            context.Request.ContentType="application/json";
            context.Request.ContentLength=body.Length;
            context.Request.Body=new MemoryStream(body);
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new HasBody());
        }
        var endpoint=Endpoints(f.Application).Single(e=>e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method));
        await endpoint.RequestDelegate!(context);
        Assert.Equal(expectedStatus,context.Response.StatusCode);
        Assert.Equal("no-store",context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache",context.Response.Headers.Pragma.ToString());
        context.Response.Body.Position=0;
        var output=await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("never-echo-this-request-marker",output);
        if(!allowStepUp) Assert.Contains("step_up_required",output);
        Assert.Equal(0,f.Runtime.Calls);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<MemDbContext>().MigrationStagingRetirements.ToArrayAsync());
    }

    private static RouteEndpoint[] Endpoints(WebApplication app) => ((IEndpointRouteBuilder)app).DataSources.SelectMany(x=>x.Endpoints)
        .OfType<RouteEndpoint>().Where(x=>x.RoutePattern.RawText?.TrimEnd('/')==Route).ToArray();
    private sealed class HasBody : IHttpRequestBodyDetectionFeature { public bool CanHaveBody => true; }
    private sealed class Fixture : IAsyncDisposable
    {
        public WebApplication Application { get; }
        public NoopRuntime Runtime { get; }
        private readonly string databasePath;
        private Fixture(WebApplication application,NoopRuntime runtime,string path) {Application=application;Runtime=runtime;databasePath=path;}
        public static async Task<Fixture> CreateAsync(bool allow)
        {
            var path=Path.Combine(Path.GetTempPath(),"mem-retirement-endpoint-"+Guid.NewGuid().ToString("N")+".db");
            var builder=WebApplication.CreateBuilder();
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton<IAuthorizationService>(new Authorization(allow));
            builder.Services.AddDbContext<MemDbContext>(options=>options.UseSqlite("Data Source="+path));
            builder.Services.AddSingleton(TimeProvider.System);
            var runtime=new NoopRuntime();
            builder.Services.AddSingleton<IMigrationStagingRetirementRuntime>(runtime);
            builder.Services.AddSingleton<ITemporaryStagingInventorySource,UnusedSource>();
            builder.Services.AddScoped<MigrationStagingRetirementService>();
            var app=builder.Build();
            new MigrationStagingRetirementEndpoints().AddRoutes(app);
            await using(var scope=app.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<MemDbContext>().Database.EnsureCreatedAsync();
            return new Fixture(app,runtime,path);
        }
        public async ValueTask DisposeAsync() { await Application.DisposeAsync(); if(File.Exists(databasePath)) File.Delete(databasePath); }
    }
    private sealed class Authorization(bool allow) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user,object? resource,string policy) => Task.FromResult(
            policy==MemOperatorPolicies.RecentStepUp && !allow ? AuthorizationResult.Failed() : AuthorizationResult.Success());
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user,object? resource,IEnumerable<IAuthorizationRequirement> requirements) => Task.FromResult(allow ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }
    private sealed class UnusedSource : ITemporaryStagingInventorySource
    {
        public Task<StagingInventorySnapshot> ReadAsync(CancellationToken ct) => throw new InvalidOperationException("No admitted staging in this contract fixture.");
    }
    private sealed class NoopRuntime : IMigrationStagingRetirementRuntime
    {
        public int Calls { get; private set; }
        public Task<MigrationStagingRetirementInspection> InspectAsync(string stagingId,string candidateId,CancellationToken ct) { Calls++; throw new InvalidOperationException("No runtime expected."); }
        public Task RetireAsync(MigrationStagingRetirementPlan plan,Func<string,CancellationToken,Task> progress,CancellationToken ct) { Calls++; throw new InvalidOperationException("No runtime expected."); }
    }
}
