using System.Security.Claims;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;
using Modules.Integrations.Seq.Contracts;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Contracts;
using Modules.Shared.RuntimeImages;
using Microsoft.Extensions.Hosting;
using Modules.Shared.Docker;
using Shared.ControlPlane.Runtime;

namespace Modules.Operator.Diagnostics.Services;

/// <summary>
/// Produces a secret-free, browser-safe Seq projection and setup review for
/// Diagnostics. Lifecycle mutations are coordinated separately so this remains
/// the single source of current-state and capability truth.
/// </summary>
public sealed class DiagnosticsSeqService(
    SeqDiagnosticsOptions options,
    SeqSecretResolver secrets,
    ISeqHealthReader healthReader,
    ISeqRuntimeStatusReader runtimeService,
    IRuntimeImageInspector imageInspector,
    TimeProvider timeProvider,
    ISeqDeliveryStateStore? deliveryStateStore = null,
    SeqEffectiveConfigurationProvider? effectiveConfigurationProvider = null,
    IHostEnvironment? environment = null,
    ISeqBootstrapStateStore? bootstrapStateStore = null,
    IMemSecuritySettingsService? securitySettings = null,
    SeqDeliveryProcessIdentity? deliveryProcessIdentity = null,
    SeqLoggingRuntimeState? loggingRuntimeState = null,
    MemControlPlaneRuntimeContext? runtimeContext = null,
    IControlPlaneDockerOwnershipGuard? ownershipGuard = null)
{
    private static readonly TimeSpan ReviewLifetime = TimeSpan.FromMinutes(10);

    public async Task<DiagnosticsSeqOverviewResponse> GetOverviewAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var effectiveOptions = GetEffectiveOptions();
        var owner = principal.IsInRole(MemOperatorRoles.PlatformOwner);
        var requiresRecentStepUp = securitySettings is null ||
            (await securitySettings.GetEffectiveAsync(cancellationToken)).RequireHighRiskStepUp;
        var health = healthReader.GetHealth();
        var apiKey = secrets.ResolveApiKey(effectiveOptions);
        var validationErrors = SeqDiagnosticsOptionsValidator.Validate(effectiveOptions);
        var warnings = new List<string>();
        var dockerOwnership = ownershipGuard is null
            ? MemDockerOwnershipProjection.Unchecked
            : await ownershipGuard.InspectAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(dockerOwnership.WarningCode))
        {
            warnings.Add(dockerOwnership.WarningCode);
        }

        var runtime = await TryGetRuntimeAsync(warnings, cancellationToken);
        var ui = NormalizeUiAuthority(effectiveOptions);
        var preference = deliveryStateStore?.GetState() ?? new SeqDeliveryState(
            EffectiveEnabled: effectiveOptions.SinkEnabled,
            DesiredEnabled: effectiveOptions.SinkEnabled,
            RestartRequired: false,
            UpdatedAtUtc: null,
            WarningCode: null);

        if (!string.IsNullOrWhiteSpace(health.WarningCode))
        {
            warnings.Add(health.WarningCode);
        }

        if (validationErrors.Count > 0)
        {
            warnings.Add("seq_configuration_invalid");
        }

        if ((preference.EffectiveEnabled || preference.DesiredEnabled) && !apiKey.Available)
        {
            warnings.Add(apiKey.WarningCode ?? "diagnostics.seq_api_key_unavailable");
        }

        if (effectiveOptions.ManagementEnabled && ui is null)
        {
            warnings.Add("seq_ui_authority_not_configured");
        }

        if (!string.IsNullOrWhiteSpace(preference.WarningCode))
        {
            warnings.Add(preference.WarningCode);
        }

        var loggingRuntimeWarning = loggingRuntimeState?.WarningCode;
        if (preference.EffectiveEnabled &&
            loggingRuntimeState is { SinkConfigured: false } &&
            !string.IsNullOrWhiteSpace(loggingRuntimeWarning))
        {
            warnings.Add(loggingRuntimeWarning);
        }

        var runtimeOverview = ToRuntimeOverview(runtime, effectiveOptions);
        var bootstrapState = bootstrapStateStore?.Read().State;
        var connectionVerified = apiKey.Available &&
                                 bootstrapState?.IngestionCredentialState == "available" &&
                                 bootstrapState.DeliveryVerifiedAtUtc is not null;
        var deliveryState = ResolveDeliveryState(
            health,
            validationErrors.Count > 0,
            preference,
            loggingRuntimeState);
        var activationState = ResolveActivationState(
            health,
            preference,
            bootstrapState,
            deliveryProcessIdentity,
            loggingRuntimeState);
        var secretRequired = preference.EffectiveEnabled || preference.DesiredEnabled;
        var secretState = secretRequired
            ? apiKey.Available ? "available" : "unavailable"
            : "not-required";
        var mutableRuntime = owner &&
                             dockerOwnership.MutationsAllowed &&
                             effectiveOptions.ManagementEnabled &&
                             runtimeOverview.Managed &&
                             runtimeOverview.Present;
        var deliveryCanBeEnabled = owner &&
                                   dockerOwnership.MutationsAllowed &&
                                   !preference.DesiredEnabled &&
                                   runtimeOverview.Managed &&
                                   runtimeOverview.Running &&
                                   connectionVerified &&
                                   SeqDiagnosticsOptionsValidator.TryNormalizeUrl(
                                       effectiveOptions.IngestionUrl,
                                       out _);
        var ownershipSafeForDeploy = runtimeOverview.State is "absent" or "not-configured";
        var uiRuntimeAvailable = runtimeOverview.Managed &&
                                 runtimeOverview.Present &&
                                 runtimeOverview.Running &&
                                 health.Reachable;
        var uiConfigurable = owner &&
                             dockerOwnership.MutationsAllowed &&
                             runtimeOverview.Managed &&
                             runtimeOverview.Present &&
                             bootstrapState is {
                                 ManagementEnabled: true,
                                 RuntimeVerifiedAtUtc: not null
                             };

        return new DiagnosticsSeqOverviewResponse(
            SchemaVersion: 5,
            ObservedAtUtc: timeProvider.GetUtcNow(),
            Configured: preference.EffectiveEnabled ||
                        preference.DesiredEnabled ||
                        effectiveOptions.ManagementEnabled ||
                        runtime?.Exists == true,
            Delivery: new DiagnosticsSeqDeliveryOverview(
                Enabled: preference.EffectiveEnabled,
                DesiredEnabled: preference.DesiredEnabled,
                ConfigurationState: deliveryState,
                SecretState: secretState,
                RequiresApiRestartToChange: true,
                RestartRequired: preference.RestartRequired,
                PreferenceUpdatedAtUtc: preference.UpdatedAtUtc,
                ActivationState: activationState,
                ActivationVerifiedAtUtc: activationState == "verified"
                    ? bootstrapState?.ActiveDeliveryVerifiedAtUtc
                    : null,
                LastActivationVerificationId: owner && activationState == "verified"
                    ? bootstrapState?.LastActiveDeliveryVerificationId
                    : null,
                Restart: runtimeContext is null
                    ? new MemRestartContract(
                        MemRestartKinds.Unsupported,
                        "restart_supervisor_unknown",
                        Command: null,
                        CommandAvailable: false)
                    : MemRestartContractFactory.Create(runtimeContext)),
            Runtime: runtimeOverview,
            Health: new DiagnosticsSeqHealthOverview(
                Status: health.Status,
                Reachable: health.Reachable,
                LastCheckedAtUtc: health.LastCheckedAtUtc,
                LastSucceededAtUtc: health.LastSuccessAtUtc,
                WarningCode: health.WarningCode),
            Ui: new DiagnosticsSeqUiOverview(
                Configured: ui is not null,
                Available: owner && ui is not null && uiRuntimeAvailable,
                Url: owner && uiRuntimeAvailable ? ui?.ToString() : null,
                Configurable: uiConfigurable),
            Capabilities: new DiagnosticsSeqOverviewCapabilities(
                CanReviewSetup: owner,
                CanOpenUi: owner && ui is not null && uiRuntimeAvailable,
                CanDeploy: owner && dockerOwnership.MutationsAllowed && effectiveOptions.ManagementEnabled && ownershipSafeForDeploy,
                CanStart: mutableRuntime && !runtimeOverview.Running,
                CanStop: mutableRuntime && runtimeOverview.Running,
                CanRestart: mutableRuntime && runtimeOverview.Running,
                CanRemove: mutableRuntime &&
                           !preference.EffectiveEnabled &&
                           !preference.DesiredEnabled,
                CanEnableDelivery: deliveryCanBeEnabled,
                CanDisableDelivery: owner && dockerOwnership.MutationsAllowed && preference.DesiredEnabled,
                CanCheckHealth: runtimeOverview.Managed && runtimeOverview.Running,
                CanVerifyDelivery: owner &&
                                   activationState == "verification-required" &&
                                   preference.EffectiveEnabled &&
                                   preference.DesiredEnabled &&
                                   !preference.RestartRequired &&
                                   runtimeOverview.Managed &&
                                   runtimeOverview.Running &&
                                   runtimeOverview.UsesApprovedRuntime &&
                                   connectionVerified &&
                                   loggingRuntimeState is not { SinkConfigured: false },
                CanConnect: owner &&
                            runtimeOverview.Managed &&
                            runtimeOverview.Running &&
                            runtimeOverview.UsesApprovedRuntime &&
                            health.Reachable &&
                            !connectionVerified,
                RequiresRecentStepUp: requiresRecentStepUp),
            Warnings: warnings
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Connection: new DiagnosticsSeqConnectionOverview(
                CredentialState: apiKey.Available ? "available" : "absent",
                VerificationState: connectionVerified ? "verified" : "not-verified",
                VerifiedAtUtc: bootstrapState?.DeliveryVerifiedAtUtc,
                ApiKeyId: owner ? bootstrapState?.IngestionCredentialId : null,
                LastVerificationId: owner ? bootstrapState?.LastDeliveryVerificationId : null,
                LastEventId: owner ? bootstrapState?.LastDeliveryVerificationEventId : null));
    }

    public async Task<DiagnosticsSeqSetupReviewResponse> ReviewSetupAsync(
        CancellationToken cancellationToken)
    {
        var effectiveOptions = GetEffectiveOptions();
        var observedAt = timeProvider.GetUtcNow();
        var warnings = new List<string>();
        var validationErrors = SeqDiagnosticsOptionsValidator.Validate(effectiveOptions);
        if (validationErrors.Count > 0)
        {
            warnings.Add("seq_configuration_invalid");
        }

        var image = await InspectImageAsync(effectiveOptions, warnings, cancellationToken);
        var storage = InspectStorage(effectiveOptions);
        if (!string.IsNullOrWhiteSpace(storage.WarningCode))
        {
            warnings.Add(storage.WarningCode);
        }

        var apiKey = secrets.ResolveApiKey(effectiveOptions);
        var adminPasswordHash = secrets.ResolveAdminPasswordHash(effectiveOptions);
        var deliveryPreference = deliveryStateStore?.GetState() ?? new SeqDeliveryState(
            EffectiveEnabled: effectiveOptions.SinkEnabled,
            DesiredEnabled: effectiveOptions.SinkEnabled,
            RestartRequired: false,
            UpdatedAtUtc: null,
            WarningCode: null);
        var ingestionApiKeyRequired =
            deliveryPreference.EffectiveEnabled || deliveryPreference.DesiredEnabled;
        if (ingestionApiKeyRequired && !apiKey.Available)
        {
            warnings.Add(apiKey.WarningCode ?? "diagnostics.seq_api_key_unavailable");
        }

        if (!adminPasswordHash.Available)
        {
            warnings.Add(adminPasswordHash.WarningCode ??
                         "diagnostics.seq_admin_password_hash_unavailable");
        }

        var ui = NormalizeUiAuthority(effectiveOptions);
        if (ui is null)
        {
            warnings.Add("seq_ui_authority_not_configured");
        }

        var runtime = await TryGetRuntimeAsync(warnings, cancellationToken);
        var runtimeOwnershipState = runtime?.OwnershipState ?? "unavailable";
        var runtimeConflict = runtimeOwnershipState is
            "unmanaged-conflict" or "control-plane-mismatch" or "identity-mismatch" or
            "record-only" or "unavailable";

        if (!effectiveOptions.ManagementEnabled)
        {
            warnings.Add("seq_management_disabled");
        }

        if (!effectiveOptions.EulaAccepted)
        {
            warnings.Add("seq_eula_not_accepted");
        }

        var managedRuntimePresent =
            runtime is { Exists: true, Managed: true } && !runtimeConflict;
        var readyForDeployment = !managedRuntimePresent &&
                                 effectiveOptions.ManagementEnabled &&
                                 effectiveOptions.EulaAccepted &&
                                 validationErrors.Count == 0 &&
                                 image.Local &&
                                 image.ImmutableIdentityAvailable &&
                                 storage.State is "ready" or "ready-to-create" &&
                                 adminPasswordHash.Available &&
                                 !runtimeConflict;
        var reviewedBootstrapState = bootstrapStateStore?.Read().State;
        var readyForDelivery = apiKey.Available &&
                               reviewedBootstrapState?.IngestionCredentialState == "available" &&
                               reviewedBootstrapState.DeliveryVerifiedAtUtc is not null &&
                               SeqDiagnosticsOptionsValidator.TryNormalizeUrl(
                                   effectiveOptions.IngestionUrl,
                                   out _) &&
                               runtime?.Managed == true &&
                               runtime.Running;

        return new DiagnosticsSeqSetupReviewResponse(
            SchemaVersion: 1,
            ReviewId: $"seq_review_{Guid.NewGuid():N}",
            ObservedAtUtc: observedAt,
            ExpiresAtUtc: observedAt.Add(ReviewLifetime),
            ReadyForDeployment: readyForDeployment,
            ReadyForDelivery: readyForDelivery,
            Image: image,
            Storage: storage,
            Secrets: new DiagnosticsSeqSetupSecretReview(
                AdministratorPasswordHashAvailable: adminPasswordHash.Available,
                IngestionApiKeyRequired: ingestionApiKeyRequired,
                IngestionApiKeyAvailable: apiKey.Available),
            UiAuthority: new DiagnosticsSeqSetupUiAuthorityReview(
                Configured: ui is not null),
            EulaAccepted: effectiveOptions.EulaAccepted,
            PublishesPublicIngress: false,
            RuntimeOwnershipState: runtimeOwnershipState,
            ActionCodes: managedRuntimePresent
                ? Array.Empty<string>()
                :
                [
                    "create_mem_managed_container",
                    "attach_mem_gateway_network",
                    "preserve_seq_data_directory",
                    "do_not_create_public_ingress",
                    "leave_event_delivery_unchanged"
                ],
            Warnings: warnings
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private async Task<SeqStatusResponse?> TryGetRuntimeAsync(
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var runtime = await runtimeService.GetStatusAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(runtime.WarningCode))
            {
                warnings.Add(runtime.WarningCode);
            }

            return runtime;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            warnings.Add("seq_runtime_status_unavailable");
            return null;
        }
    }

    private async Task<DiagnosticsSeqSetupImageReview> InspectImageAsync(
        SeqDiagnosticsOptions effectiveOptions,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var inspection = await imageInspector.InspectAsync(
                effectiveOptions.ApprovedImageReference,
                cancellationToken);
            if (inspection is null)
            {
                warnings.Add("seq_approved_image_missing");
                return new DiagnosticsSeqSetupImageReview(
                    effectiveOptions.ApprovedImageReference,
                    effectiveOptions.ExpectedVersion,
                    Local: false,
                    ImmutableIdentityAvailable: false,
                    WarningCode: "seq_approved_image_missing");
            }

            var immutable = IsImmutableImageId(inspection.ImageId);
            if (!immutable)
            {
                warnings.Add("seq_approved_image_invalid");
            }

            return new DiagnosticsSeqSetupImageReview(
                effectiveOptions.ApprovedImageReference,
                effectiveOptions.ExpectedVersion,
                Local: true,
                ImmutableIdentityAvailable: immutable,
                WarningCode: immutable ? null : "seq_approved_image_invalid");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            warnings.Add("seq_approved_image_inspection_failed");
            return new DiagnosticsSeqSetupImageReview(
                effectiveOptions.ApprovedImageReference,
                effectiveOptions.ExpectedVersion,
                Local: false,
                ImmutableIdentityAvailable: false,
                WarningCode: "seq_approved_image_inspection_failed");
        }
    }

    private DiagnosticsSeqSetupStorageReview InspectStorage(
        SeqDiagnosticsOptions effectiveOptions)
    {
        if (!effectiveOptions.ManagementEnabled)
        {
            return new DiagnosticsSeqSetupStorageReview(
                ServerOwned: true,
                State: "not-configured",
                DisplayName: "MEM Seq data directory",
                WarningCode: null);
        }

        try
        {
            var fullPath = SeqFileSystemSafety.ResolveDirectory(
                effectiveOptions.HostDataPath,
                environment?.ContentRootPath ?? Directory.GetCurrentDirectory(),
                "seq_data_path_invalid");
            SeqFileSystemSafety.EnsurePathContainsNoLinks(
                fullPath,
                "seq_data_path_symlink");

            return new DiagnosticsSeqSetupStorageReview(
                ServerOwned: true,
                State: Directory.Exists(fullPath) ? "ready" : "ready-to-create",
                DisplayName: "MEM Seq data directory",
                WarningCode: null);
        }
        catch (SeqOperationException exception)
        {
            return InvalidStorage(exception.Code);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return InvalidStorage("seq_data_path_unavailable");
        }
    }

    private static DiagnosticsSeqSetupStorageReview InvalidStorage(
        string warningCode = "seq_data_path_invalid") => new(
        ServerOwned: true,
        State: "invalid",
        DisplayName: "MEM Seq data directory",
        WarningCode: warningCode);

    private DiagnosticsSeqRuntimeOverview ToRuntimeOverview(
        SeqStatusResponse? runtime,
        SeqDiagnosticsOptions effectiveOptions)
    {
        if (runtime is null)
        {
            return new DiagnosticsSeqRuntimeOverview(
                ManagementEnabled: effectiveOptions.ManagementEnabled,
                Present: false,
                Managed: false,
                State: "unavailable",
                Running: false,
                UsesApprovedRuntime: false,
                ExpectedVersion: effectiveOptions.ExpectedVersion,
                DataRetentionState: "preserved-on-remove",
                PublishesPublicIngress: false,
                WarningCode: "seq_runtime_status_unavailable");
        }

        var state = runtime.OwnershipState switch
        {
            "unmanaged-conflict" => "unmanaged-conflict",
            "control-plane-mismatch" => "control-plane-mismatch",
            "identity-mismatch" => "identity-mismatch",
            "record-only" => "record-only",
            _ when runtime.Running => "running",
            _ when runtime.Exists => "stopped",
            _ when effectiveOptions.ManagementEnabled => "absent",
            _ => "not-configured"
        };

        return new DiagnosticsSeqRuntimeOverview(
            ManagementEnabled: effectiveOptions.ManagementEnabled,
            Present: runtime.Exists,
            Managed: runtime.Managed,
            State: state,
            Running: runtime.Running,
            UsesApprovedRuntime: runtime.UsesApprovedRuntime,
            ExpectedVersion: runtime.ExpectedVersion,
            DataRetentionState: "preserved-on-remove",
            PublishesPublicIngress: false,
            WarningCode: runtime.WarningCode);
    }

    private SeqDiagnosticsOptions GetEffectiveOptions() =>
        effectiveConfigurationProvider?.CreateEffectiveOptions() ?? options;

    private static Uri? NormalizeUiAuthority(SeqDiagnosticsOptions effectiveOptions) =>
        SeqDiagnosticsOptionsValidator.TryNormalizeUrl(effectiveOptions.UiUrl, out var uri)
            ? uri
            : null;

    private static string ResolveActivationState(
        SeqHealthSnapshot health,
        SeqDeliveryState preference,
        SeqBootstrapState? bootstrapState,
        SeqDeliveryProcessIdentity? processIdentity,
        SeqLoggingRuntimeState? loggingRuntimeState)
    {
        if (string.Equals(
                preference.WarningCode,
                "seq_delivery_startup_prerequisites_unavailable",
                StringComparison.Ordinal))
        {
            return "unavailable";
        }

        if (preference.RestartRequired)
        {
            return "restart-pending";
        }

        if (!preference.EffectiveEnabled)
        {
            return "disabled";
        }

        if (loggingRuntimeState is { SinkConfigured: false })
        {
            return "unavailable";
        }

        var currentProcessVerified =
            processIdentity is not null &&
            bootstrapState is not null &&
            bootstrapState.ActiveDeliveryProcessId == processIdentity.Value &&
            bootstrapState.ActiveDeliveryVerifiedAtUtc is not null &&
            !string.IsNullOrWhiteSpace(
                bootstrapState.LastActiveDeliveryVerificationId);

        if (!currentProcessVerified)
        {
            return health.SinkConfigured
                ? "verification-required"
                : "unavailable";
        }

        return health.Reachable &&
               string.Equals(
                   health.Status,
                   "ready",
                   StringComparison.OrdinalIgnoreCase)
            ? "verified"
            : "unavailable";
    }

    private static string ResolveDeliveryState(
        SeqHealthSnapshot health,
        bool configurationInvalid,
        SeqDeliveryState preference,
        SeqLoggingRuntimeState? loggingRuntimeState)
    {
        if (preference.RestartRequired)
        {
            return preference.DesiredEnabled
                ? "enable-pending-restart"
                : "disable-pending-restart";
        }

        if (!preference.EffectiveEnabled)
        {
            return "disabled";
        }

        if (loggingRuntimeState is { SinkConfigured: false })
        {
            return "unavailable";
        }

        if (configurationInvalid || health.Status == "configuration-error")
        {
            return "invalid";
        }

        return health.SinkConfigured ? "configured" : "unavailable";
    }

    private static bool IsImmutableImageId(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized.StartsWith("sha256:", StringComparison.Ordinal) &&
               normalized.Length == "sha256:".Length + 64 &&
               normalized["sha256:".Length..].All(char.IsAsciiHexDigit);
    }
}
