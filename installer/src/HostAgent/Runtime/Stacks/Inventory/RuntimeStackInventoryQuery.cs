using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Stacks.Identity;

namespace HostAgent.Runtime.Stacks.Inventory;

public static class RuntimeStackHealthKinds
{
    public const string Healthy = "healthy";
    public const string NeedsAttention = "needs_attention";
    public const string Offline = "offline";
    public const string SettingUp = "setting_up";
    public const string Unknown = "unknown";
}

public sealed record RuntimeStackInventoryRequest(
    int? Page,
    int? PageSize,
    string? Search,
    string? Status,
    string? SortBy,
    string? SortDirection,
    string? Category = null);

public sealed record RuntimeStackInventoryCategoryFacet(
    string Category,
    int Count);

public sealed record RuntimeStackInventorySummary(
    int TotalStacks,
    int Healthy,
    int NeedsAttention,
    int Offline,
    int SettingUp,
    int Unknown);

public sealed record RuntimeStackInventoryResult(
    IReadOnlyList<RuntimeStackManifest> Stacks,
    RuntimeStackInventorySummary Summary,
    int TotalMatchingStacks,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    bool IsPaged,
    IReadOnlyList<RuntimeStackInventoryCategoryFacet> Categories);

/// <summary>
/// Applies operator-facing search, health/category filtering, deterministic sorting,
/// and optional paging to the authoritative runtime-stack manifest inventory.
/// </summary>
public static class RuntimeStackInventoryQuery
{
    private const int DefaultPageSize = 10;
    private const int MaximumPage = 100_000;
    private const int MaximumSearchLength = 200;
    private static readonly int[] AllowedPageSizes = [10, 25, 50];

    public static RuntimeStackInventoryResult Apply(
        IReadOnlyList<RuntimeStackManifest> manifests,
        RuntimeStackInventoryRequest request) =>
        Apply(manifests, request, DateTimeOffset.UtcNow);

    public static RuntimeStackInventoryResult Apply(
        IReadOnlyList<RuntimeStackManifest> manifests,
        RuntimeStackInventoryRequest request,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        ArgumentNullException.ThrowIfNull(request);

        var search = NormalizeSearch(request.Search);
        var status = NormalizeStatus(request.Status);
        var category = NormalizeCategory(request.Category);
        var sortBy = NormalizeSortBy(request.SortBy);
        var sortDirection = NormalizeSortDirection(request.SortDirection);
        var isPaged = request.Page.HasValue || request.PageSize.HasValue;
        var page = NormalizePage(request.Page);
        var pageSize = NormalizePageSize(request.PageSize);

        var health = manifests
            .GroupBy(stack => ClassifyOperationalHealth(
                stack.LastVerifiedStatus,
                stack.LastVerifiedAtUtc,
                observedAtUtc))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var summary = new RuntimeStackInventorySummary(
            TotalStacks: manifests.Count,
            Healthy: health.GetValueOrDefault(RuntimeStackHealthKinds.Healthy),
            NeedsAttention: health.GetValueOrDefault(RuntimeStackHealthKinds.NeedsAttention),
            Offline: health.GetValueOrDefault(RuntimeStackHealthKinds.Offline),
            SettingUp: health.GetValueOrDefault(RuntimeStackHealthKinds.SettingUp),
            Unknown: health.GetValueOrDefault(RuntimeStackHealthKinds.Unknown));
        var categories = BuildCategoryFacets(manifests);

        var ordered = ApplySort(
                manifests
                    .Where(stack => MatchesSearch(stack, search))
                    .Where(stack => MatchesStatus(stack, status, observedAtUtc))
                    .Where(stack => MatchesCategory(stack, category)),
                sortBy,
                sortDirection)
            .ToArray();
        var totalMatchingStacks = ordered.Length;

        // Existing internal callers expect GET /runtime-stacks with no query
        // parameters to return the complete inventory. Preserve that contract.
        if (!isPaged)
        {
            return new RuntimeStackInventoryResult(
                Stacks: ordered,
                Summary: summary,
                TotalMatchingStacks: totalMatchingStacks,
                Page: 1,
                PageSize: Math.Max(totalMatchingStacks, 1),
                TotalPages: 1,
                HasPreviousPage: false,
                HasNextPage: false,
                IsPaged: false,
                Categories: categories);
        }

        var totalPages = Math.Max(1, (int)Math.Ceiling(totalMatchingStacks / (double)pageSize));
        var effectivePage = Math.Min(page, totalPages);
        var skipped = (effectivePage - 1) * pageSize;

        return new RuntimeStackInventoryResult(
            Stacks: ordered.Skip(skipped).Take(pageSize).ToArray(),
            Summary: summary,
            TotalMatchingStacks: totalMatchingStacks,
            Page: effectivePage,
            PageSize: pageSize,
            TotalPages: totalPages,
            HasPreviousPage: effectivePage > 1,
            HasNextPage: effectivePage < totalPages,
            IsPaged: true,
            Categories: categories);
    }

