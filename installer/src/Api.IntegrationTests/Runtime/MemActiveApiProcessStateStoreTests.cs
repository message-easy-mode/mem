using Api.Runtime;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Runtime;

public sealed class MemActiveApiProcessStateStoreTests
{
    [Fact]
    public void DEF_013_host_command_uses_the_live_api_process_identity_not_its_transient_identity()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-active-api-process-{Guid.NewGuid():N}");

        try
        {
            var activeApiContext = TestRuntimeContext.Create(root);
            Assert.True(
                MemActiveApiProcessStateStore.TryWrite(
                    activeApiContext,
                    out var warningCode),
                warningCode);

            var transientCommandContext = TestRuntimeContext.Create(root);
            Assert.NotEqual(
                activeApiContext.ApiProcessInstanceId,
                transientCommandContext.ApiProcessInstanceId);

            var resolved = MemActiveApiProcessStateStore.ResolveForHostCommand(
                transientCommandContext);

            Assert.Equal(
                activeApiContext.ControlPlaneInstanceId,
                resolved.ControlPlaneInstanceId);
            Assert.Equal(
                activeApiContext.ApiProcessInstanceId,
                resolved.ApiProcessInstanceId);
            Assert.DoesNotContain(
                MemActiveApiProcessStateStore.UnavailableWarningCode,
                resolved.Warnings);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Test cleanup only.
            }
        }
    }

    [Fact]
    public void DEF_013_host_command_reports_api_process_identity_as_unavailable_when_no_live_api_is_proven()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-active-api-process-unavailable-{Guid.NewGuid():N}");

        try
        {
            var activeApiContext = TestRuntimeContext.Create(root);
            Assert.True(
                MemActiveApiProcessStateStore.TryWrite(
                    activeApiContext,
                    out var warningCode),
                warningCode);
            MemActiveApiProcessStateStore.TryClear(activeApiContext);

            var transientCommandContext = TestRuntimeContext.Create(root);
            var resolved = MemActiveApiProcessStateStore.ResolveForHostCommand(
                transientCommandContext);

            Assert.Equal(Guid.Empty, resolved.ApiProcessInstanceId);
            Assert.Contains(
                MemActiveApiProcessStateStore.UnavailableWarningCode,
                resolved.Warnings);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Test cleanup only.
            }
        }
    }
    [Fact]
    public void DEF_013_normal_api_persists_and_clears_the_active_process_evidence_at_lifecycle_boundaries()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            repositoryRoot,
            "installer",
            "src",
            "Api",
            "Program.cs");
        var source = File.ReadAllText(sourcePath);

        var startedIndex = source.IndexOf(
            "app.Lifetime.ApplicationStarted.Register",
            StringComparison.Ordinal);
        var writeIndex = source.IndexOf(
            "MemActiveApiProcessStateStore.TryWrite(runtimeContext",
            StringComparison.Ordinal);
        var stoppingIndex = source.IndexOf(
            "app.Lifetime.ApplicationStopping.Register",
            StringComparison.Ordinal);
        var clearIndex = source.IndexOf(
            "MemActiveApiProcessStateStore.TryClear(runtimeContext)",
            StringComparison.Ordinal);

        Assert.True(startedIndex >= 0);
        Assert.True(writeIndex > startedIndex);
        Assert.True(stoppingIndex > writeIndex);
        Assert.True(clearIndex > stoppingIndex);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "installer",
                    "src",
                    "MemInstaller.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the MEM repository root from the test runtime.");
    }

}
