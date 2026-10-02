using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HostAgent.Commands;
using HostAgent.Endpoints;
using HostAgent.Runtime.Backups.AdvancedCutover.Candidate.Endpoints;
using HostAgent.Runtime.Backups.AdvancedCutover.Execution.Catalog.Endpoints;
using HostAgent.Runtime.Backups.AdvancedCutover.Retirement.Endpoints;
using HostAgent.Runtime.Backups.Catalog.Endpoints;
using HostAgent.Runtime.Backups.StandardRecreate.Cleanup.Endpoints;
using HostAgent.Runtime.Backups.StandardRecreate.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modules.Auth.Endpoints;
using Modules.Auth.Identity;

namespace Api.IntegrationTests.Security;

/// <summary>
/// Direct route coverage for SEC-AUTH-06B protection points. These tests
/// intentionally replace authorization with a deterministic rejected result and
/// omit destructive/governance services: a route must request RecentStepUp and
/// return the safe refusal before it can resolve or invoke those services.
/// SEC-AUTH-06A separately covers the real Identity-backed grant semantics.
/// </summary>
public sealed class MemOperatorHighRiskActionStepUpEnforcementTests
{
    [Fact]
    public async Task SEC_AUTH_06B_refuses_permanent_catalog_delete_without_a_recent_step_up_grant()
    {
        var authorization = new RecordingAuthorizationService(
            AuthorizationResult.Failed());

        var builder = WebApplication.CreateBuilder();
        builder.Services.Replace(
            ServiceDescriptor.Singleton<IAuthorizationService>(authorization));

        await using var application = builder.Build();
        new HostAgentBackupCatalogLifecycleEndpoints().AddRoutes(application);

        var endpoint = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                candidate.RoutePattern.RawText ==
                    "/internal/host-agent/backups/catalog/{catalogEntryId}" &&
                candidate.Metadata.OfType<HttpMethodMetadata>()
                    .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Delete)));

        var context = new DefaultHttpContext
        {
            RequestServices = application.Services,
            User = CreatePlatformOwnerPrincipal()
        };

        context.Request.Method = HttpMethods.Delete;
        context.Request.Path = "/internal/host-agent/backups/catalog/catalog-1";
        context.Request.RouteValues["catalogEntryId"] = "catalog-1";
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal(MemOperatorPolicies.RecentStepUp, authorization.LastPolicyName);

        context.Response.Body.Position = 0;

        var response = await JsonSerializer.DeserializeAsync<HostAgentErrorResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(response);
        Assert.Equal("step_up_required", response!.Error);
        Assert.DoesNotContain("catalog-1", response.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SEC_AUTH_06B_refuses_standard_recreate_without_a_recent_step_up_grant()
    {
        var authorization = new RecordingAuthorizationService(
            AuthorizationResult.Failed());

        var builder = WebApplication.CreateBuilder();
        builder.Services.Replace(
            ServiceDescriptor.Singleton<IAuthorizationService>(authorization));

        await using var application = builder.Build();
        new HostAgentStandardRecreateEndpoints().AddRoutes(application);

        var endpoint = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                candidate.RoutePattern.RawText ==
                    "/internal/host-agent/backups/restores/{restoreSessionId}/standard-recreate" &&
                candidate.Metadata.OfType<HttpMethodMetadata>()
                    .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Post)));

        var context = new DefaultHttpContext
        {
            RequestServices = application.Services,
            User = CreatePlatformOwnerPrincipal()
        };

        context.Request.Method = HttpMethods.Post;
        context.Request.Path =
            "/internal/host-agent/backups/restores/restore-high-risk-1/standard-recreate";
        context.Request.RouteValues["restoreSessionId"] = "restore-high-risk-1";
        context.Request.QueryString = new QueryString(
            "?targetStackSlug=restored-stack" +
            "&elementHost=chat.restored.example.test" +
            "&executeProductionRecreate=true" +
            "&acknowledgeCreatesRealStack=true" +
            "&acknowledgeMutatesProductionPostgres=true" +
            "&acknowledgeMutatesNpmRoutes=true" +
            "&acknowledgeNoAutomaticRollback=true");
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal(MemOperatorPolicies.RecentStepUp, authorization.LastPolicyName);

        context.Response.Body.Position = 0;

        var response = await JsonSerializer.DeserializeAsync<HostAgentErrorResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(response);
        Assert.Equal("step_up_required", response!.Error);
        Assert.DoesNotContain(
            "restore-high-risk-1",
            response.Detail ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SEC_AUTH_06B_refuses_private_production_candidate_creation_without_a_recent_step_up_grant()
    {
        var authorization = new RecordingAuthorizationService(
            AuthorizationResult.Failed());

        var builder = WebApplication.CreateBuilder();
        builder.Services.Replace(
            ServiceDescriptor.Singleton<IAuthorizationService>(authorization));

        await using var application = builder.Build();
        new HostAgentCatalogProductionCandidateEndpoints().AddRoutes(application);

        var endpoint = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                candidate.RoutePattern.RawText ==
                    "/internal/host-agent/backups/catalog/{catalogEntryId}/production-restore/candidates/recreate-private" &&
                candidate.Metadata.OfType<HttpMethodMetadata>()
                    .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Post)));

        const string catalogEntryId = "catalog-private-candidate-high-risk-1";
        const string targetStackSlug = "candidate-high-risk-stack";

        var body = Encoding.UTF8.GetBytes(
            $$"""
            {
              "keepOnFailure": false,
              "postgresImage": null,
              "synapseImage": null,
              "elementImage": null,
              "targetStackSlug": "{{targetStackSlug}}",
              "restoreMode": "recreate-production"
            }
            """);

        var context = new DefaultHttpContext
        {
            RequestServices = application.Services,
            User = CreatePlatformOwnerPrincipal()
        };

        context.Request.Method = HttpMethods.Post;
        context.Request.Path =
            $"/internal/host-agent/backups/catalog/{catalogEntryId}/production-restore/candidates/recreate-private";
        context.Request.RouteValues["catalogEntryId"] = catalogEntryId;
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        context.Features.Set<IHttpRequestBodyDetectionFeature>(
            new CanHaveBodyRequestFeature());
        context.Request.Body = new MemoryStream(body);
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal(MemOperatorPolicies.RecentStepUp, authorization.LastPolicyName);

        context.Response.Body.Position = 0;

        var response = await JsonSerializer.DeserializeAsync<HostAgentErrorResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(response);
        Assert.Equal("step_up_required", response!.Error);
        Assert.DoesNotContain(catalogEntryId, response.Detail ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(targetStackSlug, response.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SEC_AUTH_06B_refuses_public_cutover_execution_without_a_recent_step_up_grant()
    {
        var authorization = new RecordingAuthorizationService(
            AuthorizationResult.Failed());

        var builder = WebApplication.CreateBuilder();
        builder.Services.Replace(
            ServiceDescriptor.Singleton<IAuthorizationService>(authorization));

        await using var application = builder.Build();
        new HostAgentCatalogPublicCutoverExecutionEndpoints().AddRoutes(application);

        var endpoint = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                candidate.RoutePattern.RawText ==
                    "/internal/host-agent/backups/catalog/{catalogEntryId}/production-restore/cutover-executions/execute" &&
                candidate.Metadata.OfType<HttpMethodMetadata>()
                    .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Post)));

        const string catalogEntryId = "catalog-cutover-high-risk-1";
        const string confirmationId = "confirmation-high-risk-1";
        const string candidateId = "candidate-high-risk-1";
        const string oldRuntimeStackSlug = "old-production-stack";

        var body = Encoding.UTF8.GetBytes(
            $$"""
            {
              "confirmationId": "{{confirmationId}}",
              "candidateId": "{{candidateId}}",
              "oldRuntimeStackSlug": "{{oldRuntimeStackSlug}}",
              "operator": null,
              "note": null,
              "execute": true,
              "acknowledgeFinalApproval": true,
              "acknowledgeFinalBackupWillBeCaptured": true,
              "acknowledgeOldRuntimeWillBeStopped": true,
              "acknowledgePublicRouteMutation": true,
              "acknowledgeManualRollback": true,
              "gateEvaluationOnly": false
            }
            """);

        var context = new DefaultHttpContext
        {
            RequestServices = application.Services,
            User = CreatePlatformOwnerPrincipal()
        };

        context.Request.Method = HttpMethods.Post;
        context.Request.Path =
            $"/internal/host-agent/backups/catalog/{catalogEntryId}/production-restore/cutover-executions/execute";
        context.Request.RouteValues["catalogEntryId"] = catalogEntryId;
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        context.Features.Set<IHttpRequestBodyDetectionFeature>(
            new CanHaveBodyRequestFeature());
        context.Request.Body = new MemoryStream(body);
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal(MemOperatorPolicies.RecentStepUp, authorization.LastPolicyName);

        context.Response.Body.Position = 0;

        var response = await JsonSerializer.DeserializeAsync<HostAgentErrorResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(response);
        Assert.Equal("step_up_required", response!.Error);
        Assert.DoesNotContain(catalogEntryId, response.Detail ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(confirmationId, response.Detail ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(candidateId, response.Detail ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(oldRuntimeStackSlug, response.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SEC_AUTH_06B_refuses_failed_standard_recreate_cleanup_without_a_recent_step_up_grant()
    {
        var authorization = new RecordingAuthorizationService(
            AuthorizationResult.Failed());

        var builder = WebApplication.CreateBuilder();
        builder.Services.Replace(
            ServiceDescriptor.Singleton<IAuthorizationService>(authorization));

        await using var application = builder.Build();
        new HostAgentFailedStandardRecreateCleanupEndpoints().AddRoutes(application);

        var endpoint = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                candidate.RoutePattern.RawText ==
                    "/internal/host-agent/backups/standard-recreate/runs/{recreateId}/failed-cleanup/execute" &&
                candidate.Metadata.OfType<HttpMethodMetadata>()
                    .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Post)));

        const string recreateId = "failed-recreate-cleanup-high-risk-1";
        const string operatorName = "owner.cleanup-high-risk-test";

        var context = new DefaultHttpContext
        {
            RequestServices = application.Services,
            User = CreatePlatformOwnerPrincipal()
        };

        context.Request.Method = HttpMethods.Post;
        context.Request.Path =
            $"/internal/host-agent/backups/standard-recreate/runs/{recreateId}/failed-cleanup/execute";
        context.Request.RouteValues["recreateId"] = recreateId;
        context.Request.QueryString = new QueryString(
            $"?operatorName={Uri.EscapeDataString(operatorName)}&acknowledgeCleanup=true");
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal(MemOperatorPolicies.RecentStepUp, authorization.LastPolicyName);

        context.Response.Body.Position = 0;

        var response = await JsonSerializer.DeserializeAsync<HostAgentErrorResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(response);
        Assert.Equal("step_up_required", response!.Error);
        Assert.DoesNotContain(recreateId, response.Detail ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(operatorName, response.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SEC_AUTH_06B_refuses_advanced_candidate_retirement_execution_without_a_recent_step_up_grant()
    {
        var authorization = new RecordingAuthorizationService(
            AuthorizationResult.Failed());

        var builder = WebApplication.CreateBuilder();
        builder.Services.Replace(
            ServiceDescriptor.Singleton<IAuthorizationService>(authorization));

        await using var application = builder.Build();
        new HostAgentCandidateRetirementEndpoints().AddRoutes(application);

        var endpoint = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                candidate.RoutePattern.RawText ==
                    "/internal/host-agent/backups/advanced-cutover/retirement/standard-recreate-runs/{recreateId}/execute" &&
                candidate.Metadata.OfType<HttpMethodMetadata>()
                    .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Post)));

        const string recreateId = "advanced-retirement-high-risk-1";
        const string candidateId = "candidate-retirement-high-risk-1";

        var context = new DefaultHttpContext
        {
            RequestServices = application.Services,
            User = CreatePlatformOwnerPrincipal()
        };

        context.Request.Method = HttpMethods.Post;
        context.Request.Path =
            $"/internal/host-agent/backups/advanced-cutover/retirement/standard-recreate-runs/{recreateId}/execute";
        context.Request.RouteValues["recreateId"] = recreateId;
        context.Request.QueryString = new QueryString(
            $"?candidateId={Uri.EscapeDataString(candidateId)}" +
            "&retirementMode=operator-confirmed" +
            "&acknowledgeRetireCandidate=true");
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal(MemOperatorPolicies.RecentStepUp, authorization.LastPolicyName);

        context.Response.Body.Position = 0;

        var response = await JsonSerializer.DeserializeAsync<HostAgentErrorResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(response);
        Assert.Equal("step_up_required", response!.Error);
        Assert.DoesNotContain(recreateId, response.Detail ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(candidateId, response.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SEC_AUTH_06B_refuses_runtime_stack_destroy_without_a_recent_step_up_grant()
    {
        var authorization = new RecordingAuthorizationService(
            AuthorizationResult.Failed());

        var builder = WebApplication.CreateBuilder();
        builder.Services.Replace(
            ServiceDescriptor.Singleton<IAuthorizationService>(authorization));

        await using var application = builder.Build();
        new HostAgentRuntimeStacksEndpoint().AddRoutes(application);

        var endpoint = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                candidate.RoutePattern.RawText ==
                    "/internal/host-agent/runtime-stacks/{slugOrId}/destroy" &&
                candidate.Metadata.OfType<HttpMethodMetadata>()
                    .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Post)));

        const string stackSlug = "stack-destroy-high-risk-1";

        var body = Encoding.UTF8.GetBytes(
            """
            {
              "removeContainers": true,
              "removeRoutes": true,
              "removeDatabase": false,
              "removeFiles": false,
              "force": true
            }
            """);

        var context = new DefaultHttpContext
        {
            RequestServices = application.Services,
            User = CreatePlatformOwnerPrincipal()
        };

        context.Request.Method = HttpMethods.Post;
        context.Request.Path = $"/internal/host-agent/runtime-stacks/{stackSlug}/destroy";
        context.Request.RouteValues["slugOrId"] = stackSlug;
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        context.Features.Set<IHttpRequestBodyDetectionFeature>(
            new CanHaveBodyRequestFeature());
        context.Request.Body = new MemoryStream(body);
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal(MemOperatorPolicies.RecentStepUp, authorization.LastPolicyName);

        context.Response.Body.Position = 0;

        var response = await JsonSerializer.DeserializeAsync<HostAgentErrorResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(response);
        Assert.Equal("step_up_required", response!.Error);
        Assert.DoesNotContain(stackSlug, response.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SEC_AUTH_06B_refuses_every_operator_governance_mutation_without_a_recent_step_up_grant()
    {
        var authorization = new RecordingAuthorizationService(
            AuthorizationResult.Failed());

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        // This direct endpoint test isolates enforcement wiring. SEC-AUTH-06A
        // separately proves the real Identity-backed requirement fails until a
        // session-bound grant is issued. Replacing the service guarantees every
        // route below is observed to request the RecentStepUp policy before it
        // can resolve or call its governance services.
        builder.Services.Replace(
            ServiceDescriptor.Singleton<IAuthorizationService>(authorization));

        await using var application = builder.Build();
        new MemOperatorAdministrationEndpoints().AddRoutes(application);

        var operatorId = Guid.NewGuid();
        var routes = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        var cases = new[]
        {
            new GovernanceRouteCase(
                HttpMethods.Post,
                "/api/security/operators",
                "/api/security/operators",
                "{\"username\":\"pending.operator\",\"email\":null,\"roles\":[\"operator\"]}"),
            new GovernanceRouteCase(
                HttpMethods.Put,
                "/api/security/operators/{operatorId:guid}/enabled",
                $"/api/security/operators/{operatorId:D}/enabled",
                "{\"isEnabled\":false}"),
            new GovernanceRouteCase(
                HttpMethods.Put,
                "/api/security/operators/{operatorId:guid}/roles",
                $"/api/security/operators/{operatorId:D}/roles",
                "{\"roles\":[\"auditor\"]}"),
            new GovernanceRouteCase(
                HttpMethods.Post,
                "/api/security/operators/{operatorId:guid}/revoke-sessions",
                $"/api/security/operators/{operatorId:D}/revoke-sessions",
                null),
            new GovernanceRouteCase(
                HttpMethods.Post,
                "/api/security/operators/{operatorId:guid}/enrollment-grants",
                $"/api/security/operators/{operatorId:D}/enrollment-grants",
                null)
        };

        foreach (var routeCase in cases)
        {
            var endpoint = routes.Single(candidate =>
                string.Equals(
                    NormalizeRouteTemplate(candidate.RoutePattern.RawText),
                    NormalizeRouteTemplate(routeCase.Template),
                    StringComparison.Ordinal) &&
                candidate.Metadata.OfType<HttpMethodMetadata>()
                    .Any(metadata => metadata.HttpMethods.Contains(routeCase.Method)));

            var context = new DefaultHttpContext
            {
                RequestServices = application.Services,
                User = CreatePlatformOwnerPrincipal()
            };

            context.Request.Method = routeCase.Method;
            context.Request.Path = routeCase.Path;
            context.Response.Body = new MemoryStream();

            if (routeCase.Body is not null)
            {
                var body = Encoding.UTF8.GetBytes(routeCase.Body);
                context.Request.ContentType = "application/json";
                context.Request.ContentLength = body.Length;
                context.Features.Set<IHttpRequestBodyDetectionFeature>(
                    new CanHaveBodyRequestFeature());
                context.Request.Body = new MemoryStream(body);
            }

            if (routeCase.Template.Contains("{operatorId:guid}", StringComparison.Ordinal))
            {
                context.Request.RouteValues["operatorId"] = operatorId.ToString("D");
            }

            await endpoint.RequestDelegate(context);

            Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
            Assert.Contains("no-store", context.Response.Headers.CacheControl.ToString(), StringComparison.Ordinal);
            Assert.Contains("no-cache", context.Response.Headers.Pragma.ToString(), StringComparison.Ordinal);

            context.Response.Body.Position = 0;
            using var document = await JsonDocument.ParseAsync(context.Response.Body);

            Assert.Equal(
                "step_up_required",
                document.RootElement.GetProperty("status").GetString());
            Assert.DoesNotContain(operatorId.ToString("D"), document.RootElement.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("pending.operator", document.RootElement.GetRawText(), StringComparison.Ordinal);
        }

        Assert.Equal(cases.Length, authorization.PolicyNames.Count);
        Assert.All(
            authorization.PolicyNames,
            policyName => Assert.Equal(MemOperatorPolicies.RecentStepUp, policyName));
    }

    private static ClaimsPrincipal CreatePlatformOwnerPrincipal()
    {
        return new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
                    new Claim(ClaimTypes.Name, "owner.high-risk-test"),
                    new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
                ],
                IdentityConstants.ApplicationScheme));
    }

    private sealed class RecordingAuthorizationService(
        AuthorizationResult result) : IAuthorizationService
    {
        public string? LastPolicyName { get; private set; }

        public List<string> PolicyNames { get; } = [];

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
        {
            return Task.FromResult(result);
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName)
        {
            LastPolicyName = policyName;
            PolicyNames.Add(policyName);
            return Task.FromResult(result);
        }
    }

    private static string NormalizeRouteTemplate(string? template)
    {
        return string.IsNullOrWhiteSpace(template)
            ? string.Empty
            : template.TrimEnd('/');
    }

    private sealed record GovernanceRouteCase(
        string Method,
        string Template,
        string Path,
        string? Body);

    private sealed class CanHaveBodyRequestFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

}