    public static string ClassifyOperationalHealth(
        string? status,
        DateTimeOffset lastVerifiedAtUtc,
        DateTimeOffset observedAtUtc)
    {
        var recordedHealth = ClassifyHealth(status);
        if (!RuntimeStackVerificationFreshness.IsStale(lastVerifiedAtUtc, observedAtUtc))
        {
            return recordedHealth;
        }

        return recordedHealth is RuntimeStackHealthKinds.Healthy or RuntimeStackHealthKinds.SettingUp
            ? RuntimeStackHealthKinds.NeedsAttention
            : recordedHealth;
    }

    public static string ClassifyHealth(string? status)
    {
        var normalized = NormalizeRecordedStatus(status);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return RuntimeStackHealthKinds.Unknown;
        }

        if (normalized.Contains("offline", StringComparison.Ordinal) ||
            normalized.Contains("unreachable", StringComparison.Ordinal))
        {
            return RuntimeStackHealthKinds.Offline;
        }

        if (normalized.Contains("failed", StringComparison.Ordinal) ||
            normalized.Contains("error", StringComparison.Ordinal) ||
            normalized.Contains("degraded", StringComparison.Ordinal))
        {
            return RuntimeStackHealthKinds.NeedsAttention;
        }

        if (normalized is "created" or "started" or "running" or "in progress" ||
            normalized.Contains("starting", StringComparison.Ordinal))
        {
            return RuntimeStackHealthKinds.SettingUp;
        }

        if (normalized is "ready" or "passed" or "completed" or "verified" ||
            normalized.Contains("verified", StringComparison.Ordinal))
        {
            return RuntimeStackHealthKinds.Healthy;
        }

