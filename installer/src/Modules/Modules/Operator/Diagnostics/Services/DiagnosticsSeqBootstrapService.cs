using System.Security.Claims;
using Modules.Auth.Identity;
using Modules.Integrations.Seq.Contracts;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Contracts;
using Modules.Shared.RuntimeImages;

namespace Modules.Operator.Diagnostics.Services;

/// <summary>
/// Secret-free setup projection for first-time guided Seq bootstrap. It never
/// pulls an image, writes a secret, creates storage, or mutates Docker.
/// </summary>
public sealed class DiagnosticsSeqBootstrapService(
    SeqDiagnosticsOptions options,
    SeqEffectiveConfigurationProvider effectiveConfigurationProvider,
    SeqSecretResolver secrets,
    ISeqRuntimeStatusReader runtimeReader,
    IRuntimeImageInspector imageInspector,
    SeqBootstrapStorageInspector storageInspector,
    ISeqBootstrapStateStore bootstrapStateStore,
    SeqBootstrapReviewStore reviewStore,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan ReviewLifetime = TimeSpan.FromMinutes(10);

    public async Task<DiagnosticsSeqBootstrapOverviewResponse> GetOverviewAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var warnings = new List<string>();
        var effective = effectiveConfigurationProvider.Get();
        var bootstrapState = bootstrapStateStore.Read().State;
        if (!string.IsNullOrWhiteSpace(effective.WarningCode))
        {
            warnings.Add(effective.WarningCode);
        }

        var runtime = await TryGetRuntimeAsync(warnings, cancellationToken);
        var ownership = runtime?.OwnershipState ?? "unavailable";
        var image = await InspectImageAsync(warnings, cancellationToken);
        var storage = storageInspector.Inspect();
        if (!string.IsNullOrWhiteSpace(storage.WarningCode))
        {
            warnings.Add(storage.WarningCode);
        }

        var admin = secrets.ResolveAdminPasswordHash(options);
        var apiKey = secrets.ResolveApiKey(options);
        var initializedAuthoritySafe = storage.State != "initialized" || admin.Available;
        if (!initializedAuthoritySafe)
        {
            warnings.Add("seq_initialized_data_requires_existing_administrator_secret");
        }

        var ui = NormalizeUiAuthority(effective.UiUrl);
        var setupPolicyErrors = SeqDiagnosticsOptionsValidator.ValidateForSetup(options);
        if (setupPolicyErrors.Count > 0)
        {
            warnings.Add("seq_setup_configuration_invalid");
        }

        var conflict = IsOwnershipConflict(ownership);
        var storageSafe = storage.State is "ready" or "ready-to-create" or "initialized";
        var imageCanBePrepared = image.Local ||
                                 (options.AllowSetupPull && image.WarningCode is null);
        var bootstrapComplete = bootstrapState?.RuntimeVerifiedAtUtc is not null;
        var resumableManagedRuntime = runtime is { Exists: true, Managed: true } && !bootstrapComplete;
        var setupAvailable = setupPolicyErrors.Count == 0 &&
                             imageCanBePrepared &&
                             storageSafe &&
                             initializedAuthoritySafe &&
                             !conflict &&
                             (runtime?.Exists != true || resumableManagedRuntime);
        var owner = principal.IsInRole(MemOperatorRoles.PlatformOwner);
        var state = runtime switch
        {
            { Managed: true, Running: true } when bootstrapComplete => "running",
            { Managed: true } when !bootstrapComplete => "setup-incomplete",
            _ when conflict => "blocked",
            _ when effective.ManagementEnabled => "configured",
            _ => "not-installed"
        };

        return new DiagnosticsSeqBootstrapOverviewResponse(
            SchemaVersion: 1,
            ObservedAtUtc: timeProvider.GetUtcNow(),
            State: state,
            SetupAvailable: setupAvailable,
            ApprovedVersion: options.ExpectedVersion,
            ImageState: image.Local
                ? image.ImmutableIdentityAvailable ? "local" : "invalid"
                : options.AllowSetupPull ? "preparable" : "not-local",
            StorageState: storage.State,
            RuntimeOwnershipState: ownership,
            EulaAccepted: effective.EulaAccepted,
            AdministratorSecretState: admin.Available ? "available" : "absent",
            IngestionCredentialState: apiKey.Available ? "available" : "absent",
            UiAuthorityState: ui is null ? "not-configured" : "configured",
            CanStartSetup: owner && setupAvailable,
            Warnings: warnings
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    public async Task<DiagnosticsSeqBootstrapReviewResponse> ReviewAsync(
        DiagnosticsSeqBootstrapReviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var observedAt = timeProvider.GetUtcNow();
        var warnings = new List<string>();
        var bootstrapState = bootstrapStateStore.Read().State;
        var policyErrors = SeqDiagnosticsOptionsValidator.ValidateForSetup(options);
        if (policyErrors.Count > 0)
        {
            warnings.Add("seq_setup_configuration_invalid");
        }

        Uri? privateUi = null;
        var privateUiValid = string.IsNullOrWhiteSpace(request.PrivateUiUrl) ||
                             SeqDiagnosticsOptionsValidator.TryNormalizeUrl(
                                 request.PrivateUiUrl,
                                 out privateUi);
        if (!privateUiValid)
        {
            warnings.Add("seq_ui_authority_invalid");
        }

        var image = await InspectImageAsync(warnings, cancellationToken);
        var storage = storageInspector.Inspect();
        if (!string.IsNullOrWhiteSpace(storage.WarningCode))
        {
            warnings.Add(storage.WarningCode);
        }

        var admin = secrets.ResolveAdminPasswordHash(options);
        var initializedAuthoritySafe = storage.State != "initialized" || admin.Available;
        if (!initializedAuthoritySafe)
        {
            warnings.Add("seq_initialized_data_requires_existing_administrator_secret");
        }

        var runtime = await TryGetRuntimeAsync(warnings, cancellationToken);
        var ownership = runtime?.OwnershipState ?? "unavailable";
        var conflict = IsOwnershipConflict(ownership);
        var imageReady = image.Local
            ? image.ImmutableIdentityAvailable
            : options.AllowSetupPull && image.WarningCode is null;
        var storageReady = storage.State is "ready" or "ready-to-create" or "initialized";
        var bootstrapComplete = bootstrapState?.RuntimeVerifiedAtUtc is not null;
        var runtimeReady = runtime?.Exists != true ||
                           (runtime is { Managed: true } && !bootstrapComplete);
        var ready = request.AcceptEula &&
                    privateUiValid &&
                    policyErrors.Count == 0 &&
                    imageReady &&
                    storageReady &&
                    initializedAuthoritySafe &&
                    !conflict &&
                    runtimeReady;

        if (!request.AcceptEula)
        {
            warnings.Add("seq_eula_not_accepted_in_review");
        }

        var reviewId = $"seq_bootstrap_review_{Guid.NewGuid():N}";
        var expiresAt = observedAt.Add(ReviewLifetime);
        if (ready)
        {
            reviewStore.Store(new SeqBootstrapReviewSnapshot(
                ReviewId: reviewId,
                ObservedAtUtc: observedAt,
                ExpiresAtUtc: expiresAt,
                AcceptEula: request.AcceptEula,
                PrivateUiUrl: privateUi?.ToString(),
                SelectedHostPort: options.PreferredHostPort,
                ApprovedImageReference: options.ApprovedImageReference,
                ExpectedVersion: options.ExpectedVersion,
                StorageState: storage.State,
                RuntimeOwnershipState: ownership,
                RuntimeExists: runtime?.Exists == true,
                RuntimeManaged: runtime?.Managed == true,
                AdministratorPasswordRequired: !admin.Available,
                EnableEventDelivery: request.EnableEventDelivery,
                CurrentAdministratorPasswordRequired: admin.Available));
        }

        return new DiagnosticsSeqBootstrapReviewResponse(
            SchemaVersion: 1,
            ReviewId: reviewId,
            ObservedAtUtc: observedAt,
            ExpiresAtUtc: expiresAt,
            Ready: ready,
            Image: new DiagnosticsSeqBootstrapImageReview(
                options.ApprovedImageReference,
                options.ExpectedVersion,
                image.Local,
                image.ImmutableIdentityAvailable,
                WillPullDuringSetup: !image.Local && options.AllowSetupPull,
                image.WarningCode),
            Storage: new DiagnosticsSeqBootstrapStorageReview(
                ServerOwned: true,
                State: storage.State,
                DisplayName: "MEM Seq data directory",
                WarningCode: storage.WarningCode),
            Runtime: new DiagnosticsSeqBootstrapRuntimeReview(
                OwnershipState: ownership,
                SelectedHostPort: options.PreferredHostPort,
                PublishesPublicIngress: false),
            Security: new DiagnosticsSeqBootstrapSecurityReview(
                EulaAcceptedInReview: request.AcceptEula,
                AdministratorPasswordRequired: !admin.Available,
                AuthenticatedIngestionRequired: true,
                CurrentAdministratorPasswordRequired: admin.Available),
            PrivateUiAuthorityConfigured: privateUi is not null,
            ActionCodes: BuildActionCodes(
                administratorPasswordRequired: !admin.Available,
                enableEventDelivery: request.EnableEventDelivery),
            Warnings: warnings
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            EnableEventDelivery: request.EnableEventDelivery);
    }

    private static IReadOnlyList<string> BuildActionCodes(
        bool administratorPasswordRequired,
        bool enableEventDelivery)
    {
        var actions = new List<string>
        {
            "prepare_approved_image",
            "prepare_server_owned_storage"
        };
        actions.Add(administratorPasswordRequired
            ? "hash_administrator_password_securely"
            : "reuse_existing_administrator_authority");
        actions.AddRange(
        [
            "create_mem_managed_container",
            "attach_mem_gateway_network",
            "do_not_create_public_ingress",
            "preserve_seq_data_directory",
            "verify_docker_and_seq_health",
            "provision_mem_ingestion_credential",
            "verify_mem_ingestion"
        ]);
        actions.Add(enableEventDelivery
            ? "prepare_event_delivery_after_restart"
            : "leave_event_delivery_disabled");
        return actions;
    }

    private async Task<DiagnosticsSeqBootstrapImageReview> InspectImageAsync(
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var inspection = await imageInspector.InspectAsync(
                options.ApprovedImageReference,
                cancellationToken);
            if (inspection is null)
            {
                if (!options.AllowSetupPull)
                {
                    warnings.Add("seq_setup_image_preparation_disabled");
                }

                return new DiagnosticsSeqBootstrapImageReview(
                    options.ApprovedImageReference,
                    options.ExpectedVersion,
                    Local: false,
                    ImmutableIdentityAvailable: false,
                    WillPullDuringSetup: options.AllowSetupPull,
                    WarningCode: options.AllowSetupPull
                        ? null
                        : "seq_setup_image_preparation_disabled");
            }

            var immutable = IsImmutableImageId(inspection.ImageId);
            if (!immutable)
            {
                warnings.Add("seq_approved_image_invalid");
            }

            return new DiagnosticsSeqBootstrapImageReview(
                options.ApprovedImageReference,
                options.ExpectedVersion,
                Local: true,
                ImmutableIdentityAvailable: immutable,
                WillPullDuringSetup: false,
                WarningCode: immutable ? null : "seq_approved_image_invalid");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            warnings.Add("seq_approved_image_inspection_failed");
            return new DiagnosticsSeqBootstrapImageReview(
                options.ApprovedImageReference,
                options.ExpectedVersion,
                Local: false,
                ImmutableIdentityAvailable: false,
                WillPullDuringSetup: false,
                WarningCode: "seq_approved_image_inspection_failed");
        }
    }

    private async Task<SeqStatusResponse?> TryGetRuntimeAsync(
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var runtime = await runtimeReader.GetStatusAsync(cancellationToken);
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

    private static bool IsOwnershipConflict(string value) =>
        value is "unmanaged-conflict" or "control-plane-mismatch" or
            "identity-mismatch" or "record-only" or "unavailable";

    private static Uri? NormalizeUiAuthority(string? value) =>
        SeqDiagnosticsOptionsValidator.TryNormalizeUrl(value, out var uri)
            ? uri
            : null;

    private static bool IsImmutableImageId(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized.StartsWith("sha256:", StringComparison.Ordinal) &&
               normalized.Length == "sha256:".Length + 64 &&
               normalized["sha256:".Length..].All(char.IsAsciiHexDigit);
    }
}
