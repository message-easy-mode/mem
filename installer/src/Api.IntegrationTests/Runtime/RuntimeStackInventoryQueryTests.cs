using HostAgent.Endpoints;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Stacks.Inventory;
using HostAgent.Runtime.Stacks.Identity;

namespace Api.IntegrationTests.Runtime;

public sealed class RuntimeStackInventoryQueryTests
{
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-08-25T00:00:00Z");

    [Fact]
    public void Unpaged_request_preserves_full_inventory_and_conservative_summary()
    {
        var stacks = new[]
        {
            Stack("verified-stack", "Public Routes Verified", "matrix.verified.test", "chat.verified.test", 4),
            Stack("starting-stack", "Started", "matrix.starting.test", "chat.starting.test", 3),
            Stack("failed-stack", "Failed", "matrix.failed.test", "chat.failed.test", 2),
            Stack("offline-stack", "Offline", "matrix.offline.test", "chat.offline.test", 1),
            Stack("mystery-stack", "Observed", "matrix.mystery.test", "chat.mystery.test", 0),
        };

        var result = ApplyInventory(
            stacks,
            new RuntimeStackInventoryRequest(null, null, null, null, null, null));

        Assert.False(result.IsPaged);
        Assert.Equal(5, result.Stacks.Count);
        Assert.Equal(
            new[] { "failed-stack", "mystery-stack", "offline-stack", "starting-stack", "verified-stack" },
            result.Stacks.Select(stack => stack.Slug));
        Assert.Equal(5, result.Summary.TotalStacks);
        Assert.Equal(1, result.Summary.Healthy);
        Assert.Equal(1, result.Summary.NeedsAttention);
        Assert.Equal(1, result.Summary.Offline);
        Assert.Equal(1, result.Summary.SettingUp);
        Assert.Equal(1, result.Summary.Unknown);
    }

    [Fact]
    public void Search_matches_operator_visible_identity_category_and_public_hosts()
    {
        var hiddenId = Guid.Parse("7085b97d-3d30-434e-976a-62df0178be16");
        var stacks = new[]
        {
            Stack(
                "family-chat",
                "Ready",
                "matrix.family.example",
                "chat.family.example",
                2,
                hiddenId,
                displayName: "Dewar Family Chat",
                category: "Family"),
            Stack(
                "school-room",
                "Ready",
                "matrix.northside.school",
                "element.northside.school",
                1,
                displayName: "Northside School Chat",
                category: "Education"),
        };

        var hostMatch = ApplyInventory(
            stacks,
            new RuntimeStackInventoryRequest(null, null, "northside.school", null, null, null));
        Assert.Equal("school-room", Assert.Single(hostMatch.Stacks).Slug);

        var displayNameMatch = ApplyInventory(
            stacks,
            new RuntimeStackInventoryRequest(null, null, "Dewar", null, null, null));
        Assert.Equal("family-chat", Assert.Single(displayNameMatch.Stacks).Slug);

        var categoryMatch = ApplyInventory(
            stacks,
            new RuntimeStackInventoryRequest(null, null, "Education", null, null, null));
        Assert.Equal("school-room", Assert.Single(categoryMatch.Stacks).Slug);

        var slugMatch = ApplyInventory(
            stacks,
            new RuntimeStackInventoryRequest(null, null, "family-chat", null, null, null));
        Assert.Equal("family-chat", Assert.Single(slugMatch.Stacks).Slug);

        var internalIdMatch = ApplyInventory(
            stacks,
            new RuntimeStackInventoryRequest(null, null, hiddenId.ToString("D"), null, null, null));
        Assert.Empty(internalIdMatch.Stacks);
    }

    [Fact]
    public void Name_sort_uses_operator_display_name_then_slug()
    {
        var stacks = new[]
        {
            Stack("z-technical", "Ready", "matrix.z.test", "chat.z.test", 1, displayName: "Alpha Chat"),
            Stack("a-technical", "Ready", "matrix.a.test", "chat.a.test", 1, displayName: "Zulu Chat"),
        };

        var result = ApplyInventory(
            stacks,
            new RuntimeStackInventoryRequest(null, null, null, null, "name", "asc"));

        Assert.Equal(new[] { "z-technical", "a-technical" }, result.Stacks.Select(stack => stack.Slug));
    }

