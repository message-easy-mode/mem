using HostAgent.Runtime.Coturn;
using Microsoft.Extensions.Options;

namespace HostAgent.Tests.Runtime.Coturn;

public sealed class CoturnRuntimeFileStoreTests
{
    [Fact]
    public async Task Ensure_writes_private_secret_and_mounted_configuration_with_owner_only_permissions()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-coturn-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var store = new CoturnRuntimeFileStore(
                Microsoft.Extensions.Options.Options.Create(new CoturnRuntimeOptions
                {
                    StorageRootPath = root
                }),
                new FixedTimeProvider(
                    new DateTimeOffset(2026, 7, 26, 7, 30, 0, TimeSpan.Zero)));

            var result = await store.EnsureAsync(
                "example.test",
                "example.test",
                "203.0.113.10",
                CancellationToken.None);

            Assert.True(result.SecretPresent);
            Assert.True(result.ConfigPresent);
            Assert.Equal(
                CoturnProtectedEvidenceAccess.Available,
                result.ProtectedEvidenceAccess);
            Assert.NotNull(result.SecretValue);
            Assert.NotNull(result.Configuration);
            Assert.True(CoturnRuntimePolicy.HasRequiredSecurityPolicy(result.Configuration));
            Assert.DoesNotContain("external-ip=", result.Configuration!, StringComparison.Ordinal);
            Assert.Contains(
                $"static-auth-secret={result.SecretValue}",
                result.Configuration!,
                StringComparison.Ordinal);
            Assert.DoesNotContain("lt-cred-mech", result.Configuration!, StringComparison.Ordinal);
            Assert.DoesNotContain("no-cli", result.Configuration!, StringComparison.Ordinal);
            Assert.Contains("pidfile=/var/tmp/turnserver.pid", result.Configuration!, StringComparison.Ordinal);
            Assert.True(File.Exists(store.ConfigPath));

            if (!OperatingSystem.IsWindows())
            {
                Assert.True(result.PermissionsApplied);
                Assert.Equal(
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite |
                    UnixFileMode.UserExecute,
                    File.GetUnixFileMode(root));
                Assert.Equal(
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite,
                    File.GetUnixFileMode(store.ConfigPath));
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Ensure_is_idempotent_for_the_same_configuration()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-coturn-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var store = new CoturnRuntimeFileStore(
                Microsoft.Extensions.Options.Options.Create(new CoturnRuntimeOptions
                {
                    StorageRootPath = root
                }),
                TimeProvider.System);

            var first = await store.EnsureAsync(
                "example.test",
                "example.test",
                null,
                CancellationToken.None);
            var second = await store.EnsureAsync(
                "example.test",
                "example.test",
                null,
                CancellationToken.None);

            Assert.Equal(first.SecretValue, second.SecretValue);
            Assert.Equal(first.ConfigSha256, second.ConfigSha256);
            Assert.Equal(first.Configuration, second.Configuration);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }


    [Fact]
    public async Task Read_is_observational_and_does_not_repair_existing_unix_modes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-coturn-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var store = new CoturnRuntimeFileStore(
                Microsoft.Extensions.Options.Options.Create(new CoturnRuntimeOptions
                {
                    StorageRootPath = root
                }),
                TimeProvider.System);

            await store.EnsureAsync(
                "example.test",
                "example.test",
                null,
                CancellationToken.None);

            var secretPath = Path.Combine(root, "coturn-secret.json");
            var directoryMode = UnixFileMode.UserRead |
                                UnixFileMode.UserWrite |
                                UnixFileMode.UserExecute |
                                UnixFileMode.GroupRead |
                                UnixFileMode.GroupExecute;
            var fileMode = UnixFileMode.UserRead |
                           UnixFileMode.UserWrite |
                           UnixFileMode.GroupRead;

            File.SetUnixFileMode(root, directoryMode);
            File.SetUnixFileMode(secretPath, fileMode);
            File.SetUnixFileMode(store.ConfigPath, fileMode);

            var result = await store.ReadAsync(CancellationToken.None);

            Assert.True(result.SecretPresent);
            Assert.True(result.ConfigPresent);
            Assert.Equal(
                CoturnProtectedEvidenceAccess.Available,
                result.ProtectedEvidenceAccess);
            Assert.False(result.PermissionsApplied);
            Assert.Equal(directoryMode, File.GetUnixFileMode(root));
            Assert.Equal(fileMode, File.GetUnixFileMode(secretPath));
            Assert.Equal(fileMode, File.GetUnixFileMode(store.ConfigPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Relative_storage_root_remains_rejected_without_direct_source_opt_in()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new CoturnRuntimeFileStore(
                Microsoft.Extensions.Options.Options.Create(new CoturnRuntimeOptions
                {
                    StorageRootPath = "../../../dev/.state/container/host-data/platform/coturn"
                }),
                TimeProvider.System));

        Assert.Contains(
            "must be an absolute host path",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Direct_source_profile_resolves_explicit_relative_storage_root_to_absolute_path()
    {
        var store = new CoturnRuntimeFileStore(
            Microsoft.Extensions.Options.Options.Create(new CoturnRuntimeOptions
            {
                StorageRootPath = "../../../dev/.state/container/host-data/platform/coturn",
                AllowRelativeDevelopmentStorageRoot = true
            }),
            TimeProvider.System);

        Assert.True(Path.IsPathRooted(store.ConfigPath));
        Assert.EndsWith(
            Path.Combine(
                "dev",
                ".state",
                "container",
                "host-data",
                "platform",
                "coturn",
                "turnserver.conf"),
            store.ConfigPath,
            StringComparison.Ordinal);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
