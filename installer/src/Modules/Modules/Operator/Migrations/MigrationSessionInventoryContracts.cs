using Infrastructure.Data.Entities.Migrations;
using Modules.Operator.Migrations.Workspace;

namespace Modules.Operator.Migrations;

public sealed record MigrationSessionInventoryQuery(
    int Page,
    int PageSize,
    string? Search,
    string Lifecycle,
    string Action,
    string? Stage,
    string? TargetStack,
    string SortBy,
    string SortDirection,
    bool IncludeArchived)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 10;
    public const int MaximumPage = 1_000_000;
    public const int MaximumPageSize = 100;
    public const int MaximumSearchLength = 200;
    public const int MaximumTargetLength = 120;

    public static MigrationSessionInventoryQueryParseResult Parse(
        string? page,
        string? pageSize,
        string? search,
        string? lifecycle,
        string? action,
        string? stage,
        string? targetStack,
        string? sortBy,
        string? sortDirection,
        string? includeArchived)
    {
        var errors = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        var normalizedPage = ParsePositiveInt(
            page,
            DefaultPage,
            "page",
            errors);
        if (normalizedPage > MaximumPage)
        {
            errors["page"] =
                $"Page cannot exceed {MaximumPage}.";
        }

        var normalizedPageSize = ParsePositiveInt(
            pageSize,
            DefaultPageSize,
            "pageSize",
            errors);
        if (normalizedPageSize > MaximumPageSize)
        {
            errors["pageSize"] =
                $"Page size cannot exceed {MaximumPageSize}.";
        }

        var normalizedSearch = NormalizeOptional(search);
        if (normalizedSearch?.Length > MaximumSearchLength)
        {
            errors["search"] =
                $"Search cannot exceed {MaximumSearchLength} characters.";
        }

        var normalizedLifecycle = NormalizeOptional(lifecycle)?.ToLowerInvariant()
            ?? "all";
        var allowedLifecycles = new HashSet<string>(
            MigrationSessionLifecycleStatuses.All,
            StringComparer.Ordinal)
        {
            "all",
            "archived",
        };
        if (!allowedLifecycles.Contains(normalizedLifecycle))
        {
            errors["lifecycle"] =
                "Lifecycle must be all, active, completed, closed, cancelled, or archived.";
        }

        var normalizedAction = NormalizeOptional(action)?.ToLowerInvariant()
            ?? "all";
        if (normalizedAction is not "all" and not "continue" and not "review" and not "view")
        {
            errors["action"] =
                "Action must be all, continue, review, or view.";
        }

        var normalizedStage = NormalizeOptional(stage)?.ToLowerInvariant();
        if (normalizedStage is not null &&
            !MigrationWorkspaceStageCodes.Ordered.Contains(
                normalizedStage,
                StringComparer.Ordinal))
        {
            errors["stage"] =
                "Stage is not a recognised guided Migration stage.";
        }

        var normalizedTarget = NormalizeOptional(targetStack);
        if (normalizedTarget?.Length > MaximumTargetLength)
        {
            errors["targetStack"] =
                $"Target stack cannot exceed {MaximumTargetLength} characters.";
        }

        var normalizedSortBy =
            NormalizeOptional(sortBy)?.ToLowerInvariant() ?? "updated";
        if (normalizedSortBy is not "updated" and not "created" and not "name" and not "stage")
        {
            errors["sortBy"] =
                "Sort field must be updated, created, name, or stage.";
        }

        var normalizedDirection =
            NormalizeOptional(sortDirection)?.ToLowerInvariant() ?? "desc";
        if (normalizedDirection is not "asc" and not "desc")
        {
            errors["sortDirection"] =
                "Sort direction must be asc or desc.";
        }

        var normalizedIncludeArchived = false;
        if (!string.IsNullOrWhiteSpace(includeArchived) &&
            !bool.TryParse(includeArchived, out normalizedIncludeArchived))
        {
            errors["includeArchived"] =
                "Include archived must be true or false.";
        }

        if (normalizedLifecycle == "archived")
        {
            normalizedIncludeArchived = true;
        }

        return errors.Count > 0
            ? MigrationSessionInventoryQueryParseResult.Invalid(errors)
            : MigrationSessionInventoryQueryParseResult.Valid(
                new MigrationSessionInventoryQuery(
                    normalizedPage,
                    normalizedPageSize,
                    normalizedSearch,
                    normalizedLifecycle,
                    normalizedAction,
                    normalizedStage,
                    normalizedTarget,
                    normalizedSortBy,
                    normalizedDirection,
                    normalizedIncludeArchived));
    }

    private static int ParsePositiveInt(
        string? raw,
        int defaultValue,
        string field,
        IDictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        if (!int.TryParse(raw, out var value) || value < 1)
        {
            errors[field] = $"{field} must be a positive integer.";
            return defaultValue;
        }

        return value;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

public sealed record MigrationSessionInventoryQueryParseResult(
    MigrationSessionInventoryQuery? Query,
    IReadOnlyDictionary<string, string> Errors)
{
    public bool IsValid => Query is not null && Errors.Count == 0;

    public static MigrationSessionInventoryQueryParseResult Valid(
        MigrationSessionInventoryQuery query) =>
        new(query, new Dictionary<string, string>());

    public static MigrationSessionInventoryQueryParseResult Invalid(
        IReadOnlyDictionary<string, string> errors) =>
        new(null, errors);
}

public sealed record MigrationSessionInventoryResponse(
    int SchemaVersion,
    MigrationSessionInventoryQueryDto Query,
    MigrationSessionInventorySummaryDto Summary,
    int TotalSessions,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    IReadOnlyList<string> TargetStacks,
    IReadOnlyList<MigrationSessionInventoryRowDto> Sessions,
    IReadOnlyList<MigrationSessionInventoryWarningDto> Warnings);

public sealed record MigrationSessionInventoryQueryDto(
    string? Search,
    string Lifecycle,
    string Action,
    string? Stage,
    string? TargetStack,
    string SortBy,
    string SortDirection,
    bool IncludeArchived);

public sealed record MigrationSessionInventorySummaryDto(
    int TotalSessions,
    int ActiveCount,
    int NeedsActionCount,
    int CompletedCount,
    int ClosedCount,
    int CancelledCount,
    int ArchivedCount);

public sealed record MigrationSessionInventoryRowDto(
    string MigrationId,
    string DisplayName,
    string SourceAdapter,
    string SourceDisplay,
    string? SourceProduct,
    string? SourceVersion,
    string? TargetStackSlug,
    string LifecycleStatus,
    bool Archived,
    string CurrentStageCode,
    string CurrentStageState,
    string CurrentStatusCode,
    bool NeedsAttention,
    int WarningCount,
    int ErrorCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    long StateVersion,
    MigrationSessionInventoryPrimaryActionDto PrimaryAction,
    MigrationSessionInventoryCapabilitiesDto Capabilities);

public sealed record MigrationSessionInventoryPrimaryActionDto(
    string Kind,
    string Code);

public sealed record MigrationSessionInventoryCapabilitiesDto(
    bool CanOpen,
    bool CanArchive,
    bool CanUnarchive,
    bool CanCancel,
    bool CanDelete,
    string? DeleteBlockedReason);

public sealed record MigrationSessionInventoryWarningDto(
    string Code,
    string Message);

public sealed record MigrationSessionInventoryProblemResponse(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string> Errors);
