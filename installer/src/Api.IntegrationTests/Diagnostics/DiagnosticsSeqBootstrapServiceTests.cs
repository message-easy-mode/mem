using System.Security.Claims;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.Auth.Identity;
using Modules.Integrations.Seq.Contracts;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Contracts;
using Modules.Operator.Diagnostics.Services;
using Modules.Shared.RuntimeImages;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsSeqBootstrapServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"mem-seq-bootstrap-service-{Guid.NewGuid():N}");

    [Fact]
    public async Task Owner_can_review_a_clean_setup_even_when_the_approved_image_is_not_local()
    {
        var inspector = new RecordingImageInspector(null);
        var service = CreateService(
            Options(),
            RuntimeAbsent(),
            inspector);

        var overview = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.PlatformOwner),
            CancellationToken.None);
        var review = await service.ReviewAsync(
            new DiagnosticsSeqBootstrapReviewRequest(
                AcceptEula: true,
                PrivateUiUrl: null),
            CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(review);

        Assert.Equal("not-installed", overview.State);
        Assert.True(overview.SetupAvailable);
        Assert.True(overview.CanStartSetup);
        Assert.Equal("preparable", overview.ImageState);
        Assert.True(review.Ready);
        Assert.True(review.Image.WillPullDuringSetup);
        Assert.False(review.PrivateUiAuthorityConfigured);
        Assert.True(review.Security.AdministratorPasswordRequired);
        Assert.False(review.Security.CurrentAdministratorPasswordRequired);
        Assert.True(review.EnableEventDelivery);
        Assert.Contains("provision_mem_ingestion_credential", review.ActionCodes);
        Assert.Contains("verify_mem_ingestion", review.ActionCodes);
        Assert.Contains("prepare_event_delivery_after_restart", review.ActionCodes);
        Assert.False(review.Runtime.PublishesPublicIngress);
        Assert.Equal(0, inspector.PullCount);
        Assert.DoesNotContain(_root, json, StringComparison.Ordinal);
        Assert.DoesNotContain("password-hash", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ingestion-api-key", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Relative_development_storage_path_remains_available_for_retry()
    {
        var contentRoot = Path.Combine(_root, "installer", "src", "Api");
        Directory.CreateDirectory(contentRoot);
        var options = Options();
        options.HostDataPath = "../../data/seq";
        var service = CreateService(
            options,
            RuntimeAbsent(),
            new RecordingImageInspector(LocalImage()),
            contentRoot);

        var overview = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.PlatformOwner),
            CancellationToken.None);

        Assert.True(overview.SetupAvailable);
        Assert.True(overview.CanStartSetup);
        Assert.Equal("ready-to-create", overview.StorageState);
        Assert.DoesNotContain("seq_setup_configuration_invalid", overview.Warnings);
    }

    [Fact]
    public async Task Existing_administrator_secret_reuses_the_hash_but_requires_current_password_for_connection()
    {
        var options = Options();
        Directory.CreateDirectory(Path.GetDirectoryName(options.AdminPasswordHashFilePath!)!);
        await File.WriteAllTextAsync(
            options.AdminPasswordHashFilePath!,
            "existing-seq-password-hash");
        var service = CreateService(
            options,
            RuntimeAbsent(),
            new RecordingImageInspector(LocalImage()));

        var overview = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.PlatformOwner),
            CancellationToken.None);
        var review = await service.ReviewAsync(
            new DiagnosticsSeqBootstrapReviewRequest(true, null),
            CancellationToken.None);

        Assert.Equal("available", overview.AdministratorSecretState);
        Assert.True(review.Ready);
        Assert.False(review.Security.AdministratorPasswordRequired);
        Assert.True(review.Security.CurrentAdministratorPasswordRequired);
        Assert.Contains("reuse_existing_administrator_authority", review.ActionCodes);
        Assert.Contains("provision_mem_ingestion_credential", review.ActionCodes);
    }


    [Fact]
    public async Task Review_can_leave_event_delivery_disabled_while_still_connecting_MEM()
    {
        var service = CreateService(
            Options(),
            RuntimeAbsent(),
            new RecordingImageInspector(LocalImage()));

        var review = await service.ReviewAsync(
            new DiagnosticsSeqBootstrapReviewRequest(
                AcceptEula: true,
                PrivateUiUrl: null,
                EnableEventDelivery: false),
            CancellationToken.None);

        Assert.True(review.Ready);
        Assert.False(review.EnableEventDelivery);
        Assert.Contains("provision_mem_ingestion_credential", review.ActionCodes);
        Assert.Contains("verify_mem_ingestion", review.ActionCodes);
        Assert.Contains("leave_event_delivery_disabled", review.ActionCodes);
        Assert.DoesNotContain("prepare_event_delivery_after_restart", review.ActionCodes);
    }

    [Fact]
    public async Task Initialized_data_without_the_existing_administrator_secret_fails_closed()
    {
        var options = Options();
        Directory.CreateDirectory(options.HostDataPath);
        await File.WriteAllTextAsync(
            Path.Combine(options.HostDataPath, "seq.json"),
            "initialized");
        var environment = new TestHostEnvironment(_root);
        var stateStore = new SeqBootstrapStateStore(options, environment);
        await stateStore.WriteAsync(
            new SeqBootstrapState(
                ManagementEnabled: true,
                EulaAccepted: true,
                SetupStage: "failed:verifying-health"),
            CancellationToken.None);
        var service = CreateService(
            options,
            RuntimeAbsent(),
            new RecordingImageInspector(LocalImage()));

        var overview = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.PlatformOwner),
            CancellationToken.None);
        var review = await service.ReviewAsync(
            new DiagnosticsSeqBootstrapReviewRequest(true, null),
            CancellationToken.None);

        Assert.False(overview.SetupAvailable);
        Assert.False(review.Ready);
        Assert.Contains(
            "seq_initialized_data_requires_existing_administrator_secret",
            review.Warnings);
    }

    [Fact]
    public async Task Operator_receives_safe_state_but_cannot_start_setup()
    {
        var service = CreateService(
            Options(),
            RuntimeAbsent(),
            new RecordingImageInspector(null));

        var overview = await service.GetOverviewAsync(
            Principal(MemOperatorRoles.Operator),
            CancellationToken.None);

        Assert.True(overview.SetupAvailable);
        Assert.False(overview.CanStartSetup);
    }

    [Fact]
    public async Task Review_requires_explicit_EULA_acceptance()
    {
        var service = CreateService(
            Options(),
            RuntimeAbsent(),
            new RecordingImageInspector(LocalImage()));

        var review = await service.ReviewAsync(
            new DiagnosticsSeqBootstrapReviewRequest(
                AcceptEula: false,
                PrivateUiUrl: null),
            CancellationToken.None);

        Assert.False(review.Ready);
        Assert.False(review.Security.EulaAcceptedInReview);
        Assert.Contains("seq_eula_not_accepted_in_review", review.Warnings);
    }

    [Fact]
    public async Task Invalid_UI_authority_is_rejected_but_an_absent_authority_is_optional()
    {
        var service = CreateService(
            Options(),
            RuntimeAbsent(),
            new RecordingImageInspector(LocalImage()));

        var invalid = await service.ReviewAsync(
            new DiagnosticsSeqBootstrapReviewRequest(
                AcceptEula: true,
                PrivateUiUrl: "https://user:secret@seq.example.test/"),
            CancellationToken.None);
        var absent = await service.ReviewAsync(
            new DiagnosticsSeqBootstrapReviewRequest(
                AcceptEula: true,
                PrivateUiUrl: null),
            CancellationToken.None);

        Assert.False(invalid.Ready);
        Assert.Contains("seq_ui_authority_invalid", invalid.Warnings);
        Assert.True(absent.Ready);
        Assert.False(absent.PrivateUiAuthorityConfigured);
    }

    [Theory]
    [InlineData("unmanaged-conflict")]
    [InlineData("control-plane-mismatch")]
    [InlineData("identity-mismatch")]
    [InlineData("record-only")]
    public async Task Unproven_runtime_ownership_blocks_setup(string ownership)
    {
        var service = CreateService(
            Options(),
            RuntimeAbsent() with
            {
                Exists = true,
                OwnershipState = ownership,
                WarningCode = "seq_runtime_conflict"
            },
            new RecordingImageInspector(LocalImage()));

        var review = await service.ReviewAsync(
            new DiagnosticsSeqBootstrapReviewRequest(true, null),
            CancellationToken.None);

        Assert.False(review.Ready);
        Assert.Equal(ownership, review.Runtime.OwnershipState);
    }

    [Fact]
    public async Task Unexplained_existing_data_blocks_first_run()
    {
        var options = Options();
        Directory.CreateDirectory(options.HostDataPath);
        await File.WriteAllTextAsync(
            Path.Combine(options.HostDataPath, "unknown-data"),
            "data");
        var service = CreateService(
            options,
            RuntimeAbsent(),
            new RecordingImageInspector(LocalImage()));

        var review = await service.ReviewAsync(
            new DiagnosticsSeqBootstrapReviewRequest(true, null),
            CancellationToken.None);

        Assert.False(review.Ready);
        Assert.Equal("unexplained-data", review.Storage.State);
        Assert.Contains("seq_data_path_unexplained", review.Warnings);
    }

    private DiagnosticsSeqBootstrapService CreateService(
        SeqDiagnosticsOptions options,
        SeqStatusResponse runtime,
        RecordingImageInspector inspector,
        string? contentRootPath = null)
    {
        var environment = new TestHostEnvironment(contentRootPath ?? _root);
        var store = new SeqBootstrapStateStore(options, environment);
        return new DiagnosticsSeqBootstrapService(
            options,
            new SeqEffectiveConfigurationProvider(options, store),
            new SeqSecretResolver(environment),
            new FixedRuntimeReader(runtime),
            inspector,
            new SeqBootstrapStorageInspector(options, store, environment),
            store,
            new SeqBootstrapReviewStore(TimeProvider.System),
            TimeProvider.System);
    }

    private SeqDiagnosticsOptions Options() => new()
    {
        ManagementEnabled = false,
        EulaAccepted = false,
        AllowSetupPull = true,
        HostDataPath = Path.Combine(_root, "data", "seq"),
        SecretRootPath = Path.Combine(_root, "data", "secrets", "seq"),
        AdminPasswordHashEnvironmentVariableName = string.Empty,
        AdminPasswordHashFilePath = Path.Combine(
            _root,
            "data",
            "secrets",
            "seq",
            "admin-password-hash"),
        ApiKeyEnvironmentVariableName = string.Empty,
        ApiKeyFilePath = Path.Combine(
            _root,
            "data",
            "secrets",
            "seq",
            "ingestion-api-key"),
        BootstrapStatePath = Path.Combine(
            _root,
            "data",
            "diagnostics",
            "seq-bootstrap.json")
    };

    private static RuntimeImageInspection LocalImage() => new(
        $"sha256:{new string('a', 64)}",
        ["datalust/seq@sha256:" + new string('b', 64)],
        []);

    private static SeqStatusResponse RuntimeAbsent() => new(
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
        WarningCode: null);

    private static ClaimsPrincipal Principal(string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, role)
        ],
        "test"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FixedRuntimeReader(SeqStatusResponse runtime)
        : ISeqRuntimeStatusReader
    {
        public Task<SeqStatusResponse> GetStatusAsync(
            CancellationToken cancellationToken) => Task.FromResult(runtime);
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

    private sealed class TestHostEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Api.IntegrationTests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
