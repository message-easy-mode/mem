using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.Integrations.Seq.Services;

namespace Api.IntegrationTests.Diagnostics;

public sealed class SeqBootstrapStateStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"mem-seq-bootstrap-state-{Guid.NewGuid():N}");

    [Fact]
    public void Missing_state_is_a_clean_unconfigured_result()
    {
        var store = CreateStore();

        var result = store.Read();

        Assert.Null(result.State);
        Assert.Null(result.WarningCode);
    }

    [Fact]
    public async Task State_is_written_atomically_without_secret_fields()
    {
        var store = CreateStore();
        var state = new SeqBootstrapState(
            ManagementEnabled: true,
            EulaAccepted: true,
            EulaAcceptedAtUtc: DateTimeOffset.UtcNow,
            EulaAcceptedByUserId: Guid.NewGuid(),
            SelectedHostPort: 15341,
            PrivateUiUrl: "https://seq.example.test/",
            SetupStage: "reviewed");

        await store.WriteAsync(state, CancellationToken.None);
        var result = store.Read();
        var path = Path.Combine(_root, "data", "diagnostics", "seq-bootstrap.json");
        var json = await File.ReadAllTextAsync(path);

        Assert.NotNull(result.State);
        Assert.True(result.State!.ManagementEnabled);
        Assert.True(result.State.EulaAccepted);
        Assert.Equal(15341, result.State.SelectedHostPort);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp-*"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(path));
        }
    }

    [Fact]
    public async Task Invalid_json_is_reported_without_throwing_or_inventing_state()
    {
        var path = Path.Combine(_root, "data", "diagnostics", "seq-bootstrap.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{not-json");
        var store = CreateStore();

        var result = store.Read();

        Assert.Null(result.State);
        Assert.Equal("seq_bootstrap_state_invalid", result.WarningCode);
    }

    [Fact]
    public async Task Symbolic_link_target_is_rejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var actual = Path.Combine(_root, "actual.json");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(actual, "{}");
        var target = Path.Combine(_root, "data", "diagnostics", "seq-bootstrap.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.CreateSymbolicLink(target, actual);
        var store = CreateStore();

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            store.WriteAsync(new SeqBootstrapState(), CancellationToken.None));

        Assert.Equal("seq_bootstrap_state_path_invalid", exception.Code);
    }

    private SeqBootstrapStateStore CreateStore() => new(
        new SeqDiagnosticsOptions
        {
            BootstrapStatePath = "data/diagnostics/seq-bootstrap.json"
        },
        new TestHostEnvironment(_root));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestHostEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Api.IntegrationTests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
