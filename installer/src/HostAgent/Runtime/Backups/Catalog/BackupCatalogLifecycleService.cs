using HostAgent.Runtime.Backups.Artifacts.PortableExports;
using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Runtime.Backups.Catalog;

/// <summary>
/// Owns safe operator lifecycle actions for a Backup Catalog entry.
/// Payload removal was the original catalog lifecycle action. The normal
/// operator delete action is now permanent: it removes the managed payload,
/// owned portable exports, the original imported ZIP where applicable, and the
/// catalog row itself. Restore history is retained by detaching terminal
/// restore attempts before the catalog row is removed.
/// </summary>
public sealed class BackupCatalogLifecycleService
{
    private readonly MemDbContext _db;
    private readonly ImportValidationService _imports;
    private readonly CatalogPortableExportService _portableExports;
    private readonly IConfiguration _configuration;
    private readonly BackupCatalogDeleteOperationLifetime _deleteOperationLifetime;

    public BackupCatalogLifecycleService(
        MemDbContext db,
        ImportValidationService imports,
        CatalogPortableExportService portableExports,
        IConfiguration configuration,
        BackupCatalogDeleteOperationLifetime deleteOperationLifetime)
    {
        _db = db;
        _imports = imports;
        _portableExports = portableExports;
        _configuration = configuration;
        _deleteOperationLifetime = deleteOperationLifetime;
    }

    public async Task<BackupCatalogLifecycleResponse?> GetAsync(
        string id,
        CancellationToken ct)
    {
        var entry = await _db.BackupCatalogEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CatalogEntryId == id, ct);

        if (entry is null)
        {
            return null;
        }