    [Fact]
    public void Health_filter_sort_and_paging_keep_inventory_wide_summary()
    {
        var stacks = Enumerable.Range(1, 12)
            .Select(index => Stack(
                $"healthy-{index:00}",
                "Public Routes Verified",
                $"matrix-{index:00}.example.test",
                $"chat-{index:00}.example.test",
                index))
            .Concat(new[]
            {
                Stack("failed-stack", "Degraded", "matrix.failed.test", "chat.failed.test", 50),
                Stack("offline-stack", "Unreachable", "matrix.offline.test", "chat.offline.test", 49),
            })
            .ToArray();

        var result = ApplyInventory(
            stacks,
            new RuntimeStackInventoryRequest(
                Page: 2,
                PageSize: 10,
                Search: null,
                Status: "healthy",
                SortBy: "name",
                SortDirection: "desc"));

        Assert.True(result.IsPaged);
        Assert.Equal(14, result.Summary.TotalStacks);
        Assert.Equal(12, result.Summary.Healthy);
        Assert.Equal(1, result.Summary.NeedsAttention);
        Assert.Equal(1, result.Summary.Offline);
        Assert.Equal(12, result.TotalMatchingStacks);
        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(2, result.TotalPages);
        Assert.True(result.HasPreviousPage);
        Assert.False(result.HasNextPage);
        Assert.Equal(new[] { "healthy-02", "healthy-01" }, result.Stacks.Select(stack => stack.Slug));
    }


    [Fact]
    public void Category_filter_is_case_insensitive_and_facets_remain_inventory_wide()
    {
        var stacks = new[]
        {
            Stack("family-one", "Ready", "matrix.family-one.test", "chat.family-one.test", 1, displayName: "Family One", category: "Family"),
            Stack("family-two", "Ready", "matrix.family-two.test", "chat.family-two.test", 2, displayName: "Family Two", category: "family"),
            Stack("school", "Ready", "matrix.school.test", "chat.school.test", 3, displayName: "School Chat", category: "School"),
            Stack("uncategorized", "Ready", "matrix.none.test", "chat.none.test", 4, displayName: "No Category"),
        };

        var result = ApplyInventory(
            stacks,
            new RuntimeStackInventoryRequest(
                Page: 1,
                PageSize: 10,
                Search: null,
                Status: null,
                SortBy: "name",
                SortDirection: "asc",
                Category: "FAMILY"));

        Assert.Equal(new[] { "family-one", "family-two" }, result.Stacks.Select(stack => stack.Slug));
        Assert.Equal(2, result.TotalMatchingStacks);
        Assert.Equal(4, result.Summary.TotalStacks);
        Assert.Collection(
            result.Categories,
            family =>
            {
                Assert.Equal("Family", family.Category);
                Assert.Equal(2, family.Count);
            },
            school =>
            {
                Assert.Equal("School", school.Category);
                Assert.Equal(1, school.Count);
            });
    }

    [Fact]
    public void Last_checked_sort_is_deterministic()
    {
        var stacks = new[]
        {
            Stack("older", "Ready", "matrix.older.test", "chat.older.test", 1),
            Stack("newer", "Ready", "matrix.newer.test", "chat.newer.test", 3),
            Stack("middle", "Ready", "matrix.middle.test", "chat.middle.test", 2),
        };

        var result = ApplyInventory(
            stacks,
            new RuntimeStackInventoryRequest(null, null, null, null, "lastChecked", "desc"));

        Assert.Equal(new[] { "newer", "middle", "older" }, result.Stacks.Select(stack => stack.Slug));
    }

