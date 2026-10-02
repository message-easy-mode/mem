using System.Text;
using System.Security.Claims;
using Api.IntegrationTests.Runtime;
using Carter;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.Integrations.Seq.Contracts;
using Modules.Shared.RuntimeImages;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Endpoints;
using Modules.Operator.Diagnostics.Services;
using Shared.Diagnostics;
using Shared.Exceptions;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsEndpointContractTests
{
    [Theory]
    [InlineData("/api/operator/diagnostics/overview", "GET", MemOperatorPolicies.ReadSafeStatus)]
    [InlineData("/api/operator/diagnostics/attention", "GET", MemOperatorPolicies.ReadSafeStatus)]
    [InlineData("/api/operator/diagnostics/incidents", "GET", MemOperatorPolicies.ReadSafeStatus)]
    [InlineData("/api/operator/diagnostics/incidents/{incidentId}", "GET", MemOperatorPolicies.ReadSafeStatus)]
    [InlineData("/api/operator/diagnostics/incidents/{incidentId}/acknowledge", "POST", MemOperatorPolicies.Operate)]
    [InlineData("/api/operator/diagnostics/incidents/{incidentId}/snooze", "POST", MemOperatorPolicies.Operate)]
    [InlineData("/api/operator/diagnostics/incidents/{incidentId}/resolve", "POST", MemOperatorPolicies.Operate)]
    [InlineData("/api/operator/diagnostics/incidents/{incidentId}/reopen", "POST", MemOperatorPolicies.Operate)]
    [InlineData("/api/operator/diagnostics/events", "GET", MemOperatorPolicies.Operate)]
    [InlineData("/api/operator/diagnostics/events/{eventId}", "GET", MemOperatorPolicies.Operate)]
    [InlineData("/api/operator/diagnostics/logging-health", "GET", MemOperatorPolicies.ReadSafeStatus)]
    [InlineData("/api/operator/diagnostics/seq", "GET", MemOperatorPolicies.Operate)]
    [InlineData("/api/operator/diagnostics/seq/ui-authority", "PUT", MemOperatorPolicies.ManagePlatform)]
    [InlineData("/api/operator/diagnostics/seq/bootstrap", "GET", MemOperatorPolicies.Operate)]
    [InlineData("/api/operator/diagnostics/seq/bootstrap/review", "POST", MemOperatorPolicies.ManagePlatform)]
    [InlineData("/api/operator/diagnostics/seq/bootstrap/execute", "POST", MemOperatorPolicies.ManagePlatform)]
    [InlineData("/api/operator/diagnostics/seq/bootstrap/operations/{operationId:guid}", "GET", MemOperatorPolicies.Operate)]
    [InlineData("/api/operator/diagnostics/seq/connect", "POST", MemOperatorPolicies.ManagePlatform)]
    [InlineData("/api/operator/diagnostics/portainer", "GET", MemOperatorPolicies.ManagePlatform)]
    [InlineData("/api/operator/diagnostics/portainer/incidents/{incidentId}/container", "GET", MemOperatorPolicies.ManagePlatform)]
    [InlineData("/api/operator/diagnostics/portainer/resources/{resourceKind}/{resourceId}", "GET", MemOperatorPolicies.ManagePlatform)]
    [InlineData("/api/operator/diagnostics/portainer/seq/container", "GET", MemOperatorPolicies.ManagePlatform)]
    [InlineData("/api/operator/diagnostics/seq/setup/review", "POST", MemOperatorPolicies.ManagePlatform)]
    [InlineData("/api/operator/diagnostics/seq/runtime/deploy", "POST", MemOperatorPolicies.ManagePlatform)]
    [InlineData("/api/operator/diagnostics/seq/runtime/start", "POST", MemOperatorPolicies.ManagePlatform)]
    [InlineData("/api/operator/diagnostics/seq/runtime/stop", "POST", MemOperatorPolicies.ManagePlatform)]
    [InlineData("/api/operator/diagnostics/seq/runtime/restart", "POST", MemOperatorPolicies.ManagePlatform)]
    [InlineData("/api/operator/diagnostics/seq/runtime/remove", "POST", MemOperatorPolicies.ManagePlatform)]
    [InlineData("/api/operator/diagnostics/seq/delivery", "POST", MemOperatorPolicies.ManagePlatform)]
    [InlineData("/api/operator/diagnostics/seq/delivery/verify", "POST", MemOperatorPolicies.ManagePlatform)]
    [InlineData("/api/operator/diagnostics/seq/health-check", "POST", MemOperatorPolicies.Operate)]
    [InlineData("/api/operator/diagnostics/self-test", "POST", MemOperatorPolicies.ManagePlatform)]
    [InlineData("/api/operator/diagnostics/incidents/{incidentId}/docker-evidence", "GET", MemOperatorPolicies.Operate)]
    [InlineData("/api/operator/diagnostics/incidents/{incidentId}/docker-evidence/refresh", "POST", MemOperatorPolicies.Operate)]
    [InlineData("/api/operator/diagnostics/support-report", "POST", MemOperatorPolicies.Operate)]
    public async Task Diagnostics_routes_declare_explicit_authority(
        string route,
        string method,
        string policy)
    {
        await using var application = BuildApplication();
        var endpoint = FindEndpoint(application, route, method);
        var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

        Assert.Contains(
            authorization,
            data => string.Equals(data.Policy, policy, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Attention_summary_is_no_store_bounded_and_safe_for_auditors()
    {
        await using var application = BuildApplication();
        await using var scope = application.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = Principal(MemOperatorRoles.Auditor)
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/operator/diagnostics/attention";
        context.Request.QueryString = new QueryString("?limit=5");
        context.Response.Body = new MemoryStream();

        var endpoint = FindEndpoint(
            application,
            "/api/operator/diagnostics/attention",
            HttpMethods.Get);
        await (endpoint.RequestDelegate
               ?? throw new InvalidOperationException("Diagnostics endpoint has no request delegate."))(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var json = await reader.ReadToEndAsync();
        Assert.Contains("\"state\":\"ready\"", json, StringComparison.Ordinal);
        Assert.Contains("\"items\":[]", json, StringComparison.Ordinal);
        Assert.DoesNotContain("workspacePath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Attention_summary_rejects_a_limit_above_the_bounded_contract()
    {
        await using var application = BuildApplication();
        await using var scope = application.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = Principal(MemOperatorRoles.Operator)
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/operator/diagnostics/attention";
        context.Request.QueryString = new QueryString("?limit=6");
        context.Response.Body = new MemoryStream();

        var endpoint = FindEndpoint(
            application,
            "/api/operator/diagnostics/attention",
            HttpMethods.Get);
        var exception = await Assert.ThrowsAsync<MemProblemException>(() =>
            (endpoint.RequestDelegate
             ?? throw new InvalidOperationException("Diagnostics endpoint has no request delegate."))(context));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal("diagnostics_attention_limit_invalid", exception.Code);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());
    }

    [Fact]
    public async Task Logging_health_is_no_store_and_hides_owner_only_paths_from_auditors()
    {
        await using var application = BuildApplication();
        await using var scope = application.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = Principal(MemOperatorRoles.Auditor)
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/operator/diagnostics/logging-health";
        context.Response.Body = new MemoryStream();

        var endpoint = FindEndpoint(
            application,
            "/api/operator/diagnostics/logging-health",
            HttpMethods.Get);
        await (endpoint.RequestDelegate
               ?? throw new InvalidOperationException("Diagnostics endpoint has no request delegate."))(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var json = await reader.ReadToEndAsync();
        Assert.DoesNotContain("/data/logs/private", json, StringComparison.Ordinal);
        Assert.DoesNotContain("https://seq.internal.example", json, StringComparison.Ordinal);
        Assert.Contains("safeEventStore", json, StringComparison.Ordinal);
        Assert.Contains("storage", json, StringComparison.Ordinal);
        Assert.Contains("availableBytes", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logging_health_exposes_bounded_path_and_seq_authority_only_to_platform_owner()
    {
        await using var application = BuildApplication();
        await using var scope = application.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = Principal(MemOperatorRoles.PlatformOwner)
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/operator/diagnostics/logging-health";
        context.Response.Body = new MemoryStream();

        var endpoint = FindEndpoint(
            application,
            "/api/operator/diagnostics/logging-health",
            HttpMethods.Get);
        await (endpoint.RequestDelegate
               ?? throw new InvalidOperationException("Diagnostics endpoint has no request delegate."))(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var json = await reader.ReadToEndAsync();

        Assert.Contains("/data/logs/private", json, StringComparison.Ordinal);
        Assert.Contains("https://seq.internal.example", json, StringComparison.Ordinal);
        Assert.DoesNotContain("seq-user", json, StringComparison.Ordinal);
        Assert.DoesNotContain("seq-password", json, StringComparison.Ordinal);
        Assert.Contains("canViewOwnerHealthFacts", json, StringComparison.Ordinal);
    }


    [Theory]
    [InlineData("/api/operator/diagnostics/seq", "GET")]
    [InlineData("/api/operator/diagnostics/seq/bootstrap", "GET")]
    [InlineData("/api/operator/diagnostics/seq/setup/review", "POST")]
    public async Task Seq_diagnostics_routes_are_no_store_and_do_not_serialize_secrets_or_internal_authority(
        string route,
        string method)
    {
        await using var application = BuildApplication();
        await using var scope = application.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = Principal(MemOperatorRoles.PlatformOwner)
        };
        context.Request.Method = method;
        context.Request.Path = route;
        context.Response.Body = new MemoryStream();

        var endpoint = FindEndpoint(application, route, method);
        await (endpoint.RequestDelegate
               ?? throw new InvalidOperationException("Diagnostics endpoint has no request delegate."))(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var json = await reader.ReadToEndAsync();
        Assert.DoesNotContain("http://seq:5341", json, StringComparison.Ordinal);
        Assert.DoesNotContain("MEM_SEQ_API_KEY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("MEM_SEQ_ADMIN_PASSWORD_HASH", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/data/seq", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Seq_bootstrap_review_is_no_store_and_secret_free()
    {
        await using var application = BuildApplication();
        await using var scope = application.Services.CreateAsyncScope();
        var body = Encoding.UTF8.GetBytes(
            "{\"acceptEula\":true,\"privateUiUrl\":null}");
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = Principal(MemOperatorRoles.PlatformOwner)
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/operator/diagnostics/seq/bootstrap/review";
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);
        context.Features.Set<IHttpRequestBodyDetectionFeature>(
            new CanHaveBodyRequestFeature());
        context.Response.Body = new MemoryStream();

        var endpoint = FindEndpoint(
            application,
            "/api/operator/diagnostics/seq/bootstrap/review",
            HttpMethods.Post);
        await (endpoint.RequestDelegate
               ?? throw new InvalidOperationException("Diagnostics endpoint has no request delegate."))(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var json = await reader.ReadToEndAsync();
        Assert.Contains("seq_bootstrap_review_", json, StringComparison.Ordinal);
        Assert.Contains("willPullDuringSetup", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/data/seq", json, StringComparison.Ordinal);
        Assert.DoesNotContain("admin-password-hash", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ingestion-api-key", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Seq_bootstrap_execution_rejects_an_oversized_secret_request_before_deserialization()
    {
        await using var application = BuildApplication();
        await using var scope = application.Services.CreateAsyncScope();
        var body = Encoding.UTF8.GetBytes(
            $"{{\"reviewId\":\"seq_bootstrap_review_{new string('a', 2100)}\",\"administratorPassword\":\"secret\",\"administratorPasswordConfirmation\":\"secret\"}}");
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = Principal(MemOperatorRoles.PlatformOwner)
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/operator/diagnostics/seq/bootstrap/execute";
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);
        context.Response.Body = new MemoryStream();

        var endpoint = FindEndpoint(
            application,
            "/api/operator/diagnostics/seq/bootstrap/execute",
            HttpMethods.Post);
        await (endpoint.RequestDelegate
               ?? throw new InvalidOperationException("Diagnostics endpoint has no request delegate."))(context);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var json = await reader.ReadToEndAsync();
        Assert.Contains("seq_bootstrap_request_too_large", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pipeline_self_test_is_no_store_and_writes_no_incident()
    {
        await using var application = BuildApplication();
        await using var scope = application.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = Principal(MemOperatorRoles.PlatformOwner)
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/operator/diagnostics/self-test";
        context.Response.Body = new MemoryStream();

        var endpoint = FindEndpoint(
            application,
            "/api/operator/diagnostics/self-test",
            HttpMethods.Post);
        await (endpoint.RequestDelegate
               ?? throw new InvalidOperationException("Diagnostics endpoint has no request delegate."))(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());

        var writer = scope.ServiceProvider.GetRequiredService<EndpointDiagnosticWriter>();
        var request = Assert.Single(writer.Requests);
        Assert.Equal(MemDiagnosticSeverities.Information, request.Severity);
        Assert.Equal("diagnostics.pipeline_self_test", request.EventCode);
        Assert.False(request.CreateIncident);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var json = await reader.ReadToEndAsync();
        Assert.Contains("\"status\":\"passed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"verificationId\":\"diag_verify_", json, StringComparison.Ordinal);
        Assert.DoesNotContain("incidentId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Support_report_rejects_an_oversized_request_before_deserialization()
    {
        await using var application = BuildApplication();
        await using var scope = application.Services.CreateAsyncScope();
        var body = Encoding.UTF8.GetBytes(
            $"{{\"incidentId\":\"inc_{new string('a', 5000)}\"}}");
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = Principal(MemOperatorRoles.Operator)
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/operator/diagnostics/support-report";
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);
        context.Response.Body = new MemoryStream();

        var endpoint = FindEndpoint(
            application,
            "/api/operator/diagnostics/support-report",
            HttpMethods.Post);
        var exception = await Assert.ThrowsAsync<MemProblemException>(() =>
            (endpoint.RequestDelegate
             ?? throw new InvalidOperationException("Diagnostics endpoint has no request delegate."))(context));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, exception.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());
        Assert.Equal(
            "diagnostics_support_report_request_too_large",
            exception.Code);
    }

    private static WebApplication BuildApplication()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(
                MemOperatorPolicies.RecentStepUp,
                policy => policy.RequireAssertion(_ => true));
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(new DiagnosticsApiOptions());
        builder.Services.AddSingleton(new SeqDiagnosticsOptions
        {
            SinkEnabled = true,
            IngestionUrl = "http://seq:5341",
            HealthUrl = "http://seq:80",
            UiUrl = "https://seq.internal.example/"
        });
        builder.Services.AddSingleton<ISeqHealthReader>(new FakeSeqHealth());
        builder.Services.AddSingleton<SeqSecretResolver>();
        builder.Services.AddSingleton<SeqBootstrapStateStore>();
        builder.Services.AddSingleton<SeqBootstrapReviewStore>();
        builder.Services.AddSingleton<ISeqBootstrapStateStore>(serviceProvider =>
            serviceProvider.GetRequiredService<SeqBootstrapStateStore>());
        builder.Services.AddSingleton<SeqEffectiveConfigurationProvider>();
        builder.Services.AddSingleton<ISeqDeliveryStateStore>(new FixedSeqDeliveryStateStore());
        builder.Services.AddSingleton(new SeqDeliveryProcessIdentity(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));
        builder.Services.AddSingleton(new SeqLoggingRuntimeState(
            SinkConfigured: true,
            WarningCode: null));
        builder.Services.AddSingleton<ISeqRuntimeStatusReader>(new FakeSeqRuntimeStatus());
        builder.Services.AddSingleton<IRuntimeImageInspector>(new FakeSeqImageInspector());
        builder.Services.AddSingleton<IMemLocalLogHealthReader>(new FakeLocalHealth());
        builder.Services.AddSingleton<IMemDiagnosticHealthReader>(new FakeStoreHealth());
        builder.Services.AddSingleton<EndpointDiagnosticWriter>();
        builder.Services.AddSingleton<IMemDiagnosticEventWriter>(serviceProvider =>
            serviceProvider.GetRequiredService<EndpointDiagnosticWriter>());
        builder.Services.AddSingleton<IMemDiagnosticEventReader>(serviceProvider =>
            new MatchingEndpointReader(
                serviceProvider.GetRequiredService<EndpointDiagnosticWriter>()));
        builder.Services.AddSingleton<IMemDockerEvidenceReader>(new UnavailableDockerEvidenceReader());
        builder.Services.AddSingleton<IMemOperatorAuditService>(new NoOpAudit());
        builder.Services.AddDbContext<MemDbContext>(options =>
            options.UseSqlite("Data Source=:memory:"));
        builder.Services.AddScoped<DiagnosticsCapabilityService>();
        builder.Services.AddScoped<DiagnosticsWorkspaceLinkBuilder>();
        builder.Services.AddScoped<DiagnosticsQueryParser>();
        builder.Services.AddScoped<DiagnosticsEventCollector>();
        builder.Services.AddScoped<DiagnosticsEventService>();
        builder.Services.AddScoped<DiagnosticsIncidentService>();
        // Registered so Minimal API binds the action-service parameter from DI.
        // This endpoint-contract host does not execute lifecycle mutations.
        builder.Services.AddScoped<DiagnosticsIncidentActionService>();
        builder.Services.AddScoped<DiagnosticsDockerEvidenceService>();
        builder.Services.AddScoped<DiagnosticsLoggingHealthService>();
        builder.Services.AddScoped<DiagnosticsActiveContextService>();
        builder.Services.AddScoped<DiagnosticsOverviewService>();
        builder.Services.AddScoped<DiagnosticsAttentionService>();
        builder.Services.AddScoped<SeqBootstrapStorageInspector>();
        builder.Services.AddScoped<DiagnosticsSeqService>();
        builder.Services.AddScoped<DiagnosticsSeqBootstrapService>();
        builder.Services.AddScoped<DiagnosticsSeqUiAuthorityService>();
        builder.Services.AddSingleton<SeqBootstrapOperationQueue>();
        builder.Services.AddScoped<DiagnosticsSeqBootstrapExecutionService>();
        builder.Services.AddScoped<DiagnosticsSeqLifecycleService>(_ => null!);
        builder.Services.AddScoped<DiagnosticsSeqConnectionService>(_ => null!);
        builder.Services.AddScoped<DiagnosticsSeqDeliveryVerificationService>(_ => null!);
        builder.Services.AddScoped<DiagnosticsPortainerService>(_ => null!);
        builder.Services.AddSingleton<DiagnosticsPipelineSelfTestService>();
        builder.Services.AddScoped<DiagnosticsSupportReportSizeLimiter>();
        builder.Services.AddScoped<DiagnosticsSupportReportService>();
        builder.Services.AddSingleton(TestRuntimeContext.Create(
            Path.Combine(
                Path.GetTempPath(),
                "mem-diagnostics-endpoint-runtime-context")));
        builder.Services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());

        var application = builder.Build();
        var module = new DiagnosticsEndpoints();
        Assert.IsAssignableFrom<ICarterModule>(module);
        module.AddRoutes(application);
        return application;
    }

    private static RouteEndpoint FindEndpoint(
        WebApplication application,
        string route,
        string method) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                string.Equals(candidate.RoutePattern.RawText, route, StringComparison.Ordinal) &&
                candidate.Metadata.OfType<HttpMethodMetadata>()
                    .Any(metadata => metadata.HttpMethods.Contains(method)));

    private static ClaimsPrincipal Principal(string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, role)
        ],
        "test"));

    private sealed class CanHaveBodyRequestFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    private sealed class EndpointDiagnosticWriter : IMemDiagnosticEventWriter
    {
        public List<MemDiagnosticWriteRequest> Requests { get; } = [];

        public Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new MemDiagnosticWriteResult(
                Stored: true,
                EventId: "evt_endpoint_self_test",
                IncidentId: null,
                WarningCode: null));
        }
    }

    private sealed class MatchingEndpointReader(EndpointDiagnosticWriter writer)
        : IMemDiagnosticEventReader
    {
        public Task<MemDiagnosticEventPage> QueryAsync(
            MemDiagnosticQuery query,
            CancellationToken cancellationToken = default)
        {
            var timestamp = DateTimeOffset.UtcNow;
            if (writer.Requests.Count == 0)
            {
                return Task.FromResult(new MemDiagnosticEventPage(
                    query.FromUtc ?? timestamp.AddHours(-1),
                    query.UntilUtc ?? timestamp,
                    query.PageSize ?? 1,
                    false,
                    Array.Empty<MemDiagnosticEvent>(),
                    null,
                    Array.Empty<string>()));
            }

            var request = writer.Requests.Single();
            return Task.FromResult(new MemDiagnosticEventPage(
                timestamp.AddHours(-1),
                timestamp,
                query.PageSize ?? 1,
                false,
                [new MemDiagnosticEvent(
                    SchemaVersion: 1,
                    EventId: query.EventId ?? "evt_endpoint_self_test",
                    TimestampUtc: timestamp,
                    Severity: request.Severity,
                    EventCode: request.EventCode,
                    Source: request.Source,
                    Feature: request.Feature,
                    Stage: request.Stage,
                    Message: request.Message,
                    IncidentId: null,
                    TraceId: null,
                    SpanId: null,
                    RequestId: null,
                    CorrelationId: request.Context?.CorrelationId,
                    OperationId: null,
                    Resource: null,
                    Expected: null,
                    Observed: null,
                    Details: null,
                    Exception: null,
                    SuggestedAction: null,
                    Retryable: false,
                    RedactionsApplied: false,
                    Truncated: false)],
                null,
                []));
        }
    }

    private sealed class UnavailableDockerEvidenceReader : IMemDockerEvidenceReader
    {
        public Task<MemDockerEvidenceReadResult> ReadForIncidentAsync(
            string incidentId,
            IReadOnlyCollection<MemDiagnosticEvent> incidentEvents,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MemDockerEvidenceReadResult(
                false,
                null,
                MemDiagnosticCodes.DockerEvidenceResourceNotResolved,
                [MemDiagnosticCodes.DockerEvidenceResourceNotResolved]));
    }

    private sealed class FakeSeqHealth : ISeqHealthReader
    {
        public SeqHealthSnapshot GetHealth() => new(
            "ready",
            SinkConfigured: true,
            Reachable: true,
            LastCheckedAtUtc: DateTimeOffset.UtcNow,
            LastSuccessAtUtc: DateTimeOffset.UtcNow,
            WarningCode: null);
    }

    private sealed class FakeSeqRuntimeStatus : ISeqRuntimeStatusReader
    {
        public Task<SeqStatusResponse> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SeqStatusResponse(
                "seq",
                "mem-seq",
                "2026.1.17044",
                null,
                null,
                Exists: false,
                Running: false,
                State: null,
                Image: null,
                UsesApprovedRuntime: false,
                Warnings: [],
                Managed: false,
                OwnershipState: "absent",
                WarningCode: null));
    }

    private sealed class FakeSeqImageInspector : IRuntimeImageInspector
    {
        public Task<RuntimeImageInspection?> InspectAsync(
            string immutableReference,
            CancellationToken cancellationToken) => Task.FromResult<RuntimeImageInspection?>(null);

        public Task PullAsync(
            string immutableReference,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedSeqDeliveryStateStore : ISeqDeliveryStateStore
    {
        public SeqDeliveryState GetState() => new(
            EffectiveEnabled: true,
            DesiredEnabled: true,
            RestartRequired: false,
            UpdatedAtUtc: null,
            WarningCode: null);

        public Task<SeqDeliveryState> SetDesiredAsync(
            bool enabled,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FakeLocalHealth : IMemLocalLogHealthReader
    {
        public MemLocalLogHealth GetHealth() => new(
            true,
            "ready",
            true,
            true,
            "/data/logs/private/mem-control-plane-.clef",
            DateTimeOffset.UtcNow,
            2,
            2048,
            0,
            null,
            new MemStorageCapacityHealth(
                "ready",
                AvailableBytes: 10_000_000,
                TotalBytes: 20_000_000,
                WarningCode: null));
    }

    private sealed class FakeStoreHealth : IMemDiagnosticHealthReader
    {
        public MemDiagnosticStoreHealth GetHealth() => new(
            true,
            "ready",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            2,
            0,
            0,
            null,
            null,
            DateTimeOffset.UtcNow,
            0,
            0,
            null,
            new MemStorageCapacityHealth(
                "ready",
                AvailableBytes: 10_000_000,
                TotalBytes: 20_000_000,
                WarningCode: null),
            HasEverRecordedEvent: true);
    }

    private sealed class NoOpAudit : IMemOperatorAuditService
    {
        public Task WriteAsync(
            MemOperatorAuditEventWrite auditEvent,
            CancellationToken ct = default) => Task.CompletedTask;
    }
}
