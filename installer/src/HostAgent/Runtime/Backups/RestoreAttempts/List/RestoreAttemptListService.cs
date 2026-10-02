using HostAgent.Runtime.Backups.Coordination;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostAgent.Runtime.Backups.RestoreAttempts.List;

/// <summary>
/// Canonical durable Restore Attempt inventory for the operator /restores
/// screen. This service reads RestoreAttempts directly; it never scans import
/// directories, validation receipts, or workflow-history JSON.
/// </summary>
public sealed class RestoreAttemptListService
{
    private const int DefaultPageSize = 10;
    private const int MaximumPageSize = 100;
    private const int MaximumPage = 100_000;
    private const int MaximumSearchLength = 200;
    private const int MaximumTargetStackLength = 150;

    private readonly MemDbContext _db;

    public RestoreAttemptListService(MemDbContext db)
    {
        _db = db;
    }

    public async Task<RestoreAttemptListResponse> ListAsync(
        RestoreAttemptListQuery request,
        CancellationToken ct)
    {
        var query = NormalizeQuery(request);

        // The complete filtered set is intentionally assembled inside the
        // HostAgent. Summary cards and target-stack choices must describe the
        // same result set as the table, rather than only the current page.
        // Restore attempt history is a compact control-plane ledger, while all
        // large evidence stays in the workspace/filesystem stores.
        var entities = await _db.RestoreAttempts
            .AsNoTracking()
            .Include(x => x.BackupCatalogEntry)
            .Include(x => x.TargetClaims)
            .ToListAsync(ct);

        var candidates = entities
            .Select(ToCandidate)
            .ToArray();

        var matchingAttempts = candidates
            .Where(item => MatchesStatus(item, query.Status))
            .Where(item => MatchesSearch(item, query.Search))
            .ToArray();

        // Derive choices before applying the selected target-stack filter so a
        // browser can change target safely without losing all alternative
        // values from the same search/lifecycle result set.
        var targetStacks = matchingAttempts
            .Select(item => item.TargetStackSlug)
            .Where(static targetStack => !string.IsNullOrWhiteSpace(targetStack))
            .Select(static targetStack => targetStack!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static targetStack => targetStack, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var filtered = matchingAttempts
            .Where(item => MatchesTargetStack(item, query.TargetStack))
            .ToArray();

        var summary = new RestoreAttemptListSummary(
            TotalSessions: filtered.Length,
            ProductionRecreateCount: filtered.Count(static item => item.ProductionRecreateStarted),
            PubliclyVerifiedCount: filtered.Count(static item => item.PubliclyVerified),
            NeedsActionCount: filtered.Count(RequiresAttention));

        var ordered = ApplySort(
                filtered,
                query.SortBy,
                query.SortDirection)
            .ToArray();

        var totalSessions = ordered.Length;
        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalSessions / (double)query.PageSize));
        var effectivePage = Math.Min(query.Page, totalPages);
        var skipped = (effectivePage - 1) * query.PageSize;

        var sessions = ordered
            .Skip(skipped)
            .Take(query.PageSize)
            .Select(ToListItem)
            .ToArray();

        var responseQuery = query with { Page = effectivePage };