    [Fact]
    public void Stale_successful_verification_requires_attention_until_refreshed()
    {
        var current = Stack(
            "current",
            "Ready",
            "matrix.current.test",
            "chat.current.test",
            1,
            lastVerifiedAtUtc: ObservedAtUtc
                .Subtract(RuntimeStackVerificationFreshness.MaximumAge)
                .AddMinutes(1));
        var stale = Stack(
            "stale",
            "Public Routes Verified",
            "matrix.stale.test",
            "chat.stale.test",
            2,
            lastVerifiedAtUtc: ObservedAtUtc.Subtract(RuntimeStackVerificationFreshness.MaximumAge).AddMinutes(-1));
        var failed = Stack(
            "failed",
            "Failed",
            "matrix.failed.test",
            "chat.failed.test",
            3,
            lastVerifiedAtUtc: ObservedAtUtc.AddDays(-20));

        var result = ApplyInventory(
            [current, stale, failed],
            new RuntimeStackInventoryRequest(null, null, null, null, null, null));

        Assert.Equal(3, result.Summary.TotalStacks);
        Assert.Equal(1, result.Summary.Healthy);
        Assert.Equal(2, result.Summary.NeedsAttention);
        Assert.Equal(
            RuntimeStackHealthKinds.NeedsAttention,
            RuntimeStackInventoryQuery.ClassifyOperationalHealth(
                stale.LastVerifiedStatus,
                stale.LastVerifiedAtUtc,
                ObservedAtUtc));
        Assert.Equal(
            RuntimeStackVerificationFreshness.Stale,
            RuntimeStackVerificationFreshness.Classify(stale.LastVerifiedAtUtc, ObservedAtUtc));

        var projected = RuntimeStackSummaryResponse.FromManifest(stale, ObservedAtUtc);
        Assert.Equal(RuntimeStackHealthKinds.NeedsAttention, projected.Health);
        Assert.Equal(RuntimeStackVerificationFreshness.Stale, projected.VerificationFreshness);

        var inspected = RuntimeStackInspectResponse.FromManifest(stale, ObservedAtUtc);
        Assert.Equal(RuntimeStackHealthKinds.NeedsAttention, inspected.Health);
        Assert.Equal(RuntimeStackVerificationFreshness.Stale, inspected.VerificationFreshness);

        var attention = ApplyInventory(
            [current, stale, failed],
            new RuntimeStackInventoryRequest(
                Page: null,
                PageSize: null,
                Search: null,
                Status: RuntimeStackHealthKinds.NeedsAttention,
                SortBy: "name",
                SortDirection: "asc"));

        Assert.Equal(new[] { "failed", "stale" }, attention.Stacks.Select(stack => stack.Slug));
    }

    [Fact]
    public void Unsupported_inventory_values_are_rejected()
    {
        var stacks = new[] { Stack("demo", "Ready", "matrix.demo.test", "chat.demo.test", 1) };

        Assert.Throws<InvalidOperationException>(() => ApplyInventory(
            stacks,
            new RuntimeStackInventoryRequest(1, 11, null, null, null, null)));
        Assert.Throws<InvalidOperationException>(() => ApplyInventory(
            stacks,
            new RuntimeStackInventoryRequest(null, null, null, "broken", null, null)));
        Assert.Throws<InvalidOperationException>(() => ApplyInventory(
            stacks,
            new RuntimeStackInventoryRequest(null, null, null, null, "id", null)));
        Assert.Throws<InvalidOperationException>(() => ApplyInventory(
            stacks,
            new RuntimeStackInventoryRequest(
                Page: null,
                PageSize: null,
                Search: null,
                Status: null,
                SortBy: null,
                SortDirection: null,
                Category: new string('x', RuntimeStackIdentity.MaximumCategoryLength + 1))));
    }

    private static RuntimeStackInventoryResult ApplyInventory(
        IReadOnlyList<RuntimeStackManifest> stacks,
        RuntimeStackInventoryRequest request) =>
        RuntimeStackInventoryQuery.Apply(stacks, request, ObservedAtUtc);

    private static RuntimeStackManifest Stack(
        string slug,
        string status,
        string matrixHost,
        string elementHost,
        int minutes,
        Guid? stackId = null,
        string? displayName = null,
        string? category = null,
        DateTimeOffset? lastVerifiedAtUtc = null) =>
        new(
            Source: "control-plane",
            StackId: stackId ?? Guid.NewGuid(),
            Slug: slug,
            LastVerifiedStatus: status,
            LastVerifiedAtUtc: lastVerifiedAtUtc ?? DateTimeOffset.Parse("2026-08-24T00:00:00Z").AddMinutes(minutes),
            Matrix: Service("matrix", matrixHost),
            Element: Service("element", elementHost),
            Warnings: [],
            Metadata: RuntimeStackIdentity.WithIdentityMetadata(
                new Dictionary<string, string?>(),
                slug,
                displayName,
                category));

    private static RuntimeStackServiceManifest Service(string serviceKey, string host) =>
        new(
            InstanceId: Guid.NewGuid(),
            ServiceKey: serviceKey,
            ContainerId: null,
            ContainerName: null,
            HostPort: 0,
            DataPath: null,
            ServerName: host,
            PublicHost: host,
            PublicBaseUrl: $"https://{host}",
            InternalHost: null,
            InternalBaseUrl: null,
            PublicRouteId: null,
            InternalRouteId: null,
            NpmCertificateId: null,
            RuntimeMetadata: new Dictionary<string, string?>());
}
