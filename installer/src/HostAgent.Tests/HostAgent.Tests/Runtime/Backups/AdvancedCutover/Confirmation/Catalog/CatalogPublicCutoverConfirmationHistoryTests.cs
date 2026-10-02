using System.Text.Json;
using HostAgent.Runtime.Backups.AdvancedCutover.Confirmation.Catalog;
using HostAgent.Runtime.Backups.AdvancedCutover.Preflight;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace HostAgent.Tests.Runtime.Backups.AdvancedCutover.Confirmation.Catalog;

public sealed class CatalogPublicCutoverConfirmationHistoryTests
{
    [Fact]
    public async Task Lists_only_requested_catalog_entry_and_preserves_catalog_identity()
    {
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            $"mem-catalog-confirmation-history-{Guid.NewGuid():N}");

        try
        {
            await WriteConfirmationAsync(
                dataRoot,
                CreateConfirmation(
                    confirmationId: "confirmation-catalog-a",
                    catalogEntryId: "bkp_catalog_a",
                    previewId: "preview-a",
                    candidateId: "candidate-a"));

            await WriteConfirmationAsync(
                dataRoot,
                CreateConfirmation(
                    confirmationId: "confirmation-catalog-b",
                    catalogEntryId: "bkp_catalog_b",
                    previewId: "preview-b",
                    candidateId: "candidate-b"));

            var history = new CatalogPublicCutoverConfirmationHistoryService(
                new TestConfiguration(dataRoot));

            var listed = await history.ListConfirmationsAsync(
                catalogEntryId: "bkp_catalog_a",
                previewId: null,
                candidateId: null,
                status: null,
                max: 10,
                CancellationToken.None);

            var summary = Assert.Single(listed.Confirmations);
            Assert.Equal("confirmation-catalog-a", summary.ConfirmationId);
            Assert.Equal("bkp_catalog_a", summary.CatalogEntryId);
            Assert.Equal("backup-catalog", summary.SourceKind);
            Assert.Equal("preview-a", summary.PreviewId);
            Assert.Equal("candidate-a", summary.CandidateId);
            Assert.True(summary.ProductionExecutionLocked);
            Assert.False(summary.ExecutionAvailable);
        }
        finally
        {
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    private static async Task WriteConfirmationAsync(
        string dataRoot,
        CatalogPublicCutoverConfirmationResult result)
    {
        var directory = Path.Combine(
            dataRoot,
            "production-restore",
            "catalog-cutover-confirmations",
            result.ConfirmationId);

        Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(
            Path.Combine(directory, "catalog-public-cutover-confirmation.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static CatalogPublicCutoverConfirmationResult CreateConfirmation(
        string confirmationId,
        string catalogEntryId,
        string previewId,
        string candidateId)
    {
        var mutationSummary = new RuntimeStackBackupProductionRestoreMutationSummary(
            RuntimeChanged: false,
            ProductionContainersTouched: false,
            ProductionDatabasesTouched: false,
            DnsChanged: false,
            NpmRoutesChanged: false,
            CertificatesChanged: false,
            PublicRoutesChanged: false,
            FederationExposureChanged: false,
            Notes: []);

        var certificate = new CatalogPublicCutoverConfirmationCertificateGate(
            Component: "matrix",
            Host: "matrix.demo.test",
            Required: true,
            CertificateFound: true,
            CertificateEntityId: null,
            CommonName: "*.demo.test",
            NpmCertificateId: 2,
            ImportedToNpm: true,
            IsStaging: false,
            Passed: true,
            Status: "satisfied",
            Detail: "Test certificate.");

        var route = new CatalogPublicCutoverConfirmationRouteGate(
            Component: "matrix",
            Host: "matrix.demo.test",
            Required: true,
            PreviewAction: "create",
            PreviewAvailableForFutureExecution: true,
            PreviewRouteCurrentlyExists: false,
            FreshRouteCurrentlyExists: false,
            LiveStateMatchesPreview: true,
            CertificateReady: true,
            ExistingRouteAlreadyTargetsCandidate: false,
            UpstreamContainerName: "synapse-candidate",
            UpstreamPort: 8008,
            Passed: true,
            Status: "satisfied",
            Detail: "Test route.");

        return new CatalogPublicCutoverConfirmationResult(
            Source: "control-plane",
            Status: "confirmed_ready_for_future_executor",
            ConfirmationId: confirmationId,
            CatalogEntryId: catalogEntryId,
            SourceKind: "backup-catalog",
            RestoreSessionId: "restore-session-catalog",
            PreviewId: previewId,
            CandidateId: candidateId,
            FreshPlanId: "plan-catalog",
            RestoreMode: "recreate-production",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Operator: "Nigel",
            Note: null,
            ProductionExecutionLocked: true,
            ExecutionAvailable: false,
            Mutations: mutationSummary,
            Preview: new CatalogPublicCutoverConfirmationPreviewSummary(
                PreviewFound: true,
                PreviewId: previewId,
                Status: "ready_for_review",
                SourceKind: "backup-catalog",
                CatalogEntryId: catalogEntryId,
                CandidateId: candidateId,
                RestoreSessionId: "restore-session-catalog",
                CreatedAtUtc: DateTimeOffset.UtcNow,
                ProductionExecutionLocked: true,
                MutationFlagsClear: true,
                HasNoBlockers: true,
                HasNoErrors: true,
                ReadyForReview: true,
                CatalogEntryMatches: true,
                CandidateMatches: true,
                Detail: "Test preview."),
            Candidate: new CatalogPublicCutoverConfirmationCandidateSummary(
                CandidateFound: true,
                CandidateId: candidateId,
                Status: "private_candidate_ready",
                SourceKind: "backup-catalog",
                CatalogEntryId: catalogEntryId,
                RestoreSessionId: "restore-session-catalog",
                PrivateRuntimeStatus: "ready",
                PrivateOnly: true,
                Destroyed: false,
                DatabaseImportSucceeded: true,
                SynapseHealthPassed: true,
                ElementHealthPassed: true,
                MatrixServerName: "matrix.demo.test",
                SynapseContainerName: "synapse-candidate",
                ElementContainerName: "element-candidate",
                CatalogEntryMatches: true,
                RestoreSessionMatches: true,
                Detail: "Test candidate."),
            Certificates: new CatalogPublicCutoverConfirmationCertificateSummary(
                Matrix: certificate,
                Element: certificate with { Component = "element", Host = "chat.demo.test" },
                AllRequiredCertificatesReady: true,
                Notes: []),
            Routes: new CatalogPublicCutoverConfirmationRouteSummary(
                Matrix: route,
                Element: route with { Component = "element", Host = "chat.demo.test", UpstreamPort = 80 },
                AllRequiredRoutesEligible: true,
                LiveNpmStateMatchesPreview: true,
                Notes: []),
            Acknowledgements:
            [
                new CatalogPublicCutoverConfirmationAcknowledgement(
                    Code: "test",
                    Label: "Test acknowledgement",
                    Description: "Test acknowledgement.",
                    Severity: "info",
                    Required: true,
                    Acknowledged: true,
                    Status: "acknowledged")
            ],
            Checks: [],
            Blockers: [],
            Warnings: [],
            Errors: [],
            Detail: "Test confirmation.");
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