        var activeRestoreSessionId = await _db.RestoreAttempts
            .AsNoTracking()
            .Where(x => x.BackupCatalogEntryId == entry.Id && x.ActiveSourceKey != null)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => x.RestoreSessionId)
            .FirstOrDefaultAsync(ct);

        var hasActiveRestore = activeRestoreSessionId is not null;
        var archive = await ArchiveAsync(entry.ValidationId, entry.CatalogEntryId, ct);

        return new BackupCatalogLifecycleResponse(
            CatalogEntryId: entry.CatalogEntryId,
            PayloadState: entry.PayloadState,
            PayloadPresent: Directory.Exists(entry.PayloadDirectoryPath),
            HasActiveRestore: hasActiveRestore,
            ActiveRestoreSessionId: activeRestoreSessionId,
            CanDelete: !hasActiveRestore,
            DeleteBlockReason: hasActiveRestore
                ? "An active restore workspace references this Backup Catalog entry. Complete, cancel, or otherwise close that restore before permanently deleting its source backup."
                : null,
            OriginalArchive: archive);
    }

    /// <summary>
    /// Permanently removes one Backup Catalog source. This action never removes
    /// a completed restored runtime stack. Any terminal restore attempts are
    /// retained for audit, but their nullable catalog foreign key is detached so
    /// the catalog row can be removed without leaving a tombstone in inventory.
    /// </summary>
    public async Task<BackupCatalogPermanentDeleteResponse?> DeleteAsync(
        string id,
        string? actor,
        CancellationToken ct)
    {
        var entry = await _db.BackupCatalogEntries
            .SingleOrDefaultAsync(x => x.CatalogEntryId == id, ct);

        if (entry is null)
        {
            return null;
        }

        var activeRestoreSessionId = await _db.RestoreAttempts
            .AsNoTracking()
            .Where(x => x.BackupCatalogEntryId == entry.Id && x.ActiveSourceKey != null)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => x.RestoreSessionId)
            .FirstOrDefaultAsync(ct);

        if (activeRestoreSessionId is not null)
        {
            throw new BackupCatalogLifecycleConflictException(
                $"Restore workspace '{activeRestoreSessionId}' is still active and references this Backup Catalog entry.");
        }

        // Entry existence and active-restore conflict checks above are request-owned
        // preconditions. From this point onward, permanent deletion mutates owned
        // filesystem artifacts and durable catalog state. The browser/request may
        // observe the outcome, but it must no longer own the mutation lifetime.
        using var operationLifetime = _deleteOperationLifetime.BeginAfterAcceptance(
            entry.CatalogEntryId,
            ct);
        var operationToken = operationLifetime.CancellationToken;

        try
        {
            var originalArchiveDeleted = await DeleteOriginalArchiveAsync(
                entry.ValidationId,
                operationToken);

            var portableExportsDeleted = await _portableExports
                .DeleteGeneratedExportsAsync(
                    entry.CatalogEntryId,
                    operationToken);

            var payloadDeleted = DeletePayloadDirectory(
                entry.PayloadDirectoryPath,
                operationToken);

            var historicalAttempts = await _db.RestoreAttempts
                .Where(x => x.BackupCatalogEntryId == entry.Id)
                .ToListAsync(operationToken);

            foreach (var attempt in historicalAttempts)
            {
                attempt.BackupCatalogEntryId = null;
            }

            _db.BackupCatalogEntries.Remove(entry);
            await _db.SaveChangesAsync(operationToken);

            return new BackupCatalogPermanentDeleteResponse(
                Source: "control-plane",
                Status: "deleted",
                CatalogEntryId: entry.CatalogEntryId,
                OriginKind: entry.OriginKind,
                DeletedBy: NormalizeActor(actor),
                PayloadDeleted: payloadDeleted,
                OriginalArchiveDeleted: originalArchiveDeleted,
                PortableExportsDeleted: portableExportsDeleted,
                DetachedRestoreAttempts: historicalAttempts.Count,
                Detail: BuildDeleteDetail(
                    entry.OriginKind,
                    historicalAttempts.Count,
                    payloadDeleted,
                    originalArchiveDeleted,
                    portableExportsDeleted));
        }
        catch (OperationCanceledException ex) when (operationLifetime.OperationTimeoutRequested)
        {
            // Do not allow a concurrent browser disconnect to make a genuine
            // server-side mutation timeout look like an ordinary HTTP 499. The
            // global exception boundary classifies TimeoutException as an
            // incident-producing technical failure.
            throw new TimeoutException(
                $"Permanent Backup Catalog deletion for '{entry.CatalogEntryId}' exceeded its bounded server-owned operation timeout.",
                ex);
        }
    }

    public async Task<BackupCatalogImportArchiveResponse?> GetArchiveAsync(
        string validationId,
        CancellationToken ct)
    {
        var entry = await _db.BackupCatalogEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ValidationId == validationId, ct);

        return entry is null
            ? null
            : await ArchiveAsync(validationId, entry.CatalogEntryId, ct);
    }

    public async Task<BackupCatalogImportArchiveDeleteResponse?> DeleteArchiveAsync(
        string validationId,
        string? actor,
        CancellationToken ct)
    {
        var entry = await _db.BackupCatalogEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ValidationId == validationId, ct);

        if (entry is null)
        {
            return null;
        }

        var validation = await _imports.GetCachedValidationAsync(validationId, ct);
        if (!string.IsNullOrWhiteSpace(validation?.StoredZipPath) &&
            File.Exists(validation.StoredZipPath))
        {
            File.Delete(validation.StoredZipPath);
        }

        return new BackupCatalogImportArchiveDeleteResponse(
            Source: "control-plane",
            Status: "ok",
            ValidationId: validationId,
            ArchiveState: "removed",
            CatalogEntryId: entry.CatalogEntryId,
            Detail: "Original ZIP transport archive was removed. The managed Backup Catalog payload was not changed.");
    }

    private async Task<bool> DeleteOriginalArchiveAsync(
        string? validationId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(validationId) ||
            !IsSafePathSegment(validationId))
        {
            return false;
        }

        var validation = await _imports.GetCachedValidationAsync(validationId, ct);
        var storedZipPath = validation?.StoredZipPath;
        var archiveDeleted = !string.IsNullOrWhiteSpace(storedZipPath) &&
                             File.Exists(storedZipPath);

        // A validation id owns one upload directory. Remove the whole directory
        // rather than only source.zip so a hard catalog delete does not leave a
        // stale validation receipt or original-upload metadata behind. Use the
        // canonical data-root path rather than trusting a receipt path.
        var uploadDirectory = Path.Combine(
            GetDataRoot(),
            "imports",
            "uploads",
            validationId);

        ct.ThrowIfCancellationRequested();
        if (Directory.Exists(uploadDirectory))
        {
            Directory.Delete(uploadDirectory, recursive: true);
            return archiveDeleted;
        }

        if (archiveDeleted && !string.IsNullOrWhiteSpace(storedZipPath))
        {
            File.Delete(storedZipPath);
        }

        return archiveDeleted;
    }

    private string GetDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static bool IsSafePathSegment(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) < 0 &&
        !value.Contains("..", StringComparison.Ordinal);

    private static bool DeletePayloadDirectory(
        string payloadDirectoryPath,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(payloadDirectoryPath) ||
            !Directory.Exists(payloadDirectoryPath))
        {
            return false;
        }

        Directory.Delete(payloadDirectoryPath, recursive: true);
        return true;
    }

    private async Task<BackupCatalogImportArchiveResponse?> ArchiveAsync(
        string? validationId,
        string catalogEntryId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(validationId))
        {
            return null;
        }

        var validation = await _imports.GetCachedValidationAsync(validationId, ct);
        var retained = !string.IsNullOrWhiteSpace(validation?.StoredZipPath) &&
                       File.Exists(validation.StoredZipPath);

        return new BackupCatalogImportArchiveResponse(
            ValidationId: validationId,
            ArchiveState: retained ? "retained" : "removed",
            OriginalFileName: validation?.UploadedFileName,
            ArchiveBytes: retained ? validation!.ZipBytes : null,
            CatalogEntryLinked: true,
            CatalogEntryId: catalogEntryId,
            Detail: retained
                ? "Original ZIP transport archive is retained."
                : "Original ZIP transport archive is not retained.");
    }

    private static string? NormalizeActor(string? actor) =>
        string.IsNullOrWhiteSpace(actor) ? null : actor.Trim();

    private static string BuildDeleteDetail(
        string originKind,
        int detachedRestoreAttempts,
        bool payloadDeleted,
        bool originalArchiveDeleted,
        int portableExportsDeleted)
    {
        var parts = new List<string>
        {
            "The Backup Catalog record was permanently deleted."
        };

        if (payloadDeleted)
        {
            parts.Add("The managed recovery payload was deleted.");
        }

        if (string.Equals(originKind, BackupCatalogOriginKinds.ImportedZip, StringComparison.Ordinal) &&
            originalArchiveDeleted)
        {
            parts.Add("The original imported ZIP archive was deleted.");
        }

        if (portableExportsDeleted > 0)
        {
            parts.Add($"{portableExportsDeleted} generated portable export artifact(s) were deleted.");
        }

        if (detachedRestoreAttempts > 0)
        {
            parts.Add("Completed restore history was retained without a catalog-source link.");
        }

        return string.Join(" ", parts);
    }
}

public sealed class BackupCatalogLifecycleConflictException : InvalidOperationException
{
    public BackupCatalogLifecycleConflictException(string message)
        : base(message)
    {
    }
}
