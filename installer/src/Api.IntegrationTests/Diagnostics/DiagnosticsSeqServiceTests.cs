using System.Security.Claims;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Infrastructure.Data.Entities;
using Infrastructure.Docker.Models;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;
using Modules.Integrations.Seq.Contracts;
using Modules.Integrations.Seq.Mappers;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Services;
using Modules.Shared.RuntimeImages;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsSeqServiceTests
{
    [Fact]
    public void Runtime_status_projection_requires_the_expected_container_name_and_identity()
    {
        var record = new RuntimeServiceEntity
        {
            ServiceName = "seq",
            ContainerName = "unexpected-seq",
            Image = "datalust/seq:2026.1.17044",
            ContainerId = "container-1",
            Status = "running"
        };
        var container = new DockerContainerInspection(
            "container-1",
            "mem-seq",
            "datalust/seq:2026.1.17044",
            "running",
            Running: true,
            Ports: []);

        var status = SeqMappers.ToStatusResponse(
            record,
            container,
            "seq",
            "mem-seq",
            "2026.1.17044",
            "datalust/seq:2026.1.17044",
            []);

        Assert.False(status.Managed);
        Assert.False(status.UsesApprovedRuntime);
        Assert.Equal("unmanaged-conflict", status.OwnershipState);
        Assert.Equal("seq_unmanaged_container", status.WarningCode);
    }

    [Fact]
    public async Task Overview_keeps_seq_optional_and_hides_owner_UI_authority_from_operator()
    {
        var options = new SeqDiagnosticsOptions();
        var service = CreateService(
            options,
            new SeqHealthSnapshot(
                "optional-disabled",
                SinkConfigured: false,
                Reachable: false,
                LastCheckedAtUtc: null,
                LastSuccessAtUtc: null,
                WarningCode: null),
            RuntimeAbsent(options),
            image: null);

        var overview = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.Operator),
            CancellationToken.None);

        Assert.False(overview.Configured);
        Assert.False(overview.Delivery.Enabled);
        Assert.Equal("disabled", overview.Delivery.ConfigurationState);
        Assert.Equal("not-required", overview.Delivery.SecretState);
        Assert.Equal("not-configured", overview.Runtime.State);
        Assert.Equal("optional-disabled", overview.Health.Status);
        Assert.False(overview.Ui.Configured);
        Assert.False(overview.Ui.Available);
        Assert.Null(overview.Ui.Url);
        Assert.False(overview.Capabilities.CanReviewSetup);
        Assert.False(overview.Capabilities.CanOpenUi);
        Assert.False(overview.Ui.Configurable);
    }

    [Fact]
    public async Task Owner_receives_only_the_authoritative_UI_URL_and_safe_runtime_facts()
    {
        var options = ManagementOptions();
        options.UiUrl = "https://seq.example.test/";
        var service = CreateService(
            options,
            ReadyHealth(),
            ManagedRunning(options),
            LocalImage());

        var owner = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.PlatformOwner),
            CancellationToken.None);
        var operation = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.Operator),
            CancellationToken.None);

        Assert.Equal("https://seq.example.test/", owner.Ui.Url);
        Assert.True(owner.Ui.Available);
        Assert.True(owner.Capabilities.CanReviewSetup);
        Assert.True(owner.Capabilities.CanOpenUi);
        Assert.False(owner.Ui.Configurable);
        Assert.True(owner.Capabilities.CanStop);
        Assert.True(owner.Capabilities.CanRestart);
        Assert.False(owner.Capabilities.CanEnableDelivery);
        Assert.True(owner.Capabilities.CanCheckHealth);
        Assert.Null(operation.Ui.Url);
        Assert.False(operation.Ui.Available);
        Assert.False(operation.Capabilities.CanReviewSetup);
        Assert.DoesNotContain("/data/seq", System.Text.Json.JsonSerializer.Serialize(owner));
        Assert.DoesNotContain("container-1", System.Text.Json.JsonSerializer.Serialize(owner));
    }

    [Fact]
    public async Task Removed_runtime_retains_UI_configuration_without_projecting_an_openable_UI()
    {
        var options = ManagementOptions();
        options.UiUrl = "https://seq.example.test/";
        var service = CreateService(
            options,
            new SeqHealthSnapshot(
                "runtime-absent",
                SinkConfigured: false,
                Reachable: false,
                LastCheckedAtUtc: DateTimeOffset.UtcNow,
                LastSuccessAtUtc: null,
                WarningCode: null),
            RuntimeAbsent(options),
            LocalImage());

        var owner = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.PlatformOwner),
            CancellationToken.None);

        Assert.True(owner.Ui.Configured);
        Assert.False(owner.Ui.Available);
        Assert.Null(owner.Ui.Url);
        Assert.False(owner.Capabilities.CanOpenUi);
        Assert.False(owner.Runtime.Present);
        Assert.False(owner.Runtime.Running);
    }

    [Fact]
    public async Task Overview_separates_effective_and_desired_delivery_until_restart()
    {
        var options = ManagementOptions();
        options.SinkEnabled = true;
        options.UiUrl = "https://seq.example.test/";
        var delivery = new FixedDeliveryStateStore(new SeqDeliveryState(
            EffectiveEnabled: true,
            DesiredEnabled: false,
            RestartRequired: true,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            WarningCode: null));
        var service = CreateService(
            options,
            ReadyHealth(),
            ManagedRunning(options),
            LocalImage(),
            deliveryStateStore: delivery);

        var overview = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.PlatformOwner),
            CancellationToken.None);

        Assert.True(overview.Delivery.Enabled);
        Assert.False(overview.Delivery.DesiredEnabled);
        Assert.True(overview.Delivery.RestartRequired);
        Assert.Equal("disable-pending-restart", overview.Delivery.ConfigurationState);
        Assert.False(overview.Capabilities.CanEnableDelivery);
        Assert.False(overview.Capabilities.CanDisableDelivery);
        Assert.False(overview.Capabilities.CanRemove);
    }

    [Fact]
    public async Task Overview_uses_authoritative_delivery_state_when_static_sink_configuration_is_stale()
    {
        var options = ManagementOptions();
        options.SinkEnabled = true;
        options.ApiKeyEnvironmentVariableName = $"MEM_SEQ_API_MISSING_{Guid.NewGuid():N}";
        var delivery = new FixedDeliveryStateStore(new SeqDeliveryState(
            EffectiveEnabled: false,
            DesiredEnabled: false,
            RestartRequired: false,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            WarningCode: null));
        var service = CreateService(
            options,
            ReadyHealth(),
            ManagedRunning(options),
            LocalImage(),
            deliveryStateStore: delivery);

        var overview = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.PlatformOwner),
            CancellationToken.None);

        Assert.False(overview.Delivery.Enabled);
        Assert.False(overview.Delivery.DesiredEnabled);
        Assert.Equal("not-required", overview.Delivery.SecretState);
        Assert.DoesNotContain("diagnostics.seq_api_key_unavailable", overview.Warnings);
    }

    [Fact]
    public async Task Guided_bootstrap_state_enables_existing_runtime_projection_without_rewriting_static_options()
    {
        var adminVariable = $"MEM_SEQ_ADMIN_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(adminVariable, "secret-admin-hash");

        try
        {
            var options = ManagementOptions();
            options.ManagementEnabled = false;
            options.EulaAccepted = false;
            options.UiUrl = null;
            options.AdminPasswordHashEnvironmentVariableName = adminVariable;
            var effectiveProvider = new SeqEffectiveConfigurationProvider(
                options,
                new FixedBootstrapStateStore(new SeqBootstrapState(
                    ManagementEnabled: true,
                    EulaAccepted: true,
                    SelectedHostPort: 16341)));
            var service = CreateService(
                options,
                ReadyHealth(),
                RuntimeAbsent(options),
                LocalImage(),
                effectiveConfigurationProvider: effectiveProvider);

            var overview = await service.GetOverviewAsync(
                Principal(MemOperatorRoles.PlatformOwner),
                CancellationToken.None);
            var review = await service.ReviewSetupAsync(CancellationToken.None);

            Assert.True(overview.Configured);
            Assert.True(overview.Runtime.ManagementEnabled);
            Assert.True(overview.Capabilities.CanDeploy);
            Assert.True(review.ReadyForDeployment);
            Assert.True(review.EulaAccepted);
            Assert.False(review.UiAuthority.Configured);
            Assert.False(options.ManagementEnabled);
            Assert.False(options.EulaAccepted);
        }
        finally
        {
            Environment.SetEnvironmentVariable(adminVariable, null);
        }
    }

    [Fact]
    public async Task Verified_guided_runtime_allows_only_the_owner_to_configure_UI_authority()
    {
        var options = ManagementOptions();
        var bootstrap = new FixedBootstrapStateStore(new SeqBootstrapState(
            ManagementEnabled: true,
            EulaAccepted: true,
            SelectedHostPort: 15341,
            RuntimeVerifiedAtUtc: DateTimeOffset.UtcNow));
        var service = CreateService(
            options,
            ReadyHealth(),
            ManagedRunning(options),
            LocalImage(),
            bootstrapStateStore: bootstrap);

        var owner = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.PlatformOwner),
            CancellationToken.None);
        var operation = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.Operator),
            CancellationToken.None);

        Assert.True(owner.Ui.Configurable);
        Assert.False(operation.Ui.Configurable);
    }

    [Fact]
    public async Task Restarted_enabled_process_requires_a_current_process_delivery_verification()
    {
        var apiKeyVariable = $"MEM_SEQ_API_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(apiKeyVariable, "temporary-test-api-key");

        try
        {
            var options = ManagementOptions();
            options.SinkEnabled = true;
            options.ApiKeyEnvironmentVariableName = apiKeyVariable;
            var processIdentity = new SeqDeliveryProcessIdentity(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
            var bootstrap = new FixedBootstrapStateStore(new SeqBootstrapState(
                ManagementEnabled: true,
                EulaAccepted: true,
                SelectedHostPort: 15341,
                RuntimeVerifiedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-3),
                IngestionCredentialState: "available",
                DeliveryVerifiedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-2),
                ActiveDeliveryProcessId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                ActiveDeliveryVerifiedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
                LastActiveDeliveryVerificationId: "stale-process-proof"));
            var delivery = new FixedDeliveryStateStore(new SeqDeliveryState(
                EffectiveEnabled: true,
                DesiredEnabled: true,
                RestartRequired: false,
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                WarningCode: null));
            var service = CreateService(
                options,
                new SeqHealthSnapshot(
                    "configured-unprobed",
                    SinkConfigured: true,
                    Reachable: false,
                    LastCheckedAtUtc: null,
                    LastSuccessAtUtc: null,
                    WarningCode: null),
                ManagedRunning(options),
                LocalImage(),
                deliveryStateStore: delivery,
                bootstrapStateStore: bootstrap,
                deliveryProcessIdentity: processIdentity);

            var overview = await service.GetOverviewAsync(
                Principal(MemOperatorRoles.PlatformOwner),
                CancellationToken.None);

            Assert.Equal(5, overview.SchemaVersion);
            Assert.Equal("verification-required", overview.Delivery.ActivationState);
            Assert.True(overview.Capabilities.CanVerifyDelivery);
            Assert.Null(overview.Delivery.ActivationVerifiedAtUtc);
            Assert.Null(overview.Delivery.LastActivationVerificationId);
        }
        finally
        {
            Environment.SetEnvironmentVariable(apiKeyVariable, null);
        }
    }

    [Fact]
    public async Task Current_process_delivery_proof_is_projected_only_to_the_owner()
    {
        var options = ManagementOptions();
        options.SinkEnabled = true;
        var verifiedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var processIdentity = new SeqDeliveryProcessIdentity(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var bootstrap = new FixedBootstrapStateStore(new SeqBootstrapState(
            ManagementEnabled: true,
            EulaAccepted: true,
            SelectedHostPort: 15341,
            RuntimeVerifiedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-3),
            IngestionCredentialState: "available",
            DeliveryVerifiedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-2),
            ActiveDeliveryProcessId: processIdentity.Value,
            ActiveDeliveryVerifiedAtUtc: verifiedAt,
            LastActiveDeliveryVerificationId: "seq-active-current"));
        var delivery = new FixedDeliveryStateStore(new SeqDeliveryState(
            EffectiveEnabled: true,
            DesiredEnabled: true,
            RestartRequired: false,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            WarningCode: null));
        var service = CreateService(
            options,
            ReadyHealth(),
            ManagedRunning(options),
            LocalImage(),
            deliveryStateStore: delivery,
            bootstrapStateStore: bootstrap,
            deliveryProcessIdentity: processIdentity);

        var owner = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.PlatformOwner),
            CancellationToken.None);
        var operation = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.Operator),
            CancellationToken.None);

        Assert.Equal("verified", owner.Delivery.ActivationState);
        Assert.Equal(verifiedAt, owner.Delivery.ActivationVerifiedAtUtc);
        Assert.Equal("seq-active-current", owner.Delivery.LastActivationVerificationId);
        Assert.False(owner.Capabilities.CanVerifyDelivery);
        Assert.Equal("verified", operation.Delivery.ActivationState);
        Assert.Null(operation.Delivery.LastActivationVerificationId);
        Assert.False(operation.Capabilities.CanVerifyDelivery);
    }

    [Fact]
    public async Task Startup_prerequisite_failure_is_unavailable_instead_of_restart_pending()
    {
        var options = ManagementOptions();
        var service = CreateService(
            options,
            ReadyHealth(),
            ManagedRunning(options),
            LocalImage(),
            deliveryStateStore: new FixedDeliveryStateStore(new SeqDeliveryState(
                EffectiveEnabled: false,
                DesiredEnabled: true,
                RestartRequired: true,
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                WarningCode: "seq_delivery_startup_prerequisites_unavailable")));

        var overview = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.PlatformOwner),
            CancellationToken.None);

        Assert.Equal("unavailable", overview.Delivery.ActivationState);
        Assert.False(overview.Capabilities.CanVerifyDelivery);
        Assert.Contains(
            "seq_delivery_startup_prerequisites_unavailable",
            overview.Warnings);
    }


    [Fact]
    public async Task Effective_delivery_is_unavailable_when_logging_bootstrap_did_not_attach_the_sink()
    {
        var options = ManagementOptions();
        options.SinkEnabled = true;
        var service = CreateService(
            options,
            ReadyHealth(),
            ManagedRunning(options),
            LocalImage(),
            deliveryStateStore: new FixedDeliveryStateStore(new SeqDeliveryState(
                EffectiveEnabled: true,
                DesiredEnabled: true,
                RestartRequired: false,
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                WarningCode: null)),
            loggingRuntimeState: new SeqLoggingRuntimeState(
                SinkConfigured: false,
                WarningCode: "diagnostics.seq_sink_configuration_failed"));

        var overview = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.PlatformOwner),
            CancellationToken.None);

        Assert.Equal("unavailable", overview.Delivery.ConfigurationState);
        Assert.Equal("unavailable", overview.Delivery.ActivationState);
        Assert.False(overview.Capabilities.CanVerifyDelivery);
        Assert.Contains("diagnostics.seq_sink_configuration_failed", overview.Warnings);
    }

    [Fact]
    public async Task Relative_development_storage_path_does_not_make_the_overview_invalid()
    {
        var contentRoot = Path.Combine(
            Path.GetTempPath(),
            $"mem-seq-relative-overview-{Guid.NewGuid():N}",
            "installer",
            "src",
            "Api");
        Directory.CreateDirectory(contentRoot);
        try
        {
            var options = ManagementOptions();
            options.HostDataPath = "../../data/seq";
            options.AdminPasswordHashEnvironmentVariableName =
                "MEM_SEQ_RELATIVE_PATH_TEST_ADMIN_HASH";
            var service = CreateService(
                options,
                ReadyHealth(),
                RuntimeAbsent(options),
                LocalImage(),
                environment: new TestHostEnvironment(contentRoot));

            var overview = await service.GetOverviewAsync(
                Principal(MemOperatorRoles.PlatformOwner),
                CancellationToken.None);
            var review = await service.ReviewSetupAsync(CancellationToken.None);

            Assert.DoesNotContain("seq_configuration_invalid", overview.Warnings);
            Assert.Equal("ready-to-create", review.Storage.State);
            Assert.DoesNotContain("seq_data_path_invalid", review.Warnings);
        }
        finally
        {
            Directory.Delete(
                Path.GetFullPath(Path.Combine(contentRoot, "../../..")),
                recursive: true);
        }
    }

    [Fact]
    public async Task Setup_review_reports_ready_without_serializing_secret_values_or_host_path()
    {
        var apiKeyVariable = $"MEM_SEQ_API_{Guid.NewGuid():N}";
        var adminHashVariable = $"MEM_SEQ_ADMIN_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(apiKeyVariable, "secret-api-key");
        Environment.SetEnvironmentVariable(adminHashVariable, "secret-admin-hash");

        try
        {
            var options = ManagementOptions();
            options.SinkEnabled = true;
            options.ApiKeyEnvironmentVariableName = apiKeyVariable;
            options.AdminPasswordHashEnvironmentVariableName = adminHashVariable;
            options.UiUrl = "https://seq.example.test/";
            var service = CreateService(
                options,
                ReadyHealth(),
                RuntimeAbsent(options),
                LocalImage());

            var review = await service.ReviewSetupAsync(CancellationToken.None);
            var json = System.Text.Json.JsonSerializer.Serialize(review);

            Assert.True(review.ReadyForDeployment);
            Assert.False(review.ReadyForDelivery);
            Assert.True(review.Image.Local);
            Assert.True(review.Image.ImmutableIdentityAvailable);
            Assert.True(review.Secrets.AdministratorPasswordHashAvailable);
            Assert.True(review.Secrets.IngestionApiKeyAvailable);
            Assert.True(review.UiAuthority.Configured);
            Assert.False(review.PublishesPublicIngress);
            Assert.Contains("preserve_seq_data_directory", review.ActionCodes);
            Assert.DoesNotContain("secret-api-key", json, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-admin-hash", json, StringComparison.Ordinal);
            Assert.DoesNotContain(options.HostDataPath, json, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(apiKeyVariable, null);
            Environment.SetEnvironmentVariable(adminHashVariable, null);
        }
    }

    [Fact]
    public async Task Setup_review_treats_an_existing_managed_runtime_as_post_deployment_and_uses_delivery_state_for_secret_requirement()
    {
        var adminHashVariable = $"MEM_SEQ_ADMIN_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(adminHashVariable, "secret-admin-hash");

        try
        {
            var options = ManagementOptions();
            options.SinkEnabled = true;
            options.ApiKeyEnvironmentVariableName = $"MEM_SEQ_API_MISSING_{Guid.NewGuid():N}";
            options.AdminPasswordHashEnvironmentVariableName = adminHashVariable;
            options.UiUrl = "https://seq.example.test/";
            var deliveryState = new FixedDeliveryStateStore(new SeqDeliveryState(
                EffectiveEnabled: false,
                DesiredEnabled: false,
                RestartRequired: false,
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                WarningCode: null));
            var service = CreateService(
                options,
                ReadyHealth(),
                ManagedRunning(options),
                LocalImage(),
                deliveryStateStore: deliveryState);

            var review = await service.ReviewSetupAsync(CancellationToken.None);

            Assert.False(review.ReadyForDeployment);
            Assert.False(review.ReadyForDelivery);
            Assert.False(review.Secrets.IngestionApiKeyRequired);
            Assert.False(review.Secrets.IngestionApiKeyAvailable);
            Assert.Empty(review.ActionCodes);
            Assert.DoesNotContain("diagnostics.seq_api_key_unavailable", review.Warnings);
        }
        finally
        {
            Environment.SetEnvironmentVariable(adminHashVariable, null);
        }
    }

    [Fact]
    public async Task Setup_review_reports_missing_image_and_secret_without_attempting_a_pull()
    {
        var inspector = new RecordingImageInspector(null);
        var options = ManagementOptions();
        options.UiUrl = "https://seq.example.test/";
        options.AdminPasswordHashEnvironmentVariableName =
            $"MEM_SEQ_MISSING_{Guid.NewGuid():N}";
        var service = CreateService(
            options,
            new SeqHealthSnapshot(
                "optional-disabled",
                SinkConfigured: false,
                Reachable: false,
                LastCheckedAtUtc: null,
                LastSuccessAtUtc: null,
                WarningCode: null),
            RuntimeAbsent(options),
            image: null,
            inspector);

        var review = await service.ReviewSetupAsync(CancellationToken.None);

        Assert.False(review.ReadyForDeployment);
        Assert.False(review.Image.Local);
        Assert.False(review.Secrets.AdministratorPasswordHashAvailable);
        Assert.Contains("seq_approved_image_missing", review.Warnings);
        Assert.Contains("diagnostics.seq_admin_password_hash_unavailable", review.Warnings);
        Assert.Equal(0, inspector.PullCount);
    }

    [Fact]
    public async Task Setup_review_reports_invalid_UI_authority_and_missing_ingestion_secret_without_values()
    {
        var adminVariable = $"MEM_SEQ_ADMIN_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(adminVariable, "secret-admin-hash");

        try
        {
            var options = ManagementOptions();
            options.SinkEnabled = true;
            options.ApiKeyEnvironmentVariableName = $"MEM_SEQ_API_MISSING_{Guid.NewGuid():N}";
            options.AdminPasswordHashEnvironmentVariableName = adminVariable;
            options.UiUrl = "https://operator:secret@seq.example.test/";
            var service = CreateService(
                options,
                new SeqHealthSnapshot(
                    "configuration-error",
                    SinkConfigured: false,
                    Reachable: false,
                    LastCheckedAtUtc: null,
                    LastSuccessAtUtc: null,
                    WarningCode: "diagnostics.seq_api_key_unavailable"),
                RuntimeAbsent(options),
                LocalImage());

            var review = await service.ReviewSetupAsync(CancellationToken.None);
            var json = System.Text.Json.JsonSerializer.Serialize(review);

            Assert.False(review.ReadyForDeployment);
            Assert.False(review.ReadyForDelivery);
            Assert.False(review.UiAuthority.Configured);
            Assert.False(review.Secrets.IngestionApiKeyAvailable);
            Assert.Contains("diagnostics.seq_api_key_unavailable", review.Warnings);
            Assert.Contains("seq_ui_authority_not_configured", review.Warnings);
            Assert.DoesNotContain("secret-admin-hash", json, StringComparison.Ordinal);
            Assert.DoesNotContain("operator:secret", json, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(adminVariable, null);
        }
    }

    [Theory]
    [InlineData("unmanaged-conflict")]
    [InlineData("control-plane-mismatch")]
    [InlineData("identity-mismatch")]
    [InlineData("record-only")]
    public async Task Setup_review_fails_closed_for_unproven_runtime_ownership(
        string ownershipState)
    {
        var adminVariable = $"MEM_SEQ_ADMIN_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(adminVariable, "secret-admin-hash");

        try
        {
            var options = ManagementOptions();
            options.UiUrl = "https://seq.example.test/";
            options.AdminPasswordHashEnvironmentVariableName = adminVariable;
            var runtime = ManagedRunning(options) with
            {
                Managed = false,
                OwnershipState = ownershipState,
                WarningCode = ownershipState switch
                {
                    "control-plane-mismatch" => "seq_control_plane_ownership_mismatch",
                    "identity-mismatch" => "seq_container_identity_mismatch",
                    "record-only" => "seq_runtime_record_conflict",
                    _ => "seq_unmanaged_container"
                }
            };
            var service = CreateService(
                options,
                ReadyHealth(),
                runtime,
                LocalImage());

            var review = await service.ReviewSetupAsync(CancellationToken.None);

            Assert.False(review.ReadyForDeployment);
            Assert.Equal(ownershipState, review.RuntimeOwnershipState);
            Assert.Contains(runtime.WarningCode!, review.Warnings);
        }
        finally
        {
            Environment.SetEnvironmentVariable(adminVariable, null);
        }
    }

    [Fact]
    public async Task Overview_projects_the_server_owned_high_risk_step_up_policy()
    {
        var options = ManagementOptions();
        var service = CreateService(
            options,
            ReadyHealth(),
            ManagedRunning(options),
            LocalImage(),
            securitySettings: new FixedSecuritySettingsService(required: false));

        var overview = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.PlatformOwner),
            CancellationToken.None);

        Assert.Equal(5, overview.SchemaVersion);
        Assert.False(overview.Capabilities.RequiresRecentStepUp);
    }

    private static DiagnosticsSeqService CreateService(
        SeqDiagnosticsOptions options,
        SeqHealthSnapshot health,
        SeqStatusResponse runtime,
        RuntimeImageInspection? image,
        RecordingImageInspector? inspector = null,
        ISeqDeliveryStateStore? deliveryStateStore = null,
        SeqEffectiveConfigurationProvider? effectiveConfigurationProvider = null,
        IHostEnvironment? environment = null,
        ISeqBootstrapStateStore? bootstrapStateStore = null,
        IMemSecuritySettingsService? securitySettings = null,
        SeqDeliveryProcessIdentity? deliveryProcessIdentity = null,
        SeqLoggingRuntimeState? loggingRuntimeState = null) =>
        new(
            options,
            new SeqSecretResolver(environment ?? new TestHostEnvironment()),
            new FixedHealthReader(health),
            new FixedRuntimeReader(runtime),
            inspector ?? new RecordingImageInspector(image),
            TimeProvider.System,
            deliveryStateStore,
            effectiveConfigurationProvider,
            environment,
            bootstrapStateStore,
            securitySettings,
            deliveryProcessIdentity,
            loggingRuntimeState);

    private static ClaimsPrincipal Principal(string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, role)
        ],
        "test"));

    private static SeqDiagnosticsOptions ManagementOptions() => new()
    {
        ManagementEnabled = true,
        EulaAccepted = true,
        HostDataPath = Path.Combine(Path.GetTempPath(), "mem-seq-tests"),
        AdminPasswordHashEnvironmentVariableName = string.Empty,
        AdminPasswordHashFilePath = null
    };

    private static SeqHealthSnapshot ReadyHealth() => new(
        "ready",
        SinkConfigured: true,
        Reachable: true,
        LastCheckedAtUtc: DateTimeOffset.UtcNow,
        LastSuccessAtUtc: DateTimeOffset.UtcNow,
        WarningCode: null);

    private static RuntimeImageInspection LocalImage() => new(
        $"sha256:{new string('a', 64)}",
        ["datalust/seq@sha256:" + new string('b', 64)],
        []);

    private static SeqStatusResponse RuntimeAbsent(SeqDiagnosticsOptions options) => new(
        "seq",
        "mem-seq",
        options.ExpectedVersion,
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
        WarningCode: null);

    private static SeqStatusResponse ManagedRunning(SeqDiagnosticsOptions options) => new(
        "seq",
        "mem-seq",
        options.ExpectedVersion,
        "/data/seq",
        15341,
        Exists: true,
        Running: true,
        State: "running",
        Image: $"sha256:{new string('a', 64)}",
        UsesApprovedRuntime: true,
        Warnings: [],
        Managed: true,
        OwnershipState: "managed",
        WarningCode: null);

    private sealed class FixedHealthReader(SeqHealthSnapshot health) : ISeqHealthReader
    {
        public SeqHealthSnapshot GetHealth() => health;
    }

    private sealed class FixedRuntimeReader(SeqStatusResponse response) : ISeqRuntimeStatusReader
    {
        public Task<SeqStatusResponse> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private sealed class RecordingImageInspector(RuntimeImageInspection? image)
        : IRuntimeImageInspector
    {
        public int PullCount { get; private set; }

        public Task<RuntimeImageInspection?> InspectAsync(
            string immutableReference,
            CancellationToken cancellationToken) => Task.FromResult(image);

        public Task PullAsync(
            string immutableReference,
            CancellationToken cancellationToken)
        {
            PullCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedDeliveryStateStore(SeqDeliveryState state)
        : ISeqDeliveryStateStore
    {
        public SeqDeliveryState GetState() => state;

        public Task<SeqDeliveryState> SetDesiredAsync(
            bool enabled,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedBootstrapStateStore(SeqBootstrapState state)
        : ISeqBootstrapStateStore
    {
        public SeqBootstrapStateReadResult Read() => new(state, null);

        public Task WriteAsync(
            SeqBootstrapState updated,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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

    private sealed class TestHostEnvironment(
        string contentRootPath = "") : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Api.IntegrationTests";
        public string ContentRootPath { get; set; } =
            string.IsNullOrWhiteSpace(contentRootPath)
                ? AppContext.BaseDirectory
                : contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
