using Modules.Operator.Migrations.Runtime;

namespace Api.IntegrationTests.Migrations;

public sealed class MemMigrateRuntimeStartupValidatorTests
{
    [Theory]
    [InlineData(false, "/opt/mem/migrate/current/mem-migrate")]
    [InlineData(true, "/opt/mem/migrate/dev/mem-migrate")]
    public void Product_and_development_paths_are_explicit(bool development, string expected)
    {
        Assert.Equal(expected, MemMigrateRuntimeStartupValidator.ValidateCommandPath(expected, development));
    }

    [Theory]
    [InlineData(false, "/usr/local/bin/mem-migrate")]
    [InlineData(false, "/opt/mem/migrate/dev/mem-migrate")]
    [InlineData(true, "/opt/mem/migrate/current/mem-migrate")]
    [InlineData(true, "mem-migrate")]
    public void Legacy_fallback_or_wrong_environment_paths_are_rejected(bool development, string path)
    {
        Assert.Throws<InvalidOperationException>(() =>
            MemMigrateRuntimeStartupValidator.ValidateCommandPath(path, development));
    }

    [Fact]
    public void Complete_publish_payload_requires_executable_and_sqlite_companion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-migrate-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var command = Path.Combine(root, "mem-migrate");
            File.WriteAllText(command, "test");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(command, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var missing = Assert.Throws<InvalidOperationException>(() =>
                MemMigrateRuntimeStartupValidator.ValidatePayload(command));
            Assert.Contains("libe_sqlite3.so", missing.Message, StringComparison.Ordinal);

            File.WriteAllText(Path.Combine(root, "libe_sqlite3.so"), "test");
            MemMigrateRuntimeStartupValidator.ValidatePayload(command);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Conversion_runner_has_no_environment_or_usr_local_fallback()
    {
        var sourcePath = Path.Combine(
            FindInstallerSourceRoot(),
            "Modules", "Modules", "Operator", "Migrations", "Conversion",
            "MigrationConversionWorkerRunner.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("MEM_MIGRATE_COMMAND", source, StringComparison.Ordinal);
        Assert.DoesNotContain("/usr/local/bin/mem-migrate", source, StringComparison.Ordinal);
        Assert.Contains("runtimeOptions.Value.MemMigrateCommand", source, StringComparison.Ordinal);
    }

    private static string FindInstallerSourceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "MemInstaller.sln");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate installer/src.");
    }
}
