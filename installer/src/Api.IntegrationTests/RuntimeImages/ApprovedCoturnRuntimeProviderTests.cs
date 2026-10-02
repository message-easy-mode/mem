using Microsoft.Extensions.Options;
using Modules.Shared.RuntimeImages;

namespace Api.IntegrationTests.RuntimeImages;

public sealed class ApprovedCoturnRuntimeProviderTests
{
    private const string ApprovedReference =
        "coturn/coturn@sha256:0c0e8fc0c263b85a134e9e4242b5e46e1f4c077c5029633511191c05b5c2c814";
    private const string LocalImageId =
        "sha256:4cecab6dc16fc24ceff2742648282dbfaf02a1549b0d9de2d7f3cfa2a4a42924";

    [Fact]
    public async Task Operational_resolution_returns_local_immutable_identity_without_pull()
    {
        var inspector = new FakeInspector(new RuntimeImageInspection(
            LocalImageId,
            [ApprovedReference],
            []));
        var provider = CreateProvider(inspector);

        var result = await provider.ResolveForOperationAsync(CancellationToken.None);

        Assert.Equal(ApprovedReference, result.ApprovedReference);
        Assert.Equal(LocalImageId, result.ResolvedImageId);
        Assert.Equal("4.14.0", result.ExpectedVersion);
        Assert.Equal(0, inspector.PullCount);
    }

    [Fact]
    public async Task Installation_resolution_may_pull_exact_approved_digest()
    {
        var inspection = new RuntimeImageInspection(
            LocalImageId,
            [ApprovedReference],
            []);
        var inspector = new FakeInspector(null, inspection);
        var provider = CreateProvider(inspector);

        var result = await provider.ResolveForInstallationAsync(CancellationToken.None);

        Assert.Equal(LocalImageId, result.ResolvedImageId);
        Assert.Equal(1, inspector.PullCount);
        Assert.Equal(ApprovedReference, inspector.LastPulledReference);
    }

    [Fact]
    public async Task Operational_resolution_fails_closed_when_image_is_missing_without_pull()
    {
        var inspector = new FakeInspector(null);
        var provider = CreateProvider(inspector);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ResolveForOperationAsync(CancellationToken.None));

        Assert.Contains("not available locally", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, inspector.PullCount);
    }

    [Theory]
    [InlineData("coturn/coturn:latest")]
    [InlineData("coturn/coturn:4.14.0-r0-debian")]
    [InlineData("coturn/coturn@sha256:bad")]
    [InlineData("sha256:4cecab6dc16fc24ceff2742648282dbfaf02a1549b0d9de2d7f3cfa2a4a42924")]
    public void Policy_rejects_mutable_or_non_repository_references(string reference)
    {
        var provider = CreateProvider(new FakeInspector(null), reference);

        Assert.Throws<InvalidOperationException>(() => provider.GetPolicy());
    }

    [Fact]
    public void Policy_rejects_operational_pull_enablement()
    {
        var provider = CreateProvider(
            new FakeInspector(null),
            allowOperationalPull: true);

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetPolicy());

        Assert.Contains("must remain false", exception.Message, StringComparison.Ordinal);
    }

    private static ApprovedCoturnRuntimeProvider CreateProvider(
        IRuntimeImageInspector inspector,
        string approvedReference = ApprovedReference,
        bool allowOperationalPull = false)
    {
        return new ApprovedCoturnRuntimeProvider(
            Options.Create(new CoturnRuntimeImageOptions
            {
                ApprovedReference = approvedReference,
                ExpectedVersion = "4.14.0",
                AllowInstallPull = true,
                AllowOperationalPull = allowOperationalPull
            }),
            inspector,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 7, 26, 7, 0, 0, TimeSpan.Zero)));
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

        public Task PullAsync(
            string immutableReference,
            CancellationToken cancellationToken)
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
