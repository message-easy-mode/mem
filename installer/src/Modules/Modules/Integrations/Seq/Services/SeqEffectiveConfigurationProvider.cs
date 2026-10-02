using Shared.ControlPlane.Runtime;

namespace Modules.Integrations.Seq.Services;

public sealed record SeqEffectiveConfiguration(
    bool ManagementEnabled,
    bool EulaAccepted,
    string? UiUrl,
    int PreferredHostPort,
    string HostDataPath,
    string ApprovedImageReference,
    string ExpectedVersion,
    string? WarningCode);

public sealed class SeqEffectiveConfigurationProvider(
    SeqDiagnosticsOptions options,
    ISeqBootstrapStateStore bootstrapStateStore,
    MemControlPlaneRuntimeContext? runtimeContext = null)
{
    public SeqEffectiveConfiguration Get()
    {
        var result = bootstrapStateStore.Read();
        var state = result.State;
        var explicitUiUrl = FirstNonEmpty(
            state?.PrivateUiUrl,
            options.UiUrl);
        var uiUrl = explicitUiUrl ?? ResolveAutomaticUiUrl(state);
        return new SeqEffectiveConfiguration(
            ManagementEnabled: options.ManagementEnabled ||
                               state?.ManagementEnabled == true,
            EulaAccepted: options.EulaAccepted || state?.EulaAccepted == true,
            UiUrl: uiUrl,
            PreferredHostPort: state?.SelectedHostPort ?? options.PreferredHostPort,
            HostDataPath: options.HostDataPath,
            ApprovedImageReference: options.ApprovedImageReference,
            ExpectedVersion: options.ExpectedVersion,
            WarningCode: result.WarningCode);
    }

    private string? ResolveAutomaticUiUrl(SeqBootstrapState? state)
    {
        if (runtimeContext is null ||
            state is not {
                ManagementEnabled: true,
                RuntimeVerifiedAtUtc: not null,
                SelectedHostPort: > 0 and <= 65535
            })
        {
            return null;
        }

        try
        {
            var source = SeqManagedServiceAuthoritySourceFactory.CreateForBootstrap(
                options,
                state.SelectedHostPort);
            return MemManagedServiceAuthorityResolver.Resolve(
                    runtimeContext,
                    MemManagedServicePurposes.Browser,
                    source)
                .Authority
                .ToString();
        }
        catch (MemManagedServiceAuthorityException)
        {
            return null;
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values
            .Select(value => value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    /// <summary>
    /// Creates an isolated options snapshot for existing Seq services. Static
    /// policy remains authoritative while guided bootstrap state supplies only
    /// the operator decisions that setup is allowed to persist.
    /// </summary>
    public SeqDiagnosticsOptions CreateEffectiveOptions()
    {
        var effective = Get();
        return new SeqDiagnosticsOptions
        {
            SinkEnabled = options.SinkEnabled,
            ManagementEnabled = effective.ManagementEnabled,
            IngestionUrl = options.IngestionUrl,
            UiUrl = effective.UiUrl,
            HealthUrl = options.HealthUrl,
            ApiKeyEnvironmentVariableName = options.ApiKeyEnvironmentVariableName,
            ApiKeyFilePath = options.ApiKeyFilePath,
            AdminPasswordHashEnvironmentVariableName =
                options.AdminPasswordHashEnvironmentVariableName,
            AdminPasswordHashFilePath = options.AdminPasswordHashFilePath,
            EulaAccepted = effective.EulaAccepted,
            ApprovedImageReference = options.ApprovedImageReference,
            ExpectedVersion = options.ExpectedVersion,
            AllowOperationalPull = options.AllowOperationalPull,
            AllowSetupPull = options.AllowSetupPull,
            HostDataPath = options.HostDataPath,
            PreferredHostPort = effective.PreferredHostPort,
            ProbeTimeoutSeconds = options.ProbeTimeoutSeconds,
            ProbeIntervalSeconds = options.ProbeIntervalSeconds,
            PasswordHashTimeoutSeconds = options.PasswordHashTimeoutSeconds,
            AdministratorUserName = options.AdministratorUserName,
            MaximumAdministratorPasswordLength = options.MaximumAdministratorPasswordLength,
            ConnectionTimeoutSeconds = options.ConnectionTimeoutSeconds,
            ConnectionVerificationAttemptCount = options.ConnectionVerificationAttemptCount,
            ConnectionVerificationPollIntervalSeconds = options.ConnectionVerificationPollIntervalSeconds,
            BootstrapHealthAttemptCount = options.BootstrapHealthAttemptCount,
            BootstrapHealthPollIntervalSeconds = options.BootstrapHealthPollIntervalSeconds,
            SecretRootPath = options.SecretRootPath,
            BootstrapStatePath = options.BootstrapStatePath,
            DeliveryStatePath = options.DeliveryStatePath,
            DeliveryStartupWarningCode = options.DeliveryStartupWarningCode
        };
    }
}
