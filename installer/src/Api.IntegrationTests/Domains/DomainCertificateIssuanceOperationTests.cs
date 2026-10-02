using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Dns;
using Modules.Shared.Domains.Issuance;
using Modules.Shared.Domains.Renewal;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Domains;

public sealed class DomainCertificateIssuanceOperationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DOMAINS_WORKSPACE_01E_queue_is_domain_concurrent_idempotent_and_never_persists_plaintext_token()
    {
        await using var fixture = await Fixture.CreateAsync();
        var domain = await fixture.AddDomainAsync("example.com");
        const string token = "candidate-desec-secret";
        var requestId = Guid.NewGuid().ToString("D");

        var first = await fixture.Orchestrator.QueueAsync(
            domain.Id,
            new DomainCertificateIssueStartRequest(
                requestId,
                "owner@example.test",
                token,
                UseStaging: false),
            requestedByUserId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.Equal("Queued", first.Status);
        Assert.NotNull(first.Operation);

        var operation = await fixture.Db.RuntimeOperations
            .AsNoTracking()
            .SingleAsync(x => x.Id == first.Operation!.OperationId);
        Assert.Equal(DomainCertificateIssueOperation.ActiveIdempotencyKey, operation.IdempotencyKey);
        Assert.DoesNotContain(token, operation.InputJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(token, operation.ResultJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(token, operation.EvidenceJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(token, operation.LastError ?? string.Empty, StringComparison.Ordinal);

        var protectedCandidate = await fixture.SecretStore.ResolveProtectedAsync(
            domain.Id,
            DomainCertificateIssueOperation.CandidateSecretCategory,
            DomainCertificateIssueOperation.BuildCandidateSecretKey(operation.Id),
            CancellationToken.None);
        Assert.Equal(token, protectedCandidate);

        var replay = await fixture.Orchestrator.QueueAsync(
            domain.Id,
            new DomainCertificateIssueStartRequest(
                requestId,
                "owner@example.test",
                token,
                UseStaging: false),
            requestedByUserId: null,
            CancellationToken.None);
        Assert.True(replay.Accepted);
        Assert.Equal("AlreadyQueued", replay.Status);
        Assert.Equal(operation.Id, replay.Operation?.OperationId);

        var competing = await fixture.Orchestrator.QueueAsync(
            domain.Id,
            new DomainCertificateIssueStartRequest(
                Guid.NewGuid().ToString("D"),
                "owner@example.test",
                "different-secret",
                UseStaging: false),
            requestedByUserId: null,
            CancellationToken.None);
        Assert.False(competing.Accepted);
        Assert.Equal("OperationInProgress", competing.Status);
        Assert.Equal(operation.Id, competing.Operation?.OperationId);
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_B_active_projection_lists_only_non_terminal_domain_issuance()
    {
        await using var fixture = await Fixture.CreateAsync();
        var firstDomain = await fixture.AddDomainAsync("alpha.example");
        var secondDomain = await fixture.AddDomainAsync("bravo.example");

        var first = await fixture.Orchestrator.QueueAsync(
            firstDomain.Id,
            new DomainCertificateIssueStartRequest(
                Guid.NewGuid().ToString("D"),
                "owner@example.test",
                "alpha-token",
                UseStaging: false),
            requestedByUserId: null,
            CancellationToken.None);
        var second = await fixture.Orchestrator.QueueAsync(
            secondDomain.Id,
            new DomainCertificateIssueStartRequest(
                Guid.NewGuid().ToString("D"),
                "owner@example.test",
                "bravo-token",
                UseStaging: true),
            requestedByUserId: null,
            CancellationToken.None);

        var active = await fixture.Orchestrator.GetActiveAsync(CancellationToken.None);

        Assert.Equal(2, active.Count);
        Assert.Contains(active, item =>
            item.OperationId == first.Operation!.OperationId &&
            item.DomainId == firstDomain.Id &&
            item.BaseDomain == "alpha.example" &&
            item.Status == DomainCertificateIssueOperation.QueuedStatus &&
            !item.UseStaging);
        Assert.Contains(active, item =>
            item.OperationId == second.Operation!.OperationId &&
            item.DomainId == secondDomain.Id &&
            item.BaseDomain == "bravo.example" &&
            item.Status == DomainCertificateIssueOperation.QueuedStatus &&
            item.UseStaging);

        fixture.Executor.Succeed = true;
        await fixture.Orchestrator.ProcessPendingAsync(CancellationToken.None);

        Assert.Empty(await fixture.Orchestrator.GetActiveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01E_production_operation_persists_real_progress_promotes_candidate_and_is_rediscoverable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var domain = await fixture.AddDomainAsync("example.com");
        const string token = "production-candidate-token";
        fixture.Executor.Succeed = true;

        var queued = await fixture.Orchestrator.QueueAsync(
            domain.Id,
            new DomainCertificateIssueStartRequest(
                Guid.NewGuid().ToString("D"),
                "owner@example.test",
                token,
                UseStaging: false),
            requestedByUserId: null,
            CancellationToken.None);

        await fixture.Orchestrator.ProcessPendingAsync(CancellationToken.None);

        var projection = await fixture.Orchestrator.GetLatestAsync(domain.Id, CancellationToken.None);
        Assert.NotNull(projection);
        Assert.Equal(DomainCertificateIssueOperation.SucceededStatus, projection!.Status);
        Assert.True(projection.IsTerminal);
        Assert.NotNull(projection.CertificateId);
        Assert.True(projection.Result?.Succeeded);
        Assert.Contains(projection.Progress, item => item.PhaseCode == "certificate.dns-publish");
        Assert.Contains(projection.Progress, item => item.PhaseCode == "certificate.acme-validation");
        Assert.Contains(projection.Progress, item => item.PhaseCode == "certificate.renewal-credential");
        Assert.Equal(token, fixture.Executor.SeenProviderToken);

        var certificate = await fixture.Db.Certificates
            .AsNoTracking()
            .SingleAsync(x => x.CertificateId == projection.CertificateId);
        Assert.Equal(domain.Id, certificate.DomainId);

        var renewalCredential = await fixture.SecretStore.ResolveProtectedAsync(
            domain.Id,
            DomainRenewalSecretNames.DnsProviderCategory,
            DomainRenewalSecretNames.DesecProviderToken,
            CancellationToken.None);
        Assert.Equal(token, renewalCredential);

        var candidate = await fixture.SecretStore.ResolveProtectedAsync(
            domain.Id,
            DomainCertificateIssueOperation.CandidateSecretCategory,
            DomainCertificateIssueOperation.BuildCandidateSecretKey(projection.OperationId),
            CancellationToken.None);
        Assert.Null(candidate);

        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.RuntimeOperations
            .AsNoTracking()
            .SingleAsync(x => x.Id == projection.OperationId);
        var durableText = string.Join("\n", new[]
        {
            persisted.InputJson,
            persisted.ResultJson,
            persisted.EvidenceJson,
            persisted.LastError
        }.Where(x => x is not null));
        Assert.DoesNotContain(token, durableText, StringComparison.Ordinal);
        Assert.Equal(
            DomainCertificateIssueOperation.BuildCompletedIdempotencyKey(projection.RequestId),
            persisted.IdempotencyKey);

        var rediscovered = await fixture.Orchestrator.GetAsync(
            domain.Id,
            projection.OperationId,
            CancellationToken.None);
        Assert.Equal(projection.OperationId, rediscovered?.OperationId);
        Assert.Equal(projection.CertificateId, rediscovered?.CertificateId);
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01E_staging_operation_never_rotates_production_renewal_credential()
    {
        await using var fixture = await Fixture.CreateAsync();
        var domain = await fixture.AddDomainAsync("example.com");
        const string existingProductionToken = "existing-production-token";
        const string stagingToken = "staging-only-token";
        fixture.Executor.Succeed = true;

        await fixture.SecretStore.SetProtectedAsync(
            domain.Id,
            DomainRenewalSecretNames.DnsProviderCategory,
            DomainRenewalSecretNames.DesecProviderToken,
            existingProductionToken,
            "existing production credential",
            CancellationToken.None);

        var queued = await fixture.Orchestrator.QueueAsync(
            domain.Id,
            new DomainCertificateIssueStartRequest(
                Guid.NewGuid().ToString("D"),
                "owner@example.test",
                stagingToken,
                UseStaging: true),
            requestedByUserId: null,
            CancellationToken.None);

        await fixture.Orchestrator.ProcessPendingAsync(CancellationToken.None);

        var projection = await fixture.Orchestrator.GetAsync(
            domain.Id,
            queued.Operation!.OperationId,
            CancellationToken.None);
        Assert.Equal(DomainCertificateIssueOperation.SucceededStatus, projection?.Status);
        Assert.Contains(
            projection!.Result!.Evidence,
            item => item.Key == "renewal.credential" && item.Value == "unchanged by staging");

        var productionCredential = await fixture.SecretStore.ResolveProtectedAsync(
            domain.Id,
            DomainRenewalSecretNames.DnsProviderCategory,
            DomainRenewalSecretNames.DesecProviderToken,
            CancellationToken.None);
        Assert.Equal(existingProductionToken, productionCredential);

        var candidate = await fixture.SecretStore.ResolveProtectedAsync(
            domain.Id,
            DomainCertificateIssueOperation.CandidateSecretCategory,
            DomainCertificateIssueOperation.BuildCandidateSecretKey(queued.Operation.OperationId),
            CancellationToken.None);
        Assert.Null(candidate);
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01E_failure_is_structured_redacted_and_carries_diagnostics_deep_link_evidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var domain = await fixture.AddDomainAsync("example.com");
        const string token = "never-persist-this-token";
        fixture.Executor.Succeed = false;
        fixture.Executor.FailureTokenEcho = token;

        var queued = await fixture.Orchestrator.QueueAsync(
            domain.Id,
            new DomainCertificateIssueStartRequest(
                Guid.NewGuid().ToString("D"),
                "owner@example.test",
                token,
                UseStaging: false),
            requestedByUserId: null,
            CancellationToken.None);

        await fixture.Orchestrator.ProcessPendingAsync(CancellationToken.None);

        var projection = await fixture.Orchestrator.GetAsync(
            domain.Id,
            queued.Operation!.OperationId,
            CancellationToken.None);
        Assert.Equal(DomainCertificateIssueOperation.FailedStatus, projection?.Status);
        Assert.False(projection!.Result!.Succeeded);
        Assert.Equal("inc_certificate_issue", projection.DiagnosticsIncidentId);
        Assert.Contains(
            projection.Result.Evidence,
            item => item.Key == "incidentId" && item.Value == "inc_certificate_issue");
        Assert.DoesNotContain(
            token,
            System.Text.Json.JsonSerializer.Serialize(projection),
            StringComparison.Ordinal);

        var diagnostic = Assert.Single(fixture.Diagnostics.Requests);
        Assert.Contains(token, diagnostic.ExactSecrets ?? []);
        Assert.DoesNotContain(
            diagnostic.Details ?? new Dictionary<string, string?>(),
            pair => pair.Value?.Contains(token, StringComparison.Ordinal) == true);

        var candidate = await fixture.SecretStore.ResolveProtectedAsync(
            domain.Id,
            DomainCertificateIssueOperation.CandidateSecretCategory,
            DomainCertificateIssueOperation.BuildCandidateSecretKey(queued.Operation.OperationId),
            CancellationToken.None);
        Assert.Null(candidate);
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01E_terminal_candidate_credential_is_cleaned_after_restart_scan()
    {
        await using var fixture = await Fixture.CreateAsync();
        var domain = await fixture.AddDomainAsync("example.com");
        const string token = "cleanup-after-crash-token";

        var queued = await fixture.Orchestrator.QueueAsync(
            domain.Id,
            new DomainCertificateIssueStartRequest(
                Guid.NewGuid().ToString("D"),
                "owner@example.test",
                token,
                UseStaging: true),
            requestedByUserId: null,
            CancellationToken.None);

        var operation = await fixture.Db.RuntimeOperations
            .SingleAsync(x => x.Id == queued.Operation!.OperationId);
        operation.Status = DomainCertificateIssueOperation.FailedStatus;
        operation.CompletedAtUtc = fixture.Time.GetUtcNow().UtcDateTime;
        operation.IdempotencyKey = DomainCertificateIssueOperation.BuildCompletedIdempotencyKey(
            queued.Operation.RequestId);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        Assert.Equal(
            token,
            await fixture.SecretStore.ResolveProtectedAsync(
                domain.Id,
                DomainCertificateIssueOperation.CandidateSecretCategory,
                DomainCertificateIssueOperation.BuildCandidateSecretKey(queued.Operation.OperationId),
                CancellationToken.None));

        await fixture.Orchestrator.ProcessPendingAsync(CancellationToken.None);

        Assert.Null(await fixture.SecretStore.ResolveProtectedAsync(
            domain.Id,
            DomainCertificateIssueOperation.CandidateSecretCategory,
            DomainCertificateIssueOperation.BuildCandidateSecretKey(queued.Operation.OperationId),
            CancellationToken.None));
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01E_expired_running_lease_is_recovered_by_server_scan()
    {
        await using var fixture = await Fixture.CreateAsync();
        var domain = await fixture.AddDomainAsync("example.com");
        fixture.Executor.Succeed = true;

        var queued = await fixture.Orchestrator.QueueAsync(
            domain.Id,
            new DomainCertificateIssueStartRequest(
                Guid.NewGuid().ToString("D"),
                "owner@example.test",
                "restart-safe-token",
                UseStaging: true),
            requestedByUserId: null,
            CancellationToken.None);

        var operation = await fixture.Db.RuntimeOperations
            .SingleAsync(x => x.Id == queued.Operation!.OperationId);
        operation.Status = DomainCertificateIssueOperation.RunningStatus;
        operation.LockedUntilUtc = fixture.Time.GetUtcNow().UtcDateTime.AddMinutes(-1);
        operation.CurrentStep = "certificate.acme-validation";
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        await fixture.Orchestrator.ProcessPendingAsync(CancellationToken.None);

        var recovered = await fixture.Orchestrator.GetAsync(
            domain.Id,
            queued.Operation.OperationId,
            CancellationToken.None);
        Assert.Equal(DomainCertificateIssueOperation.SucceededStatus, recovered?.Status);
        Assert.True(recovered?.AttemptCount >= 1);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly ServiceProvider _services;

        private Fixture(
            string root,
            ServiceProvider services,
            MemDbContext db,
            ProtectedDomainSecretStore secretStore,
            MutableTimeProvider time,
            FakeExecutor executor,
            RecordingDiagnosticWriter diagnostics,
            DomainCertificateIssueOrchestrator orchestrator)
        {
            _root = root;
            _services = services;
            Db = db;
            SecretStore = secretStore;
            Time = time;
            Executor = executor;
            Diagnostics = diagnostics;
            Orchestrator = orchestrator;
        }

        public MemDbContext Db { get; }
        public ProtectedDomainSecretStore SecretStore { get; }
        public MutableTimeProvider Time { get; }
        public FakeExecutor Executor { get; }
        public RecordingDiagnosticWriter Diagnostics { get; }
        public DomainCertificateIssueOrchestrator Orchestrator { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"mem-domain-issue-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var keyRingPath = Path.Combine(root, "keys");
            Directory.CreateDirectory(keyRingPath);

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "control-plane.db")}")
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var services = new ServiceCollection();
            services.AddDataProtection()
                .SetApplicationName("mem-domain-issue-tests")
                .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
            var provider = services.BuildServiceProvider();

            var secretStore = new ProtectedDomainSecretStore(
                db,
                provider.GetRequiredService<IDataProtectionProvider>());
            var time = new MutableTimeProvider(Now);
            var executor = new FakeExecutor(db, time);
            var diagnostics = new RecordingDiagnosticWriter();
            var renewal = new DomainCertificateRenewalService(
                db,
                secretStore,
                new SuccessfulDnsProbe(),
                NullLogger<DomainCertificateRenewalService>.Instance);
            var orchestrator = new DomainCertificateIssueOrchestrator(
                db,
                secretStore,
                executor,
                renewal,
                time,
                diagnostics,
                NullLogger<DomainCertificateIssueOrchestrator>.Instance);

            return new Fixture(
                root,
                provider,
                db,
                secretStore,
                time,
                executor,
                diagnostics,
                orchestrator);
        }

        public async Task<DomainEntity> AddDomainAsync(string baseDomain)
        {
            var now = Time.GetUtcNow().UtcDateTime;
            var domain = new DomainEntity
            {
                Id = Guid.NewGuid(),
                BaseDomain = baseDomain,
                DisplayName = baseDomain,
                Purpose = "chat",
                IsMainPlatformDomain = false,
                DnsProvider = "desec",
                DnsZone = baseDomain,
                Status = "Active",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            Db.Domains.Add(domain);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return domain;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            _services.Dispose();
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch
            {
                // Test cleanup only.
            }
        }
    }

    private sealed class FakeExecutor(MemDbContext db, MutableTimeProvider time)
        : IDomainCertificateIssueExecutor
    {
        public bool Succeed { get; set; }
        public string? FailureTokenEcho { get; set; }
        public string? SeenProviderToken { get; private set; }

        public async Task<CertificateOperationResult> ExecuteAsync(
            DomainCertificateIssueExecutionRequest request,
            CertificateIssueProgressCallback progressCallback,
            CancellationToken cancellationToken)
        {
            SeenProviderToken = request.CertificateRequest.ProviderToken;
            await progressCallback(
                new CertificateIssueProgress(
                    "certificate.dns-publish",
                    "Publishing the DNS-01 challenge through deSEC."),
                cancellationToken);
            time.Advance(TimeSpan.FromSeconds(5));
            await progressCallback(
                new CertificateIssueProgress(
                    "certificate.acme-validation",
                    "Let's Encrypt is validating the DNS challenge."),
                cancellationToken);

            if (!Succeed)
            {
                return new CertificateOperationResult(
                    false,
                    "Failed",
                    $"Synthetic issuance failure {FailureTokenEcho}",
                    "AcmeChallengeValidationFailed",
                    $"Synthetic detail {FailureTokenEcho}",
                    [
                        new CertificateOperationEvidence("domain", request.CertificateRequest.Domain),
                        new CertificateOperationEvidence("secretEcho", FailureTokenEcho ?? string.Empty),
                        new CertificateOperationEvidence("privateKey", FailureTokenEcho ?? string.Empty, Sensitive: true)
                    ]);
            }

            var certificateId = $"cert-domain-{request.OperationId:N}";
            var now = time.GetUtcNow().UtcDateTime;
            db.Certificates.Add(new CertificateEntity
            {
                Id = Guid.NewGuid(),
                DomainId = request.DomainId,
                CertificateId = certificateId,
                CommonName = request.CertificateRequest.Domain,
                Provider = "desec",
                IsWildcard = true,
                IsStaging = request.CertificateRequest.UseStaging,
                IsMainPlatformCertificate = false,
                IsActive = true,
                Status = "Valid",
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(90),
                FullchainPath = "/protected/fullchain.pem",
                PrivateKeyPath = "/protected/privkey.pem"
            });
            await db.SaveChangesAsync(cancellationToken);

            return new CertificateOperationResult(
                true,
                "Succeeded",
                "Certificate issued, stored, and validated.",
                null,
                null,
                [
                    new CertificateOperationEvidence("certificateId", certificateId, Status: "Succeeded"),
                    new CertificateOperationEvidence("fullchainPath", "/protected/fullchain.pem", Status: "Succeeded"),
                    new CertificateOperationEvidence("privateKeyPath", "/protected/privkey.pem", Sensitive: true, Status: "Succeeded")
                ]);
        }
    }

    private sealed class SuccessfulDnsProbe : IDnsZoneAccessProbe
    {
        public Task<DnsZoneAccessProbeResult> ProbeAsync(
            DnsZoneAccessProbeRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DnsZoneAccessProbeResult(
                true,
                "ok",
                null,
                []));
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
                EventId: "evt_certificate_issue",
                IncidentId: request.CreateIncident ? "inc_certificate_issue" : null,
                WarningCode: null));
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
