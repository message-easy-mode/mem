using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Shared.Domains;
using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Renewal;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Domains;

public sealed class DomainCertificateRenewalWorkerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01B_more_than_30_days_remaining_is_not_eligible()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddReadyDomainAsync(
            "later.example",
            expiresAtUtc: Now.UtcDateTime.AddDays(31));

        var result = await fixture.Orchestrator.ScanAsync(CancellationToken.None);

        Assert.Equal(0, result.RenewalCyclesDue);
        Assert.Equal(0, fixture.Issuer.IssueCount);
        Assert.Empty(await fixture.Db.RuntimeOperations.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01B_exactly_30_days_and_expired_certificates_are_eligible()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddReadyDomainAsync(
            "threshold.example",
            expiresAtUtc: Now.UtcDateTime.AddDays(30));
        await fixture.AddReadyDomainAsync(
            "expired.example",
            expiresAtUtc: Now.UtcDateTime.AddHours(-1));

        var result = await fixture.Orchestrator.ScanAsync(CancellationToken.None);

        Assert.Equal(2, result.RenewalCyclesDue);
        Assert.Equal(2, result.AwaitingActivation);
        Assert.Equal(2, fixture.Issuer.IssueCount);

        var operations = await fixture.Db.RuntimeOperations
            .AsNoTracking()
            .Where(item => item.Operation == DomainCertificateRenewalOperation.OperationName)
            .ToListAsync();

        Assert.Equal(2, operations.Count);
        Assert.All(
            operations,
            operation => Assert.Equal(
                DomainCertificateRenewalOperation.AwaitingActivationStatus,
                operation.Status));
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01B_staging_or_disabled_domains_are_not_renewed()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddReadyDomainAsync(
            "staging.example",
            expiresAtUtc: Now.UtcDateTime.AddDays(1),
            isStaging: true);
        await fixture.AddReadyDomainAsync(
            "disabled.example",
            expiresAtUtc: Now.UtcDateTime.AddDays(1),
            autoRenewEnabled: false);

        var result = await fixture.Orchestrator.ScanAsync(CancellationToken.None);

        Assert.Equal(0, result.RenewalCyclesDue);
        Assert.Equal(0, fixture.Issuer.IssueCount);
        Assert.Empty(await fixture.Db.RuntimeOperations.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01B_main_domain_role_drift_fails_closed_without_issuing_or_switching()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeded = await fixture.AddReadyDomainAsync(
            "main-role-drift.example",
            expiresAtUtc: Now.UtcDateTime.AddDays(1));

        var domain = await fixture.Db.Domains
            .SingleAsync(item => item.Id == seeded.Domain.Id);
        domain.IsMainPlatformDomain = true;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.Orchestrator.ScanAsync(CancellationToken.None);

        Assert.Equal(1, result.RenewalCyclesDue);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, fixture.Issuer.IssueCount);

        var operation = await fixture.Db.RuntimeOperations
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal("MainPlatformCertificateRoleInconsistent", operation.LastError);

        var reloaded = await fixture.Db.Domains
            .AsNoTracking()
            .SingleAsync(item => item.Id == seeded.Domain.Id);
        Assert.Equal(seeded.SourceCertificate.Id, reloaded.ActiveCertificateId);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01B_unsupported_provider_never_reaches_issuance()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeded = await fixture.AddReadyDomainAsync(
            "unsupported.example",
            expiresAtUtc: Now.UtcDateTime.AddDays(1));

        var domain = await fixture.Db.Domains
            .SingleAsync(item => item.Id == seeded.Domain.Id);
        domain.DnsProvider = "future-provider";
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.Orchestrator.ScanAsync(CancellationToken.None);

        Assert.Equal(1, result.RenewalCyclesDue);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, fixture.Issuer.IssueCount);

        var operation = await fixture.Db.RuntimeOperations
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal("UnsupportedDnsProvider", operation.LastError);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01D_missing_credential_or_email_creates_immediate_safe_readiness_attention_without_issuing()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string credentialDomain = "missing-credential.example";
        const string emailDomain = "missing-email.example";

        var missingCredential = await fixture.AddReadyDomainAsync(
            credentialDomain,
            expiresAtUtc: Now.UtcDateTime.AddDays(45),
            withCredential: false);
        var missingEmail = await fixture.AddReadyDomainAsync(
            emailDomain,
            expiresAtUtc: Now.UtcDateTime.AddDays(20),
            acmeEmail: null);

        var result = await fixture.Orchestrator.ScanAsync(CancellationToken.None);

        Assert.Equal(0, result.RenewalCyclesDue);
        Assert.Equal(0, fixture.Issuer.IssueCount);
        Assert.Empty(await fixture.Db.RuntimeOperations.AsNoTracking().ToListAsync());

        var failures = fixture.Diagnostics.Requests
            .Where(request =>
                request.EventCode ==
                DomainCertificateRenewalOrchestrator.RenewalReadinessFailureEventCode)
            .ToArray();
        Assert.Equal(2, failures.Length);
        Assert.Contains(
            failures,
            request =>
                request.IncidentId ==
                DomainCertificateRenewalOrchestrator.BuildRenewalReadinessIncidentId(
                    missingCredential.Domain.Id) &&
                request.Observed?["errorCode"] == "RenewalCredentialRequired" &&
                request.Severity == MemDiagnosticSeverities.Warning);
        Assert.Contains(
            failures,
            request =>
                request.IncidentId ==
                DomainCertificateRenewalOrchestrator.BuildRenewalReadinessIncidentId(
                    missingEmail.Domain.Id) &&
                request.Observed?["errorCode"] == "AcmeEmailRequired" &&
                request.Severity == MemDiagnosticSeverities.Warning);

        Assert.All(
            failures,
            request =>
            {
                Assert.True(request.CreateIncident);
                Assert.DoesNotContain(
                    "desec-renewal-worker-token",
                    JsonSerializer.Serialize(request),
                    StringComparison.Ordinal);
            });

        // An unchanged hourly scan is calm: the same readiness condition does not
        // append another event or create another incident.
        fixture.Time.Advance(TimeSpan.FromHours(1));
        await fixture.Orchestrator.ScanAsync(CancellationToken.None);
        Assert.Equal(
            2,
            fixture.Diagnostics.Requests.Count(request =>
                request.EventCode ==
                DomainCertificateRenewalOrchestrator.RenewalReadinessFailureEventCode));

        await fixture.SecretStore.SetProtectedAsync(
            missingCredential.Domain.Id,
            DomainRenewalSecretNames.DnsProviderCategory,
            DomainRenewalSecretNames.DesecProviderToken,
            "rotated-desec-readiness-secret",
            "restored renewal credential",
            CancellationToken.None);

        await fixture.Orchestrator.ScanAsync(CancellationToken.None);

        var recovered = Assert.Single(fixture.Diagnostics.Requests.Where(request =>
            request.EventCode ==
            DomainCertificateRenewalOrchestrator.RenewalReadinessRecoveredEventCode));
        Assert.Equal(
            DomainCertificateRenewalOrchestrator.BuildRenewalReadinessIncidentId(
                missingCredential.Domain.Id),
            recovered.IncidentId);
        Assert.False(recovered.CreateIncident);

        Assert.Contains(
            fixture.IncidentLifecycle.Resolutions,
            resolution =>
                resolution.IncidentId ==
                DomainCertificateRenewalOrchestrator.BuildRenewalReadinessIncidentId(
                    missingCredential.Domain.Id));
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01D_readiness_incident_updates_when_the_blocker_changes_without_spamming_a_new_incident()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeded = await fixture.AddReadyDomainAsync(
            "readiness-transition.example",
            expiresAtUtc: Now.UtcDateTime.AddDays(20),
            withCredential: false);

        await fixture.Orchestrator.ScanAsync(CancellationToken.None);

        await fixture.SecretStore.SetProtectedAsync(
            seeded.Domain.Id,
            DomainRenewalSecretNames.DnsProviderCategory,
            DomainRenewalSecretNames.DesecProviderToken,
            "rotated-readiness-transition-secret",
            "replacement renewal credential",
            CancellationToken.None);

        var policy = await fixture.Db.DomainCertificateRenewalPolicies
            .SingleAsync(item => item.DomainId == seeded.Domain.Id);
        policy.AcmeEmail = null;
        policy.UpdatedAtUtc = fixture.Time.GetUtcNow().UtcDateTime;
        await fixture.Db.SaveChangesAsync();

        await fixture.Orchestrator.ScanAsync(CancellationToken.None);

        var failures = fixture.Diagnostics.Requests
            .Where(request =>
                request.EventCode ==
                DomainCertificateRenewalOrchestrator.RenewalReadinessFailureEventCode)
            .ToArray();

        Assert.Equal(2, failures.Length);
        Assert.Equal(failures[0].IncidentId, failures[1].IncidentId);
        Assert.Equal("RenewalCredentialRequired", failures[0].Observed?["errorCode"]);
        Assert.Equal("AcmeEmailRequired", failures[1].Observed?["errorCode"]);
        Assert.Equal(MemDiagnosticSeverities.Warning, failures[0].Severity);
        Assert.Equal(MemDiagnosticSeverities.Warning, failures[1].Severity);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01D_manual_renew_now_queues_the_same_durable_cycle_and_runs_outside_the_automatic_window()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeded = await fixture.AddReadyDomainAsync(
            "manual-renew.example",
            expiresAtUtc: Now.UtcDateTime.AddDays(60));
        var operatorId = Guid.NewGuid();

        var queued = await fixture.Orchestrator.QueueManualRenewalAsync(
            seeded.Domain.Id,
            operatorId,
            CancellationToken.None);

        Assert.True(queued.Accepted);
        Assert.Equal("Queued", queued.Status);
        Assert.NotNull(queued.OperationId);

        var operation = await fixture.Db.RuntimeOperations
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(seeded.Domain.Id, operation.DomainId);
        Assert.Equal(DomainCertificateRenewalOperation.OperationName, operation.Operation);
        Assert.Equal(DomainCertificateRenewalOperation.QueuedStatus, operation.Status);
        Assert.Equal("operator", operation.RequestedBy);
        Assert.Equal(operatorId, operation.RequestedByUserId);

        var scan = await fixture.Orchestrator.ScanAsync(CancellationToken.None);

        Assert.Equal(1, scan.RenewalCyclesDue);
        Assert.Equal(1, fixture.Issuer.IssueCount);
        var completedQueue = await fixture.Db.RuntimeOperations
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(
            DomainCertificateRenewalOperation.AwaitingActivationStatus,
            completedQueue.Status);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01B_one_cycle_is_idempotent_across_concurrent_and_repeated_scans()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddReadyDomainAsync(
            "idempotent.example",
            expiresAtUtc: Now.UtcDateTime.AddDays(1));

        await Task.WhenAll(
            fixture.Orchestrator.ScanAsync(CancellationToken.None),
            fixture.Orchestrator.ScanAsync(CancellationToken.None));

        await fixture.Orchestrator.ScanAsync(CancellationToken.None);

        Assert.Equal(1, fixture.Issuer.IssueCount);

        var operations = await fixture.Db.RuntimeOperations
            .AsNoTracking()
            .Where(item => item.Operation == DomainCertificateRenewalOperation.OperationName)
            .ToListAsync();

        var operation = Assert.Single(operations);
        Assert.Equal(
            DomainCertificateRenewalOperation.AwaitingActivationStatus,
            operation.Status);
        Assert.StartsWith("renewal:", operation.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01B_failed_attempt_respects_24_hour_retry_boundary()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Issuer.FailuresRemaining = 1;
        const string secret = "desec-retry-secret-that-must-never-enter-diagnostics";

        await fixture.AddReadyDomainAsync(
            "retry.example",
            expiresAtUtc: Now.UtcDateTime.AddDays(1),
            providerToken: secret);

        await fixture.Orchestrator.ScanAsync(CancellationToken.None);

        var failed = await fixture.Db.RuntimeOperations
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(DomainCertificateRenewalOperation.FailedStatus, failed.Status);
        Assert.Equal(1, failed.AttemptCount);
        Assert.Equal(1, fixture.Issuer.IssueCount);

        var failure = Assert.Single(fixture.Diagnostics.Requests.Where(request =>
            request.EventCode == "domains.certificate.renewal_failed"));
        Assert.True(failure.CreateIncident);
        Assert.Equal(
            DomainCertificateRenewalOrchestrator.BuildRenewalIncidentId(failed.Id),
            failure.IncidentId);
        Assert.Equal(MemDiagnosticSeverities.Critical, failure.Severity);
        Assert.Equal(failed.Id, failure.OperationId);
        Assert.Equal("SyntheticIssueFailure", failure.Observed?["errorCode"]);
        Assert.DoesNotContain(
            secret,
            JsonSerializer.Serialize(failure),
            StringComparison.Ordinal);

        fixture.Time.Advance(TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)));
        await fixture.Orchestrator.ScanAsync(CancellationToken.None);
        Assert.Equal(1, fixture.Issuer.IssueCount);
        Assert.Single(fixture.Diagnostics.Requests.Where(request =>
            request.EventCode == "domains.certificate.renewal_failed"));

        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        await fixture.Orchestrator.ScanAsync(CancellationToken.None);

        var retried = await fixture.Db.RuntimeOperations
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(2, retried.AttemptCount);
        Assert.Equal(
            DomainCertificateRenewalOperation.AwaitingActivationStatus,
            retried.Status);
        Assert.Equal(2, fixture.Issuer.IssueCount);

        var recovered = Assert.Single(fixture.Diagnostics.Requests.Where(request =>
            request.EventCode == "domains.certificate.renewal_recovered"));
        Assert.False(recovered.CreateIncident);
        Assert.Equal(failure.IncidentId, recovered.IncidentId);
        Assert.Equal(
            MemDiagnosticIncidentLifecycle.ResolvedState,
            recovered.Details?[MemDiagnosticIncidentLifecycle.StateDetailKey]);
        Assert.Equal(
            MemDiagnosticIncidentLifecycle.SelfRecoveredResolutionCode,
            recovered.Details?[MemDiagnosticIncidentLifecycle.ResolutionCodeDetailKey]);
        Assert.DoesNotContain(
            secret,
            JsonSerializer.Serialize(recovered),
            StringComparison.Ordinal);

        var resolved = Assert.Single(fixture.IncidentLifecycle.Resolutions);
        Assert.Equal(failure.IncidentId, resolved.IncidentId);
        Assert.Equal(fixture.Time.GetUtcNow(), resolved.ObservedThroughAtUtc);
        Assert.StartsWith("evt_test_", resolved.ObservedThroughEventId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01B_repeated_failed_retries_append_to_one_incident_per_cycle()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Issuer.FailuresRemaining = 2;

        await fixture.AddReadyDomainAsync(
            "deduplicated-incident.example",
            expiresAtUtc: Now.UtcDateTime.AddDays(10));

        await fixture.Orchestrator.ScanAsync(CancellationToken.None);
        fixture.Time.Advance(TimeSpan.FromHours(24));
        await fixture.Orchestrator.ScanAsync(CancellationToken.None);

        var failures = fixture.Diagnostics.Requests
            .Where(request => request.EventCode == "domains.certificate.renewal_failed")
            .ToArray();
        Assert.Equal(2, failures.Length);
        Assert.Single(failures.Select(request => request.IncidentId!).Distinct(StringComparer.Ordinal));
        Assert.All(failures, request => Assert.True(request.CreateIncident));

        var operation = await fixture.Db.RuntimeOperations
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(2, operation.AttemptCount);
        Assert.Equal(
            DomainCertificateRenewalOrchestrator.BuildRenewalIncidentId(operation.Id),
            failures[0].IncidentId);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01B_expired_lease_recovers_existing_candidate_without_duplicate_issuance()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeded = await fixture.AddReadyDomainAsync(
            "restart.example",
            expiresAtUtc: Now.UtcDateTime.AddDays(1));

        var operationId = Guid.NewGuid();
        var key = DomainCertificateRenewalOperation.BuildIdempotencyKey(
            seeded.Domain.Id,
            seeded.SourceCertificate.Id,
            seeded.SourceCertificate.ExpiresAtUtc!.Value);

        fixture.Db.RuntimeOperations.Add(new RuntimeOperationEntity
        {
            Id = operationId,
            DomainId = seeded.Domain.Id,
            Operation = DomainCertificateRenewalOperation.OperationName,
            Status = DomainCertificateRenewalOperation.RunningStatus,
            IdempotencyKey = key,
            RequestedBy = "system",
            RequestedAtUtc = Now.UtcDateTime.AddMinutes(-20),
            StartedAtUtc = Now.UtcDateTime.AddMinutes(-20),
            CurrentStep = "certificate.validate",
            AttemptCount = 1,
            LockedUntilUtc = Now.UtcDateTime.AddMinutes(-1),
            HostMutationLevel = "certificate-issuance",
            RequiresConfirmation = false
        });

        var candidate = fixture.Issuer.CreateCandidate(
            operationId,
            seeded.Domain,
            seeded.SourceCertificate);
        fixture.Db.Certificates.Add(candidate);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Orchestrator.ScanAsync(CancellationToken.None);

        Assert.Equal(1, result.AwaitingActivation);
        Assert.Equal(0, fixture.Issuer.IssueCount);

        var operation = await fixture.Db.RuntimeOperations
            .AsNoTracking()
            .SingleAsync(item => item.Id == operationId);
        Assert.Equal(2, operation.AttemptCount);
        Assert.Equal(
            DomainCertificateRenewalOperation.AwaitingActivationStatus,
            operation.Status);
        Assert.Contains(candidate.CertificateId, operation.ResultJson ?? string.Empty, StringComparison.Ordinal);

        var domain = await fixture.Db.Domains
            .AsNoTracking()
            .SingleAsync(item => item.Id == seeded.Domain.Id);
        Assert.Equal(seeded.SourceCertificate.Id, domain.ActiveCertificateId);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01B_candidate_stays_domain_owned_old_certificate_stays_active_and_operation_is_redacted()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string secret = "desec-worker-secret-that-must-never-be-persisted";

        var seeded = await fixture.AddReadyDomainAsync(
            "candidate.example",
            expiresAtUtc: Now.UtcDateTime.AddDays(1),
            providerToken: secret);

        var result = await fixture.Orchestrator.ScanAsync(CancellationToken.None);

        Assert.Equal(1, result.AwaitingActivation);
        Assert.Equal(secret, Assert.Single(fixture.Issuer.ProviderTokens));

        var operation = await fixture.Db.RuntimeOperations
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(seeded.Domain.Id, operation.DomainId);
        Assert.Equal(
            DomainCertificateRenewalOperation.AwaitingActivationStatus,
            operation.Status);
        Assert.Equal("awaiting-activation", operation.CurrentStep);
        Assert.Null(operation.LockedUntilUtc);
        Assert.Null(operation.CompletedAtUtc);

        var candidate = await fixture.Db.Certificates
            .AsNoTracking()
            .SingleAsync(item =>
                item.DomainId == seeded.Domain.Id &&
                item.Id != seeded.SourceCertificate.Id);

        Assert.False(candidate.IsStaging);
        Assert.Equal(seeded.Domain.Id, candidate.DomainId);

        var domain = await fixture.Db.Domains
            .AsNoTracking()
            .SingleAsync(item => item.Id == seeded.Domain.Id);
        Assert.Equal(seeded.SourceCertificate.Id, domain.ActiveCertificateId);

        var persistedOperation = string.Join(
            "\n",
            operation.InputJson,
            operation.ResultJson,
            operation.EvidenceJson,
            operation.LastError);
        Assert.DoesNotContain(secret, persistedOperation, StringComparison.Ordinal);
        Assert.Contains("candidateCertificateId", operation.ResultJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("validation", operation.EvidenceJson ?? string.Empty, StringComparison.Ordinal);

        Assert.Contains(
            fixture.Issuer.ObservedProgressStates,
            state =>
                state.CurrentStep == "certificate.acme-order" &&
                (state.EvidenceJson?.Contains("certificate.acme-order", StringComparison.Ordinal) ?? false));
        Assert.All(
            fixture.Issuer.ObservedProgressStates,
            state => Assert.DoesNotContain(
                secret,
                state.EvidenceJson ?? string.Empty,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01B_worker_scan_failure_creates_one_stable_safe_diagnostics_incident()
    {
        var time = new MutableTimeProvider(Now);
        var diagnostics = new RecordingDiagnosticWriter(time);
        var worker = new DomainCertificateRenewalWorker(
            new ThrowingScopeFactory(),
            time,
            NullLogger<DomainCertificateRenewalWorker>.Instance,
            diagnostics);

        await worker.RunScanSafelyAsync(CancellationToken.None);

        var request = Assert.Single(diagnostics.Requests);
        Assert.Equal(DomainCertificateRenewalWorker.ScanFailureEventCode, request.EventCode);
        Assert.Equal(DomainCertificateRenewalWorker.ScanIncidentId, request.IncidentId);
        Assert.True(request.CreateIncident);
        Assert.Equal(MemDiagnosticSeverities.Error, request.Severity);
        Assert.Equal("InvalidOperationException", request.Details?["failureType"]);
        Assert.DoesNotContain(
            "synthetic renewal scan failure",
            JsonSerializer.Serialize(request),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01B_successful_scan_resolves_the_stable_scan_incident()
    {
        await using var fixture = await Fixture.CreateAsync();
        var incidentLifecycle = new RecordingIncidentLifecycleWriter();
        var services = new ServiceCollection();
        services.AddSingleton(fixture.Orchestrator);
        services.AddSingleton<IMemDiagnosticIncidentLifecycleWriter>(incidentLifecycle);
        await using var provider = services.BuildServiceProvider();

        var diagnostics = new RecordingDiagnosticWriter(fixture.Time);
        var reader = new FixedDiagnosticReader(
            new MemDiagnosticEvent(
                SchemaVersion: 1,
                EventId: "evt_scan_failed_0001",
                TimestampUtc: fixture.Time.GetUtcNow().AddMinutes(-1),
                Severity: MemDiagnosticSeverities.Error,
                EventCode: DomainCertificateRenewalWorker.ScanFailureEventCode,
                Source: nameof(DomainCertificateRenewalWorker),
                Feature: "domains",
                Stage: "automatic-renewal-scan",
                Message: "MEM could not complete the automatic certificate renewal scan.",
                IncidentId: DomainCertificateRenewalWorker.ScanIncidentId,
                TraceId: null,
                SpanId: null,
                RequestId: null,
                CorrelationId: null,
                OperationId: null,
                Resource: null,
                Expected: null,
                Observed: null,
                Details: null,
                Exception: null,
                SuggestedAction: null,
                Retryable: true,
                RedactionsApplied: false,
                Truncated: false));
        var worker = new DomainCertificateRenewalWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            fixture.Time,
            NullLogger<DomainCertificateRenewalWorker>.Instance,
            diagnostics,
            reader);

        await worker.RunScanSafelyAsync(CancellationToken.None);

        var recovered = Assert.Single(diagnostics.Requests.Where(request =>
            request.EventCode == DomainCertificateRenewalWorker.ScanRecoveredEventCode));
        Assert.Equal(DomainCertificateRenewalWorker.ScanIncidentId, recovered.IncidentId);
        Assert.False(recovered.CreateIncident);

        var resolved = Assert.Single(incidentLifecycle.Resolutions);
        Assert.Equal(DomainCertificateRenewalWorker.ScanIncidentId, resolved.IncidentId);
        Assert.Equal(fixture.Time.GetUtcNow(), resolved.ObservedThroughAtUtc);
    }

    [Theory]
    [InlineData(-1, MemDiagnosticSeverities.Critical)]
    [InlineData(1, MemDiagnosticSeverities.Critical)]
    [InlineData(7, MemDiagnosticSeverities.Critical)]
    [InlineData(8, MemDiagnosticSeverities.Error)]
    [InlineData(14, MemDiagnosticSeverities.Error)]
    [InlineData(15, MemDiagnosticSeverities.Warning)]
    public void DOMAINS_CERTIFICATE_RENEWAL_01D_diagnostics_escalate_as_certificate_expiry_approaches(
        int daysRemaining,
        string expectedSeverity)
    {
        var severity = DomainCertificateRenewalOrchestrator.ResolveRenewalFailureSeverity(
            Now.UtcDateTime.AddDays(daysRemaining),
            Now.UtcDateTime);

        Assert.Equal(expectedSeverity, severity);
    }

    [Fact]
    public void DOMAINS_CERTIFICATE_RENEWAL_01B_registers_a_server_owned_background_worker()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddLogging();
        services.AddSharedDomains(configuration);

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(DomainCertificateRenewalWorker));
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
            FakeCandidateIssuer issuer,
            RecordingDiagnosticWriter diagnostics,
            RecordingDiagnosticReader diagnosticReader,
            RecordingIncidentLifecycleWriter incidentLifecycle,
            DomainCertificateRenewalOrchestrator orchestrator)
        {
            _root = root;
            _services = services;
            Db = db;
            SecretStore = secretStore;
            Time = time;
            Issuer = issuer;
            Diagnostics = diagnostics;
            DiagnosticReader = diagnosticReader;
            IncidentLifecycle = incidentLifecycle;
            Orchestrator = orchestrator;
        }

        public MemDbContext Db { get; }
        public ProtectedDomainSecretStore SecretStore { get; }
        public MutableTimeProvider Time { get; }
        public FakeCandidateIssuer Issuer { get; }
        public RecordingDiagnosticWriter Diagnostics { get; }
        public RecordingDiagnosticReader DiagnosticReader { get; }
        public RecordingIncidentLifecycleWriter IncidentLifecycle { get; }
        public DomainCertificateRenewalOrchestrator Orchestrator { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"mem-domain-renewal-worker-{Guid.NewGuid():N}");
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
                .SetApplicationName("mem-domain-renewal-worker-tests")
                .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
            var provider = services.BuildServiceProvider();

            var secretStore = new ProtectedDomainSecretStore(
                db,
                provider.GetRequiredService<IDataProtectionProvider>());
            var time = new MutableTimeProvider(Now);
            var issuer = new FakeCandidateIssuer(db, time);
            var diagnostics = new RecordingDiagnosticWriter(time);
            var diagnosticReader = new RecordingDiagnosticReader(diagnostics);
            var incidentLifecycle = new RecordingIncidentLifecycleWriter();
            var orchestrator = new DomainCertificateRenewalOrchestrator(
                db,
                secretStore,
                issuer,
                time,
                NullLogger<DomainCertificateRenewalOrchestrator>.Instance,
                diagnostics,
                incidentLifecycle,
                candidateActivator: null,
                diagnosticReader: diagnosticReader);

            return new Fixture(
                root,
                provider,
                db,
                secretStore,
                time,
                issuer,
                diagnostics,
                diagnosticReader,
                incidentLifecycle,
                orchestrator);
        }

        public async Task<SeededDomain> AddReadyDomainAsync(
            string baseDomain,
            DateTime expiresAtUtc,
            bool autoRenewEnabled = true,
            bool isStaging = false,
            bool withCredential = true,
            string? acmeEmail = "ops@example.test",
            string providerToken = "desec-renewal-worker-token")
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

            var source = new CertificateEntity
            {
                Id = Guid.NewGuid(),
                DomainId = domain.Id,
                CertificateId = $"cert-source-{Guid.NewGuid():N}",
                CommonName = $"*.{baseDomain}",
                Provider = "desec",
                IsWildcard = true,
                IsStaging = isStaging,
                IsMainPlatformCertificate = false,
                IsActive = true,
                Status = "Active",
                CreatedAtUtc = now.AddDays(-60),
                ExpiresAtUtc = expiresAtUtc,
                FullchainPath = "/protected/source/fullchain.pem",
                PrivateKeyPath = "/protected/source/privkey.pem"
            };
            Db.Certificates.Add(source);
            await Db.SaveChangesAsync();

            domain.ActiveCertificateId = source.Id;
            domain.UpdatedAtUtc = now;

            Db.DomainCertificateRenewalPolicies.Add(
                new DomainCertificateRenewalPolicyEntity
                {
                    DomainId = domain.Id,
                    AutoRenewEnabled = autoRenewEnabled,
                    AcmeEmail = acmeEmail,
                    RenewalWindowDays = DomainCertificateRenewalService.DefaultRenewalWindowDays,
                    RetryIntervalHours = DomainCertificateRenewalService.DefaultRetryIntervalHours,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            await Db.SaveChangesAsync();

            if (withCredential)
            {
                await SecretStore.SetProtectedAsync(
                    domain.Id,
                    DomainRenewalSecretNames.DnsProviderCategory,
                    DomainRenewalSecretNames.DesecProviderToken,
                    providerToken,
                    "test renewal credential",
                    CancellationToken.None);
            }

            Db.ChangeTracker.Clear();
            return new SeededDomain(domain, source);
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

    private sealed record SeededDomain(
        DomainEntity Domain,
        CertificateEntity SourceCertificate);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() =>
            throw new InvalidOperationException("synthetic renewal scan failure");
    }

    private sealed class FixedDiagnosticReader(params MemDiagnosticEvent[] events)
        : IMemDiagnosticEventReader
    {
        public Task<MemDiagnosticEventPage> QueryAsync(
            MemDiagnosticQuery query,
            CancellationToken cancellationToken = default)
        {
            var filtered = events
                .Where(@event => query.IncidentId is null || string.Equals(
                    @event.IncidentId,
                    query.IncidentId,
                    StringComparison.Ordinal))
                .ToArray();
            return Task.FromResult(new MemDiagnosticEventPage(
                query.FromUtc ?? DateTimeOffset.MinValue,
                query.UntilUtc ?? DateTimeOffset.MaxValue,
                query.PageSize ?? 200,
                WindowClamped: false,
                Events: filtered,
                NextCursor: null,
                Warnings: Array.Empty<string>()));
        }
    }

    private sealed class RecordingDiagnosticWriter(TimeProvider timeProvider)
        : IMemDiagnosticEventWriter
    {
        private int _sequence;

        public List<MemDiagnosticWriteRequest> Requests { get; } = [];
        public List<MemDiagnosticEvent> Events { get; } = [];

        public Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var incidentId = request.IncidentId ??
                (request.CreateIncident ? $"inc_test_{Guid.NewGuid():N}" : null);
            var eventId = $"evt_test_{++_sequence:0000}";
            var timestamp = timeProvider.GetUtcNow();

            Events.Add(new MemDiagnosticEvent(
                SchemaVersion: 1,
                EventId: eventId,
                TimestampUtc: timestamp,
                Severity: request.Severity,
                EventCode: request.EventCode,
                Source: request.Source,
                Feature: request.Feature,
                Stage: request.Stage,
                Message: request.Message,
                IncidentId: incidentId,
                TraceId: null,
                SpanId: null,
                RequestId: null,
                CorrelationId: null,
                OperationId: request.OperationId,
                Resource: request.Resource,
                Expected: request.Expected,
                Observed: request.Observed,
                Details: request.Details,
                Exception: null,
                SuggestedAction: request.SuggestedAction,
                Retryable: request.Retryable,
                RedactionsApplied: false,
                Truncated: false));

            return Task.FromResult(new MemDiagnosticWriteResult(
                Stored: true,
                EventId: eventId,
                IncidentId: incidentId,
                WarningCode: null,
                TimestampUtc: timestamp));
        }
    }

    private sealed class RecordingDiagnosticReader(RecordingDiagnosticWriter writer)
        : IMemDiagnosticEventReader
    {
        public Task<MemDiagnosticEventPage> QueryAsync(
            MemDiagnosticQuery query,
            CancellationToken cancellationToken = default)
        {
            var filtered = writer.Events
                .Where(@event => query.IncidentId is null || string.Equals(
                    @event.IncidentId,
                    query.IncidentId,
                    StringComparison.Ordinal))
                .OrderByDescending(@event => @event.TimestampUtc)
                .ThenByDescending(@event => @event.EventId, StringComparer.Ordinal)
                .Take(query.PageSize ?? 200)
                .ToArray();

            return Task.FromResult(new MemDiagnosticEventPage(
                query.FromUtc ?? DateTimeOffset.MinValue,
                query.UntilUtc ?? DateTimeOffset.MaxValue,
                query.PageSize ?? 200,
                WindowClamped: false,
                Events: filtered,
                NextCursor: null,
                Warnings: Array.Empty<string>()));
        }
    }

    private sealed class RecordingIncidentLifecycleWriter
        : IMemDiagnosticIncidentLifecycleWriter
    {
        public List<RecordedResolution> Resolutions { get; } = [];

        public Task ResolveSelfRecoveredAsync(
            string incidentId,
            DateTimeOffset observedThroughAtUtc,
            string observedThroughEventId,
            CancellationToken cancellationToken = default)
        {
            Resolutions.Add(new RecordedResolution(
                incidentId,
                observedThroughAtUtc,
                observedThroughEventId));
            return Task.CompletedTask;
        }
    }

    private sealed record RecordedResolution(
        string IncidentId,
        DateTimeOffset ObservedThroughAtUtc,
        string ObservedThroughEventId);

    private sealed class FakeCandidateIssuer(
        MemDbContext db,
        TimeProvider timeProvider) : IDomainCertificateRenewalCandidateIssuer
    {
        public int IssueCount { get; private set; }
        public int FailuresRemaining { get; set; }
        public List<string> ProviderTokens { get; } = [];
        public List<(string? CurrentStep, string? EvidenceJson)> ObservedProgressStates { get; } = [];

        public Task<DomainRenewalCandidateIssueResult?> TryRecoverCandidateAsync(
            DomainRenewalCandidateRequest request,
            CancellationToken cancellationToken) =>
            FindCandidateAsync(request, cancellationToken);

        public async Task<DomainRenewalCandidateIssueResult> IssueCandidateAsync(
            DomainRenewalCandidateRequest request,
            string providerToken,
            CertificateIssueProgressCallback progressCallback,
            CancellationToken cancellationToken)
        {
            IssueCount++;
            ProviderTokens.Add(providerToken);

            await progressCallback(
                new CertificateIssueProgress(
                    "certificate.acme-order",
                    "Creating certificate order."),
                cancellationToken);
            await CaptureProgressStateAsync(request.OperationId, cancellationToken);

            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                return new DomainRenewalCandidateIssueResult(
                    false,
                    "Failed",
                    "SyntheticIssueFailure",
                    null,
                    null);
            }

            await progressCallback(
                new CertificateIssueProgress(
                    "certificate.validate",
                    "Validating replacement candidate."),
                cancellationToken);
            await CaptureProgressStateAsync(request.OperationId, cancellationToken);

            var source = await db.Certificates
                .AsNoTracking()
                .SingleAsync(
                    item => item.Id == request.SourceCertificateEntityId,
                    cancellationToken);
            var domain = await db.Domains
                .AsNoTracking()
                .SingleAsync(
                    item => item.Id == request.DomainId,
                    cancellationToken);

            var candidate = CreateCandidate(
                request.OperationId,
                domain,
                source);
            db.Certificates.Add(candidate);
            await db.SaveChangesAsync(cancellationToken);
            db.Entry(candidate).State = EntityState.Detached;

            return new DomainRenewalCandidateIssueResult(
                true,
                "Issued",
                null,
                candidate.Id,
                candidate.CertificateId);
        }

        public CertificateEntity CreateCandidate(
            Guid operationId,
            DomainEntity domain,
            CertificateEntity source) =>
            new()
            {
                Id = Guid.NewGuid(),
                DomainId = domain.Id,
                CertificateId = $"cert_renewal-{operationId:N}_candidate",
                CommonName = source.CommonName,
                Provider = "desec",
                IsWildcard = source.IsWildcard,
                IsStaging = false,
                IsMainPlatformCertificate = false,
                IsActive = true,
                Status = "Valid",
                CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
                ExpiresAtUtc = timeProvider.GetUtcNow().UtcDateTime.AddDays(90),
                FullchainPath = "/protected/candidate/fullchain.pem",
                PrivateKeyPath = "/protected/candidate/privkey.pem",
                LastValidatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            };

        private async Task CaptureProgressStateAsync(
            Guid operationId,
            CancellationToken cancellationToken)
        {
            var operation = await db.RuntimeOperations
                .AsNoTracking()
                .Where(item => item.Id == operationId)
                .Select(item => new { item.CurrentStep, item.EvidenceJson })
                .SingleAsync(cancellationToken);

            ObservedProgressStates.Add((operation.CurrentStep, operation.EvidenceJson));
        }

        private async Task<DomainRenewalCandidateIssueResult?> FindCandidateAsync(
            DomainRenewalCandidateRequest request,
            CancellationToken cancellationToken)
        {
            var prefix = $"cert_renewal-{request.OperationId:N}_";
            var candidate = await db.Certificates
                .AsNoTracking()
                .Where(item =>
                    item.DomainId == request.DomainId &&
                    item.Id != request.SourceCertificateEntityId &&
                    item.CertificateId.StartsWith(prefix) &&
                    !item.IsStaging &&
                    item.Status != "Deleted")
                .OrderByDescending(item => item.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            return candidate is null
                ? null
                : new DomainRenewalCandidateIssueResult(
                    true,
                    "Recovered",
                    null,
                    candidate.Id,
                    candidate.CertificateId);
        }
    }
}
