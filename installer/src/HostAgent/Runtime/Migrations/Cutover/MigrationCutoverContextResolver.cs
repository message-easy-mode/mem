using System.IO.Compression;
using System.Text.Json;
using HostAgent.Runtime.Backups.AdvancedCutover.Preflight;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Operator.Migrations;

namespace HostAgent.Runtime.Migrations.Cutover;

public sealed record MigrationCutoverContext(
    MigrationIntakeEntity Intake,
    MigrationProductionAuthorityEntity? ProductionAuthority,
    MigrationPackageRevisionEntity PackageRevision,
    MigrationCandidateArtifactEntity CandidateArtifact,
    MigrationStagingRunEntity StagingRun,
    PrivateStagingRunResult PrivateStaging,
    RuntimeStackBackupProductionRestoreSourceSummary SourceSummary,
    MigrationCutoverCaptureSummary Capture);

public sealed class MigrationCutoverContextResolver(
    MemDbContext db,
    PrivateStagingService privateStagingService,
    IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public Task<MigrationCutoverContext> ResolveAsync(
        string migrationId,
        CancellationToken ct) =>
        ResolveAsync(
            migrationId,
            allowVerifiedPreviewWithoutAuthority: false,
            ct);

    public Task<MigrationCutoverContext> ResolvePlanningAsync(
        string migrationId,
        CancellationToken ct) =>
        ResolveAsync(
            migrationId,
            allowVerifiedPreviewWithoutAuthority: true,
            ct);

    private async Task<MigrationCutoverContext> ResolveAsync(
        string migrationId,
        bool allowVerifiedPreviewWithoutAuthority,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
        {
            throw new InvalidOperationException("Migration id is required.");
        }

        var intake = await db.MigrationIntakes
            .AsNoTracking()
            .AsSplitQuery()
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
            .SingleOrDefaultAsync(x => x.IntakeId == migrationId, ct)
            ?? throw new FileNotFoundException(
                $"Migration Session '{migrationId}' was not found.");

        var selection = allowVerifiedPreviewWithoutAuthority
            ? ResolvePlanningSelection(intake)
            : ResolveAuthoritativeSelection(intake);
        var authority = selection.ProductionAuthority;
        var authoritativeRevision = selection.PackageRevision;
        var conversionAttempt = selection.ConversionAttempt;
        var candidate = selection.CandidateArtifact;
        var stagingRun = selection.StagingRun;

        ValidateCandidateSessionOwnership(intake, conversionAttempt, candidate);

        var privateStaging = await privateStagingService.GetAsync(
            stagingRun.PrivateRuntimeStagingId!,
            ct)
            ?? throw new FileNotFoundException(
                $"Private staging evidence '{stagingRun.PrivateRuntimeStagingId}' was not found.");

        if (privateStaging.Destroy is not null ||
            !string.Equals(
                privateStaging.Status,
                "ready",
                StringComparison.OrdinalIgnoreCase) ||
            !privateStaging.Runtime.ElementContainerStarted ||
            !privateStaging.Runtime.ElementHealthPassed ||
            !privateStaging.Runtime.ElementSynapseConnectivityPassed ||
            !privateStaging.Runtime.ElementNetworkAttached)
        {
            throw new InvalidOperationException(
                "The resolved full private staging runtime is no longer active and ready.");
        }

        var archivePath = MigrationPackageRevisionStorage.ResolveExistingDecryptedArchivePath(
            ResolveDataRoot(),
            migrationId,
            authoritativeRevision.PackageRevisionId,
            allowLegacyPreviewFallback:
                authority?.AuthorityType == MigrationProductionAuthorityTypes.OperatorAttestedSnapshot ||
                allowVerifiedPreviewWithoutAuthority &&
                string.Equals(
                    authoritativeRevision.Purpose,
                    "preview",
                    StringComparison.OrdinalIgnoreCase));

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException(
                $"Validated authoritative source migration archive was not found for Migration Session '{migrationId}'.");
        }

        var provenance = ReadCandidateProvenance(candidate.ProvenanceJson);
        ValidateCandidateSourceProvenance(
            intake,
            authoritativeRevision,
            conversionAttempt,
            candidate,
            provenance,
            authority,
            allowVerifiedPreviewWithoutAuthority);

        var archive = ReadArchiveSummary(
            archivePath,
            authoritativeRevision.ArchiveMigrationId!,
            provenance.SourceStackId,
            provenance.MatrixServerName);
        ValidateArchiveCapture(authoritativeRevision, archive.Manifest.Capture);

        if (authority is not null &&
            !string.Equals(
                authority.MatrixServerName,
                archive.Stack.MatrixServerName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The active production authority Matrix server identity does not match the validated source archive.");
        }

        var homeserverPath = Path.Combine(
            privateStaging.MatrixDataPath,
            "homeserver.yaml");
        var signingKeyPath = Path.Combine(
            privateStaging.MatrixDataPath,
            "signing.key");
        var mediaPath = Path.Combine(
            privateStaging.MatrixDataPath,
            "media_store");
        var elementConfigPath = string.IsNullOrWhiteSpace(privateStaging.ElementDataPath)
            ? null
            : Path.Combine(privateStaging.ElementDataPath!, "config.json");

        var sourceSummary = new RuntimeStackBackupProductionRestoreSourceSummary(
            UploadedZipFound: true,
            UploadedZipPath: archivePath,
            UploadedZipName: Path.GetFileName(archivePath),
            UploadedZipSizeBytes: new FileInfo(archivePath).Length,
            ManifestPresent: true,
            ManifestVersion: 1,
            ExportKind: "migration-candidate",
            CreatedAtUtc: authoritativeRevision.ValidatedAtUtc is { } validatedAtUtc
                ? new DateTimeOffset(
                    DateTime.SpecifyKind(validatedAtUtc, DateTimeKind.Utc))
                : null,
            CreatedBy: null,
            MemVersion: authoritativeRevision.ArchiveSourceVersion,
            SourceStackSlug: archive.Stack.Slug,
            SourceDisplayName: archive.Stack.DisplayName,
            SourceMatrixServerName: archive.Stack.MatrixServerName,
            MatrixPublicUrl: archive.Stack.MatrixPublicUrl,
            ElementPublicUrl: archive.Stack.ElementPublicUrl,
            ManifestMatrixHost:
                TryGetHost(archive.Stack.MatrixPublicUrl) ??
                archive.Stack.MatrixServerName,
            ManifestElementHost: TryGetHost(archive.Stack.ElementPublicUrl),
            DatabaseDumpPresent: File.Exists(candidate.ArtifactPath),
            DatabaseDumpPath: candidate.ArtifactPath,
            HomeserverConfigPresent: File.Exists(homeserverPath),
            HomeserverConfigPath: homeserverPath,
            SigningKeyPresent: File.Exists(signingKeyPath),
            SigningKeyPath: signingKeyPath,
            MediaStorePresent: Directory.Exists(mediaPath),
            MediaFiles: privateStaging.Runtime.MediaFiles,
            MediaBytes: privateStaging.Runtime.MediaBytes,
            ElementConfigPresent:
                elementConfigPath is not null &&
                File.Exists(elementConfigPath),
            ElementConfigPath: elementConfigPath,
            ManifestWarnings: archive.Manifest.Capture.RehearsalOnly
                ? authority?.AuthorityType == MigrationProductionAuthorityTypes.OperatorAttestedSnapshot
                    ? ["The source archive is rehearsal-only and is authorized through durable operator attestation. Formal source-freeze and final-recapture evidence were not collected."]
                    : ["The source archive is rehearsal-only and cannot authorize public cutover execution."]
                : [],
            SourceKind: "migration-session",
            CatalogEntryId: null);

        var capture = new MigrationCutoverCaptureSummary(
            archive.Manifest.Capture.Kind,
            archive.Manifest.Capture.SourceFrozen,
            archive.Manifest.Capture.RehearsalOnly,
            authority is not null ||
                archive.Manifest.Capture.SourceFrozen &&
                !archive.Manifest.Capture.RehearsalOnly &&
                string.Equals(
                    archive.Manifest.Capture.Kind,
                    "final",
                    StringComparison.OrdinalIgnoreCase),
            archive.Manifest.MigrationId,
            archive.Stack.Slug,
            archive.Stack.MatrixServerName,
            archive.Stack.MatrixPublicUrl,
            archive.Stack.ElementPublicUrl);

        return new MigrationCutoverContext(
            intake,
            authority,
            authoritativeRevision,
            candidate,
            stagingRun,
            privateStaging,
            sourceSummary,
            capture);
    }

    internal static AuthoritativeSelection ResolveAuthoritativeSelection(
        MigrationIntakeEntity intake)
    {
        var activeAuthorities = intake.ProductionAuthorities
            .Where(x =>
                x.Status == MigrationProductionAuthorityStatuses.Active &&
                x.ActiveMigrationKey == intake.IntakeId)
            .ToArray();

        if (activeAuthorities.Length > 1)
        {
            throw new InvalidDataException(
                "Migration has more than one active production authority.");
        }

        if (activeAuthorities.Length == 1)
        {
            return ResolveAuthoritySelection(intake, activeAuthorities[0]);
        }

        var finalRevision = ResolveAuthoritativeFinalRevision(intake);
        return ResolveSelectionForRevision(
            intake,
            finalRevision,
            productionAuthority: null,
            "authoritative final package revision");
    }

    internal static AuthoritativeSelection ResolvePlanningSelection(
        MigrationIntakeEntity intake)
    {
        ArgumentNullException.ThrowIfNull(intake);

        var activeAuthorities = intake.ProductionAuthorities
            .Where(x =>
                x.Status == MigrationProductionAuthorityStatuses.Active &&
                x.ActiveMigrationKey == intake.IntakeId)
            .ToArray();

        if (activeAuthorities.Length > 1)
        {
            throw new InvalidDataException(
                "Migration has more than one active production authority.");
        }

        if (activeAuthorities.Length == 1)
        {
            return ResolveAuthoritySelection(intake, activeAuthorities[0]);
        }

        var finalRevision = intake.PackageRevisions
            .SingleOrDefault(x =>
                x.Purpose == "final" &&
                x.ActivePurposeKey is not null);
        if (finalRevision is not null)
        {
            finalRevision = ResolveAuthoritativeFinalRevision(intake);
            return ResolveSelectionForRevision(
                intake,
                finalRevision,
                productionAuthority: null,
                "authoritative final package revision");
        }

        var previewRevision = intake.PackageRevisions
            .SingleOrDefault(x =>
                x.Purpose == "preview" &&
                x.ActivePurposeKey == $"{intake.IntakeId}:preview")
            ?? throw new InvalidOperationException(
                "A validated preview or final package revision is required before target identity review.");

        if (previewRevision.Status != "package-validated" ||
            previewRevision.RetentionState != "active" ||
            !string.Equals(
                previewRevision.CaptureKind,
                "preview",
                StringComparison.OrdinalIgnoreCase) ||
            previewRevision.SourceFrozen is not false ||
            previewRevision.RehearsalOnly is not true ||
            string.IsNullOrWhiteSpace(previewRevision.DecryptedArchiveSha256) ||
            string.IsNullOrWhiteSpace(previewRevision.ArchiveMigrationId))
        {
            throw new InvalidOperationException(
                "The active preview package revision does not contain the verified rehearsal evidence required for target identity review.");
        }

        return ResolveSelectionForRevision(
            intake,
            previewRevision,
            productionAuthority: null,
            "active validated preview package revision");
    }

    private static AuthoritativeSelection ResolveAuthoritySelection(
        MigrationIntakeEntity intake,
        MigrationProductionAuthorityEntity authority)
    {
        if (authority.MigrationIntakeEntityId != intake.Id ||
            authority.ActiveMigrationKey != intake.IntakeId ||
            authority.Status != MigrationProductionAuthorityStatuses.Active ||
            !MigrationProductionAuthorityTypes.IsKnown(authority.AuthorityType))
        {
            throw new InvalidDataException(
                "The active production authority does not belong to the requested Migration Session.");
        }

        var revision = intake.PackageRevisions.SingleOrDefault(x =>
            x.Id == authority.MigrationPackageRevisionEntityId)
            ?? throw new InvalidDataException(
                "The active production authority package revision is missing from the Migration Session.");

        var attempt = intake.ConversionAttempts.SingleOrDefault(x =>
            x.CandidateArtifact is not null &&
            x.CandidateArtifact.Id == authority.MigrationCandidateArtifactEntityId)
            ?? throw new InvalidDataException(
                "The active production authority candidate is not bound to a conversion attempt in the Migration Session.");

        var candidate = attempt.CandidateArtifact!;
        var staging = intake.StagingRuns.SingleOrDefault(x =>
            x.Id == authority.MigrationStagingRunEntityId)
            ?? throw new InvalidDataException(
                "The active production authority staging run is missing from the Migration Session.");

        if (attempt.MigrationPackageRevisionEntityId != revision.Id ||
            candidate.MigrationConversionAttemptEntityId != attempt.Id ||
            staging.MigrationCandidateArtifactEntityId != candidate.Id)
        {
            throw new InvalidDataException(
                "The active production authority does not bind one package, conversion candidate, and staging chain.");
        }

        ValidateAuthorityPackage(authority, intake, revision, attempt, candidate, staging);
        ValidateCandidateAndStaging(candidate, staging);

        return new AuthoritativeSelection(
            authority,
            revision,
            attempt,
            candidate,
            staging);
    }

    private static AuthoritativeSelection ResolveSelectionForRevision(
        MigrationIntakeEntity intake,
        MigrationPackageRevisionEntity revision,
        MigrationProductionAuthorityEntity? productionAuthority,
        string revisionLabel)
    {
        var candidateSelection = intake.ConversionAttempts
            .Where(x =>
                x.MigrationPackageRevisionEntityId == revision.Id &&
                x.CandidateArtifact is not null &&
                string.Equals(
                    x.CandidateArtifact.VerificationStatus,
                    "verified",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    x.CandidateArtifact.RetentionState,
                    "active",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    x.SourcePackageSha256,
                    revision.DecryptedArchiveSha256,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    x.CandidateArtifact.SourcePackageSha256,
                    revision.DecryptedArchiveSha256,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x =>
                x.CandidateArtifact!.VerifiedAtUtc ??
                x.CandidateArtifact.CreatedAtUtc)
            .Select(x => new CandidateSelection(x, x.CandidateArtifact!))
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Migration has no active verified candidate artifact for the {revisionLabel}.");

        var staging = intake.StagingRuns
            .Where(x =>
                x.MigrationCandidateArtifactEntityId == candidateSelection.CandidateArtifact.Id &&
                IsFullyVerifiedStaging(x))
            .OrderByDescending(x => x.CompletedAtUtc ?? x.UpdatedAtUtc)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Migration has no active fully verified PostgreSQL, Synapse, and Element private staging runtime. Start or recreate private staging before cutover preparation.");

        return new AuthoritativeSelection(
            productionAuthority,
            revision,
            candidateSelection.ConversionAttempt,
            candidateSelection.CandidateArtifact,
            staging);
    }

    private static void ValidateAuthorityPackage(
        MigrationProductionAuthorityEntity authority,
        MigrationIntakeEntity intake,
        MigrationPackageRevisionEntity revision,
        MigrationConversionAttemptEntity attempt,
        MigrationCandidateArtifactEntity candidate,
        MigrationStagingRunEntity staging)
    {
        var expectedPurpose = authority.AuthorityType ==
            MigrationProductionAuthorityTypes.OperatorAttestedSnapshot
                ? "preview"
                : "final";
        var expectedCaptureKind = expectedPurpose;
        var expectedSourceFrozen = authority.AuthorityType ==
            MigrationProductionAuthorityTypes.FinalFrozen;
        var expectedRehearsalOnly = !expectedSourceFrozen;

        if (revision.MigrationIntakeEntityId != intake.Id ||
            revision.Purpose != expectedPurpose ||
            revision.Status != "package-validated" ||
            revision.RetentionState != "active" ||
            revision.ActivePurposeKey != $"{intake.IntakeId}:{expectedPurpose}" ||
            !string.Equals(revision.CaptureKind, expectedCaptureKind, StringComparison.OrdinalIgnoreCase) ||
            revision.SourceFrozen != expectedSourceFrozen ||
            revision.RehearsalOnly != expectedRehearsalOnly ||
            string.IsNullOrWhiteSpace(revision.EncryptedPackageSha256) ||
            string.IsNullOrWhiteSpace(revision.DecryptedArchiveSha256) ||
            string.IsNullOrWhiteSpace(revision.ArchiveMigrationId))
        {
            throw new InvalidDataException(
                "The active production authority package no longer has the required validated capture semantics.");
        }

        if (attempt.Status is not ("completed" or "completed-with-warnings") ||
            !string.Equals(attempt.CurrentStep, "candidate-created", StringComparison.Ordinal) ||
            staging.ActiveMigrationKey != intake.IntakeId ||
            !string.Equals(authority.EncryptedPackageSha256, revision.EncryptedPackageSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(authority.DecryptedArchiveSha256, revision.DecryptedArchiveSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(authority.SourceMigrationId, revision.ArchiveMigrationId, StringComparison.Ordinal) ||
            !string.Equals(authority.CaptureKind, revision.CaptureKind, StringComparison.OrdinalIgnoreCase) ||
            authority.SourceFrozen != revision.SourceFrozen ||
            authority.RehearsalOnly != revision.RehearsalOnly ||
            !string.Equals(attempt.SourcePackageSha256, revision.DecryptedArchiveSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(candidate.SourcePackageSha256, revision.DecryptedArchiveSha256, StringComparison.OrdinalIgnoreCase) ||
            staging.MigrationIntakeEntityId != intake.Id)
        {
            throw new InvalidDataException(
                "The active production authority evidence no longer matches its package, candidate, and staging chain.");
        }
    }

    private static void ValidateCandidateAndStaging(
        MigrationCandidateArtifactEntity candidate,
        MigrationStagingRunEntity staging)
    {
        if (!string.Equals(candidate.VerificationStatus, "verified", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(candidate.RetentionState, "active", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The production authority candidate is no longer active and verified.");
        }

        if (!IsFullyVerifiedStaging(staging))
        {
            throw new InvalidOperationException(
                "The production authority staging runtime is no longer active and fully verified.");
        }
    }

    private static bool IsFullyVerifiedStaging(MigrationStagingRunEntity staging) =>
        staging.DestroyedAtUtc is null &&
        string.Equals(staging.Status, "verified", StringComparison.OrdinalIgnoreCase) &&
        staging.PrivateOnly &&
        !staging.PublicRoutesCreated &&
        staging.DatabaseImportSucceeded &&
        staging.SynapseHealthPassed &&
        staging.ElementConfigPresent &&
        staging.ElementContainerStarted &&
        staging.ElementHealthPassed &&
        staging.ElementSynapseConnectivityPassed &&
        staging.ElementNetworkAttached &&
        !string.IsNullOrWhiteSpace(staging.PrivateRuntimeStagingId);

    internal static MigrationPackageRevisionEntity ResolveAuthoritativeFinalRevision(
        MigrationIntakeEntity intake)
    {
        var finalRevision = intake.PackageRevisions
            .SingleOrDefault(x =>
                x.Purpose == "final" &&
                x.ActivePurposeKey is not null)
            ?? throw new InvalidOperationException(
                "A validated final frozen package revision is required before cutover preparation.");

        if (finalRevision.Status != "package-validated" ||
            finalRevision.CaptureKind != "final" ||
            finalRevision.SourceFrozen is not true ||
            finalRevision.RehearsalOnly is not false ||
            string.IsNullOrWhiteSpace(finalRevision.DecryptedArchiveSha256) ||
            string.IsNullOrWhiteSpace(finalRevision.ArchiveMigrationId))
        {
            throw new InvalidOperationException(
                "The active final package revision does not prove final frozen package authority.");
        }

        return finalRevision;
    }

    internal static void ValidateCandidateSessionOwnership(
        MigrationIntakeEntity intake,
        MigrationConversionAttemptEntity conversionAttempt,
        MigrationCandidateArtifactEntity candidate)
    {
        if (conversionAttempt.MigrationIntakeEntityId != intake.Id ||
            candidate.MigrationConversionAttemptEntityId != conversionAttempt.Id)
        {
            throw new InvalidDataException(
                "Migration candidate relationship does not belong to the requested Migration Session.");
        }
    }

    internal static void ValidateCandidateSourceProvenance(
        MigrationIntakeEntity intake,
        MigrationPackageRevisionEntity packageRevision,
        MigrationConversionAttemptEntity conversionAttempt,
        MigrationCandidateArtifactEntity candidate,
        CandidateProvenance provenance,
        MigrationProductionAuthorityEntity? authority = null,
        bool allowVerifiedPreviewWithoutAuthority = false)
    {
        if (packageRevision.MigrationIntakeEntityId != intake.Id ||
            packageRevision.Status != "package-validated" ||
            string.IsNullOrWhiteSpace(packageRevision.ArchiveMigrationId) ||
            string.IsNullOrWhiteSpace(packageRevision.DecryptedArchiveSha256))
        {
            throw new InvalidDataException(
                "Authoritative package revision provenance is incomplete.");
        }

        if (authority is null &&
            !allowVerifiedPreviewWithoutAuthority &&
            !string.Equals(packageRevision.Purpose, "final", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "A preview package requires active production authority before cutover preparation.");
        }

        if (authority is not null &&
            (authority.MigrationPackageRevisionEntityId != packageRevision.Id ||
             authority.MigrationCandidateArtifactEntityId != candidate.Id ||
             authority.MigrationIntakeEntityId != intake.Id))
        {
            throw new InvalidDataException(
                "Migration candidate provenance does not match the active production authority.");
        }

        if (conversionAttempt.MigrationPackageRevisionEntityId != packageRevision.Id)
        {
            throw new InvalidDataException(
                "Migration conversion attempt does not belong to the authoritative package revision.");
        }

        if (!string.Equals(
                provenance.MigrationId,
                packageRevision.ArchiveMigrationId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Migration candidate source migration identity does not match the authoritative package revision.");
        }

        if (!string.Equals(
                conversionAttempt.SourcePackageSha256,
                packageRevision.DecryptedArchiveSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                candidate.SourcePackageSha256,
                packageRevision.DecryptedArchiveSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Migration candidate source package checksum does not match the authoritative package revision.");
        }
    }

    private static void ValidateArchiveCapture(
        MigrationPackageRevisionEntity revision,
        ArchiveCapture capture)
    {
        if (revision.SourceFrozen is not bool sourceFrozen ||
            revision.RehearsalOnly is not bool rehearsalOnly ||
            !string.Equals(capture.Kind, revision.CaptureKind, StringComparison.OrdinalIgnoreCase) ||
            capture.SourceFrozen != sourceFrozen ||
            capture.RehearsalOnly != rehearsalOnly)
        {
            throw new InvalidDataException(
                "The validated archive capture semantics do not match the authoritative package revision.");
        }
    }

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(configuration);

    internal static CandidateProvenance ReadCandidateProvenance(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var migrationId = root.GetProperty("MigrationId").GetString();
            var sourceStackId = root.GetProperty("SourceStackId").GetGuid();
            var matrixServerName = root.GetProperty("MatrixServerName").GetString();

            if (string.IsNullOrWhiteSpace(migrationId) ||
                sourceStackId == Guid.Empty ||
                string.IsNullOrWhiteSpace(matrixServerName))
            {
                throw new InvalidDataException(
                    "Migration candidate provenance is incomplete.");
            }

            return new CandidateProvenance(
                migrationId,
                sourceStackId,
                matrixServerName);
        }
        catch (Exception ex) when (
            ex is JsonException or
            KeyNotFoundException or
            InvalidOperationException or
            FormatException)
        {
            throw new InvalidDataException(
                "Migration candidate provenance is invalid.",
                ex);
        }
    }

    internal static ArchiveResolution ReadArchiveSummary(
        string archivePath,
        string expectedMigrationId,
        Guid expectedSourceStackId,
        string expectedMatrixServerName)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var manifestEntry = archive.GetEntry(
            "mem-migration/migration-manifest.json")
            ?? throw new InvalidDataException(
                "Migration archive manifest is missing.");

        using var stream = manifestEntry.Open();
        var manifest = JsonSerializer.Deserialize<ArchiveManifest>(
            stream,
            JsonOptions)
            ?? throw new InvalidDataException(
                "Migration archive manifest is empty.");

        if (!string.Equals(
                manifest.Schema,
                "mem-v010-migration",
                StringComparison.Ordinal) ||
            manifest.SchemaVersion != 2 ||
            !string.Equals(
                manifest.MigrationId,
                expectedMigrationId,
                StringComparison.Ordinal) ||
            manifest.Stacks is null)
        {
            throw new InvalidDataException(
                "Migration archive manifest is unsupported.");
        }

        var stacks = manifest.Stacks
            .Where(x => x.SourceStackId == expectedSourceStackId)
            .ToArray();

        if (stacks.Length != 1 ||
            !string.Equals(
                stacks[0].MatrixServerName,
                expectedMatrixServerName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Migration candidate identity does not match the validated source archive.");
        }

        return new ArchiveResolution(manifest, stacks[0]);
    }

    private static string? TryGetHost(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri.Host
            : null;

    internal sealed record AuthoritativeSelection(
        MigrationProductionAuthorityEntity? ProductionAuthority,
        MigrationPackageRevisionEntity PackageRevision,
        MigrationConversionAttemptEntity ConversionAttempt,
        MigrationCandidateArtifactEntity CandidateArtifact,
        MigrationStagingRunEntity StagingRun);

    private sealed record CandidateSelection(
        MigrationConversionAttemptEntity ConversionAttempt,
        MigrationCandidateArtifactEntity CandidateArtifact);

    internal sealed record CandidateProvenance(
        string MigrationId,
        Guid SourceStackId,
        string MatrixServerName);

    internal sealed record ArchiveResolution(
        ArchiveManifest Manifest,
        ArchiveStack Stack);

    internal sealed record ArchiveManifest(
        string Schema,
        int SchemaVersion,
        string MigrationId,
        ArchiveCapture Capture,
        ArchiveStack[]? Stacks);

    internal sealed record ArchiveCapture(
        string Kind,
        bool SourceFrozen,
        bool RehearsalOnly);

    internal sealed record ArchiveStack(
        Guid SourceStackId,
        string Slug,
        string DisplayName,
        string MatrixServerName,
        string? MatrixPublicUrl,
        string? ElementPublicUrl);
}
