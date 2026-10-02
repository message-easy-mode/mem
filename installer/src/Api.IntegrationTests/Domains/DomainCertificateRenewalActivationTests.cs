using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modules.Shared.Domains;
using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Dns;
using Modules.Shared.Domains.Renewal;

namespace Api.IntegrationTests.Domains;

public sealed class DomainCertificateRenewalActivationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DOMAINS_CERTIFICATE_RENEWAL_01C_registers_the_production_activation_pipeline()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddLogging();
        services.AddSharedDomains(configuration);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IDomainCertificateRenewalCandidateValidator) &&
            descriptor.ImplementationType == typeof(DomainCertificateRenewalCandidateValidator));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IDomainCertificateRenewalIngressActivator) &&
            descriptor.ImplementationType == typeof(NpmDomainCertificateRenewalIngressActivator));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IDomainCertificateRenewalCandidateActivator) &&
            descriptor.ImplementationType == typeof(DomainCertificateRenewalCandidateActivator));
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01C_rejects_candidate_owned_by_another_domain()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeded = await fixture.SeedAsync("ownership.example");
        var otherDomain = new DomainEntity
        {
            Id = Guid.NewGuid(),
            BaseDomain = "other.example",
            DisplayName = "other.example",
            Purpose = "chat",
            IsMainPlatformDomain = false,
            DnsProvider = "desec",
            DnsZone = "other.example",
            Status = "Active",
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        };
        fixture.Db.Domains.Add(otherDomain);
        await fixture.Db.SaveChangesAsync();

        var candidate = await fixture.Db.Certificates.SingleAsync(item => item.Id == seeded.Candidate.Id);
        candidate.DomainId = otherDomain.Id;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.ActivateAsync(seeded);

        Assert.False(result.Succeeded);
        Assert.Equal("RenewalCandidateOwnershipMismatch", result.ErrorCode);
        Assert.Equal(0, fixture.Ingress.CallCount);
        Assert.Equal(seeded.Source.Id, await fixture.ActiveCertificateIdAsync(seeded.Domain.Id));
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01C_rejects_operation_that_is_not_awaiting_activation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeded = await fixture.SeedAsync("status.example");
        var operation = await fixture.Db.RuntimeOperations.SingleAsync(item => item.Id == seeded.Operation.Id);
        operation.Status = DomainCertificateRenewalOperation.RunningStatus;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.ActivateAsync(seeded);

        Assert.False(result.Succeeded);
        Assert.Equal("RenewalOperationNotAwaitingActivation", result.ErrorCode);
        Assert.Equal(0, fixture.Validator.CallCount);
        Assert.Equal(0, fixture.Ingress.CallCount);
        Assert.Equal(seeded.Source.Id, await fixture.ActiveCertificateIdAsync(seeded.Domain.Id));
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01C_rejects_candidate_from_another_renewal_cycle()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeded = await fixture.SeedAsync("cycle.example");
        var candidate = await fixture.Db.Certificates.SingleAsync(item => item.Id == seeded.Candidate.Id);
        candidate.CertificateId = $"cert_renewal-{Guid.NewGuid():N}_candidate";
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.Activator.ActivateAsync(
            new DomainRenewalCandidateActivationRequest(
                seeded.Operation.Id,
                seeded.Domain.Id,
                seeded.Source.Id,
                seeded.Source.CertificateId,
                seeded.Candidate.Id,
                candidate.CertificateId),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("RenewalCandidateCycleMismatch", result.ErrorCode);
        Assert.Equal(0, fixture.Validator.CallCount);
        Assert.Equal(0, fixture.Ingress.CallCount);
        Assert.Equal(seeded.Source.Id, await fixture.ActiveCertificateIdAsync(seeded.Domain.Id));
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01C_validation_failure_leaves_old_pointer_authoritative()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeded = await fixture.SeedAsync("validation.example");
        fixture.Validator.FailWithErrorCode = "CertificateChainInvalid";

        var result = await fixture.ActivateAsync(seeded);

        Assert.False(result.Succeeded);
        Assert.Equal("CertificateChainInvalid", result.ErrorCode);
        Assert.Equal(1, fixture.Validator.CallCount);
        Assert.Equal(0, fixture.Ingress.CallCount);
        Assert.Equal(seeded.Source.Id, await fixture.ActiveCertificateIdAsync(seeded.Domain.Id));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01C_rejects_staging_or_expired_candidates(
        bool staging,
        bool expired)
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeded = await fixture.SeedAsync("invalid.example");
        var candidate = await fixture.Db.Certificates.SingleAsync(item => item.Id == seeded.Candidate.Id);
        candidate.IsStaging = staging;
        if (expired)
        {
            candidate.ExpiresAtUtc = Now.UtcDateTime.AddMinutes(-1);
        }
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.ActivateAsync(seeded);

        Assert.False(result.Succeeded);
        Assert.Equal(
            staging ? "RenewalCandidateStagingRejected" : "RenewalCandidateInvalid",
            result.ErrorCode);
        Assert.Equal(0, fixture.Validator.CallCount);
        Assert.Equal(0, fixture.Ingress.CallCount);
        Assert.Equal(seeded.Source.Id, await fixture.ActiveCertificateIdAsync(seeded.Domain.Id));
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01C_npm_free_activation_validates_before_pointer_switch_and_retains_previous_certificate()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeded = await fixture.SeedAsync("direct.example");
        fixture.Validator.BeforeValidation = async certificateId =>
        {
            Assert.Equal(seeded.Candidate.CertificateId, certificateId);
            Assert.Equal(seeded.Source.Id, await fixture.ActiveCertificateIdAsync(seeded.Domain.Id));
        };

        var result = await fixture.ActivateAsync(seeded);

        Assert.True(result.Succeeded);
        Assert.Equal("Activated", result.Status);
        Assert.Equal(1, fixture.Validator.CallCount);
        Assert.Equal(0, fixture.Ingress.CallCount);

        var domain = await fixture.Db.Domains.AsNoTracking().SingleAsync(item => item.Id == seeded.Domain.Id);
        var source = await fixture.Db.Certificates.AsNoTracking().SingleAsync(item => item.Id == seeded.Source.Id);
        var candidate = await fixture.Db.Certificates.AsNoTracking().SingleAsync(item => item.Id == seeded.Candidate.Id);
        var operation = await fixture.Db.RuntimeOperations.AsNoTracking().SingleAsync(item => item.Id == seeded.Operation.Id);

        Assert.Equal(candidate.Id, domain.ActiveCertificateId);
        Assert.False(source.IsActive);
        Assert.True(candidate.IsActive);
        Assert.Equal(DomainCertificateRenewalOperation.SucceededStatus, operation.Status);
        Assert.Equal("activation-complete", operation.CurrentStep);
        Assert.True(File.Exists(seeded.SourceFullchainPath));
        Assert.True(File.Exists(seeded.SourcePrivateKeyPath));
        Assert.True(await fixture.Db.Certificates.AsNoTracking().AnyAsync(item => item.Id == seeded.Source.Id));

        var sourceMetadata = await fixture.Storage.GetMetadataAsync(source.CertificateId, CancellationToken.None);
        var candidateMetadata = await fixture.Storage.GetMetadataAsync(candidate.CertificateId, CancellationToken.None);
        Assert.NotNull(sourceMetadata);
        Assert.NotNull(candidateMetadata);
        Assert.False(sourceMetadata!.IsInUse);
        Assert.True(candidateMetadata!.IsInUse);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01C_npm_activation_preserves_expected_certificate_identity_before_pointer_switch()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeded = await fixture.SeedAsync("npm.example", npmCertificateId: 17);
        fixture.Ingress.BeforeActivation = async (certificateId, npmCertificateId) =>
        {
            Assert.Equal(seeded.Candidate.CertificateId, certificateId);
            Assert.Equal(17, npmCertificateId);
            Assert.Equal(seeded.Source.Id, await fixture.ActiveCertificateIdAsync(seeded.Domain.Id));
        };
        fixture.Ingress.ReappliedProxyHostCount = 3;

        var result = await fixture.ActivateAsync(seeded);

        Assert.True(result.Succeeded);
        Assert.Equal(1, fixture.Ingress.CallCount);
        Assert.Equal(17, result.NpmCertificateId);
        Assert.Equal(3, result.ReappliedProxyHostCount);

        var source = await fixture.Db.Certificates.AsNoTracking().SingleAsync(item => item.Id == seeded.Source.Id);
        var candidate = await fixture.Db.Certificates.AsNoTracking().SingleAsync(item => item.Id == seeded.Candidate.Id);
        var operation = await fixture.Db.RuntimeOperations.AsNoTracking().SingleAsync(item => item.Id == seeded.Operation.Id);
        Assert.False(source.ImportedToNpm);
        Assert.Null(source.NpmCertificateId);
        Assert.True(candidate.ImportedToNpm);
        Assert.Equal(17, candidate.NpmCertificateId);
        Assert.Equal(candidate.Id, await fixture.ActiveCertificateIdAsync(seeded.Domain.Id));
        Assert.Contains("\"npmCertificateId\":17", operation.ResultJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("\"reappliedProxyHostCount\":3", operation.ResultJson ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01C_npm_activation_failure_leaves_old_pointer_and_operation_retryable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeded = await fixture.SeedAsync("npm-failure.example", npmCertificateId: 23);
        fixture.Ingress.FailWithErrorCode = "NpmRenewalActivationFailed";

        var orchestrator = fixture.CreateOrchestrator(seeded);
        var result = await orchestrator.ScanAsync(CancellationToken.None);

        Assert.Equal(1, result.RenewalCyclesDue);
        Assert.Equal(1, result.OperationsStarted);
        Assert.Equal(1, result.Failed);
        Assert.Equal(seeded.Source.Id, await fixture.ActiveCertificateIdAsync(seeded.Domain.Id));

        var operation = await fixture.Db.RuntimeOperations.AsNoTracking().SingleAsync(item => item.Id == seeded.Operation.Id);
        Assert.Equal(DomainCertificateRenewalOperation.FailedStatus, operation.Status);
        Assert.Equal("NpmRenewalActivationFailed", operation.LastError);
        Assert.NotNull(operation.CompletedAtUtc);
        Assert.Null(operation.LockedUntilUtc);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01C_post_ingress_state_drift_keeps_old_pointer_and_requires_attention()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeded = await fixture.SeedAsync("npm-drift.example", npmCertificateId: 29);
        fixture.Ingress.AfterActivation = async () =>
        {
            var candidate = await fixture.Db.Certificates.SingleAsync(item => item.Id == seeded.Candidate.Id);
            candidate.Status = "Deleted";
            await fixture.Db.SaveChangesAsync();
        };

        var result = await fixture.ActivateAsync(seeded);

        Assert.False(result.Succeeded);
        Assert.Equal("NpmActivationStateChangedAfterIngress", result.ErrorCode);
        Assert.Equal(seeded.Source.Id, await fixture.ActiveCertificateIdAsync(seeded.Domain.Id));

        var source = await fixture.Db.Certificates.AsNoTracking().SingleAsync(item => item.Id == seeded.Source.Id);
        var candidateAfter = await fixture.Db.Certificates.AsNoTracking().SingleAsync(item => item.Id == seeded.Candidate.Id);
        Assert.True(source.ImportedToNpm);
        Assert.Equal(29, source.NpmCertificateId);
        Assert.False(candidateAfter.ImportedToNpm);
        Assert.Null(candidateAfter.NpmCertificateId);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01C_main_platform_role_moves_atomically_with_active_pointer()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeded = await fixture.SeedAsync("main.example", mainPlatform: true);

        var result = await fixture.ActivateAsync(seeded);

        Assert.True(result.Succeeded);
        var domain = await fixture.Db.Domains.AsNoTracking().SingleAsync(item => item.Id == seeded.Domain.Id);
        var source = await fixture.Db.Certificates.AsNoTracking().SingleAsync(item => item.Id == seeded.Source.Id);
        var candidate = await fixture.Db.Certificates.AsNoTracking().SingleAsync(item => item.Id == seeded.Candidate.Id);

        Assert.True(domain.IsMainPlatformDomain);
        Assert.Equal(candidate.Id, domain.ActiveCertificateId);
        Assert.False(source.IsMainPlatformCertificate);
        Assert.True(candidate.IsMainPlatformCertificate);
        Assert.False(source.IsActive);
        Assert.True(candidate.IsActive);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01C_successful_activation_refreshes_domain_and_certificate_projections()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeded = await fixture.SeedAsync("projection.example");

        Assert.True((await fixture.ActivateAsync(seeded)).Succeeded);

        var renewalService = new DomainCertificateRenewalService(
            fixture.Db,
            new NullDomainSecretStore(),
            new SuccessfulDnsZoneAccessProbe(),
            NullLogger<DomainCertificateRenewalService>.Instance);
        var renewal = await renewalService.GetAsync(seeded.Domain.Id, CancellationToken.None);
        Assert.NotNull(renewal);
        Assert.Equal(seeded.Candidate.CertificateId, renewal!.ActiveCertificateId);

        var registry = new DomainRegistryService(
            fixture.Db,
            fixture.Storage,
            NullLogger<DomainRegistryService>.Instance);
        var certificates = await registry.ListCertificatesAsync(CancellationToken.None);
        var sourceProjection = Assert.Single(certificates.Where(item => item.CertificateId == seeded.Source.CertificateId));
        var candidateProjection = Assert.Single(certificates.Where(item => item.CertificateId == seeded.Candidate.CertificateId));
        Assert.False(sourceProjection.IsInUse);
        Assert.True(candidateProjection.IsInUse);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01C_restart_replays_awaiting_activation_once_and_then_becomes_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeded = await fixture.SeedAsync("restart.example", npmCertificateId: 31);
        var orchestrator = fixture.CreateOrchestrator(seeded);

        var first = await orchestrator.ScanAsync(CancellationToken.None);
        var second = await orchestrator.ScanAsync(CancellationToken.None);

        Assert.Equal(1, first.OperationsStarted);
        Assert.Equal(0, first.Failed);
        Assert.Equal(0, second.RenewalCyclesDue);
        Assert.Equal(1, fixture.Ingress.CallCount);
        Assert.Equal(seeded.Candidate.Id, await fixture.ActiveCertificateIdAsync(seeded.Domain.Id));

        var operation = await fixture.Db.RuntimeOperations.AsNoTracking().SingleAsync(item => item.Id == seeded.Operation.Id);
        Assert.Equal(DomainCertificateRenewalOperation.SucceededStatus, operation.Status);
        Assert.Equal("activation-complete", operation.CurrentStep);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly MutableTimeProvider _time;
        private readonly RecoveringCandidateIssuer _issuer;

        private Fixture(
            string root,
            MemDbContext db,
            CertificateStorageService storage,
            MutableTimeProvider time,
            RecordingCandidateValidator validator,
            RecordingIngressActivator ingress,
            RecoveringCandidateIssuer issuer,
            DomainCertificateRenewalCandidateActivator activator)
        {
            _root = root;
            Db = db;
            Storage = storage;
            _time = time;
            Validator = validator;
            Ingress = ingress;
            _issuer = issuer;
            Activator = activator;
        }

        public MemDbContext Db { get; }
        public CertificateStorageService Storage { get; }
        public RecordingCandidateValidator Validator { get; }
        public RecordingIngressActivator Ingress { get; }
        public DomainCertificateRenewalCandidateActivator Activator { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"mem-renewal-activation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            var db = new MemDbContext(
                new DbContextOptionsBuilder<MemDbContext>()
                    .UseSqlite($"Data Source={Path.Combine(root, "control-plane.db")}")
                    .Options);
            await db.Database.EnsureCreatedAsync();

            var storage = new CertificateStorageService(
                Options.Create(new CertificateStorageOptions
                {
                    RootPath = Path.Combine(root, "certificates")
                }),
                NullLogger<CertificateStorageService>.Instance);
            var time = new MutableTimeProvider(Now);
            var validator = new RecordingCandidateValidator();
            var ingress = new RecordingIngressActivator();
            var issuer = new RecoveringCandidateIssuer();
            var activator = new DomainCertificateRenewalCandidateActivator(
                db,
                validator,
                ingress,
                storage,
                time,
                NullLogger<DomainCertificateRenewalCandidateActivator>.Instance);

            return new Fixture(root, db, storage, time, validator, ingress, issuer, activator);
        }

        public async Task<SeededActivation> SeedAsync(
            string baseDomain,
            bool mainPlatform = false,
            int? npmCertificateId = null)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var domain = new DomainEntity
            {
                Id = Guid.NewGuid(),
                BaseDomain = baseDomain,
                DisplayName = baseDomain,
                Purpose = mainPlatform ? "platform-main" : "chat",
                IsMainPlatformDomain = mainPlatform,
                DnsProvider = "desec",
                DnsZone = baseDomain,
                Status = "Active",
                CreatedAtUtc = now.AddDays(-90),
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
                IsStaging = false,
                IsMainPlatformCertificate = mainPlatform,
                IsActive = true,
                Status = "Active",
                CreatedAtUtc = now.AddDays(-60),
                ExpiresAtUtc = now.AddDays(1),
                NpmCertificateId = npmCertificateId,
                ImportedToNpm = npmCertificateId.HasValue
            };
            Db.Certificates.Add(source);
            await Db.SaveChangesAsync();

            domain.ActiveCertificateId = source.Id;
            domain.UpdatedAtUtc = now;
            Db.DomainCertificateRenewalPolicies.Add(new DomainCertificateRenewalPolicyEntity
            {
                DomainId = domain.Id,
                AutoRenewEnabled = true,
                AcmeEmail = $"ops@{baseDomain}",
                RenewalWindowDays = 30,
                RetryIntervalHours = 24,
                CreatedAtUtc = now.AddDays(-30),
                UpdatedAtUtc = now.AddDays(-30)
            });
            await Db.SaveChangesAsync();

            var operation = new RuntimeOperationEntity
            {
                Id = Guid.NewGuid(),
                DomainId = domain.Id,
                Operation = DomainCertificateRenewalOperation.OperationName,
                Status = DomainCertificateRenewalOperation.AwaitingActivationStatus,
                IdempotencyKey = DomainCertificateRenewalOperation.BuildIdempotencyKey(
                    domain.Id,
                    source.Id,
                    source.ExpiresAtUtc!.Value),
                RequestedBy = "system",
                RequestedAtUtc = now.AddMinutes(-5),
                StartedAtUtc = now.AddMinutes(-4),
                CurrentStep = "awaiting-activation",
                AttemptCount = 1,
                ResultJson = "{\"status\":\"awaiting-activation\"}",
                EvidenceJson = "{\"phaseCode\":\"awaiting-activation\",\"recoveringIncident\":false}"
            };
            Db.RuntimeOperations.Add(operation);

            var candidate = new CertificateEntity
            {
                Id = Guid.NewGuid(),
                DomainId = domain.Id,
                CertificateId = $"cert_renewal-{operation.Id:N}_candidate",
                CommonName = source.CommonName,
                Provider = "desec",
                IsWildcard = true,
                IsStaging = false,
                IsMainPlatformCertificate = false,
                IsActive = true,
                Status = "Valid",
                CreatedAtUtc = now.AddMinutes(-3),
                ExpiresAtUtc = now.AddDays(90),
                LastValidatedAtUtc = now.AddMinutes(-2)
            };
            Db.Certificates.Add(candidate);
            await Db.SaveChangesAsync();

            var sourcePaths = await SaveStoredCertificateAsync(source, baseDomain, isMainPlatform: mainPlatform, isInUse: true);
            await SaveStoredCertificateAsync(candidate, baseDomain, isMainPlatform: false, isInUse: false);

            Db.ChangeTracker.Clear();
            return new SeededActivation(
                domain,
                source,
                candidate,
                operation,
                sourcePaths.FullchainPath,
                sourcePaths.PrivateKeyPath);
        }

        public Task<DomainRenewalCandidateActivationResult> ActivateAsync(SeededActivation seeded) =>
            Activator.ActivateAsync(
                new DomainRenewalCandidateActivationRequest(
                    seeded.Operation.Id,
                    seeded.Domain.Id,
                    seeded.Source.Id,
                    seeded.Source.CertificateId,
                    seeded.Candidate.Id,
                    seeded.Candidate.CertificateId),
                CancellationToken.None);

        public DomainCertificateRenewalOrchestrator CreateOrchestrator(SeededActivation seeded)
        {
            _issuer.Candidate = seeded.Candidate;
            return new DomainCertificateRenewalOrchestrator(
                Db,
                new NullDomainSecretStore(),
                _issuer,
                _time,
                NullLogger<DomainCertificateRenewalOrchestrator>.Instance,
                candidateActivator: Activator);
        }

        public Task<Guid?> ActiveCertificateIdAsync(Guid domainId) =>
            Db.Domains
                .AsNoTracking()
                .Where(item => item.Id == domainId)
                .Select(item => item.ActiveCertificateId)
                .SingleAsync();

        private async Task<StoredPaths> SaveStoredCertificateAsync(
            CertificateEntity certificate,
            string baseDomain,
            bool isMainPlatform,
            bool isInUse)
        {
            var directory = Storage.GetCertificateDirectory(certificate.CertificateId);
            Directory.CreateDirectory(directory);
            var fullchainPath = Path.Combine(directory, "fullchain.pem");
            var privateKeyPath = Path.Combine(directory, "privkey.pem");
            await File.WriteAllTextAsync(fullchainPath, "test certificate");
            await File.WriteAllTextAsync(privateKeyPath, "test private key");

            certificate.FullchainPath = fullchainPath;
            certificate.PrivateKeyPath = privateKeyPath;
            await Db.SaveChangesAsync();

            await Storage.SaveMetadataAsync(
                new StoredCertificateMetadata
                {
                    CertificateId = certificate.CertificateId,
                    Domain = certificate.CommonName,
                    Zone = baseDomain,
                    Provider = certificate.Provider,
                    IsWildcard = certificate.IsWildcard,
                    IsStaging = certificate.IsStaging,
                    CreatedAtUtc = new DateTimeOffset(certificate.CreatedAtUtc, TimeSpan.Zero),
                    ExpiresAtUtc = certificate.ExpiresAtUtc.HasValue
                        ? new DateTimeOffset(certificate.ExpiresAtUtc.Value, TimeSpan.Zero)
                        : null,
                    FullchainPath = fullchainPath,
                    PrivateKeyPath = privateKeyPath,
                    Status = certificate.Status,
                    IsMainPlatformCertificate = isMainPlatform,
                    IsInUse = isInUse,
                    Purpose = isMainPlatform ? "platform-main" : "chat"
                },
                CancellationToken.None);

            return new StoredPaths(fullchainPath, privateKeyPath);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Test cleanup only.
            }
        }
    }

    private sealed class RecordingCandidateValidator : IDomainCertificateRenewalCandidateValidator
    {
        public int CallCount { get; private set; }
        public Func<string, Task>? BeforeValidation { get; set; }
        public string? FailWithErrorCode { get; set; }

        public async Task<DomainRenewalCandidateActivationResult> ValidateAsync(
            string certificateId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            if (BeforeValidation is not null)
            {
                await BeforeValidation(certificateId);
            }

            return FailWithErrorCode is null
                ? new DomainRenewalCandidateActivationResult(true, "Validated", null)
                : new DomainRenewalCandidateActivationResult(false, "ValidationFailed", FailWithErrorCode);
        }
    }

    private sealed class RecordingIngressActivator : IDomainCertificateRenewalIngressActivator
    {
        public int CallCount { get; private set; }
        public int ReappliedProxyHostCount { get; set; }
        public string? FailWithErrorCode { get; set; }
        public Func<string, int, Task>? BeforeActivation { get; set; }
        public Func<Task>? AfterActivation { get; set; }

        public async Task<DomainRenewalCandidateActivationResult> ActivateAsync(
            string certificateId,
            int expectedNpmCertificateId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            if (BeforeActivation is not null)
            {
                await BeforeActivation(certificateId, expectedNpmCertificateId);
            }

            if (FailWithErrorCode is not null)
            {
                return new DomainRenewalCandidateActivationResult(
                    false,
                    "NpmActivationFailed",
                    FailWithErrorCode);
            }

            if (AfterActivation is not null)
            {
                await AfterActivation();
            }

            return new DomainRenewalCandidateActivationResult(
                true,
                "NpmActivated",
                null,
                expectedNpmCertificateId,
                ReappliedProxyHostCount);
        }
    }

    private sealed class RecoveringCandidateIssuer
        : IDomainCertificateRenewalCandidateIssuer
    {
        public CertificateEntity? Candidate { get; set; }

        public Task<DomainRenewalCandidateIssueResult?> TryRecoverCandidateAsync(
            DomainRenewalCandidateRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Candidate is null)
            {
                return Task.FromResult<DomainRenewalCandidateIssueResult?>(null);
            }

            return Task.FromResult<DomainRenewalCandidateIssueResult?>(new DomainRenewalCandidateIssueResult(
                true,
                "Recovered",
                null,
                Candidate.Id,
                Candidate.CertificateId));
        }

        public Task<DomainRenewalCandidateIssueResult> IssueCandidateAsync(
            DomainRenewalCandidateRequest request,
            string providerToken,
            CertificateIssueProgressCallback progressCallback,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Activation replay must not issue another candidate.");
    }

    private sealed class NullDomainSecretStore : IDomainSecretStore
    {
        public Task SetProtectedAsync(Guid domainId, string category, string key, string secret, string? description, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<string?> ResolveProtectedAsync(Guid domainId, string category, string key, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<bool> ExistsAsync(Guid domainId, string category, string key, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task DeleteAsync(Guid domainId, string category, string key, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class SuccessfulDnsZoneAccessProbe : IDnsZoneAccessProbe
    {
        public Task<DnsZoneAccessProbeResult> ProbeAsync(
            DnsZoneAccessProbeRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DnsZoneAccessProbeResult(
                true,
                "ready",
                null,
                Array.Empty<CertificateOperationEvidence>()));
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record SeededActivation(
        DomainEntity Domain,
        CertificateEntity Source,
        CertificateEntity Candidate,
        RuntimeOperationEntity Operation,
        string SourceFullchainPath,
        string SourcePrivateKeyPath);

    private sealed record StoredPaths(string FullchainPath, string PrivateKeyPath);
}
