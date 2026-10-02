using HostAgent.Runtime.Backups.Artifacts.LocalBackups;
using HostAgent.Runtime.Backups.Artifacts.LocalBackups.History;

namespace HostAgent.Runtime.Backups.Catalog;

/// <summary>
/// Registers local backup directories in the durable Backup Catalog. It is an
/// adapter over the existing local-backup inspector: current physical payloads
/// remain in their original locations and no payload files are moved.
/// </summary>
public sealed class LocalCapturedBackupCatalogRegistrationService
{
    private readonly LocalBackupCatalogService _localBackupCatalogService;
    private readonly BackupCatalogStore _catalogStore;

    public LocalCapturedBackupCatalogRegistrationService(
        LocalBackupCatalogService localBackupCatalogService,
        BackupCatalogStore catalogStore)
    {
        _localBackupCatalogService = localBackupCatalogService;
        _catalogStore = catalogStore;
    }

    /// <summary>
    /// Registers a backup captured in the current request. This is called after
    /// its manifest has been durably written to the local backup directory.
    /// </summary>
    public Task<BackupCatalogLocalRegistrationResult> RegisterCapturedAsync(
        LocalBackupCaptureResult capture,
        LocalBackupManifest manifest,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(manifest);

        return _catalogStore.EnsureLocalCapturedAsync(
            BuildRegistration(
                stackSlug: capture.StackSlug,
                backupId: capture.BackupId,
                backupRootPath: capture.BackupRootPath,
                capturedAtUtc: capture.CreatedAtUtc,
                totalBytes: capture.Stats.TotalBytes,
                warnings: capture.Warnings,
                databaseDumpPresent: capture.Stats.DatabaseDump.Included,
                homeserverConfigPresent: capture.Stats.HomeserverConfig.Included,
                signingKeyPresent: capture.Stats.SigningKey.Included,
                manifestVersion: ResolveManifestVersion(manifest.BackupVersion),
                memVersion: manifest.MemVersion,
                matrixServerName: manifest.MatrixServerName,
                matrixHost: manifest.Routes?.MatrixHost,
                elementHost: manifest.Routes?.ElementHost),
            ct);
    }

    /// <summary>
    /// Explicitly discovers existing local backup directories and registers each
    /// one as a catalog entry. It is safe to repeat and never exposes host paths
    /// through the returned operator contract.
    /// </summary>
    public async Task<BackupCatalogLocalBackfillResponse> BackfillAsync(
        CancellationToken ct)
    {
        var localCatalog = await _localBackupCatalogService.ListAsync(ct);
        var items = new List<BackupCatalogLocalBackfillItem>();
        var failures = new List<BackupCatalogLocalBackfillFailure>();
        var scanned = 0;
        var created = 0;
        var updated = 0;
        var skippedRemoved = 0;

        foreach (var backup in localCatalog.Stacks.SelectMany(static stack => stack.Backups))
        {
            ct.ThrowIfCancellationRequested();
            scanned++;

            try
            {
                var detail = await _localBackupCatalogService.InspectAsync(
                    backup.StackSlug,
                    backup.BackupId,
                    ct);

                var matrixServerName = FirstKnownValue(
                    detail.Manifest?.MatrixServerName,
                    await TryReadMatrixServerNameAsync(
                        detail.Components.MatrixConfig,
                        ct));

                var registration = BuildRegistration(
                    // The directory identity remains authoritative for local
                    // captures. Manifest values are evidence, not a substitute
                    // for the physical local-backup source key.
                    stackSlug: backup.StackSlug,
                    backupId: backup.BackupId,
                    backupRootPath: detail.BackupRootPath,
                    capturedAtUtc: detail.CreatedAtUtc ?? DateTime.UtcNow,
                    totalBytes: detail.TotalBytes,
                    warnings: detail.Warnings,
                    databaseDumpPresent: detail.Components.DatabaseDump.Present,
                    homeserverConfigPresent: detail.Components.MatrixConfig.Present,
                    signingKeyPresent: detail.Components.MatrixSigningKey.Present,
                    manifestVersion: ResolveManifestVersion(detail.Manifest?.BackupVersion),
                    memVersion: detail.Manifest?.MemVersion,
                    matrixServerName: matrixServerName,
                    matrixHost: detail.Manifest?.Routes?.MatrixHost,
                    elementHost: detail.Manifest?.Routes?.ElementHost);

                var result = await _catalogStore.EnsureLocalCapturedAsync(
                    registration,
                    ct);

                switch (result.Action)
                {
                    case "created":
                        created++;
                        break;
                    case "updated":
                        updated++;
                        break;
                    case "skipped-removed":
                        skippedRemoved++;
                        break;
                }

                items.Add(new BackupCatalogLocalBackfillItem(
                    backup.StackSlug,
                    backup.BackupId,
                    result.CatalogEntryId,
                    result.Action,
                    result.PayloadState,
                    result.IntegrityStatus,
                    registration.WarningCount));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(new BackupCatalogLocalBackfillFailure(
                    backup.StackSlug,
                    backup.BackupId,
                    ex.Message));
            }
        }

        var status = failures.Count == 0 ? "ok" : "warning";

        return new BackupCatalogLocalBackfillResponse(
            Source: "control-plane",
            Status: status,
            Scanned: scanned,
            Created: created,
            Updated: updated,
            SkippedRemoved: skippedRemoved,
            Failed: failures.Count,
            Entries: items,
            Failures: failures,
            Detail: failures.Count == 0
                ? $"Registered {created + updated + skippedRemoved} local backup(s) in the Backup Catalog."
                : $"Registered {created + updated + skippedRemoved} local backup(s); {failures.Count} item(s) require attention.");
    }

