using Api.Recovery;

namespace Api.IntegrationTests.Runtime;

public sealed class MemRuntimeManifestHostRecoveryCommandTests
{
    [Fact]
    public void DEV_AUTHORITY_01A_CORR_02_recognizes_only_the_runtime_manifest_reconstruct_host_command()
    {
        Assert.True(MemRuntimeManifestHostRecoveryCommand.IsRequested(
        [
            "runtime-manifest",
            "reconstruct",
            "6d1ccd8f-bb1d-4e96-94b4-359746efb2fe",
            "/tmp/manifest.json"
        ]));

        Assert.False(MemRuntimeManifestHostRecoveryCommand.IsRequested(
        [
            "runtime-manifest",
            "inspect"
        ]));
    }

    [Fact]
    public void DEV_AUTHORITY_01A_CORR_02_parses_an_exact_stack_id_and_output_path_without_slug_fallback()
    {
        var expected = Guid.Parse("6d1ccd8f-bb1d-4e96-94b4-359746efb2fe");

        Assert.True(MemRuntimeManifestHostRecoveryCommand.TryParse(
        [
            "runtime-manifest",
            "reconstruct",
            expected.ToString("D"),
            "/tmp/runtime-stacks/6d1ccd8fbb1d4e9694b4359746efb2fe.json"
        ], out var actual, out var output));

        Assert.Equal(expected, actual);
        Assert.Equal(
            "/tmp/runtime-stacks/6d1ccd8fbb1d4e9694b4359746efb2fe.json",
            output);

        Assert.False(MemRuntimeManifestHostRecoveryCommand.TryParse(
        [
            "runtime-manifest",
            "reconstruct",
            "demo-stack",
            "/tmp/runtime-stacks/demo-stack.json"
        ], out _, out _));
    }
}
