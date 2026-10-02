using System.Text.Json;
using Api.IntegrationTests.Runtime;
using HostAgent.Runtime.Diagnostics;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Operator.Diagnostics.Services;
using Modules.Shared.Docker;
using Modules.Setup.InstallPlans;
using Modules.Setup.InstallRuns;
using Modules.Setup.SupportReports;
using Shared.ControlPlane.Runtime;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Setup;

public sealed class SetupInstallationSupportReportTests
{
    [Fact]
    public async Task STARTUP_INSTALL_REL_01F_projects_failed_installation_without_secret_material()
    {
        var fixture = await Fixture.CreateAsync();
        await using (fixture)
        {
            var report = await fixture.Service.GenerateAsync(
                fixture.InstallationId,
                new SetupInstallationSupportReportRequest(
                    IncludeDockerEvidence: true,
                    Format: "json"),
                CancellationToken.None);

            Assert.Equal(fixture.InstallationId, report.Setup?.InstallationId);
            Assert.True(report.RuntimeContext.ControlPlaneExposure.IsPrivate);
            Assert.Equal(
                MemControlPlaneAccessModes.TrustedLan,
                report.RuntimeContext.ControlPlaneExposure.AccessMode);
            Assert.Equal(
                "192.168.10.20",
                report.RuntimeContext.ControlPlaneExposure.HostAddress);
            Assert.Equal(8443, report.RuntimeContext.ControlPlaneExposure.HostPort);
            Assert.Equal("Failed", report.Setup?.Status);
            Assert.Equal("Issue and import platform certificate", report.Setup?.Stage);
            Assert.Equal("review-fingerprint-test", report.ReviewedPlan?.Fingerprint);
            Assert.Equal("deltabox.test", report.ReviewedPlan?.BaseDomain);
            Assert.Contains("Coturn (TURN)", report.ReviewedPlan?.Services ?? []);
            Assert.Contains("mem-coturn", report.ReviewedPlan?.Containers ?? []);
            Assert.Equal(3478, report.ReviewedPlan!.Ports["coturn.turn"]);
            Assert.Equal(49160, report.ReviewedPlan!.Ports["coturn.relayMinUdp"]);
            Assert.Equal(49200, report.ReviewedPlan!.Ports["coturn.relayMaxUdp"]);
            Assert.True(report.ReviewedPlan?.SecretsOmitted == true);
            Assert.Equal("run-preflight-test", report.ServerChecks?.RunId);
            Assert.True(report.Recovery.RetryAllowed);
            Assert.Equal($"/setup/install/{fixture.InstallationId}", report.Recovery.SuggestedRoute);
            Assert.Contains(report.Steps, step => step.Sequence == 8 && step.Status == "Failed");
            Assert.Single(report.Diagnostics.Incidents);
            Assert.True(report.Diagnostics.DockerEvidence?.Available);
            Assert.Single(fixture.Writer.Requests);
            Assert.Equal(
                "setup.install_support_report.generated",
                fixture.Writer.Requests[0].EventCode);

            var json = JsonSerializer.Serialize(report);
            Assert.DoesNotContain("raw-super-secret", json, StringComparison.Ordinal);
            Assert.DoesNotContain("raw-bearer-token", json, StringComparison.Ordinal);
            Assert.DoesNotContain("dns-token-value", json, StringComparison.Ordinal);
            Assert.Contains("[redacted]", json, StringComparison.Ordinal);
            Assert.Contains(
                report.Redaction.OmittedContent,
                value => value.Contains("Passwords", StringComparison.OrdinalIgnoreCase));

            var formatter = new SetupInstallationSupportReportFormatter();
            var textDocument = formatter.Format(report, SetupSupportReportFormats.Text);
            Assert.Equal("text/plain", textDocument.ContentType);
            Assert.Equal("txt", textDocument.FileExtension);
            Assert.Contains("Installation timeline", textDocument.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("raw-super-secret", textDocument.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("dns-token-value", textDocument.Content, StringComparison.Ordinal);

            var jsonDocument = formatter.Format(report, SetupSupportReportFormats.Json);
            Assert.Equal("application/json", jsonDocument.ContentType);
            Assert.Equal("json", jsonDocument.FileExtension);
            Assert.DoesNotContain("raw-super-secret", jsonDocument.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("dns-token-value", jsonDocument.Content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01F_bounds_docker_log_content()
    {
        var fixture = await Fixture.CreateAsync(
            dockerLog: new string('x', 5000),
            maximumDockerLogCharacters: 512);
        await using (fixture)
        {
            var report = await fixture.Service.GenerateAsync(
                fixture.InstallationId,
                new SetupInstallationSupportReportRequest(IncludeDockerEvidence: true),
                CancellationToken.None);

            var log = report.Diagnostics.DockerEvidence?.LogTail;
            Assert.NotNull(log);
            Assert.True(log!.Content.Length <= 512);
            Assert.True(log.Truncated);
            Assert.True(report.Truncated);
            Assert.Contains(MemDiagnosticCodes.SupportReportDockerLogTruncated, report.Warnings);
        }
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01F_trace_fallback_works_without_installation_id()
    {
        var fixture = await Fixture.CreateAsync(includeInstallationResourceOnEvent: false);
        await using (fixture)
        {
            var report = await fixture.Service.GenerateAsync(
                installationId: null,
                new SetupInstallationSupportReportRequest(
                    TraceId: Fixture.TraceId,
                    IncludeDockerEvidence: false,
                    Format: "text"),
                CancellationToken.None);

            Assert.Null(report.Setup);
            Assert.Equal("trace-id", report.Selection.Mode);
            Assert.Equal(Fixture.TraceId, report.Selection.TraceId);
            Assert.NotEmpty(report.Diagnostics.Events);
            Assert.Contains(
                report.Warnings,
                value => value == "setup.support_report_docker_evidence_not_requested");
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string TraceId = "trace-setup-support-test";

        private readonly string _root;
        private readonly MemDbContext _db;

        private Fixture(
            string root,
            MemDbContext db,
            Guid installationId,
            SetupInstallationSupportReportService service,
            RecordingDiagnosticWriter writer)
        {
            _root = root;
            _db = db;
            InstallationId = installationId;
            Service = service;
            Writer = writer;
        }

        public Guid InstallationId { get; }
        public SetupInstallationSupportReportService Service { get; }
        public RecordingDiagnosticWriter Writer { get; }

        public static async Task<Fixture> CreateAsync(
            string? dockerLog = null,
            int maximumDockerLogCharacters = 1000,
            bool includeInstallationResourceOnEvent = true)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"mem-setup-support-report-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "control-plane.db")}")
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var installationId = Guid.NewGuid();
            var plan = BuildPlan();
            var planJson = JsonSerializer.Serialize(
                plan,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            planJson = planJson[..^1] +
                ",\"legacySecretFixture\":\"dns-token-value\"}";
            db.Installations.Add(new InstallationEntity
            {
                Id = installationId,
                Status = InstallationStatuses.Failed,
                ConfigJson = planJson,
                FrozenConfigJson = planJson,
                LastError = "NPM import failed password=raw-super-secret Authorization=Bearer raw-bearer-token",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-20),
                UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-19)
            });
            db.InstallationStepExecutions.AddRange(
                new InstallationStepExecutionEntity
                {
                    Id = Guid.NewGuid(),
                    InstallationId = installationId,
                    StepName = InstallStepNames.ValidateInstallPlan,
                    Sequence = 1,
                    Status = InstallationStepStatuses.Succeeded,
                    Message = "Reviewed plan validated.",
                    AttemptCount = 1,
                    StartedAtUtc = DateTime.UtcNow.AddMinutes(-19),
                    CompletedAtUtc = DateTime.UtcNow.AddMinutes(-19)
                },
                new InstallationStepExecutionEntity
                {
                    Id = Guid.NewGuid(),
                    InstallationId = installationId,
                    StepName = InstallStepNames.IssueAndImportPlatformCertificate,
                    Sequence = 8,
                    Status = InstallationStepStatuses.Failed,
                    Message = "Certificate retained; password=raw-super-secret",
                    ErrorMessage = "Authorization=Bearer raw-bearer-token",
                    AttemptCount = 2,
                    StartedAtUtc = DateTime.UtcNow.AddMinutes(-2),
                    CompletedAtUtc = DateTime.UtcNow.AddMinutes(-1)
                });
            await db.SaveChangesAsync();

            var incidentId = $"inc_{Guid.NewGuid():N}";
            var diagnosticEvent = new MemDiagnosticEvent(
                SchemaVersion: 1,
                EventId: $"evt_{Guid.NewGuid():N}",
                TimestampUtc: DateTimeOffset.UtcNow.AddSeconds(-30),
                Severity: MemDiagnosticSeverities.Error,
                EventCode: "installation.step.failed",
                Source: "install-runner",
                Feature: "installation",
                Stage: "step-8",
                Message: "NPM certificate import failed safely.",
                IncidentId: incidentId,
                TraceId: TraceId,
                SpanId: null,
                RequestId: null,
                CorrelationId: "corr-setup-support-test",
                OperationId: null,
                Resource: includeInstallationResourceOnEvent
                    ? new MemDiagnosticResource(
                        "installation",
                        installationId.ToString(),
                        "MEM installation",
                        WorkspacePath: $"/setup/install/{installationId}")
                    : null,
                Expected: null,
                Observed: null,
                Details: null,
                Exception: null,
                SuggestedAction: "Retry the failed step after correcting NPM readiness.",
                Retryable: true,
                RedactionsApplied: true,
                Truncated: false);

            var diagnosticsOptions = new DiagnosticsApiOptions
            {
                RetentionDays = 14,
                MaximumQueryWindowHours = 168,
                MaximumPageSize = 200,
                SupportReportMaximumEvents = 250,
                SupportReportMaximumBytes = 2 * 1024 * 1024,
                SupportReportMaximumDockerLogCharacters = maximumDockerLogCharacters
            };
            var reader = new RecordingDiagnosticReader([diagnosticEvent]);
            var writer = new RecordingDiagnosticWriter();
            var redactor = new MemDiagnosticRedactor(new MemDiagnosticsOptions());
            var health = new RecordingDiagnosticHealthReader();
            var docker = new RecordingDockerEvidenceReader(
                incidentId,
                diagnosticEvent.Resource ?? new MemDiagnosticResource("installation", installationId.ToString()),
                dockerLog ?? "safe docker log tail");
            var runtime = TestRuntimeContext.Create(root);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RuntimeImages:Postgres:ApprovedReference"] =
                        "postgres@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                })
                .Build();
            var limiter = new SetupInstallationSupportReportSizeLimiter(diagnosticsOptions);
            var service = new SetupInstallationSupportReportService(
                db,
                diagnosticsOptions,
                reader,
                health,
                redactor,
                docker,
                runtime,
                configuration,
                limiter,
                TimeProvider.System,
                NullLogger<SetupInstallationSupportReportService>.Instance,
                writer,
                exposureInspector: new FixedExposureInspector(
                    new MemControlPlaneExposureProjection(
                        MemControlPlaneExposureStates.Private,
                        MemControlPlaneAccessModes.TrustedLan,
                        "192.168.10.20",
                        8443,
                        BindingCount: 1,
                        WarningCode: null)));

            return new Fixture(root, db, installationId, service, writer);
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Test cleanup only.
            }
        }

        private static InstallPlan BuildPlan() =>
            new(
                General: new GeneralSetupConfig("standard", "Support report test", "deltabox.test"),
                Platform: new PlatformSetupConfig(
                    Postgres: new PostgresSetupConfig(true, "mem-postgres", "mem", "mem", true, "mem_postgres_data"),
                    Ingress: new IngressSetupConfig("Npm", true, "mem-npm", 80, 443, 81, true),
                    MemApi: new MemApiSetupConfig(false, "mem-api", "retired", 0),
                    MemWeb: new MemWebSetupConfig(false, "mem-web", "retired", 0)),
                PublicAccess: new PublicAccessSetupConfig(
                    Domain: "deltabox.test",
                    Zone: "deltabox.test",
                    AcmeEmail: "admin@deltabox.test",
                    DnsProvider: "desec",
                    UseStaging: true,
                    CertificateId: "cert-test",
                    NpmCertificateId: null,
                    ProxyHostDomain: null,
                    ForwardHost: null,
                    ForwardPort: null,
                    ForwardScheme: "http",
                    CertificateValidated: false,
                    ImportedToNpm: false,
                    ProxyHostVerified: false,
                    LastVerifiedAtUtc: null,
                    Preparation: new DomainPreparationSetupConfig(
                        "validated",
                        DateTime.UtcNow.AddMinutes(-30),
                        ProviderAccessConfirmed: true,
                        ProviderCredentialStored: true)),
                SupportTools: new SupportToolsSetupConfig(
                    new SeqSetupConfig(false, "mem-seq", 5341),
                    new PgAdminSetupConfig(false, "mem-pgadmin", 5050),
                    new PortainerSetupConfig(true, true, "portainer", 9443)),
                Preflight: new PreflightSetupConfig(
                    "run-preflight-test",
                    DateTimeOffset.UtcNow.AddMinutes(-25),
                    "Completed",
                    Passed: 8,
                    Warnings: 1,
                    Failed: 0,
                    Skipped: 0,
                    Unavailable: 1,
                    Unknown: 0,
                    BlockingIssueCount: 0,
                    WarningCheckKeys: ["disk.warning"],
                    UnavailableCheckKeys: ["host.utility.unavailable"]),
                Review: new ReviewSetupConfig(
                    DateTime.UtcNow.AddMinutes(-22),
                    "review-fingerprint-test"));
    }

    private sealed class RecordingDiagnosticReader(IReadOnlyList<MemDiagnosticEvent> events)
        : IMemDiagnosticEventReader
    {
        public Task<MemDiagnosticEventPage> QueryAsync(
            MemDiagnosticQuery query,
            CancellationToken cancellationToken = default)
        {
            var filtered = events.Where(@event =>
                    (query.FromUtc is null || @event.TimestampUtc >= query.FromUtc) &&
                    (query.UntilUtc is null || @event.TimestampUtc <= query.UntilUtc) &&
                    (query.TraceId is null || string.Equals(@event.TraceId, query.TraceId, StringComparison.Ordinal)) &&
                    (query.IncidentId is null || string.Equals(@event.IncidentId, query.IncidentId, StringComparison.Ordinal)) &&
                    (query.ResourceKind is null || string.Equals(@event.Resource?.Kind, query.ResourceKind, StringComparison.OrdinalIgnoreCase)))
                .Take(query.PageSize ?? 200)
                .ToArray();
            return Task.FromResult(new MemDiagnosticEventPage(
                query.FromUtc ?? DateTimeOffset.UtcNow.AddHours(-24),
                query.UntilUtc ?? DateTimeOffset.UtcNow,
                query.PageSize ?? 200,
                WindowClamped: false,
                filtered,
                NextCursor: null,
                Warnings: []));
        }
    }

    private sealed class RecordingDiagnosticWriter : IMemDiagnosticEventWriter
    {
        public List<MemDiagnosticWriteRequest> Requests { get; } = [];

        public Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new MemDiagnosticWriteResult(
                Stored: true,
                EventId: $"evt_{Guid.NewGuid():N}",
                IncidentId: null,
                WarningCode: null));
        }
    }

    private sealed class RecordingDiagnosticHealthReader : IMemDiagnosticHealthReader
    {
        public MemDiagnosticStoreHealth GetHealth() => new(
            Enabled: true,
            Status: "ready",
            LastWriteAtUtc: DateTimeOffset.UtcNow,
            LastReadAtUtc: DateTimeOffset.UtcNow,
            StoredEventCount: 12,
            DroppedEventCount: 0,
            MalformedLineCount: 0,
            LastWriteErrorCode: null,
            LastReadWarningCode: null,
            LastRetentionRunAtUtc: DateTimeOffset.UtcNow,
            LastRetentionDeletedFileCount: 0,
            LastRetentionDeletedBytes: 0,
            LastRetentionErrorCode: null);
    }

    private sealed class RecordingDockerEvidenceReader(
        string incidentId,
        MemDiagnosticResource resource,
        string logContent) : IMemDockerEvidenceReader
    {
        public Task<MemDockerEvidenceReadResult> ReadForIncidentAsync(
            string requestedIncidentId,
            IReadOnlyCollection<MemDiagnosticEvent> incidentEvents,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(incidentId, requestedIncidentId);
            return Task.FromResult(new MemDockerEvidenceReadResult(
                Available: true,
                Evidence: new MemDockerEvidence(
                    resource,
                    DateTimeOffset.UtcNow,
                    new MemDockerEvidenceContainer(
                        "mem-npm",
                        "running",
                        ExitCode: null,
                        Health: "healthy",
                        StartedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-10),
                        FinishedAtUtc: null,
                        RestartCount: 0,
                        Image: "jc21/nginx-proxy-manager:2.15.1"),
                    new MemDockerEvidenceLogTail(
                        RequestedLines: 200,
                        ReturnedLines: 1,
                        MaximumCharacters: Math.Max(1, logContent.Length),
                        Content: logContent,
                        Truncated: false,
                        RedactionsApplied: true),
                    Warnings: []),
                WarningCode: null,
                Warnings: []));
        }
    }
    private sealed class FixedExposureInspector(
        MemControlPlaneExposureProjection projection) : IControlPlaneExposureInspector
    {
        public Task<MemControlPlaneExposureProjection> InspectAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(projection);
    }

}
