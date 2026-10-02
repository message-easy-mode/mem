using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Modules.Operator.Migrations;

/// <summary>
/// Composes the durable Migration Session root and its canonical package,
/// conversion, staging, production, acceptance and backup authorities into a
/// migration-specific read model. This service performs no mutation.
/// </summary>
public sealed class MigrationSessionProjectionService
{
    private readonly MemDbContext _db;
    private readonly TimeProvider _timeProvider;

    public MigrationSessionProjectionService(
        MemDbContext db,
        TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<MigrationSessionDetailDto?> GetAsync(
        string migrationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
        {
            return null;
        }

        var intake = await _db.MigrationIntakes
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Sources)
            .Include(x => x.PackageRevisions)
            .Include(x => x.ConversionAttempts)
                .ThenInclude(x => x.CandidateArtifact)
            .Include(x => x.ConversionAttempts)
                .ThenInclude(x => x.PackageRevision)
            .Include(x => x.StagingRuns)
            .Include(x => x.ProductionAdoption)
            .Include(x => x.Acceptance)
                .ThenInclude(x => x!.LegacyRetentionRecord)
            .Include(x => x.Acceptance)
                .ThenInclude(x => x!.BaselineBackupHandoff)
            .Include(x => x.LegacyRetentionRecord)
            .Include(x => x.BaselineBackupHandoff)
            .SingleOrDefaultAsync(
                x => x.IntakeId == migrationId,
                cancellationToken);

        if (intake is null)
        {
            return null;
        }

        var related = await LoadRelatedStateAsync(new[] { intake }, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var summary = BuildSummary(intake, related, now);

        return new MigrationSessionDetailDto(
            summary,
            BuildPackage(intake, now),
            BuildPackageRevisions(intake, now),
            BuildSources(intake),
            Array.Empty<MigrationSessionFindingDto>(),
            BuildLinkedObjects(intake, related));
    }

    private async Task<RelatedState> LoadRelatedStateAsync(
        IReadOnlyCollection<MigrationIntakeEntity> intakes,
        CancellationToken cancellationToken)
    {
        var catalogEntryIds = intakes
            .Select(intake => intake.BaselineBackupHandoff?.CatalogEntryId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (catalogEntryIds.Length == 0)
        {
            return RelatedState.Empty;
        }

        var catalogRows = await _db.BackupCatalogEntries
            .AsNoTracking()
            .Where(x => catalogEntryIds.Contains(x.CatalogEntryId))
            .Select(x => new CatalogLinkRow(
                x.CatalogEntryId,
                x.DisplayName,
                x.PayloadState))
            .ToListAsync(cancellationToken);

        return new RelatedState(
            catalogRows.ToDictionary(x => x.CatalogEntryId, StringComparer.Ordinal));
    }

    private static MigrationSessionSummaryDto BuildSummary(
        MigrationIntakeEntity intake,
        RelatedState related,
        DateTime now)
    {
        const int blockerCount = 0;
        const int warningCount = 0;
        const int advisoryCount = 0;

        var lifecycle = ResolveLifecycle(intake, now, blockerCount);
        var source = ResolveSource(intake);
        var sourceRevision = ResolveSourceRevision(intake);
        var historical = BuildHistoricalCompatibility(intake, related);

        return new MigrationSessionSummaryDto(
            intake.IntakeId,
            intake.DisplayName,
            source.Adapter,
            source.Display,
            lifecycle.Phase,
            lifecycle.Status,
            lifecycle.NextAction,
            intake.CreatedAtUtc,
            ResolveUpdatedAtUtc(intake, now),
            blockerCount,
            warningCount,
            advisoryCount,
            lifecycle.RequiresAttention || blockerCount > 0 || warningCount > 0,
            ResolveSourceCount(intake),
            sourceRevision?.ArchiveStackCount ?? intake.Sources.Count,
            historical);
    }

    private static MigrationSessionPackageDto? BuildPackage(
        MigrationIntakeEntity intake,
        DateTime now)
    {
        if (!IsSecureIntake(intake))
        {
            return null;
        }

        var revision = MigrationPackageRevisionAuthority.ResolveAuthoritativeValidated(
                           intake.PackageRevisions) ??
                       MigrationPackageRevisionAuthority.ResolveCurrentPackage(
                           intake.PackageRevisions);
        if (revision is null)
        {
            return null;
        }

        return new MigrationSessionPackageDto(
            "encrypted",
            MigrationPackageRevisionAuthority.ResolveEffectiveRevisionStatus(
                revision,
                now),
            string.IsNullOrWhiteSpace(revision.PackageFileName)
                ? null
                : Path.GetFileName(revision.PackageFileName),
            revision.PackageSizeBytes,
            revision.EncryptedPackageSha256,
            revision.DecryptedArchiveSha256,
            revision.UploadedAtUtc,
            revision.ValidatedAtUtc,
            revision.ExpiresAtUtc,
            revision.AgeRecipient,
            revision.RecipientFingerprint,
            revision.ArchiveMigrationId,
            revision.ArchiveSourceProduct,
            revision.ArchiveSourceVersion,
            revision.ArchiveStackCount);
    }


    private static IReadOnlyList<MigrationSessionPackageRevisionDto> BuildPackageRevisions(
        MigrationIntakeEntity intake,
        DateTime now) =>
        intake.PackageRevisions
            .OrderBy(x => x.RevisionNumber)
            .ThenBy(x => x.CreatedAtUtc)
            .Select(revision => new MigrationSessionPackageRevisionDto(
                revision.PackageRevisionId,
                revision.RevisionNumber,
                revision.Purpose,
                ResolveEffectiveRevisionStatus(revision, now),
                revision.RetentionState,
                revision.ActivePurposeKey is not null,
                "encrypted",
                string.IsNullOrWhiteSpace(revision.PackageFileName)
                    ? null
                    : Path.GetFileName(revision.PackageFileName),
                revision.PackageSizeBytes,
                revision.EncryptedPackageSha256,
                revision.DecryptedArchiveSha256,
                revision.CreatedAtUtc,
                revision.UploadedAtUtc,
                revision.ValidatedAtUtc,
                revision.ExpiresAtUtc,
                revision.SupersededAtUtc,
                revision.RetiredAtUtc,
                revision.AgeRecipient,
                revision.RecipientFingerprint,
                revision.ArchiveMigrationId,
                revision.ArchiveSourceProduct,
                revision.ArchiveSourceVersion,
                revision.ArchiveStackCount,
                revision.CaptureKind,
                revision.SourceFrozen,
                revision.RehearsalOnly,
                revision.VerifiedFileCount,
                revision.VerifiedExpandedBytes,
                revision.ValidationCode,
                revision.ValidationSummary))
            .ToArray();

    private static string ResolveEffectiveRevisionStatus(
        MigrationPackageRevisionEntity revision,
        DateTime now) =>
        MigrationPackageRevisionAuthority.ResolveEffectiveRevisionStatus(
            revision,
            now);

    private static IReadOnlyList<MigrationSessionLinkedObjectDto> BuildLinkedObjects(
        MigrationIntakeEntity intake,
        RelatedState related)
    {
        if (intake.BaselineBackupHandoff?.CatalogEntryId is not { Length: > 0 } catalogEntryId)
        {
            return Array.Empty<MigrationSessionLinkedObjectDto>();
        }

        if (related.CatalogEntries.TryGetValue(catalogEntryId, out var catalog))
        {
            return new[]
            {
                new MigrationSessionLinkedObjectDto(
                    "backup-catalog-entry",
                    catalog.CatalogEntryId,
                    catalog.DisplayName,
                    catalog.Status,
                    "first-native-baseline-backup",
                    false),
            };
        }

        return new[]
        {
            new MigrationSessionLinkedObjectDto(
                "backup-catalog-entry",
                catalogEntryId,
                "First native MEM baseline backup",
                "not-found",
                "first-native-baseline-backup",
                false),
        };
    }

    private static IReadOnlyList<MigrationSessionSourceDto> BuildSources(
        MigrationIntakeEntity intake)
    {
        if (intake.Sources.Count > 0)
        {
            return intake.Sources
                .OrderBy(x => x.SourceId, StringComparer.Ordinal)
                .Select(x => new MigrationSessionSourceDto(
                    x.SourceId,
                    x.SourceKind,
                    x.Product,
                    x.ProductVersion,
                    x.SourceFingerprint,
                    x.CapturedAtUtc))
                .ToArray();
        }

        var revision = ResolveSourceRevision(intake);
        if (string.IsNullOrWhiteSpace(revision?.ArchiveSourceProduct))
        {
            return Array.Empty<MigrationSessionSourceDto>();
        }

        return new[]
        {
            new MigrationSessionSourceDto(
                revision.ArchiveMigrationId ?? intake.IntakeId,
                "migration-package",
                revision.ArchiveSourceProduct,
                revision.ArchiveSourceVersion,
                null,
                revision.ValidatedAtUtc),
        };
    }


    private static MigrationSessionHistoricalCompatibilityDto BuildHistoricalCompatibility(
        MigrationIntakeEntity intake,
        RelatedState related)
    {
        _ = intake;
        _ = related;
        return new MigrationSessionHistoricalCompatibilityDto(
            false,
            null,
            null,
            null,
            false,
            0,
            0);
    }

    private static SourceIdentity ResolveSource(MigrationIntakeEntity intake)
    {
        var firstSource = intake.Sources
            .OrderBy(x => x.SourceId, StringComparer.Ordinal)
            .FirstOrDefault();
        var revision = ResolveSourceRevision(intake);
        var product = firstSource?.Product ?? revision?.ArchiveSourceProduct;
        var version = firstSource?.ProductVersion ?? revision?.ArchiveSourceVersion;

        if (IsMemV010(product, version))
        {
            return new SourceIdentity("mem-v010", "Message Easy Mode 0.1.0");
        }

        if (!string.IsNullOrWhiteSpace(product))
        {
            return new SourceIdentity(
                "unknown",
                string.IsNullOrWhiteSpace(version)
                    ? product
                    : $"{product} {version}");
        }

        if (IsSecureIntake(intake))
        {
            return new SourceIdentity(
                "pending",
                "Package not yet received");
        }

        return new SourceIdentity("unknown", "Source not classified");
    }

    private static MigrationPackageRevisionEntity? ResolveSourceRevision(
        MigrationIntakeEntity intake) =>
        MigrationPackageRevisionAuthority.ResolveAuthoritativeValidated(
            intake.PackageRevisions) ??
        intake.PackageRevisions
            .Where(x => !string.IsNullOrWhiteSpace(x.ArchiveSourceProduct))
            .OrderByDescending(x => x.RevisionNumber)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();


    private static LifecycleProjection ResolveLifecycle(
        MigrationIntakeEntity intake,
        DateTime now,
        int blockerCount)
    {
        if (intake.Acceptance is not null)
        {
            return intake.BaselineBackupHandoff?.Status switch
            {
                "created" => new(
                    "completed",
                    "migration-completed",
                    "open-baseline-backup",
                    false),
                "failed" => new(
                    "acceptance",
                    "accepted-baseline-backup-failed",
                    "retry-baseline-backup",
                    true),
                _ => new(
                    "acceptance",
                    "accepted-baseline-backup-pending",
                    "review-baseline-backup",
                    false),
            };
        }

        var rawStatus = ResolveEffectiveRawStatus(intake, now);
        if (rawStatus != "package-validated")
        {
            return rawStatus switch
            {
                "awaiting-package" => new(
                    "package-transfer",
                    "awaiting-package",
                    "upload-package",
                    false),
                "expired" => new(
                    "package-transfer",
                    "expired",
                    "create-replacement-intake",
                    true),
                "cancelled" => new(
                    "closed",
                    "cancelled",
                    "none",
                    false),
                _ => new(
                    "manual-review",
                    "manual-review-required",
                    "review-migration",
                    true),
            };
        }

        var productionLifecycle = ResolveProductionLifecycle(intake.ProductionAdoption, now);
        if (productionLifecycle is not null)
        {
            return productionLifecycle;
        }

        var authoritativeRevision = ResolveAuthoritativePackageRevision(intake);
        var authoritativeAttempts = authoritativeRevision is null
            ? Array.Empty<MigrationConversionAttemptEntity>()
            : intake.ConversionAttempts
                .Where(x => x.MigrationPackageRevisionEntityId == authoritativeRevision.Id)
                .ToArray();
        var latestConversion = authoritativeAttempts
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        var verifiedCandidate = authoritativeAttempts
            .Select(x => x.CandidateArtifact)
            .Where(x =>
                x is not null &&
                x.VerificationStatus == "verified" &&
                x.RetentionState == "active" &&
                string.Equals(
                    x.SourcePackageSha256,
                    authoritativeRevision!.DecryptedArchiveSha256,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x!.CreatedAtUtc)
            .FirstOrDefault();

        if (verifiedCandidate is null)
        {
            if (latestConversion?.Status is "pending" or "running")
            {
                return new(
                    "conversion",
                    "conversion-running",
                    "review-conversion",
                    false);
            }

            if (latestConversion?.Status is "failed" or "cancelled")
            {
                return new(
                    "conversion",
                    "conversion-failed",
                    "retry-conversion",
                    true);
            }

            return new(
                "source-assessment",
                "package-validated",
                "review-source",
                blockerCount > 0);
        }

        var authoritativeCandidateIds = authoritativeAttempts
            .Where(x => x.CandidateArtifact is not null)
            .Select(x => x.CandidateArtifact!.Id)
            .ToHashSet();
        var latestStaging = intake.StagingRuns
            .Where(x => authoritativeCandidateIds.Contains(x.MigrationCandidateArtifactEntityId))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        if (latestStaging is null)
        {
            return new(
                "staging",
                "staging-ready",
                "start-private-staging",
                false);
        }

        if (latestStaging.ActiveMigrationKey is not null &&
            string.Equals(
                latestStaging.FailureCode,
                "staging_destroy_failed",
                StringComparison.Ordinal))
        {
            return new(
                "staging",
                "staging-failed-retained",
                "destroy-private-staging",
                true);
        }

        return latestStaging.Status switch
        {
            "pending" or "running" => new(
                "staging",
                "staging-running",
                "review-private-staging",
                false),
            "verified" => new(
                "staging",
                "staging-verified",
                "review-private-staging",
                false),
            "destroyed" => new(
                "staging",
                "staging-destroyed",
                "recreate-private-staging",
                false),
            "failed" or "failed-cleaned" when latestStaging.ActiveMigrationKey is not null => new(
                "staging",
                "staging-failed-retained",
                "destroy-private-staging",
                true),
            "failed" or "failed-cleaned" => new(
                "staging",
                "staging-failed",
                "retry-private-staging",
                true),
            _ => new(
                "staging",
                "staging-review-required",
                "review-private-staging",
                true),
        };
    }

    private static LifecycleProjection? ResolveProductionLifecycle(
        MigrationProductionAdoptionEntity? adoption,
        DateTime now)
    {
        if (adoption is null)
        {
            return null;
        }

        _ = now;
        if (string.Equals(
                adoption.ProductionVerificationStatus,
                "passed",
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                "acceptance",
                "production-verification-passed",
                "accept-migration",
                false);
        }

        var requiresAttention = adoption.Status is
            "materialization-failed" or
            "cutover-failed" or
            "production-verification-failed" or
            "rollback-failed";

        return new(
            "cutover",
            adoption.Status,
            "review-production-adoption",
            requiresAttention);
    }

    private static MigrationPackageRevisionEntity? ResolveAuthoritativePackageRevision(
        MigrationIntakeEntity intake) =>
        MigrationPackageRevisionAuthority.ResolveAuthoritativeValidated(
            intake.PackageRevisions);


    private static string ResolveEffectiveRawStatus(
        MigrationIntakeEntity intake,
        DateTime now) =>
        MigrationPackageRevisionAuthority.ResolveEffectiveSessionStatus(
            intake,
            now);


    private static DateTime ResolveUpdatedAtUtc(
        MigrationIntakeEntity intake,
        DateTime now)
    {
        var timestamps = new List<DateTime>
        {
            intake.CreatedAtUtc,
        };

        Add(timestamps, intake.CancelledAtUtc);

        foreach (var revision in intake.PackageRevisions)
        {
            timestamps.Add(revision.CreatedAtUtc);
            Add(timestamps, revision.UploadedAtUtc);
            Add(timestamps, revision.ValidatedAtUtc);
            Add(timestamps, revision.SupersededAtUtc);
            Add(timestamps, revision.RetiredAtUtc);
            if (revision.ExpiresAtUtc is { } revisionExpiresAtUtc && revisionExpiresAtUtc <= now)
            {
                timestamps.Add(revisionExpiresAtUtc);
            }
        }

        foreach (var source in intake.Sources)
        {
            Add(timestamps, source.CapturedAtUtc);
        }

        foreach (var attempt in intake.ConversionAttempts)
        {
            timestamps.Add(attempt.CreatedAtUtc);
            timestamps.Add(attempt.UpdatedAtUtc);
            Add(timestamps, attempt.StartedAtUtc);
            Add(timestamps, attempt.CompletedAtUtc);
            if (attempt.CandidateArtifact is { } candidate)
            {
                timestamps.Add(candidate.CreatedAtUtc);
                Add(timestamps, candidate.VerifiedAtUtc);
                Add(timestamps, candidate.RetiredAtUtc);
            }
        }

        foreach (var run in intake.StagingRuns)
        {
            timestamps.Add(run.CreatedAtUtc);
            timestamps.Add(run.UpdatedAtUtc);
            Add(timestamps, run.StartedAtUtc);
            Add(timestamps, run.CompletedAtUtc);
            Add(timestamps, run.DestroyedAtUtc);
        }

        if (intake.ProductionAdoption is { } adoption)
        {
            timestamps.Add(adoption.CreatedAtUtc);
            timestamps.Add(adoption.UpdatedAtUtc);
            timestamps.Add(adoption.PreparedAtUtc);
            Add(timestamps, adoption.MaterializationStartedAtUtc);
            Add(timestamps, adoption.MaterializationCompletedAtUtc);
            Add(timestamps, adoption.CutoverPreviewCreatedAtUtc);
            Add(timestamps, adoption.CutoverStartedAtUtc);
            Add(timestamps, adoption.CutoverCompletedAtUtc);
            Add(timestamps, adoption.TargetPublicAtUtc);
            Add(timestamps, adoption.ProductionVerificationStartedAtUtc);
            Add(timestamps, adoption.ProductionVerificationCompletedAtUtc);
        }

        if (intake.Acceptance is { } acceptance)
        {
            timestamps.Add(acceptance.AcceptedAtUtc);
            timestamps.Add(acceptance.PublicCutoverAtUtc);
        }
        if (intake.LegacyRetentionRecord is { } retention)
        {
            timestamps.Add(retention.CreatedAtUtc);
            Add(timestamps, retention.CleanupCompletedAtUtc);
        }
        if (intake.BaselineBackupHandoff is { } baseline)
        {
            timestamps.Add(baseline.CreatedAtUtc);
            timestamps.Add(baseline.UpdatedAtUtc);
            Add(timestamps, baseline.StartedAtUtc);
            Add(timestamps, baseline.CompletedAtUtc);
            Add(timestamps, baseline.BackupCreatedAtUtc);
        }

        return timestamps.Max();
    }

    private static void Add(ICollection<DateTime> timestamps, DateTime? value)
    {
        if (value.HasValue)
        {
            timestamps.Add(value.Value);
        }
    }

    private static int ResolveSourceCount(MigrationIntakeEntity intake)
    {
        if (intake.Sources.Count > 0)
        {
            return intake.Sources.Count;
        }

        return ResolveSourceRevision(intake) is null ? 0 : 1;
    }

    private static bool IsSecureIntake(MigrationIntakeEntity intake) =>
        MigrationPackageRevisionAuthority.IsSecureSession(intake);


    private static bool IsMemV010(string? product, string? version)
    {
        if (!string.Equals(version, "0.1.0", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(product))
        {
            return false;
        }

        var normalized = new string(product
            .Where(char.IsLetterOrDigit)
            .Select(character => char.ToLowerInvariant(character))
            .ToArray());

        return normalized is "matrixeasymode" or "messageeasymode" or "mem";
    }

    private sealed record SourceIdentity(string Adapter, string Display);

    private sealed record LifecycleProjection(
        string Phase,
        string Status,
        string NextAction,
        bool RequiresAttention);

    private sealed record CatalogLinkRow(
        string CatalogEntryId,
        string DisplayName,
        string Status);

    private sealed record RelatedState(
        IReadOnlyDictionary<string, CatalogLinkRow> CatalogEntries)
    {
        public static RelatedState Empty { get; } = new(
            new Dictionary<string, CatalogLinkRow>(StringComparer.Ordinal));
    }
}
