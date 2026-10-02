using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostAgent.Runtime.Backups.Catalog;

/// <summary>
/// Persistence boundary for Backup Catalog metadata. It never copies, extracts,
/// or deletes payload files; filesystem materialisation remains a separate
/// responsibility.
/// </summary>
public sealed class BackupCatalogStore
{
    private readonly MemDbContext _db;
    private readonly ImportValidationService? _importValidationService;

    public BackupCatalogStore(
        MemDbContext db,
        ImportValidationService? importValidationService = null)
    {
        _db = db;
        _importValidationService = importValidationService;
    }

    /// <summary>
    /// Returns the safe operator-facing catalog inventory in stable newest-first
    /// order. Imported ZIP validation guidance is projected as non-blocking
    /// advisories rather than being confused with payload integrity failure.
    /// </summary>
    public async Task<BackupCatalogListResponse> ListAsync(
        CancellationToken ct)
    {
        var rows = await _db.BackupCatalogEntries
            .AsNoTracking()
            .OrderByDescending(entry => entry.CapturedAtUtc)
            .ThenByDescending(entry => entry.CreatedAtUtc)
            .ThenBy(entry => entry.CatalogEntryId)
            .Select(ToReadModelExpression())
            .ToListAsync(ct);

        var entries = new List<BackupCatalogListItem>(rows.Count);

        foreach (var row in rows)
        {
            entries.Add(await ToListItemAsync(row, ct));
        }

        return new BackupCatalogListResponse(
            TotalCount: entries.Count,
            Entries: entries);
    }

    /// <summary>
    /// Finds one catalog entry by its stable public identity.
    ///
    /// This deliberately returns only safe operator metadata. It must never
    /// expose host filesystem paths, raw payload storage details, secrets, or
    /// materialised archive locations.
    /// </summary>
    public async Task<BackupCatalogDetailResponse?> FindByCatalogEntryIdAsync(
        string catalogEntryId,
        CancellationToken ct)
    {
        var normalizedCatalogEntryId = RequireValue(
            catalogEntryId,
            "Backup Catalog entry id is required.");

        var row = await _db.BackupCatalogEntries
            .AsNoTracking()
            .Where(entry => entry.CatalogEntryId == normalizedCatalogEntryId)
            .Select(ToReadModelExpression())
            .SingleOrDefaultAsync(ct);

        return row is null
            ? null
            : await ToDetailAsync(row, ct);
    }

    /// <summary>
    /// Creates or refreshes the catalog row for one local backup directory.
    /// Repeating the operation for the same local stack/backup identity is
    /// intentionally idempotent. A removed catalog row is never silently
    /// reactivated by a backfill operation.
    /// </summary>
    public async Task<BackupCatalogLocalRegistrationResult> EnsureLocalCapturedAsync(
        BackupCatalogLocalRegistration registration,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var stackSlug = RequireValue(
            registration.StackSlug,
            "Local backup stack slug is required.");

        var backupId = RequireValue(
            registration.BackupId,
            "Local backup id is required.");

        var payloadDirectoryPath = Path.GetFullPath(RequireValue(
            registration.PayloadDirectoryPath,
            "Local backup payload directory is required."));

        var integrityStatus = RequireValue(
            registration.IntegrityStatus,
            "Local backup integrity status is required.");

        var integritySummary = RequireValue(
            registration.IntegritySummary,
            "Local backup integrity summary is required.");

        var existing = await _db.BackupCatalogEntries
            .SingleOrDefaultAsync(
                entry => entry.OriginKind == BackupCatalogOriginKinds.LocalCaptured &&
                         entry.SourceStackSlug == stackSlug &&
                         entry.SourceBackupId == backupId,
                ct);

        if (existing is not null)
        {
            if (string.Equals(
                    existing.PayloadState,
                    BackupCatalogPayloadStates.Removed,
                    StringComparison.Ordinal))
            {
                return new BackupCatalogLocalRegistrationResult(
                    existing.CatalogEntryId,
                    Action: "skipped-removed",
                    existing.PayloadState,
                    existing.IntegrityStatus);
            }

            ApplyLocalRegistration(
                existing,
                stackSlug,
                backupId,
                payloadDirectoryPath,
                registration,
                integrityStatus,
                integritySummary);

            await _db.SaveChangesAsync(ct);

            return new BackupCatalogLocalRegistrationResult(
                existing.CatalogEntryId,
                Action: "updated",
                existing.PayloadState,
                existing.IntegrityStatus);
        }

        var entry = new BackupCatalogEntryEntity
        {
            Id = Guid.NewGuid(),
            CatalogEntryId = CreateCatalogEntryId(),
            OriginKind = BackupCatalogOriginKinds.LocalCaptured,
            DisplayName = CreateLocalDisplayName(stackSlug, backupId),
            PayloadState = BackupCatalogPayloadStates.Available,
            PayloadStorageKind = BackupCatalogPayloadStorageKinds.LocalBackupDirectory,
            PayloadDirectoryPath = payloadDirectoryPath,
            SourceStackSlug = stackSlug,
            SourceBackupId = backupId,
            IntegrityStatus = integrityStatus,
            IntegritySummary = integritySummary,
            WarningCount = Math.Max(0, registration.WarningCount),
            PayloadBytes = Math.Max(0, registration.PayloadBytes),
            CreatedAtUtc = registration.CapturedAtUtc,
            CapturedAtUtc = registration.CapturedAtUtc,
            ManifestVersion = NormalizePositiveVersion(registration.ManifestVersion),
            MemVersion = TrimToNull(registration.MemVersion),
            MatrixServerName = TrimToNull(registration.MatrixServerName),
            MatrixHost = TrimToNull(registration.MatrixHost),
            ElementHost = TrimToNull(registration.ElementHost)
        };

        _db.BackupCatalogEntries.Add(entry);
        await _db.SaveChangesAsync(ct);

        return new BackupCatalogLocalRegistrationResult(
            entry.CatalogEntryId,
            Action: "created",
            entry.PayloadState,
            entry.IntegrityStatus);
    }

