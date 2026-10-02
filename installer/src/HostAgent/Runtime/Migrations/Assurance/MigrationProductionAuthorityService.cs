using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostAgent.Runtime.Migrations.Cutover;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Auth.Services.Identity;
using Modules.Operator.Migrations;

namespace HostAgent.Runtime.Migrations.Assurance;

public sealed class MigrationProductionAuthorityService(
    MemDbContext db,
    IConfiguration configuration,
    TimeProvider timeProvider,
    IMemOperatorAuditService audit)
{
    private const string EvidenceSchemaVersion = "migration-production-authority-v1";
    private const string AcknowledgementsSchemaVersion = "operator-attested-snapshot-v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<MigrationProductionAuthorityStateResponse> GetStateAsync(
        string migrationId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
        {
            throw new MigrationProductionAuthorityException(
                "migration_id_required",
                "Migration id is required.",
                MigrationProductionAuthorityFailureKind.InvalidRequest);
        }

        var intakeExists = await db.MigrationIntakes
            .AsNoTracking()
            .AnyAsync(x => x.IntakeId == migrationId, ct);
        if (!intakeExists)
        {
            throw new FileNotFoundException(
                $"Migration Session '{migrationId}' was not found.");
        }

        var authority = await db.MigrationProductionAuthorities
            .AsNoTracking()
            .Include(x => x.MigrationIntake)
            .Include(x => x.PackageRevision)
            .Include(x => x.CandidateArtifact)
            .Include(x => x.StagingRun)
            .SingleOrDefaultAsync(
                x => x.ActiveMigrationKey == migrationId &&
                     x.Status == MigrationProductionAuthorityStatuses.Active,
                ct);

        if (authority is null)
        {
            return new MigrationProductionAuthorityStateResponse(
                Status: "not-created",
                MigrationId: migrationId,
                AuthorizesProduction: false,
                Authority: null,
                Detail: "No active production authority exists. The verified rehearsal snapshot may be authorized explicitly, or a final frozen package may establish high-assurance authority.");
        }

        return new MigrationProductionAuthorityStateResponse(
            Status: "active",
            MigrationId: migrationId,
            AuthorizesProduction: true,
            Authority: ToDto(authority),
            Detail: authority.AuthorityType == MigrationProductionAuthorityTypes.OperatorAttestedSnapshot
                ? "The exact verified rehearsal snapshot is authorized for production by durable operator attestation. Formal source-freeze and final-recapture evidence were not collected."
                : "The exact verified final frozen package is authorized for production.");
    }

    public async Task<MigrationProductionAuthorityStateResponse> CreateOperatorAttestedSnapshotAsync(
        string migrationId,
        Guid operatorId,
        CreateOperatorAttestedSnapshotAuthorityRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
        {
            throw new MigrationProductionAuthorityException(
                "migration_id_required",
                "Migration id is required.",
                MigrationProductionAuthorityFailureKind.InvalidRequest);
        }

        if (operatorId == Guid.Empty)
        {
            throw new MigrationProductionAuthorityException(
                "named_operator_required",
                "A named Platform Owner is required to create production authority.",
                MigrationProductionAuthorityFailureKind.InvalidRequest);
        }

        ArgumentNullException.ThrowIfNull(request);
        ValidateAcknowledgements(request);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var intake = await db.MigrationIntakes
            .AsSplitQuery()
            .Include(x => x.Sources)
            .Include(x => x.PackageRevisions)
            .Include(x => x.ConversionAttempts)
                .ThenInclude(x => x.PackageRevision)
            .Include(x => x.ConversionAttempts)
                .ThenInclude(x => x.CandidateArtifact)
            .Include(x => x.StagingRuns)
            .Include(x => x.ProductionAuthorities)
                .ThenInclude(x => x.PackageRevision)
            .Include(x => x.ProductionAuthorities)
                .ThenInclude(x => x.CandidateArtifact)
            .Include(x => x.ProductionAuthorities)
                .ThenInclude(x => x.StagingRun)
            .Include(x => x.ProductionAdoption)
            .Include(x => x.Acceptance)
            .SingleOrDefaultAsync(x => x.IntakeId == migrationId, ct)
            ?? throw new FileNotFoundException(
                $"Migration Session '{migrationId}' was not found.");

        var existingAuthority = intake.ProductionAuthorities.SingleOrDefault(x =>
            x.ActiveMigrationKey == migrationId &&
            x.Status == MigrationProductionAuthorityStatuses.Active);
        if (existingAuthority is not null)
        {
            await transaction.CommitAsync(ct);
            return new MigrationProductionAuthorityStateResponse(
                Status: "active",
                MigrationId: migrationId,
                AuthorizesProduction: true,
                Authority: ToDto(existingAuthority),
                Detail: existingAuthority.AuthorityType == MigrationProductionAuthorityTypes.OperatorAttestedSnapshot
                    ? "The verified snapshot already has active operator-attested production authority."
                    : "A different active production authority already exists for this migration.");
        }

        if (intake.Acceptance is not null)
        {
            throw Conflict(
                "migration_already_accepted",
                "Production authority cannot be created after migration acceptance.");
        }

        if (intake.ProductionAdoption is not null)
        {
            throw Conflict(
                "production_adoption_already_prepared",
                "Production authority must be established before a production adoption plan is prepared.");
        }

        var selection = ResolveVerifiedPreviewSelection(intake, migrationId);
        var dataRoot = ResolveDataRoot();
        var archivePath = MigrationPackageRevisionStorage.ResolveExistingDecryptedArchivePath(
            dataRoot,
            migrationId,
            selection.Revision.PackageRevisionId,
            allowLegacyPreviewFallback: true);
        var encryptedPath = ResolveExistingEncryptedArchivePath(
            dataRoot,
            migrationId,
            selection.Revision);

        RequireFile(archivePath, "The validated rehearsal archive is no longer available on the target.");
        RequireFile(encryptedPath, "The encrypted rehearsal package is no longer available on the target.");
        RequireFile(selection.Candidate.ArtifactPath, "The verified migration candidate payload is no longer available on the target.");

        var actualEncryptedSha256 = await HashFileAsync(encryptedPath, ct);
        var actualArchiveSha256 = await HashFileAsync(archivePath, ct);
        var actualCandidateSha256 = await HashFileAsync(selection.Candidate.ArtifactPath, ct);

        RequireHashMatch(
            actualEncryptedSha256,
            selection.Revision.EncryptedPackageSha256,
            "The retained encrypted package checksum no longer matches the validated package revision.");
        RequireHashMatch(
            actualArchiveSha256,
            selection.Revision.DecryptedArchiveSha256,
            "The retained decrypted archive checksum no longer matches the validated package revision.");
        RequireHashMatch(
            actualCandidateSha256,
            selection.Candidate.ArtifactSha256,
            "The retained migration candidate checksum no longer matches its verified evidence.");

        var provenance = MigrationCutoverContextResolver.ReadCandidateProvenance(
            selection.Candidate.ProvenanceJson);
        if (!string.Equals(
                provenance.MigrationId,
                selection.Revision.ArchiveMigrationId,
                StringComparison.Ordinal))
        {
            throw EvidenceInvalid(
                "The migration candidate source identity does not match the validated rehearsal package.");
        }

        var archive = MigrationCutoverContextResolver.ReadArchiveSummary(
            archivePath,
            selection.Revision.ArchiveMigrationId!,
            provenance.SourceStackId,
            provenance.MatrixServerName);
        ValidateArchiveCapture(selection.Revision, archive.Manifest.Capture);
        var archiveSourceIdentity = ReadArchiveSourceIdentity(
            archivePath,
            selection.Revision);

        var stagingEvidence = await LoadRetainedStagingEvidenceAsync(
            dataRoot,
            selection.Staging.PrivateRuntimeStagingId!,
            ct);
        ValidateRetainedStaging(
            selection.Staging,
            stagingEvidence,
            archive.Stack.MatrixServerName);

        var matrixDataPath = EnsureContainedPath(
            dataRoot,
            stagingEvidence.MatrixDataPath,
            "The retained staging Matrix data path is outside the MEM data root.");
        var signingKeyPath = EnsureContainedPath(
            matrixDataPath,
            Path.Combine(matrixDataPath, "signing.key"),
            "The retained staging signing-key path is invalid.");
        RequireFile(
            signingKeyPath,
            "The preserved Synapse signing key is missing from the retained verified staging runtime.");
        var signingKeySha256 = await HashFileAsync(signingKeyPath, ct);

        var source = ResolveOrCreateDurableSourceIdentity(
            intake,
            archiveSourceIdentity);
        if (db.Entry(source).State == EntityState.Detached)
        {
            db.MigrationSources.Add(source);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var authorityId = $"mpa_{now:yyyyMMdd-HHmmssZ}_{Guid.NewGuid():N}"[..44];

        var acknowledgementsDocument = new OperatorAttestedAcknowledgementsEvidence(
            AcknowledgementsSchemaVersion,
            request.AcknowledgeUsersWereInstructedNotToUseSource,
            request.AcknowledgePostCaptureWritesWillNotMigrate,
            request.AcknowledgeSelectedSnapshotBecomesAuthoritative,
            request.AcknowledgeSourceWillBeRetainedUntilVerification,
            request.AcknowledgeNoFormalSourceFreezeEvidence,
            request.AcknowledgeReducedRollbackAssurance);
        var acknowledgementsJson = SerializeCanonical(acknowledgementsDocument);
        var acknowledgementsSha256 = HashText(acknowledgementsJson);

        var evidenceDocument = new OperatorAttestedProductionAuthorityEvidence(
            EvidenceSchemaVersion,
            authorityId,
            migrationId,
            MigrationProductionAuthorityTypes.OperatorAttestedSnapshot,
            selection.Revision.PackageRevisionId,
            actualEncryptedSha256,
            actualArchiveSha256,
            selection.Revision.ArchiveMigrationId!,
            selection.Revision.CaptureKind!,
            selection.Revision.SourceFrozen!.Value,
            selection.Revision.RehearsalOnly!.Value,
            source.SourceFingerprint,
            source.CapturedAtUtc,
            selection.Candidate.CandidateArtifactId,
            actualCandidateSha256,
            selection.Staging.StagingRunId,
            selection.Staging.PrivateRuntimeStagingId!,
            archive.Stack.MatrixServerName,
            signingKeySha256,
            stagingEvidence.Database.UsersCount,
            stagingEvidence.Database.RoomsCount,
            stagingEvidence.Database.EventsCount,
            FormalSourceFreezeEvidenceCollected: false,
            FinalRecapturePerformed: false,
            PotentialPostCaptureWritesIndependentlyExcluded: false,
            RollbackAssurance: "reduced",
            operatorId,
            now,
            acknowledgementsSha256);
        var evidenceJson = SerializeCanonical(evidenceDocument);
        var evidenceSha256 = HashText(evidenceJson);

        var authority = new MigrationProductionAuthorityEntity
        {
            Id = Guid.NewGuid(),
            ProductionAuthorityId = authorityId,
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            MigrationPackageRevisionEntityId = selection.Revision.Id,
            PackageRevision = selection.Revision,
            MigrationCandidateArtifactEntityId = selection.Candidate.Id,
            CandidateArtifact = selection.Candidate,
            MigrationStagingRunEntityId = selection.Staging.Id,
            StagingRun = selection.Staging,
            AuthorityType = MigrationProductionAuthorityTypes.OperatorAttestedSnapshot,
            Status = MigrationProductionAuthorityStatuses.Active,
            ActiveMigrationKey = migrationId,
            EncryptedPackageSha256 = actualEncryptedSha256,
            DecryptedArchiveSha256 = actualArchiveSha256,
            SourceMigrationId = selection.Revision.ArchiveMigrationId!,
            SourceFingerprint = source.SourceFingerprint,
            MatrixServerName = archive.Stack.MatrixServerName,
            SigningKeyIdentitySha256 = signingKeySha256,
            CaptureKind = selection.Revision.CaptureKind!,
            SourceFrozen = selection.Revision.SourceFrozen!.Value,
            RehearsalOnly = selection.Revision.RehearsalOnly!.Value,
            CapturedAtUtc = source.CapturedAtUtc,
            EvidenceSchemaVersion = EvidenceSchemaVersion,
            EvidenceJson = evidenceJson,
            EvidenceSha256 = evidenceSha256,
            AcknowledgementsSchemaVersion = AcknowledgementsSchemaVersion,
            AcknowledgementsJson = acknowledgementsJson,
            AcknowledgementsSha256 = acknowledgementsSha256,
            CreatedByOperatorId = operatorId,
            CreatedAtUtc = now,
        };

        db.MigrationProductionAuthorities.Add(authority);

        try
        {
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "migration.production_authority.operator_attested_snapshot.created",
                    Outcome: "succeeded",
                    ActorOperatorId: operatorId,
                    SubjectOperatorId: operatorId,
                    CorrelationId: migrationId,
                    ReasonCode: authorityId),
                ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            throw new MigrationProductionAuthorityException(
                "production_authority_conflict",
                "An active production authority was created concurrently. Refresh the Migration Session before continuing.",
                MigrationProductionAuthorityFailureKind.Conflict,
                ex);
        }

        return new MigrationProductionAuthorityStateResponse(
            Status: "active",
            MigrationId: migrationId,
            AuthorizesProduction: true,
            Authority: ToDto(authority),
            Detail: "The exact verified rehearsal snapshot now has durable operator-attested production authority. Its original preview and rehearsal evidence remains unchanged.");
    }

    private static VerifiedPreviewSelection ResolveVerifiedPreviewSelection(
        MigrationIntakeEntity intake,
        string migrationId)
    {
        var staging = intake.StagingRuns
            .Where(x =>
                x.ActiveMigrationKey == migrationId &&
                x.DestroyedAtUtc is null &&
                string.Equals(x.Status, "verified", StringComparison.Ordinal) &&
                x.PrivateOnly &&
                !x.PublicRoutesCreated &&
                x.DatabaseImportSucceeded &&
                x.SynapseHealthPassed &&
                x.ElementConfigPresent &&
                x.ElementContainerStarted &&
                x.ElementHealthPassed &&
                x.ElementSynapseConnectivityPassed &&
                x.ElementNetworkAttached &&
                !string.IsNullOrWhiteSpace(x.PrivateRuntimeStagingId))
            .OrderByDescending(x => x.CompletedAtUtc ?? x.UpdatedAtUtc)
            .FirstOrDefault()
            ?? throw Conflict(
                "verified_staging_required",
                "A retained fully verified PostgreSQL, Synapse, and Element private staging runtime is required before this snapshot can authorize production.");

        var conversion = intake.ConversionAttempts.SingleOrDefault(x =>
            x.CandidateArtifact is not null &&
            x.CandidateArtifact.Id == staging.MigrationCandidateArtifactEntityId)
            ?? throw EvidenceInvalid(
                "The retained verified staging run is not bound to a conversion attempt in this Migration Session.");
        var candidate = conversion.CandidateArtifact!;
        var revision = conversion.PackageRevision ??
            intake.PackageRevisions.SingleOrDefault(x =>
                conversion.MigrationPackageRevisionEntityId == x.Id)
            ?? throw EvidenceInvalid(
                "The verified conversion attempt is not bound to a package revision.");

        if (conversion.MigrationIntakeEntityId != intake.Id ||
            revision.MigrationIntakeEntityId != intake.Id ||
            staging.MigrationIntakeEntityId != intake.Id ||
            candidate.MigrationConversionAttemptEntityId != conversion.Id)
        {
            throw EvidenceInvalid(
                "The package, candidate, and staging evidence do not belong to the same Migration Session.");
        }

        if (conversion.Status is not ("completed" or "completed-with-warnings") ||
            !string.Equals(conversion.CurrentStep, "candidate-created", StringComparison.Ordinal) ||
            !string.Equals(candidate.VerificationStatus, "verified", StringComparison.Ordinal) ||
            !string.Equals(candidate.RetentionState, "active", StringComparison.Ordinal))
        {
            throw Conflict(
                "verified_candidate_required",
                "The retained staging run is not bound to an active verified migration candidate.");
        }

        if (!string.Equals(revision.Purpose, "preview", StringComparison.Ordinal) ||
            !string.Equals(revision.Status, "package-validated", StringComparison.Ordinal) ||
            !string.Equals(revision.RetentionState, "active", StringComparison.Ordinal) ||
            revision.ActivePurposeKey is null ||
            !string.Equals(revision.CaptureKind, "preview", StringComparison.OrdinalIgnoreCase) ||
            revision.SourceFrozen is not false ||
            revision.RehearsalOnly is not true ||
            string.IsNullOrWhiteSpace(revision.EncryptedPackageSha256) ||
            string.IsNullOrWhiteSpace(revision.DecryptedArchiveSha256) ||
            string.IsNullOrWhiteSpace(revision.ArchiveMigrationId))
        {
            throw Conflict(
                "verified_preview_package_required",
                "Operator-attested production authority requires the active validated rehearsal package revision that produced the retained verified staging runtime.");
        }

        if (!string.Equals(
                conversion.SourcePackageSha256,
                revision.DecryptedArchiveSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                candidate.SourcePackageSha256,
                revision.DecryptedArchiveSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw EvidenceInvalid(
                "The package, conversion, and candidate source checksums do not identify the same validated rehearsal snapshot.");
        }

        return new VerifiedPreviewSelection(revision, conversion, candidate, staging);
    }

    private async Task<RetainedStagingEvidence> LoadRetainedStagingEvidenceAsync(
        string dataRoot,
        string privateStagingId,
        CancellationToken ct)
    {
        if (!IsSafePathSegment(privateStagingId))
        {
            throw EvidenceInvalid(
                "The retained private staging identity contains unsafe characters.");
        }

        var resultPath = EnsureContainedPath(
            dataRoot,
            Path.Combine(
                dataRoot,
                "restore-staging",
                "history",
                privateStagingId,
                "restore-staging-result.json"),
            "The retained staging evidence path is outside the MEM data root.");
        RequireFile(
            resultPath,
            "The retained private staging evidence file is missing.");

        await using var stream = File.OpenRead(resultPath);
        var evidence = await JsonSerializer.DeserializeAsync<RetainedStagingEvidence>(
            stream,
            JsonOptions,
            ct);
        if (evidence is null ||
            string.IsNullOrWhiteSpace(evidence.Status) ||
            string.IsNullOrWhiteSpace(evidence.StagingId) ||
            string.IsNullOrWhiteSpace(evidence.MatrixDataPath) ||
            evidence.Safety is null ||
            evidence.Database is null ||
            evidence.Runtime is null)
        {
            throw EvidenceInvalid(
                "The retained private staging evidence is incomplete.");
        }

        return evidence;
    }

    private static void ValidateRetainedStaging(
        MigrationStagingRunEntity staging,
        RetainedStagingEvidence evidence,
        string archiveMatrixServerName)
    {
        if (!string.Equals(evidence.StagingId, staging.PrivateRuntimeStagingId, StringComparison.Ordinal) ||
            !string.Equals(evidence.Status, "ready", StringComparison.OrdinalIgnoreCase) ||
            evidence.Destroy is not null ||
            !evidence.Safety.PrivateOnly ||
            evidence.Safety.PublicRoutesCreated ||
            evidence.Safety.ProductionContainersTouched ||
            evidence.Safety.ProductionDatabasesTouched ||
            !evidence.Safety.RequiresExplicitDestroy ||
            !evidence.Database.ImportSucceeded ||
            !evidence.Runtime.SigningKeyExtracted ||
            !evidence.Runtime.SynapseHealthPassed ||
            !evidence.Runtime.ElementConfigExtracted ||
            !evidence.Runtime.ElementContainerStarted ||
            !evidence.Runtime.ElementHealthPassed ||
            !evidence.Runtime.ElementSynapseConnectivityPassed ||
            !evidence.Runtime.ElementNetworkAttached)
        {
            throw Conflict(
                "retained_staging_not_ready",
                "The retained private staging runtime no longer proves a complete private PostgreSQL, Synapse, and Element verification.");
        }

        if (string.IsNullOrWhiteSpace(evidence.MatrixServerName) ||
            !string.Equals(evidence.MatrixServerName, staging.MatrixServerName, StringComparison.Ordinal) ||
            !string.Equals(evidence.MatrixServerName, archiveMatrixServerName, StringComparison.Ordinal))
        {
            throw EvidenceInvalid(
                "The retained staging Matrix server identity does not match the validated source archive.");
        }

        RequireNullableCountMatch(staging.UsersCount, evidence.Database.UsersCount, "users");
        RequireNullableCountMatch(staging.RoomsCount, evidence.Database.RoomsCount, "rooms");
        RequireNullableCountMatch(staging.EventsCount, evidence.Database.EventsCount, "events");
    }


    private static DurableSourceIdentity ReadArchiveSourceIdentity(
        string archivePath,
        MigrationPackageRevisionEntity revision)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var manifestEntry = archive.GetEntry("mem-migration/migration-manifest.json")
            ?? throw EvidenceInvalid("Migration archive manifest is missing.");

        try
        {
            using var stream = manifestEntry.Open();
            var manifest = JsonSerializer.Deserialize<ArchiveIdentityManifest>(
                stream,
                JsonOptions)
                ?? throw EvidenceInvalid("Migration archive manifest is empty.");

            if (!string.Equals(manifest.Schema, "mem-v010-migration", StringComparison.Ordinal) ||
                manifest.SchemaVersion != 2 ||
                !string.Equals(manifest.MigrationId, revision.ArchiveMigrationId, StringComparison.Ordinal) ||
                manifest.Source is null ||
                manifest.Capture is null ||
                string.IsNullOrWhiteSpace(manifest.Source.Product) ||
                string.IsNullOrWhiteSpace(manifest.Source.Version) ||
                !string.Equals(manifest.Source.Product, revision.ArchiveSourceProduct, StringComparison.Ordinal) ||
                !string.Equals(manifest.Source.Version, revision.ArchiveSourceVersion, StringComparison.Ordinal) ||
                !IsSha256(manifest.Source.StartFingerprint) ||
                !IsSha256(manifest.Source.CompletionFingerprint) ||
                !string.Equals(
                    manifest.Source.StartFingerprint,
                    manifest.Source.CompletionFingerprint,
                    StringComparison.OrdinalIgnoreCase) ||
                manifest.Capture.SourceChangedDuringCapture ||
                manifest.Capture.StartedAtUtc == default ||
                manifest.Capture.CompletedAtUtc == default ||
                manifest.Capture.CompletedAtUtc < manifest.Capture.StartedAtUtc)
            {
                throw EvidenceInvalid(
                    "The validated migration archive does not contain one complete, stable source identity.");
            }

            return new DurableSourceIdentity(
                manifest.MigrationId,
                "mem-v010-capture",
                manifest.Source.Product,
                manifest.Source.Version,
                manifest.Source.CompletionFingerprint.ToLowerInvariant(),
                manifest.Capture.CompletedAtUtc.UtcDateTime);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Migration archive source identity is invalid.",
                ex);
        }
    }

    private static MigrationSourceEntity ResolveOrCreateDurableSourceIdentity(
        MigrationIntakeEntity intake,
        DurableSourceIdentity identity)
    {
        var sources = intake.Sources.ToArray();
        if (sources.Length > 1)
        {
            throw EvidenceInvalid(
                "The Migration Session contains more than one durable source identity.");
        }

        if (sources.Length == 0)
        {
            var source = new MigrationSourceEntity
            {
                Id = Guid.NewGuid(),
                MigrationIntakeEntityId = intake.Id,
                MigrationIntake = intake,
                SourceId = identity.SourceId,
                SourceKind = identity.SourceKind,
                Product = identity.Product,
                ProductVersion = identity.ProductVersion,
                SourceFingerprint = identity.SourceFingerprint,
                CapturedAtUtc = identity.CapturedAtUtc,
            };
            intake.Sources.Add(source);
            return source;
        }

        var existing = sources[0];
        if (!string.Equals(existing.SourceId, identity.SourceId, StringComparison.Ordinal) ||
            !string.Equals(existing.Product, identity.Product, StringComparison.Ordinal) ||
            !string.Equals(existing.ProductVersion, identity.ProductVersion, StringComparison.Ordinal) ||
            !string.Equals(
                existing.SourceFingerprint,
                identity.SourceFingerprint,
                StringComparison.OrdinalIgnoreCase) ||
            existing.CapturedAtUtc is not { } existingCapturedAtUtc ||
            NormalizeUtc(existingCapturedAtUtc) != identity.CapturedAtUtc)
        {
            throw EvidenceInvalid(
                "The durable Migration Session source identity does not match the validated rehearsal archive.");
        }

        return existing;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static void ValidateArchiveCapture(
        MigrationPackageRevisionEntity revision,
        MigrationCutoverContextResolver.ArchiveCapture capture)
    {
        if (revision.SourceFrozen is not bool sourceFrozen ||
            revision.RehearsalOnly is not bool rehearsalOnly ||
            !string.Equals(capture.Kind, revision.CaptureKind, StringComparison.OrdinalIgnoreCase) ||
            capture.SourceFrozen != sourceFrozen ||
            capture.RehearsalOnly != rehearsalOnly)
        {
            throw EvidenceInvalid(
                "The validated archive capture semantics do not match the durable package revision.");
        }
    }

    private static void ValidateAcknowledgements(
        CreateOperatorAttestedSnapshotAuthorityRequest request)
    {
        if (!request.AcknowledgeUsersWereInstructedNotToUseSource ||
            !request.AcknowledgePostCaptureWritesWillNotMigrate ||
            !request.AcknowledgeSelectedSnapshotBecomesAuthoritative ||
            !request.AcknowledgeSourceWillBeRetainedUntilVerification ||
            !request.AcknowledgeNoFormalSourceFreezeEvidence ||
            !request.AcknowledgeReducedRollbackAssurance)
        {
            throw new MigrationProductionAuthorityException(
                "operator_attestation_incomplete",
                "Every simplified-assurance acknowledgement must be confirmed before the verified snapshot can authorize production.",
                MigrationProductionAuthorityFailureKind.InvalidRequest);
        }
    }

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(configuration);

    private static string ResolveExistingEncryptedArchivePath(
        string dataRoot,
        string migrationId,
        MigrationPackageRevisionEntity revision)
    {
        var revisionPath = MigrationPackageRevisionStorage.ResolveEncryptedArchivePath(
            dataRoot,
            migrationId,
            revision.PackageRevisionId);
        if (File.Exists(revisionPath))
        {
            return revisionPath;
        }

        return MigrationPackageRevisionStorage.ResolveLegacyEncryptedArchivePath(
            dataRoot,
            migrationId);
    }

    private static MigrationProductionAuthorityDto ToDto(
        MigrationProductionAuthorityEntity authority) => new(
        authority.ProductionAuthorityId,
        authority.AuthorityType,
        authority.Status,
        authority.PackageRevision.PackageRevisionId,
        authority.CandidateArtifact.CandidateArtifactId,
        authority.StagingRun.StagingRunId,
        authority.EncryptedPackageSha256,
        authority.DecryptedArchiveSha256,
        authority.SourceMigrationId,
        authority.SourceFingerprint,
        authority.MatrixServerName,
        authority.SigningKeyIdentitySha256,
        authority.CaptureKind,
        authority.SourceFrozen,
        authority.RehearsalOnly,
        authority.CapturedAtUtc,
        authority.StagingRun.UsersCount,
        authority.StagingRun.RoomsCount,
        authority.StagingRun.EventsCount,
        authority.EvidenceSchemaVersion,
        authority.EvidenceSha256,
        authority.AcknowledgementsSchemaVersion,
        authority.AcknowledgementsSha256,
        authority.CreatedAtUtc);

    private static void RequireFile(string path, string message)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(message);
        }
    }

    private static void RequireHashMatch(
        string actual,
        string? expected,
        string message)
    {
        if (string.IsNullOrWhiteSpace(expected) ||
            !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw EvidenceInvalid(message);
        }
    }

    private static void RequireNullableCountMatch(
        long? durable,
        long? evidence,
        string label)
    {
        if (durable is not null && evidence is not null && durable != evidence)
        {
            throw EvidenceInvalid(
                $"The retained private staging {label} count does not match its durable verification record.");
        }
    }

    private static async Task<string> HashFileAsync(
        string path,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var digest = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string SerializeCanonical<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    private static bool IsSafePathSegment(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal) &&
        !value.Contains(Path.DirectorySeparatorChar) &&
        !value.Contains(Path.AltDirectorySeparatorChar);

    private static string EnsureContainedPath(
        string root,
        string path,
        string message)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.Ordinal))
        {
            throw EvidenceInvalid(message);
        }

        return fullPath;
    }

    private static MigrationProductionAuthorityException Conflict(
        string code,
        string message) => new(
        code,
        message,
        MigrationProductionAuthorityFailureKind.Conflict);

    private static InvalidDataException EvidenceInvalid(string message) => new(message);


    private sealed record DurableSourceIdentity(
        string SourceId,
        string SourceKind,
        string Product,
        string ProductVersion,
        string SourceFingerprint,
        DateTime CapturedAtUtc);

    private sealed record ArchiveIdentityManifest(
        string Schema,
        int SchemaVersion,
        string MigrationId,
        ArchiveIdentitySource Source,
        ArchiveIdentityCapture Capture);

    private sealed record ArchiveIdentitySource(
        string Product,
        string Version,
        string StartFingerprint,
        string CompletionFingerprint);

    private sealed record ArchiveIdentityCapture(
        bool SourceChangedDuringCapture,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc);

    private sealed record VerifiedPreviewSelection(
        MigrationPackageRevisionEntity Revision,
        MigrationConversionAttemptEntity Conversion,
        MigrationCandidateArtifactEntity Candidate,
        MigrationStagingRunEntity Staging);

    private sealed class RetainedStagingEvidence
    {
        public RetainedStagingEvidence() { }

        public string Status { get; set; } = default!;
        public string StagingId { get; set; } = default!;
        public string MatrixDataPath { get; set; } = default!;
        public string? MatrixServerName { get; set; }
        public RetainedStagingSafety Safety { get; set; } = default!;
        public RetainedStagingDatabase Database { get; set; } = default!;
        public RetainedStagingRuntime Runtime { get; set; } = default!;
        public JsonElement? Destroy { get; set; }
    }

    private sealed class RetainedStagingSafety
    {
        public RetainedStagingSafety() { }

        public bool PrivateOnly { get; set; }
        public bool PublicRoutesCreated { get; set; }
        public bool ProductionContainersTouched { get; set; }
        public bool ProductionDatabasesTouched { get; set; }
        public bool RequiresExplicitDestroy { get; set; }
    }

    private sealed class RetainedStagingDatabase
    {
        public RetainedStagingDatabase() { }

        public bool ImportSucceeded { get; set; }
        public long? UsersCount { get; set; }
        public long? RoomsCount { get; set; }
        public long? EventsCount { get; set; }
    }

    private sealed class RetainedStagingRuntime
    {
        public RetainedStagingRuntime() { }

        public bool SigningKeyExtracted { get; set; }
        public bool SynapseHealthPassed { get; set; }
        public bool ElementConfigExtracted { get; set; }
        public bool ElementContainerStarted { get; set; }
        public bool ElementHealthPassed { get; set; }
        public bool ElementSynapseConnectivityPassed { get; set; }
        public bool ElementNetworkAttached { get; set; }
    }

    private sealed record OperatorAttestedAcknowledgementsEvidence(
        string SchemaVersion,
        bool UsersWereInstructedNotToUseSource,
        bool PostCaptureWritesWillNotMigrate,
        bool SelectedSnapshotBecomesAuthoritative,
        bool SourceWillBeRetainedUntilVerification,
        bool NoFormalSourceFreezeEvidence,
        bool ReducedRollbackAssurance);

    private sealed record OperatorAttestedProductionAuthorityEvidence(
        string SchemaVersion,
        string ProductionAuthorityId,
        string MigrationId,
        string AuthorityType,
        string PackageRevisionId,
        string EncryptedPackageSha256,
        string DecryptedArchiveSha256,
        string SourceMigrationId,
        string CaptureKind,
        bool SourceFrozen,
        bool RehearsalOnly,
        string SourceFingerprint,
        DateTime? CapturedAtUtc,
        string CandidateArtifactId,
        string CandidateArtifactSha256,
        string StagingRunId,
        string PrivateRuntimeStagingId,
        string MatrixServerName,
        string SigningKeyIdentitySha256,
        long? UsersCount,
        long? RoomsCount,
        long? EventsCount,
        bool FormalSourceFreezeEvidenceCollected,
        bool FinalRecapturePerformed,
        bool PotentialPostCaptureWritesIndependentlyExcluded,
        string RollbackAssurance,
        Guid CreatedByOperatorId,
        DateTime CreatedAtUtc,
        string AcknowledgementsSha256);
}