        return new RestoreAttemptListResponse(
            Source: "control-plane",
            Status: "ok",
            Query: responseQuery,
            Summary: summary,
            TotalSessions: totalSessions,
            Page: effectivePage,
            PageSize: query.PageSize,
            TotalPages: totalPages,
            HasPreviousPage: effectivePage > 1,
            HasNextPage: effectivePage < totalPages,
            TargetStacks: targetStacks,
            Sessions: sessions,
            Warnings: Array.Empty<string>(),
            Detail: totalSessions == 0
                ? "No restore attempts match the current filters."
                : null);
    }

    private static RestoreAttemptListCandidate ToCandidate(
        RestoreAttemptEntity entity)
    {
        var survivingCatalog = entity.BackupCatalogEntry;
        var sourceDeleted = survivingCatalog is null;
        var targetStackSlug = entity.TargetClaims
            .Where(claim => string.Equals(
                claim.ResourceType,
                RestoreTargetResourceTypes.StackSlug,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(claim => claim.ClaimedAtUtc)
            .Select(claim => claim.ResourceValue)
            .FirstOrDefault();

        var productionRecreateStarted = !string.IsNullOrWhiteSpace(targetStackSlug) ||
            string.Equals(entity.Status, RestoreAttemptStatuses.Recreating, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entity.Status, RestoreAttemptStatuses.Verifying, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entity.CurrentStage, RestoreAttemptStages.CreateRestoredChatServer, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entity.CurrentStage, RestoreAttemptStages.PublicVerification, StringComparison.OrdinalIgnoreCase);

        var publiclyVerified = string.Equals(
            entity.Status,
            RestoreAttemptStatuses.Completed,
            StringComparison.OrdinalIgnoreCase);

        var sourceLabel = sourceDeleted
            ? "Deleted backup"
            : survivingCatalog!.DisplayName;

        return new RestoreAttemptListCandidate(
            RestoreSessionId: entity.RestoreSessionId,
            SourceKind: entity.SourceKind,
            SourceLabel: sourceLabel,
            SourceDeleted: sourceDeleted,
            CatalogEntryId: sourceDeleted
                ? null
                : survivingCatalog!.CatalogEntryId,
            SearchableCatalogEntryId: survivingCatalog?.CatalogEntryId ?? entity.SourceCatalogEntryIdSnapshot,
            SourceDisplayNameSnapshot: entity.SourceDisplayNameSnapshot,
            SourceStackSlug: entity.SourceStackSlugSnapshot,
            SourceBackupId: entity.SourceBackupIdSnapshot,
            TargetStackSlug: targetStackSlug,
            Status: entity.Status,
            CurrentStage: entity.CurrentStage,
            StartedAtUtc: ToOffset(entity.CreatedAtUtc),
            LastUpdatedAtUtc: ToOffset(entity.UpdatedAtUtc),
            TerminalAtUtc: ToOffset(entity.TerminalAtUtc),
            WarningCount: entity.WarningCount,
            ErrorCount: entity.ErrorCount,
            ProductionRecreateStarted: productionRecreateStarted,
            PubliclyVerified: publiclyVerified);
    }

    private static RestoreAttemptListItem ToListItem(
        RestoreAttemptListCandidate candidate)
    {
        var statusPresentation = PresentStatus(candidate.Status);
        var stagePresentation = PresentStage(candidate.CurrentStage);
        var nextAction = BuildNextAction(candidate);

        return new RestoreAttemptListItem(
            RestoreSessionId: candidate.RestoreSessionId,
            WorkspaceAvailable: true,
            SourceKind: candidate.SourceKind,
            SourceLabel: candidate.SourceLabel,
            SourceDeleted: candidate.SourceDeleted,
            CatalogEntryId: candidate.CatalogEntryId,
            SourceStackSlug: candidate.SourceStackSlug,
            SourceBackupId: candidate.SourceBackupId,
            TargetStackSlug: candidate.TargetStackSlug,
            Status: candidate.Status,
            StatusLabel: statusPresentation.Label,
            CurrentStage: candidate.CurrentStage,
            CurrentStageLabel: stagePresentation.Label,
            ProgressPercent: ResolveProgress(candidate),
            StartedAtUtc: candidate.StartedAtUtc,
            LastUpdatedAtUtc: candidate.LastUpdatedAtUtc,
            TerminalAtUtc: candidate.TerminalAtUtc,
            WarningCount: candidate.WarningCount,
            ErrorCount: candidate.ErrorCount,
            ProductionRecreateStarted: candidate.ProductionRecreateStarted,
            PubliclyVerified: candidate.PubliclyVerified,
            NextActionCode: nextAction.Code,
            NextActionTitle: nextAction.Title,
            Detail: nextAction.Detail);
    }

    private static RestoreAttemptListQuery NormalizeQuery(
        RestoreAttemptListQuery request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = request.Page <= 0 ? 1 : Math.Min(request.Page, MaximumPage);
        var pageSize = request.PageSize <= 0
            ? DefaultPageSize
            : Math.Min(request.PageSize, MaximumPageSize);

        var search = NormalizeOptional(request.Search, MaximumSearchLength);
        var status = NormalizeStatus(request.Status);
        var targetStack = NormalizeOptional(request.TargetStack, MaximumTargetStackLength);
        var sortBy = NormalizeSortBy(request.SortBy);
        var sortDirection = NormalizeSortDirection(request.SortDirection);

        return new RestoreAttemptListQuery(
            Page: page,
            PageSize: pageSize,
            Search: search,
            Status: status,
            TargetStack: targetStack,
            SortBy: sortBy,
            SortDirection: sortDirection);
    }

    private static string? NormalizeStatus(string? value)
    {
        var normalized = NormalizeOptional(value, 80)?.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalized) ||
            string.Equals(normalized, "all", StringComparison.Ordinal))
        {
            return null;
        }

        var allowed = new[]
        {
            "in-progress",
            "warnings",
            RestoreAttemptStatuses.Creating,
            RestoreAttemptStatuses.Ready,
            RestoreAttemptStatuses.Testing,
            RestoreAttemptStatuses.Planning,
            RestoreAttemptStatuses.Recreating,
            RestoreAttemptStatuses.Verifying,
            RestoreAttemptStatuses.NeedsAttention,
            RestoreAttemptStatuses.Completed,
            RestoreAttemptStatuses.Cancelled,
            RestoreAttemptStatuses.Abandoned,
            RestoreAttemptStatuses.Superseded,
            "failed"
        };

        if (!allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Restore lifecycle status '{value}' is not supported.");
        }

        return normalized;
    }

    private static string NormalizeSortBy(string? value)
    {
        var normalized = NormalizeOptional(value, 80)?.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "updated";
        }

        return normalized switch
        {
            "updated" or "started" or "source" or "target" or "status" or "session" => normalized,
            _ => throw new InvalidOperationException(
                $"Restore sort field '{value}' is not supported.")
        };
    }

    private static string NormalizeSortDirection(string? value)
    {
        var normalized = NormalizeOptional(value, 16)?.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "desc";
        }

        return normalized switch
        {
            "asc" or "desc" => normalized,
            _ => throw new InvalidOperationException(
                $"Restore sort direction '{value}' is not supported.")
        };
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new InvalidOperationException(
                $"Restore list query value exceeds the maximum length of {maximumLength} characters.");
        }

        return normalized;
    }

    private static bool MatchesStatus(
        RestoreAttemptListCandidate candidate,
        string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        if (string.Equals(status, "in-progress", StringComparison.OrdinalIgnoreCase))
        {
            return !RestoreAttemptStatuses.IsTerminal(candidate.Status) &&
                   !string.Equals(candidate.Status, RestoreAttemptStatuses.NeedsAttention, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(status, "warnings", StringComparison.OrdinalIgnoreCase))
        {
            return candidate.WarningCount > 0;
        }

        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(candidate.Status, RestoreAttemptStatuses.NeedsAttention, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate.Status, RestoreAttemptStatuses.Abandoned, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(candidate.Status, status, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesSearch(
        RestoreAttemptListCandidate candidate,
        string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return Contains(candidate.RestoreSessionId, search) ||
               Contains(candidate.SourceDisplayNameSnapshot, search) ||
               Contains(candidate.SourceLabel, search) ||
               Contains(candidate.SearchableCatalogEntryId, search) ||
               Contains(candidate.TargetStackSlug, search);
    }

    private static bool MatchesTargetStack(
        RestoreAttemptListCandidate candidate,
        string? targetStack)
    {
        return string.IsNullOrWhiteSpace(targetStack) ||
               string.Equals(
                   candidate.TargetStackSlug,
                   targetStack,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(string? value, string search) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static IOrderedEnumerable<RestoreAttemptListCandidate> ApplySort(
        IEnumerable<RestoreAttemptListCandidate> attempts,
        string sortBy,
        string sortDirection)
    {
        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return sortBy switch
        {
            "started" => descending
                ? attempts.OrderByDescending(item => item.StartedAtUtc).ThenByDescending(item => item.RestoreSessionId, StringComparer.OrdinalIgnoreCase)
                : attempts.OrderBy(item => item.StartedAtUtc).ThenBy(item => item.RestoreSessionId, StringComparer.OrdinalIgnoreCase),
            "source" => descending
                ? attempts.OrderByDescending(item => item.SourceLabel, StringComparer.OrdinalIgnoreCase).ThenByDescending(item => item.RestoreSessionId, StringComparer.OrdinalIgnoreCase)
                : attempts.OrderBy(item => item.SourceLabel, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.RestoreSessionId, StringComparer.OrdinalIgnoreCase),
            "target" => descending
                ? attempts.OrderByDescending(item => item.TargetStackSlug ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenByDescending(item => item.RestoreSessionId, StringComparer.OrdinalIgnoreCase)
                : attempts.OrderBy(item => item.TargetStackSlug ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.RestoreSessionId, StringComparer.OrdinalIgnoreCase),
            "status" => descending
                ? attempts.OrderByDescending(item => item.Status, StringComparer.OrdinalIgnoreCase).ThenByDescending(item => item.RestoreSessionId, StringComparer.OrdinalIgnoreCase)
                : attempts.OrderBy(item => item.Status, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.RestoreSessionId, StringComparer.OrdinalIgnoreCase),
            "session" => descending
                ? attempts.OrderByDescending(item => item.RestoreSessionId, StringComparer.OrdinalIgnoreCase)
                : attempts.OrderBy(item => item.RestoreSessionId, StringComparer.OrdinalIgnoreCase),
            _ => descending
                ? attempts.OrderByDescending(item => item.LastUpdatedAtUtc).ThenByDescending(item => item.RestoreSessionId, StringComparer.OrdinalIgnoreCase)
                : attempts.OrderBy(item => item.LastUpdatedAtUtc).ThenBy(item => item.RestoreSessionId, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static bool RequiresAttention(RestoreAttemptListCandidate item) =>
        string.Equals(item.Status, RestoreAttemptStatuses.NeedsAttention, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(item.Status, RestoreAttemptStatuses.Abandoned, StringComparison.OrdinalIgnoreCase) ||
        item.ErrorCount > 0;

    private static (string Label, string Tone) PresentStatus(string status) =>
        status.ToLowerInvariant() switch
        {
            RestoreAttemptStatuses.Creating => ("Preparing backup", "info"),
            RestoreAttemptStatuses.Ready => ("Ready to continue", "neutral"),
            RestoreAttemptStatuses.Testing => ("Private test running", "info"),
            RestoreAttemptStatuses.Planning => ("Planning restore", "info"),
            RestoreAttemptStatuses.Recreating => ("Creating restored server", "info"),
            RestoreAttemptStatuses.Verifying => ("Verifying restored server", "info"),
            RestoreAttemptStatuses.NeedsAttention => ("Needs attention", "danger"),
            RestoreAttemptStatuses.Completed => ("Completed", "success"),
            RestoreAttemptStatuses.Cancelled => ("Cancelled", "muted"),
            RestoreAttemptStatuses.Abandoned => ("Failed", "danger"),
            RestoreAttemptStatuses.Superseded => ("Superseded", "muted"),
            _ => ("Unknown", "muted")
        };

    private static (string Label, string Tone) PresentStage(string stage) =>
        stage.ToLowerInvariant() switch
        {
            RestoreAttemptStages.Creating => ("Preparing restore", "info"),
            RestoreAttemptStages.PreparingSource => ("Preparing backup", "info"),
            RestoreAttemptStages.BackupReady => ("Backup ready", "neutral"),
            RestoreAttemptStages.PrivateTest => ("Private test", "info"),
            RestoreAttemptStages.CreateRestoredChatServer => ("Creating restored server", "info"),
            RestoreAttemptStages.PublicVerification => ("Public verification", "info"),
            RestoreAttemptStages.NeedsAttention => ("Needs attention", "danger"),
            RestoreAttemptStages.Cancelled => ("Cancelled", "muted"),
            _ => ("Restore in progress", "neutral")
        };

    private static int? ResolveProgress(RestoreAttemptListCandidate candidate)
    {
        if (string.Equals(candidate.Status, RestoreAttemptStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (RestoreAttemptStatuses.IsTerminal(candidate.Status) ||
            string.Equals(candidate.Status, RestoreAttemptStatuses.NeedsAttention, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return candidate.CurrentStage.ToLowerInvariant() switch
        {
            RestoreAttemptStages.Creating => 10,
            RestoreAttemptStages.PreparingSource => 15,
            RestoreAttemptStages.BackupReady => 20,
            RestoreAttemptStages.PrivateTest => 40,
            RestoreAttemptStages.CreateRestoredChatServer => 70,
            RestoreAttemptStages.PublicVerification => 90,
            _ => candidate.Status.ToLowerInvariant() switch
            {
                RestoreAttemptStatuses.Creating => 10,
                RestoreAttemptStatuses.Ready => 20,
                RestoreAttemptStatuses.Testing => 40,
                RestoreAttemptStatuses.Planning => 55,
                RestoreAttemptStatuses.Recreating => 70,
                RestoreAttemptStatuses.Verifying => 90,
                _ => null
            }
        };
    }

    private static (string Code, string Title, string Detail) BuildNextAction(
        RestoreAttemptListCandidate candidate)
    {
        if (candidate.PubliclyVerified)
        {
            return ("view-restore", "Open restore", "View the completed restore workspace, evidence, and support record.");
        }

        if (string.Equals(candidate.Status, RestoreAttemptStatuses.Cancelled, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Status, RestoreAttemptStatuses.Superseded, StringComparison.OrdinalIgnoreCase))
        {
            return ("view-restore", "Open restore", "View the terminal restore audit record.");
        }

        if (RequiresAttention(candidate))
        {
            return ("review-issue", "Review issue", "Open the restore workspace to review safe evidence and structured logs.");
        }

        if (string.Equals(candidate.Status, RestoreAttemptStatuses.Verifying, StringComparison.OrdinalIgnoreCase))
        {
            return ("continue-verification", "Open restore", "Review and complete restored-server verification.");
        }

        if (candidate.ProductionRecreateStarted)
        {
            return ("view-progress", "Open restore", "Review the active restore stage and operation state.");
        }

        return ("continue-restore", "Open restore", "Continue the catalog-backed restore workflow.");
    }

    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value.HasValue
            ? ToOffset(value.Value)
            : null;

    private sealed record RestoreAttemptListCandidate(
        string RestoreSessionId,
        string SourceKind,
        string SourceLabel,
        bool SourceDeleted,
        string? CatalogEntryId,
        string SearchableCatalogEntryId,
        string SourceDisplayNameSnapshot,
        string? SourceStackSlug,
        string? SourceBackupId,
        string? TargetStackSlug,
        string Status,
        string CurrentStage,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset LastUpdatedAtUtc,
        DateTimeOffset? TerminalAtUtc,
        int WarningCount,
        int ErrorCount,
        bool ProductionRecreateStarted,
        bool PubliclyVerified);
}