    private static void ApplyLocalRegistration(
        BackupCatalogEntryEntity entry,
        string stackSlug,
        string backupId,
        string payloadDirectoryPath,
        BackupCatalogLocalRegistration registration,
        string integrityStatus,
        string integritySummary)
    {
        entry.DisplayName = CreateLocalDisplayName(stackSlug, backupId);
        entry.PayloadDirectoryPath = payloadDirectoryPath;
        entry.PayloadStorageKind = BackupCatalogPayloadStorageKinds.LocalBackupDirectory;
        entry.SourceStackSlug = stackSlug;
        entry.SourceBackupId = backupId;
        entry.IntegrityStatus = integrityStatus;
        entry.IntegritySummary = integritySummary;
        entry.WarningCount = Math.Max(0, registration.WarningCount);
        entry.PayloadBytes = Math.Max(0, registration.PayloadBytes);
        entry.CapturedAtUtc = registration.CapturedAtUtc;

        // Provenance is capture-time evidence. A later backfill of an older
        // manifest must not erase metadata already recorded by the original
        // capture merely because that historical payload lacks newer fields.
        entry.ManifestVersion = NormalizePositiveVersion(registration.ManifestVersion)
            ?? entry.ManifestVersion;
        entry.MemVersion = TrimToNull(registration.MemVersion)
            ?? entry.MemVersion;
        entry.MatrixServerName = TrimToNull(registration.MatrixServerName)
            ?? entry.MatrixServerName;
        entry.MatrixHost = TrimToNull(registration.MatrixHost);
        entry.ElementHost = TrimToNull(registration.ElementHost);
    }

    /// <summary>
    /// Resolves the internal source contract needed to begin a catalog-backed
    /// restore attempt. This intentionally remains separate from operator-facing
    /// DTOs because it includes the managed payload path.
    /// </summary>
    public async Task<BackupCatalogRestoreSource?> FindRestoreSourceAsync(
        string catalogEntryId,
        CancellationToken ct)
    {
        var normalizedCatalogEntryId = RequireValue(
            catalogEntryId,
            "Backup Catalog entry id is required.");

        var row = await _db.BackupCatalogEntries
            .AsNoTracking()
            .Where(entry => entry.CatalogEntryId == normalizedCatalogEntryId)
            .Select(ToReadModelExpression())
            .SingleOrDefaultAsync(ct);

        return row is null
            ? null
            : await ToRestoreSourceAsync(row, ct);
    }

