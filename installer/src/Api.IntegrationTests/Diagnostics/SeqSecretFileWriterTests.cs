using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.Integrations.Seq.Services;

namespace Api.IntegrationTests.Diagnostics;

public sealed class SeqSecretFileWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"mem-seq-secret-writer-{Guid.NewGuid():N}");

    [Fact]
    public async Task Administrator_hash_and_api_key_are_atomic_and_owner_only()
    {
        var options = Options();
        var environment = new TestHostEnvironment(_root);
        var writer = new SeqSecretFileWriter(options, environment);

        await writer.WriteAdministratorPasswordHashAsync(
            "$PH$administrator-hash",
            CancellationToken.None);
        await writer.WriteIngestionApiKeyAsync(
            "ingestion-token",
            CancellationToken.None);

        var resolver = new SeqSecretResolver(environment);
        var admin = resolver.ResolveAdminPasswordHash(options);
        var apiKey = resolver.ResolveApiKey(options);
        var secretRoot = Path.Combine(_root, "data", "secrets", "seq");

        Assert.Equal("$PH$administrator-hash", admin.Value);
        Assert.Equal("ingestion-token", apiKey.Value);
        Assert.Empty(Directory.GetFiles(secretRoot, "*.tmp-*"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(Path.Combine(secretRoot, "admin-password-hash")));
            Assert.Equal(
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute,
                File.GetUnixFileMode(secretRoot));
        }
    }

    [Fact]
    public async Task Secret_path_outside_server_owned_root_is_rejected()
    {
        var options = Options();
        options.AdminPasswordHashFilePath = "data/outside/admin-password-hash";
        var writer = new SeqSecretFileWriter(
            options,
            new TestHostEnvironment(_root));

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            writer.WriteAdministratorPasswordHashAsync(
                "hash",
                CancellationToken.None));

        Assert.Equal("seq_admin_password_hash_path_invalid", exception.Code);
    }

    [Fact]
    public async Task Symbolic_link_secret_target_is_rejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var options = Options();
        var secretRoot = Path.Combine(_root, "data", "secrets", "seq");
        Directory.CreateDirectory(secretRoot);
        var actual = Path.Combine(_root, "outside-secret");
        await File.WriteAllTextAsync(actual, "original");
        File.CreateSymbolicLink(
            Path.Combine(secretRoot, "admin-password-hash"),
            actual);
        var environment = new TestHostEnvironment(_root);
        var writer = new SeqSecretFileWriter(options, environment);
        var resolver = new SeqSecretResolver(environment);

        var resolution = resolver.ResolveAdminPasswordHash(options);
        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            writer.WriteAdministratorPasswordHashAsync(
                "replacement",
                CancellationToken.None));

        Assert.False(resolution.Available);
        Assert.Equal("diagnostics.seq_admin_password_hash_unavailable", resolution.WarningCode);
        Assert.Equal("seq_admin_password_hash_path_invalid", exception.Code);
        Assert.Equal("original", await File.ReadAllTextAsync(actual));
    }

    private static SeqDiagnosticsOptions Options() => new()
    {
        SecretRootPath = "data/secrets/seq",
        AdminPasswordHashEnvironmentVariableName = string.Empty,
        AdminPasswordHashFilePath = "data/secrets/seq/admin-password-hash",
        ApiKeyEnvironmentVariableName = string.Empty,
        ApiKeyFilePath = "data/secrets/seq/ingestion-api-key"
    };

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
