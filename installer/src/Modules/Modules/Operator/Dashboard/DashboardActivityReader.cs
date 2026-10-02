using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Modules.Operator.Dashboard;

/// <summary>
/// Reads a bounded, deliberately sparse Home activity feed from durable state.
/// It never projects operation input/results/evidence, raw errors, operator
/// identifiers, archive names, validation identifiers, or filesystem paths.
/// </summary>
public sealed class DashboardActivityReader
{
    private const int CandidateLimitPerSource = 16;
    private const int MaximumItems = 8;

    private readonly MemDbContext _db;

    public DashboardActivityReader(MemDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<DashboardActivityItem>> ReadRecentAsync(
        CancellationToken ct)
    {
        var runtimeOperations = await _db.RuntimeOperations
            .AsNoTracking()
            .OrderByDescending(x => x.CompletedAtUtc ?? x.StartedAtUtc ?? x.RequestedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(CandidateLimitPerSource)
            .Select(x => new RuntimeOperationActivityRow(
                x.Id,
                x.RuntimeStackId,
                x.Operation,
                x.Status,
                x.CompletedAtUtc ?? x.StartedAtUtc ?? x.RequestedAtUtc))
            .ToListAsync(ct);

        var stackSlugs = await ReadStackSlugMapAsync(
            runtimeOperations
                .Where(x => x.RuntimeStackId.HasValue)
                .Select(x => x.RuntimeStackId!.Value)
                .ToArray(),
            ct);

        var catalogEntries = await _db.BackupCatalogEntries
            .AsNoTracking()
            .OrderByDescending(x => x.MaterialisedAtUtc ?? x.CapturedAtUtc ?? x.ImportedAtUtc ?? x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(CandidateLimitPerSource)
            .Select(x => new BackupCatalogActivityRow(
                x.Id,
                x.SourceStackSlug,
                x.OriginKind,
                x.PayloadState,
                x.IntegrityStatus,
                x.MaterialisedAtUtc ?? x.CapturedAtUtc ?? x.ImportedAtUtc ?? x.CreatedAtUtc))
            .ToListAsync(ct);

        var restoreAttempts = await _db.RestoreAttempts
            .AsNoTracking()
            .OrderByDescending(x => x.LastEventAtUtc ?? x.UpdatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(CandidateLimitPerSource)
            .Select(x => new RestoreAttemptActivityRow(
                x.Id,
                x.RestoreSessionId,
                x.SourceStackSlugSnapshot,
                x.Status,
                x.WarningCount,
                x.ErrorCount,
                x.LastEventAtUtc ?? x.UpdatedAtUtc))
            .ToListAsync(ct);

        var certificates = await _db.Certificates
            .AsNoTracking()
            .Where(x => x.IsMainPlatformCertificate || x.IsActive)
            .OrderByDescending(x => x.LastValidatedAtUtc ?? x.LastImportedToNpmAtUtc ?? x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(CandidateLimitPerSource)
            .Select(x => new CertificateActivityRow(
                x.Id,
                x.Status,
                x.IsStaging,
                x.LastValidatedAtUtc ?? x.LastImportedToNpmAtUtc ?? x.CreatedAtUtc))
            .ToListAsync(ct);

        var activity = new List<DashboardActivityItem>();
        activity.AddRange(runtimeOperations.Select(row => ToRuntimeOperationItem(row, stackSlugs)));
        activity.AddRange(catalogEntries.Select(ToCatalogItem));
        activity.AddRange(restoreAttempts.Select(ToRestoreAttemptItem));
        activity.AddRange(certificates.Select(ToCertificateItem));

        return activity
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .Select(group => group.Single())
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .Take(MaximumItems)
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ReadStackSlugMapAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await _db.RuntimeStacks
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Slug })
            .ToDictionaryAsync(x => x.Id, x => x.Slug, ct);
    }

    private static DashboardActivityItem ToRuntimeOperationItem(
        RuntimeOperationActivityRow row,
        IReadOnlyDictionary<Guid, string> stackSlugs)
    {
        var stackSlug = row.RuntimeStackId.HasValue &&
                        stackSlugs.TryGetValue(row.RuntimeStackId.Value, out var value)
            ? value
            : null;

        return new DashboardActivityItem(
            Id: $"runtime-operation:{row.Id:N}",
            Kind: "runtime_operation",
            Severity: ToOperationSeverity(row.Status),
            OccurredAtUtc: ToUtc(row.OccurredAtUtc),
            TitleCode: ToOperationTitleCode(row.Operation, row.Status),
            DetailCode: null,
            StackSlug: stackSlug,
            RestoreSessionId: null);
    }

    private static DashboardActivityItem ToCatalogItem(BackupCatalogActivityRow row) =>
        new(
            Id: $"backup-catalog:{row.Id:N}",
            Kind: "backup_catalog",
            Severity: ToCatalogSeverity(row.PayloadState, row.IntegrityStatus),
            OccurredAtUtc: ToUtc(row.OccurredAtUtc),
            TitleCode: ToCatalogTitleCode(row.OriginKind, row.PayloadState, row.IntegrityStatus),
            DetailCode: null,
            StackSlug: row.SourceStackSlug,
            RestoreSessionId: null);

    private static DashboardActivityItem ToRestoreAttemptItem(RestoreAttemptActivityRow row) =>
        new(
            Id: $"restore-attempt:{row.Id:N}",
            Kind: "restore_attempt",
            Severity: ToRestoreSeverity(row.Status, row.WarningCount, row.ErrorCount),
            OccurredAtUtc: ToUtc(row.OccurredAtUtc),
            TitleCode: ToRestoreTitleCode(row.Status, row.WarningCount, row.ErrorCount),
            DetailCode: null,
            StackSlug: row.SourceStackSlug,
            RestoreSessionId: row.RestoreSessionId);

    private static DashboardActivityItem ToCertificateItem(CertificateActivityRow row) =>
        new(
            Id: $"certificate:{row.Id:N}",
            Kind: "certificate",
            Severity: row.IsStaging || !string.Equals(row.Status, "active", StringComparison.OrdinalIgnoreCase)
                ? "warning"
                : "success",
            OccurredAtUtc: ToUtc(row.OccurredAtUtc),
            TitleCode: row.IsStaging
                ? "certificate_staging_observed"
                : "certificate_updated",
            DetailCode: null,
            StackSlug: null,
            RestoreSessionId: null);

    private static string ToOperationSeverity(string status) =>
        status.ToLowerInvariant() switch
        {
            "failed" => "danger",
            "completed" or "succeeded" or "success" => "success",
            _ => "info"
        };

    private static string ToOperationTitleCode(string operation, string status)
    {
        var prefix = operation.ToLowerInvariant() switch
        {
            "create-stack-runtime" => "chat_server_created",
            "destroy-stack-runtime" => "chat_server_removed",
            "backup-stack" => "backup_completed",
            "doctor-stack" => "chat_server_diagnostics_completed",
            "restore.standard-recreate" => "restore_recreate_updated",
            "restore.private-test" => "restore_private_test_updated",
            "restore.standard-recreate.cleanup" => "restore_cleanup_updated",
            _ => "runtime_operation_updated"
        };

        return string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
            ? $"{prefix}_failed"
            : prefix;
    }

    private static string ToCatalogSeverity(string payloadState, string integrityStatus)
    {
        if (string.Equals(payloadState, "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(integrityStatus, "invalid", StringComparison.OrdinalIgnoreCase))
        {
            return "danger";
        }

        return string.Equals(integrityStatus, "warning", StringComparison.OrdinalIgnoreCase)
            ? "warning"
            : "success";
    }

    private static string ToCatalogTitleCode(
        string originKind,
        string payloadState,
        string integrityStatus)
    {
        if (string.Equals(payloadState, "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(integrityStatus, "invalid", StringComparison.OrdinalIgnoreCase))
        {
            return "backup_needs_attention";
        }

        if (string.Equals(integrityStatus, "warning", StringComparison.OrdinalIgnoreCase))
        {
            return "backup_catalog_warning";
        }

        return string.Equals(originKind, "imported-zip", StringComparison.OrdinalIgnoreCase)
            ? "backup_imported"
            : "backup_captured";
    }

    private static string ToRestoreSeverity(string status, int warningCount, int errorCount)
    {
        if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return "success";
        }

        if (errorCount > 0 || string.Equals(status, "needs-attention", StringComparison.OrdinalIgnoreCase))
        {
            return "danger";
        }

        return warningCount > 0
            ? "warning"
            : "info";
    }

    private static string ToRestoreTitleCode(string status, int warningCount, int errorCount)
    {
        if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return "restore_completed";
        }

        if (errorCount > 0 || string.Equals(status, "needs-attention", StringComparison.OrdinalIgnoreCase))
        {
            return "restore_needs_attention";
        }

        return warningCount > 0
            ? "restore_warning"
            : "restore_updated";
    }

    private static DateTimeOffset ToUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record RuntimeOperationActivityRow(
        Guid Id,
        Guid? RuntimeStackId,
        string Operation,
        string Status,
        DateTime OccurredAtUtc);

    private sealed record BackupCatalogActivityRow(
        Guid Id,
        string? SourceStackSlug,
        string OriginKind,
        string PayloadState,
        string IntegrityStatus,
        DateTime OccurredAtUtc);

    private sealed record RestoreAttemptActivityRow(
        Guid Id,
        string RestoreSessionId,
        string? SourceStackSlug,
        string Status,
        int WarningCount,
        int ErrorCount,
        DateTime OccurredAtUtc);

    private sealed record CertificateActivityRow(
        Guid Id,
        string Status,
        bool IsStaging,
        DateTime OccurredAtUtc);
}
