using Modules.Integrations.Seq.Services;
using Modules.Shared.RuntimeImages;

namespace Api.IntegrationTests.Diagnostics;

public sealed class SeqRuntimeImageResolverTests
{
    [Fact]
    public async Task Operational_resolution_uses_the_local_immutable_image_identity_without_pull()
    {
        var inspector = new RecordingInspector(new RuntimeImageInspection(
            $"sha256:{new string('a', 64)}",
            ["datalust/seq@sha256:" + new string('b', 64)],
            []));
        var resolver = new SeqRuntimeImageResolver(ManagementOptions(), inspector);

        var result = await resolver.ResolveForOperationAsync(CancellationToken.None);

        Assert.Equal($"sha256:{new string('a', 64)}", result.ResolvedImageId);
        Assert.Equal("datalust/seq:2026.1.17044", inspector.LastInspectedReference);
        Assert.Equal(0, inspector.PullCount);
    }

    [Fact]
    public async Task Explicit_setup_prepares_the_exact_approved_image_and_returns_immutable_identity()
    {
        var inspector = new PreparingInspector();
        var resolver = new SeqRuntimeImageResolver(SetupOptions(), inspector);

        var result = await resolver.ResolveForSetupAsync(CancellationToken.None);

        Assert.True(result.PreparedByPull);
        Assert.Equal($"sha256:{new string('c', 64)}", result.ResolvedImageId);
        Assert.Equal(1, inspector.PullCount);
        Assert.Equal("datalust/seq:2026.1.17044", inspector.LastPulledReference);
    }

    [Fact]
    public async Task Setup_preparation_can_be_disabled_without_weakening_operational_policy()
    {
        var inspector = new RecordingInspector(null);
        var options = SetupOptions();
        options.AllowSetupPull = false;
        var resolver = new SeqRuntimeImageResolver(options, inspector);

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            resolver.ResolveForSetupAsync(CancellationToken.None));

        Assert.Equal("seq_setup_image_preparation_disabled", exception.Code);
        Assert.Equal(0, inspector.PullCount);
    }

    [Fact]
    public async Task Missing_local_image_fails_closed_instead_of_pulling()
    {
        var inspector = new RecordingInspector(null);
        var resolver = new SeqRuntimeImageResolver(ManagementOptions(), inspector);

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            resolver.ResolveForOperationAsync(CancellationToken.None));

        Assert.Equal("seq_approved_image_missing", exception.Code);
        Assert.Equal(0, inspector.PullCount);
    }

    private static SeqDiagnosticsOptions SetupOptions() => new()
    {
        AllowSetupPull = true
    };

    private static SeqDiagnosticsOptions ManagementOptions() => new()
    {
        ManagementEnabled = true,
        EulaAccepted = true,
        AdminPasswordHashEnvironmentVariableName = "MEM_SEQ_ADMIN_PASSWORD_HASH",
        HostDataPath = "/data/seq"
    };

    private sealed class PreparingInspector : IRuntimeImageInspector
    {
        private RuntimeImageInspection? _inspection;
        public int PullCount { get; private set; }
        public string? LastPulledReference { get; private set; }

        public Task<RuntimeImageInspection?> InspectAsync(
            string immutableReference,
            CancellationToken cancellationToken) => Task.FromResult(_inspection);

        public Task PullAsync(
            string immutableReference,
            CancellationToken cancellationToken)
        {
            PullCount++;
            LastPulledReference = immutableReference;
            _inspection = new RuntimeImageInspection(
                $"sha256:{new string('c', 64)}",
                ["datalust/seq@sha256:" + new string('d', 64)],
                []);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingInspector(RuntimeImageInspection? inspection)
        : IRuntimeImageInspector
    {
        public string? LastInspectedReference { get; private set; }
        public int PullCount { get; private set; }

        public Task<RuntimeImageInspection?> InspectAsync(
            string immutableReference,
            CancellationToken cancellationToken)
        {
            LastInspectedReference = immutableReference;
            return Task.FromResult(inspection);
        }

        public Task PullAsync(
            string immutableReference,
            CancellationToken cancellationToken)
        {
            PullCount++;
            return Task.CompletedTask;
        }
    }
}