        return RuntimeStackHealthKinds.Unknown;
    }

    private static IEnumerable<RuntimeStackManifest> ApplySort(
        IEnumerable<RuntimeStackManifest> stacks,
        string sortBy,
        string sortDirection)
    {
        IOrderedEnumerable<RuntimeStackManifest> ordered = (sortBy, sortDirection) switch
        {
            ("lastchecked", "asc") => stacks.OrderBy(stack => stack.LastVerifiedAtUtc),
            ("lastchecked", "desc") => stacks.OrderByDescending(stack => stack.LastVerifiedAtUtc),
            ("name", "desc") => stacks.OrderByDescending(RuntimeStackIdentity.ResolveDisplayName, StringComparer.OrdinalIgnoreCase),
            _ => stacks.OrderBy(RuntimeStackIdentity.ResolveDisplayName, StringComparer.OrdinalIgnoreCase)
        };

        return ordered
            .ThenBy(stack => stack.Slug, StringComparer.OrdinalIgnoreCase)
            .ThenBy(stack => stack.StackId);
    }

    private static bool MatchesSearch(RuntimeStackManifest stack, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return Contains(RuntimeStackIdentity.ResolveDisplayName(stack), search) ||
            Contains(RuntimeStackIdentity.ResolveCategory(stack), search) ||
            Contains(stack.Slug, search) ||
            Contains(stack.Matrix.PublicHost, search) ||
            Contains(stack.Matrix.PublicBaseUrl, search) ||
            Contains(stack.Element?.PublicHost, search) ||
            Contains(stack.Element?.PublicBaseUrl, search);
    }

    private static bool MatchesStatus(
        RuntimeStackManifest stack,
        string? status,
        DateTimeOffset observedAtUtc) =>
        string.IsNullOrWhiteSpace(status) ||
        string.Equals(
            ClassifyOperationalHealth(
                stack.LastVerifiedStatus,
                stack.LastVerifiedAtUtc,
                observedAtUtc),
            status,
            StringComparison.Ordinal);

    private static bool MatchesCategory(RuntimeStackManifest stack, string? category) =>
        string.IsNullOrWhiteSpace(category) ||
        string.Equals(
            RuntimeStackIdentity.ResolveCategory(stack),
            category,
            StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<RuntimeStackInventoryCategoryFacet> BuildCategoryFacets(
        IReadOnlyList<RuntimeStackManifest> manifests) =>
        manifests
            .Select(RuntimeStackIdentity.ResolveCategory)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Select(category => category!)
            .GroupBy(category => category, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RuntimeStackInventoryCategoryFacet(
                Category: group.OrderBy(value => value, StringComparer.Ordinal).First(),
                Count: group.Count()))
            .OrderBy(facet => facet.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(facet => facet.Category, StringComparer.Ordinal)
            .ToArray();

    private static bool Contains(string? value, string search) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static int NormalizePage(int? value) =>
        value.GetValueOrDefault(1) <= 0
            ? 1
            : Math.Min(value.GetValueOrDefault(1), MaximumPage);

    private static int NormalizePageSize(int? value)
    {
        var pageSize = value.GetValueOrDefault(DefaultPageSize);
        if (!AllowedPageSizes.Contains(pageSize))
        {
            throw new InvalidOperationException(
                $"Runtime stack page size '{pageSize}' is not supported. Use 10, 25, or 50.");
        }

        return pageSize;
    }

    private static string? NormalizeSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var search = value.Trim();
        if (search.Length > MaximumSearchLength)
        {
            throw new InvalidOperationException(
                $"Runtime stack search exceeds the maximum length of {MaximumSearchLength} characters.");
        }

        return search;
    }

    private static string? NormalizeStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant().Replace('-', '_');

        return normalized switch
        {
            RuntimeStackHealthKinds.Healthy or
            RuntimeStackHealthKinds.NeedsAttention or
            RuntimeStackHealthKinds.Offline or
            RuntimeStackHealthKinds.SettingUp or
            RuntimeStackHealthKinds.Unknown => normalized,
            _ => throw new InvalidOperationException(
                $"Runtime stack status filter '{value}' is not supported.")
        };
    }

    private static string? NormalizeCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return RuntimeStackIdentity.NormalizeCategory(value);
    }

    private static string NormalizeSortBy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "name";
        }

        var normalized = value.Trim()
            .ToLowerInvariant()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);

        return normalized switch
        {
            "name" or "lastchecked" => normalized,
            _ => throw new InvalidOperationException(
                $"Runtime stack sort field '{value}' is not supported.")
        };
    }

    private static string NormalizeSortDirection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "asc";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "asc" => "asc",
            "desc" => "desc",
            _ => throw new InvalidOperationException(
                $"Runtime stack sort direction '{value}' is not supported.")
        };
    }

    private static string NormalizeRecordedStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            ' ',
            value.Trim()
                .ToLowerInvariant()
                .Replace('_', ' ')
                .Replace('-', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
