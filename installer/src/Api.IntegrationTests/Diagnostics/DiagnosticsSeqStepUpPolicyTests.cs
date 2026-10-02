using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;
using Modules.Operator.Diagnostics.Endpoints;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsSeqStepUpPolicyTests
{
    [Fact]
    public async Task Disabled_high_risk_policy_skips_recent_step_up_authorization()
    {
        var authorization = new RecordingAuthorizationService(
            AuthorizationResult.Failed());
        var context = CreateContext(requireHighRiskStepUp: false);

        var result = await DiagnosticsEndpoints.RequireRecentStepUpAsync(
            context,
            authorization);

        Assert.Null(result);
        Assert.Equal(0, authorization.CallCount);
        Assert.Null(authorization.LastPolicyName);
    }

    [Fact]
    public async Task Required_high_risk_policy_enforces_recent_step_up_authorization()
    {
        var authorization = new RecordingAuthorizationService(
            AuthorizationResult.Failed());
        var context = CreateContext(requireHighRiskStepUp: true);

        var result = await DiagnosticsEndpoints.RequireRecentStepUpAsync(
            context,
            authorization);

        Assert.NotNull(result);
        Assert.Equal(1, authorization.CallCount);
        Assert.Equal(MemOperatorPolicies.RecentStepUp, authorization.LastPolicyName);
    }

    [Fact]
    public async Task Missing_settings_service_fails_closed_to_recent_step_up()
    {
        var authorization = new RecordingAuthorizationService(
            AuthorizationResult.Failed());
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };

        var result = await DiagnosticsEndpoints.RequireRecentStepUpAsync(
            context,
            authorization);

        Assert.NotNull(result);
        Assert.Equal(1, authorization.CallCount);
        Assert.Equal(MemOperatorPolicies.RecentStepUp, authorization.LastPolicyName);
    }

    private static DefaultHttpContext CreateContext(bool requireHighRiskStepUp)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemSecuritySettingsService>(
            new FixedSecuritySettingsService(requireHighRiskStepUp));
        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
    }

    private sealed class FixedSecuritySettingsService(bool required)
        : IMemSecuritySettingsService
    {
        public Task<MemSecuritySettingsSnapshot> GetEffectiveAsync(
            CancellationToken ct = default) =>
            Task.FromResult(new MemSecuritySettingsSnapshot(
                RequireHighRiskStepUp: required,
                HighRiskStepUpGrantMinutes: 15,
                IsDefaulted: false,
                UpdatedAtUtc: null,
                UpdatedByOperatorId: null));

        public Task<MemSecuritySettingsSnapshot> UpdateHighRiskStepUpAsync(
            Guid actorOperatorId,
            UpdateMemSecuritySettingsCommand command,
            string? correlationId = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingAuthorizationService(
        AuthorizationResult result) : IAuthorizationService
    {
        public int CallCount { get; private set; }
        public string? LastPolicyName { get; private set; }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
        {
            CallCount++;
            return Task.FromResult(result);
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName)
        {
            CallCount++;
            LastPolicyName = policyName;
            return Task.FromResult(result);
        }
    }
}