    /// <summary>
    /// Resolves the internal restore source by its durable database identity.
    /// Restore attempts store this identity so a canonical workspace can dispatch
    /// back to the catalog without treating a validation receipt as its source.
    /// </summary>
    public async Task<BackupCatalogRestoreSource?> FindRestoreSourceByEntryIdAsync(
        Guid entryId,
        CancellationToken ct)
    {
        if (entryId == Guid.Empty)
        {
            throw new InvalidOperationException("Backup Catalog entry identity is required.");
        }

        var row = await _db.BackupCatalogEntries
            .AsNoTracking()
            .Where(entry => entry.Id == entryId)
            .Select(ToReadModelExpression())
            .SingleOrDefaultAsync(ct);

        return row is null
            ? null
            : await ToRestoreSourceAsync(row, ct);
    }

    private static string CreateCatalogEntryId() =>
        $"bkp_{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssZ}-{Guid.NewGuid():N}";

    private static string CreateLocalDisplayName(
        string stackSlug,
        string backupId) =>
        $"Local backup {stackSlug} / {backupId}";

    private static string RequireValue(
        string? value,
        string message)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException(message);
        }

        return trimmed;
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrWhiteSpace(trimmed)
            ? null
            : trimmed;
    }

    private static int? NormalizePositiveVersion(int? value) =>
        value is > 0 ? value : null;

    /// <summary>
    /// Creates or resumes the durable catalog record for a validated uploaded
    /// ZIP. It never marks an entry available: that transition happens only
    /// after the managed payload directory has been fully materialised.
    /// </summary>
    public async Task<BackupCatalogImportedMaterialisationStartResult> StartImportedMaterialisationAsync(
        BackupCatalogImportedMaterialisationRegistration registration,
        string payloadDirectoryPath,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var validationId = RequireValue(registration.ValidationId, "Validation id is required.");
        var payloadPath = Path.GetFullPath(RequireValue(
            payloadDirectoryPath,
            "Managed catalog payload directory is required."));

        var existing = await _db.BackupCatalogEntries
            .SingleOrDefaultAsync(entry => entry.ValidationId == validationId, ct);

        if (existing is not null)
        {
            if (existing.PayloadState == BackupCatalogPayloadStates.Available)
            {
                return new BackupCatalogImportedMaterialisationStartResult(
                    existing.CatalogEntryId,
                    "already-available",
                    existing.PayloadState,
                    existing.PayloadDirectoryPath);
            }

            if (existing.PayloadState == BackupCatalogPayloadStates.Removed)
            {
                return new BackupCatalogImportedMaterialisationStartResult(
                    existing.CatalogEntryId,
                    "skipped-removed",
                    existing.PayloadState,
                    existing.PayloadDirectoryPath);
            }

            ApplyImportedRegistration(existing, registration, payloadPath);
            existing.PayloadState = BackupCatalogPayloadStates.Materialising;
            existing.MaterialisedAtUtc = null;
            existing.PayloadBytes = null;
            await _db.SaveChangesAsync(ct);

            return new BackupCatalogImportedMaterialisationStartResult(
                existing.CatalogEntryId,
                "resumed",
                existing.PayloadState,
                existing.PayloadDirectoryPath);
        }

        var entry = new BackupCatalogEntryEntity
        {
            Id = Guid.NewGuid(),
            CatalogEntryId = CreateCatalogEntryId(),
            OriginKind = BackupCatalogOriginKinds.ImportedZip,
            PayloadState = BackupCatalogPayloadStates.Materialising,
            PayloadStorageKind = BackupCatalogPayloadStorageKinds.CatalogManagedDirectory,
            PayloadDirectoryPath = payloadPath,
            CreatedAtUtc = registration.ImportedAtUtc,
            ImportedAtUtc = registration.ImportedAtUtc
        };

        ApplyImportedRegistration(entry, registration, payloadPath);
        _db.BackupCatalogEntries.Add(entry);
        await _db.SaveChangesAsync(ct);

        return new BackupCatalogImportedMaterialisationStartResult(
            entry.CatalogEntryId,
            "created",
            entry.PayloadState,
            entry.PayloadDirectoryPath);
    }

    public async Task CompleteImportedMaterialisationAsync(
        string catalogEntryId,
        long payloadBytes,
        CancellationToken ct)
    {
        var entry = await FindTrackedImportedEntryAsync(catalogEntryId, ct);

        entry.PayloadState = BackupCatalogPayloadStates.Available;
        entry.PayloadBytes = Math.Max(0, payloadBytes);
        entry.MaterialisedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task FailImportedMaterialisationAsync(
        string catalogEntryId,
        string detail,
        CancellationToken ct)
    {
        var entry = await FindTrackedImportedEntryAsync(catalogEntryId, ct);

        entry.PayloadState = BackupCatalogPayloadStates.Failed;
        entry.IntegrityStatus = BackupCatalogIntegrityStatuses.Invalid;
        entry.IntegritySummary = RequireValue(detail, "Materialisation failure detail is required.");
        entry.MaterialisedAtUtc = null;
        entry.PayloadBytes = null;

        await _db.SaveChangesAsync(ct);
    }

    private async Task<BackupCatalogEntryEntity> FindTrackedImportedEntryAsync(
        string catalogEntryId,
        CancellationToken ct)
    {
        var normalizedCatalogEntryId = RequireValue(
            catalogEntryId,
            "Backup Catalog entry id is required.");

        var entry = await _db.BackupCatalogEntries.SingleOrDefaultAsync(
            value => value.CatalogEntryId == normalizedCatalogEntryId &&
                     value.OriginKind == BackupCatalogOriginKinds.ImportedZip,
            ct);

        return entry ?? throw new InvalidOperationException(
            $"Imported Backup Catalog entry '{normalizedCatalogEntryId}' was not found.");
    }

    private static void ApplyImportedRegistration(
        BackupCatalogEntryEntity entry,
        BackupCatalogImportedMaterialisationRegistration registration,
        string payloadPath)
    {
        entry.DisplayName = RequireValue(registration.DisplayName, "Imported backup display name is required.");
        entry.PayloadStorageKind = BackupCatalogPayloadStorageKinds.CatalogManagedDirectory;
        entry.PayloadDirectoryPath = payloadPath;
        entry.ValidationId = RequireValue(registration.ValidationId, "Validation id is required.");
        entry.SourceStackSlug = TrimToNull(registration.SourceStackSlug);
        entry.ManifestVersion = registration.ManifestVersion;
        entry.MemVersion = TrimToNull(registration.MemVersion);
        entry.MatrixServerName = TrimToNull(registration.MatrixServerName);
        entry.MatrixHost = TrimToNull(registration.MatrixHost);
        entry.ElementHost = TrimToNull(registration.ElementHost);
        entry.IntegrityStatus = RequireValue(registration.IntegrityStatus, "Imported backup integrity status is required.");
        entry.IntegritySummary = RequireValue(registration.IntegritySummary, "Imported backup integrity summary is required.");
        entry.WarningCount = Math.Max(0, registration.WarningCount);
        entry.ImportedAtUtc ??= registration.ImportedAtUtc;
    }

    private async Task<BackupCatalogListItem> ToListItemAsync(
        CatalogEntryReadModel row,
        CancellationToken ct)
    {
        var presentation = await ResolveIntegrityPresentationAsync(row, ct);

        return new BackupCatalogListItem(
            row.CatalogEntryId,
            row.OriginKind,
            row.DisplayName,
            row.SourceStackSlug,
            row.SourceBackupId,
            row.CapturedAtUtc,
            row.PayloadState,
            presentation.IntegrityStatus,
            presentation.WarningCount,
            row.PayloadBytes,
            row.CreatedAtUtc,
            row.ImportedAtUtc,
            row.MaterialisedAtUtc,
            row.PayloadRemovedAtUtc,
            presentation.Advisories.Count);
    }

    private async Task<BackupCatalogDetailResponse> ToDetailAsync(
        CatalogEntryReadModel row,
        CancellationToken ct)
    {
        var presentation = await ResolveIntegrityPresentationAsync(row, ct);

        return new BackupCatalogDetailResponse(
            row.CatalogEntryId,
            row.OriginKind,
            row.DisplayName,
            row.SourceStackSlug,
            row.SourceBackupId,
            row.ValidationId,
            row.ManifestVersion,
            row.MemVersion,
            row.MatrixServerName,
            row.MatrixHost,
            row.ElementHost,
            row.CapturedAtUtc,
            row.PayloadState,
            presentation.IntegrityStatus,
            presentation.IntegritySummary,
            presentation.WarningCount,
            row.PayloadBytes,
            row.CreatedAtUtc,
            row.ImportedAtUtc,
            row.MaterialisedAtUtc,
            row.PayloadRemovedAtUtc,
            row.PayloadRemovedBy,
            presentation.Advisories.Count,
            presentation.Advisories);
    }

    private async Task<BackupCatalogRestoreSource> ToRestoreSourceAsync(
        CatalogEntryReadModel row,
        CancellationToken ct)
    {
        var presentation = await ResolveIntegrityPresentationAsync(row, ct);

        return new BackupCatalogRestoreSource(
            row.Id,
            row.CatalogEntryId,
            row.OriginKind,
            row.DisplayName,
            row.PayloadState,
            row.PayloadDirectoryPath,
            row.SourceStackSlug,
            row.SourceBackupId,
            row.ValidationId,
            presentation.IntegrityStatus,
            presentation.WarningCount,
            row.ElementHost,
            row.CapturedAtUtc,
            row.PayloadBytes);
    }

    private async Task<CatalogIntegrityPresentation> ResolveIntegrityPresentationAsync(
        CatalogEntryReadModel row,
        CancellationToken ct)
    {
        var fallback = new CatalogIntegrityPresentation(
            row.IntegrityStatus,
            row.IntegritySummary,
            row.WarningCount,
            []);

        if (_importValidationService is null ||
            !string.Equals(row.OriginKind, BackupCatalogOriginKinds.ImportedZip, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(row.ValidationId))
        {
            return fallback;
        }

        try
        {
            var validation = await _importValidationService.GetCachedValidationAsync(
                row.ValidationId,
                ct);

            var classified = ImportedZipCatalogIntegrityClassifier.Classify(validation);

            return classified is null
                ? fallback
                : new CatalogIntegrityPresentation(
                    classified.IntegrityStatus,
                    classified.IntegritySummary,
                    classified.IntegrityWarningCount,
                    classified.Advisories);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            // A readable catalog entry must never disappear merely because an
            // optional retained validation receipt cannot be read. Preserve its
            // durable stored state and let the detail page explain what is known.
            return fallback;
        }
    }

    private static System.Linq.Expressions.Expression<Func<BackupCatalogEntryEntity, CatalogEntryReadModel>> ToReadModelExpression() =>
        entry => new CatalogEntryReadModel(
            entry.Id,
            entry.CatalogEntryId,
            entry.OriginKind,
            entry.DisplayName,
            entry.PayloadState,
            entry.PayloadDirectoryPath,
            entry.SourceStackSlug,
            entry.SourceBackupId,
            entry.ValidationId,
            entry.ManifestVersion,
            entry.MemVersion,
            entry.MatrixServerName,
            entry.MatrixHost,
            entry.ElementHost,
            entry.IntegrityStatus,
            entry.IntegritySummary,
            entry.WarningCount,
            entry.PayloadBytes,
            entry.CreatedAtUtc,
            entry.CapturedAtUtc,
            entry.ImportedAtUtc,
            entry.MaterialisedAtUtc,
            entry.PayloadRemovedAtUtc,
            entry.PayloadRemovedBy);

    private sealed record CatalogEntryReadModel(
        Guid Id,
        string CatalogEntryId,
        string OriginKind,
        string DisplayName,
        string PayloadState,
        string PayloadDirectoryPath,
        string? SourceStackSlug,
        string? SourceBackupId,
        string? ValidationId,
        int? ManifestVersion,
        string? MemVersion,
        string? MatrixServerName,
        string? MatrixHost,
        string? ElementHost,
        string IntegrityStatus,
        string? IntegritySummary,
        int WarningCount,
        long? PayloadBytes,
        DateTime CreatedAtUtc,
        DateTime? CapturedAtUtc,
        DateTime? ImportedAtUtc,
        DateTime? MaterialisedAtUtc,
        DateTime? PayloadRemovedAtUtc,
        string? PayloadRemovedBy);

    private sealed record CatalogIntegrityPresentation(
        string IntegrityStatus,
        string? IntegritySummary,
        int WarningCount,
        IReadOnlyList<BackupCatalogAdvisory> Advisories);
}
