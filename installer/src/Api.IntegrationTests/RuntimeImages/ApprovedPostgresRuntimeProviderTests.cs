using Microsoft.Extensions.Options;
using Modules.Shared.RuntimeImages;

namespace Api.IntegrationTests.RuntimeImages;

public sealed class ApprovedPostgresRuntimeProviderTests
{
    private const string ApprovedReference =
        "docker.io/library/postgres:16@sha256:33f923b05f64ca54ac4401c01126a6b92afe839a0aa0a52bc5aeb5cc958e5f20";
    private const string LocalImageId =
        "sha256:da8cf245a60506e50a0a8cbb0f39c559ca622d92490605b67fcadc74ca1ea8e4";

    [Fact]
    public async Task Operational_resolution_returns_verified_local_immutable_identity_without_pull()
    {
        var inspector = new FakeInspector(new RuntimeImageInspection(
            LocalImageId,
            [ApprovedReference],
            ["PG_MAJOR=16", "PG_VERSION=16.14"]));
        var provider = CreateProvider(inspector);

        var result = await provider.ResolveForOperationAsync(CancellationToken.None);

        Assert.Equal(ApprovedReference, result.ApprovedReference);
        Assert.Equal(LocalImageId, result.ResolvedImageId);
        Assert.Equal(16, result.PostgresMajorVersion);
        Assert.Equal("16.14", result.PostgresVersion);
        Assert.Equal(0, inspector.PullCount);
    }

    [Fact]
    public async Task Operational_resolution_accepts_packaging_revision_for_the_exact_approved_upstream_release()
    {
        const string packagedVersion = "16.14-1.pgdg13+1";
        var inspector = new FakeInspector(new RuntimeImageInspection(
            LocalImageId,
            [ApprovedReference],
            ["PG_MAJOR=16", $"PG_VERSION={packagedVersion}"]));
        var provider = CreateProvider(inspector);

        var result = await provider.ResolveForOperationAsync(CancellationToken.None);

        Assert.Equal(packagedVersion, result.PostgresVersion);
        Assert.Equal(0, inspector.PullCount);
    }

    [Fact]
    public async Task Installation_may_pull_the_exact_approved_digest_once_then_resolve_it()
    {
        var inspection = new RuntimeImageInspection(
            LocalImageId,
            [ApprovedReference],
            ["PG_MAJOR=16", "PG_VERSION=16.14"]);
        var inspector = new FakeInspector(null, inspection);
        var provider = CreateProvider(inspector);

        var result = await provider.ResolveForInstallationAsync(CancellationToken.None);

        Assert.Equal(LocalImageId, result.ResolvedImageId);
        Assert.Equal(1, inspector.PullCount);
        Assert.Equal(ApprovedReference, inspector.LastPulledReference);
    }

    [Fact]
    public async Task Operational_resolution_fails_closed_when_approved_image_is_missing()
    {
        var inspector = new FakeInspector(null);
        var provider = CreateProvider(inspector);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ResolveForOperationAsync(CancellationToken.None));

        Assert.Contains("not available locally", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, inspector.PullCount);
    }

    [Theory]
    [InlineData("postgres:16")]
    [InlineData("sha256:da8cf245a60506e50a0a8cbb0f39c559ca622d92490605b67fcadc74ca1ea8e4")]
    [InlineData("postgres@sha256:bad")]
    public void Policy_rejects_mutable_or_non_repository_references(string reference)
    {
        var provider = CreateProvider(new FakeInspector(null), reference);

        Assert.Throws<InvalidOperationException>(() => provider.GetPolicy());
    }

    [Fact]
    public async Task Resolution_rejects_wrong_postgres_patch_release()
    {
        var inspector = new FakeInspector(new RuntimeImageInspection(
            LocalImageId,
            [ApprovedReference],
            ["PG_MAJOR=16", "PG_VERSION=16.13-1.pgdg13+1"]));
        var provider = CreateProvider(inspector);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ResolveForOperationAsync(CancellationToken.None));

        Assert.Contains("approves upstream PostgreSQL 16.14", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("16.140")]
    [InlineData("16.14foo")]
    [InlineData("16.14-")]
    [InlineData("16.14 package")]
    public async Task Resolution_rejects_malformed_or_non_package_qualified_versions(string reportedVersion)
    {
        var inspector = new FakeInspector(new RuntimeImageInspection(
            LocalImageId,
            [ApprovedReference],
            ["PG_MAJOR=16", $"PG_VERSION={reportedVersion}"]));
        var provider = CreateProvider(inspector);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ResolveForOperationAsync(CancellationToken.None));
    }

    private static ApprovedPostgresRuntimeProvider CreateProvider(
        IRuntimeImageInspector inspector,
        string approvedReference = ApprovedReference)
    {
        return new ApprovedPostgresRuntimeProvider(
            Options.Create(new PostgresRuntimeImageOptions
            {
                ApprovedReference = approvedReference,
                RequiredMajorVersion = 16,
                ExpectedVersion = "16.14",
                AllowInstallPull = true,
                AllowOperationalPull = false,
            }),
            inspector,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 17, 4, 0, 0, TimeSpan.Zero)));
    }

    private sealed class FakeInspector(params RuntimeImageInspection?[] inspections)
        : IRuntimeImageInspector
    {
        private readonly Queue<RuntimeImageInspection?> _inspections = new(inspections ?? []);
        public int PullCount { get; private set; }
        public string? LastPulledReference { get; private set; }

        public Task<RuntimeImageInspection?> InspectAsync(
            string immutableReference,
            CancellationToken cancellationToken)
        {
            var result = _inspections.Count == 0 ? null : _inspections.Dequeue();
            return Task.FromResult(result);
        }

        public Task PullAsync(string immutableReference, CancellationToken cancellationToken)
        {
            PullCount++;
            LastPulledReference = immutableReference;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