    private static BackupCatalogLocalRegistration BuildRegistration(
        string stackSlug,
        string backupId,
        string backupRootPath,
        DateTime capturedAtUtc,
        long totalBytes,
        IReadOnlyCollection<string> warnings,
        bool databaseDumpPresent,
        bool homeserverConfigPresent,
        bool signingKeyPresent,
        int? manifestVersion,
        string? memVersion,
        string? matrixServerName,
        string? matrixHost,
        string? elementHost)
    {
        var assessment = BackupCatalogLocalIntegrityEvaluator.Assess(
            databaseDumpPresent,
            homeserverConfigPresent,
            signingKeyPresent,
            warnings);

        return new BackupCatalogLocalRegistration(
            StackSlug: stackSlug,
            BackupId: backupId,
            PayloadDirectoryPath: backupRootPath,
            CapturedAtUtc: capturedAtUtc,
            PayloadBytes: totalBytes,
            WarningCount: warnings.Count(static warning => !string.IsNullOrWhiteSpace(warning)),
            IntegrityStatus: assessment.Status,
            IntegritySummary: assessment.Summary,
            MatrixHost: matrixHost,
            ElementHost: elementHost)
        {
            ManifestVersion = manifestVersion,
            MemVersion = memVersion,
            MatrixServerName = matrixServerName
        };
    }

    private static int? ResolveManifestVersion(string? backupVersion)
    {
        const string prefix = "mem-stack-backup-v";

        if (string.IsNullOrWhiteSpace(backupVersion) ||
            !backupVersion.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(backupVersion[prefix.Length..], out var version) && version > 0
            ? version
            : null;
    }

    private static async Task<string?> TryReadMatrixServerNameAsync(
        LocalBackupCatalogFileComponent matrixConfig,
        CancellationToken ct)
    {
        if (!matrixConfig.Present ||
            string.IsNullOrWhiteSpace(matrixConfig.AbsolutePath) ||
            !File.Exists(matrixConfig.AbsolutePath))
        {
            return null;
        }

        try
        {
            await foreach (var rawLine in File.ReadLinesAsync(matrixConfig.AbsolutePath, ct))
            {
                var value = TryReadTopLevelYamlScalar(rawLine, "server_name");

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            // Provenance projection is best-effort for historical local backups.
            // Payload integrity is assessed independently and remains authoritative.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep unknown provenance unknown rather than failing the whole backfill.
        }

        return null;
    }

    private static string? TryReadTopLevelYamlScalar(string rawLine, string key)
    {
        if (string.IsNullOrWhiteSpace(rawLine) || char.IsWhiteSpace(rawLine[0]))
        {
            return null;
        }

        var trimmed = rawLine.Trim();

        if (trimmed.StartsWith('#'))
        {
            return null;
        }

        var prefix = key + ":";

        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var value = trimmed[prefix.Length..].Trim();

        if (value.Length == 0)
        {
            return null;
        }

        if ((value.StartsWith('"') && value.EndsWith('"')) ||
            (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            value = value[1..^1].Trim();
        }

        var commentIndex = value.IndexOf(" #", StringComparison.Ordinal);

        if (commentIndex >= 0)
        {
            value = value[..commentIndex].TrimEnd();
        }

        return TrimToNull(value);
    }

    private static string? FirstKnownValue(params string?[] candidates) =>
        candidates
            .Select(TrimToNull)
            .FirstOrDefault(static value => value is not null);

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
