using System.Text.Json;
using HostAgent.Runtime.Backups.AdvancedCutover.Preflight;
using HostAgent.Runtime.Backups.AdvancedCutover.Preview;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace HostAgent.Tests.Runtime.Backups.AdvancedCutover.Preview;

public sealed class CatalogPublicCutoverPreviewHistoryTests
{
    [Fact]
    public async Task Catalog_preview_history_summary_preserves_canonical_source_identity()
    {
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            $"mem-catalog-cutover-preview-history-{Guid.NewGuid():N}");

        try
        {
            var preview = CreatePreview();
            var previewDirectory = Path.Combine(
                dataRoot,
                "production-restore",
                "cutover-previews",
                preview.PreviewId);

            Directory.CreateDirectory(previewDirectory);

            await File.WriteAllTextAsync(
                Path.Combine(previewDirectory, "public-cutover-preview.json"),
                JsonSerializer.Serialize(
                    preview,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            var history = new RuntimeStackBackupPublicCutoverPreviewHistoryService(
                new TestConfiguration(dataRoot));

            var listed = await history.ListPreviewsAsync(
                validationId: null,
                candidateId: "candidate-catalog-preview",
                status: null,
                max: 10,
                CancellationToken.None);

            var summary = Assert.Single(listed.Previews);

            Assert.Equal("bkp_cutover_preview", summary.CatalogEntryId);
            Assert.Equal("backup-catalog", summary.SourceKind);
            Assert.Equal("restore-session-preview", summary.RestoreSessionId);
            Assert.Equal("candidate-catalog-preview", summary.CandidateId);
            Assert.True(summary.CandidateReady);
            Assert.True(summary.ElementCandidateReady);
        }
        finally
        {
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Catalog_preview_request_keeps_candidate_and_operator_inputs_explicit()
    {
        var request = new CatalogPublicCutoverPreviewRequest(
            CandidateId: "candidate-catalog-preview",
            TargetStackSlug: "demo-stack-candidate",
            RestoreMode: "recreate-production",
            IntendedMatrixHost: "matrix.demo.test",
            IntendedElementHost: "chat.demo.test");

        Assert.Equal("candidate-catalog-preview", request.CandidateId);
        Assert.Equal("demo-stack-candidate", request.TargetStackSlug);
        Assert.Equal("recreate-production", request.RestoreMode);
        Assert.Equal("matrix.demo.test", request.IntendedMatrixHost);
        Assert.Equal("chat.demo.test", request.IntendedElementHost);
    }

    private static RuntimeStackBackupPublicCutoverPreviewResult CreatePreview()
    {
        return new RuntimeStackBackupPublicCutoverPreviewResult(
            Source: "control-plane",
            Status: "ready_for_review",
            PreviewId: "preview-catalog",
            ValidationId: "legacy-catalog-alias",
            CandidateId: "candidate-catalog-preview",
            PlanId: "plan-catalog",
            RestoreMode: "recreate-production",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ProductionExecutionLocked: true,
            Mutations: new RuntimeStackBackupProductionRestoreMutationSummary(
                RuntimeChanged: false,
                ProductionContainersTouched: false,
                ProductionDatabasesTouched: false,
                DnsChanged: false,
                NpmRoutesChanged: false,
                CertificatesChanged: false,
                PublicRoutesChanged: false,
                FederationExposureChanged: false,
                Notes: []),
            Candidate: new RuntimeStackBackupPublicCutoverPreviewCandidateSummary(
                CandidateFound: true,
                CandidateId: "candidate-catalog-preview",
                ValidationId: "legacy-catalog-alias",
                Status: "private_candidate_ready",
                RestoreMode: "recreate-production",
                TargetStackSlug: "demo-stack-candidate",
                MatrixServerName: "matrix.demo.test",
                PrivateRuntimeId: "candidate-catalog-preview",
                PrivateRuntimeStatus: "ready",
                PrivateOnly: true,
                Destroyed: false,
                DatabaseImportSucceeded: true,
                SynapseHealthPassed: true,
                SynapseHealthResponse: "OK",
                NetworkName: "mem-restore-staging-candidate-catalog-preview",
                PostgresContainerName: "postgres-candidate-catalog-preview",
                SynapseContainerName: "synapse-candidate-catalog-preview",
                ElementDataPath: "/tmp/element",
                ElementConfigExtracted: true,
                ElementConfigPatched: true,
                ElementContainerName: "element-candidate-catalog-preview",
                ElementContainerId: null,
                ElementContainerStarted: true,
                ElementHealthPassed: true,
                ElementHealthResponse: "OK",
                ReadyForMatrixCutoverPreview: true,
                ReadyForElementCutoverPreview: true,
                Notes: []),
            Routes: new RuntimeStackBackupPublicCutoverPreviewRouteSummary(
                Matrix: CreateRouteAction("matrix", "matrix.demo.test"),
                Element: CreateRouteAction("element", "chat.demo.test"),
                Notes: []),
            Checks: [],
            Blockers: [],
            Warnings: [],
            Errors: [],
            Detail: "Test catalog preview.",
            CatalogEntryId: "bkp_cutover_preview",
            SourceKind: "backup-catalog",
            RestoreSessionId: "restore-session-preview");
    }

    private static RuntimeStackBackupPublicCutoverPreviewRouteAction CreateRouteAction(
        string component,
        string host)
    {
        return new RuntimeStackBackupPublicCutoverPreviewRouteAction(
            Component: component,
            Host: host,
            UpstreamContainerName: $"{component}-candidate",
            UpstreamPort: component == "matrix" ? 8008 : 80,
            ForwardScheme: "http",
            CertificateEntityId: null,
            NpmCertificateId: 2,
            CertificateAvailable: true,
            CertificateImportedToNpm: true,
            CertificateIsStaging: false,
            RouteCurrentlyExists: false,
            ExistingRoute: null,
            ExistingRouteAlreadyTargetsCandidate: false,
            Action: "create",
            AvailableForFutureExecution: true,
            WillMutateNow: false,
            RequiredBeforeExecution: [],
            Notes: [],
            Detail: "Test action.");
    }

    private sealed class TestConfiguration : IConfiguration
    {
        private readonly string _dataRoot;

        public TestConfiguration(string dataRoot)
        {
            _dataRoot = dataRoot;
        }

        public string? this[string key]
        {
            get => string.Equals(key, "HostAgent:DataRoot", StringComparison.Ordinal)
                ? _dataRoot
                : null;
            set { }
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => NoopChangeToken.Instance;

        public IConfigurationSection GetSection(string key) =>
            new TestConfigurationSection(key, this[key]);
    }

    private sealed class TestConfigurationSection : IConfigurationSection
    {
        public TestConfigurationSection(string key, string? value)
        {
            Key = key;
            Value = value;
        }

        public string Key { get; }
        public string Path => Key;
        public string? Value { get; set; }

        public string? this[string key]
        {
            get => null;
            set { }
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => NoopChangeToken.Instance;

        public IConfigurationSection GetSection(string key) =>
            new TestConfigurationSection(key, null);
    }

    private sealed class NoopChangeToken : IChangeToken
    {
        public static NoopChangeToken Instance { get; } = new();

        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;

        public IDisposable RegisterChangeCallback(
            Action<object?> callback,
            object? state) => NoopDisposable.Instance;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
