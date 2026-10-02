using System.Reflection;
using Mem.Migrate.Infrastructure.Conversion;

namespace Mem.Migrate.UnitTests;

public sealed class SynapseConversionConfigurationTests
{
    [Fact]
    public void Postgres_configuration_includes_the_captured_matrix_server_name()
    {
        var method = typeof(SynapseConversionService).GetMethod(
            "BuildPostgresConfiguration",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "BuildPostgresConfiguration was not found.");

        var configuration = method.Invoke(
            null,
            ["matrix.example.test", "postgres", "test-password"]) as string
            ?? throw new InvalidOperationException(
                "BuildPostgresConfiguration returned no configuration.");

        var normalized = configuration.ReplaceLineEndings("\n");

        Assert.StartsWith(
            "server_name: \"matrix.example.test\"\nreport_stats: false\nmedia_store_path: \"/tmp/media_store\"\nsigning_key_path: \"/input/signing.key\"\n",
            normalized);
        Assert.Contains(
            "media_store_path: \"/tmp/media_store\"\n",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "signing_key_path: \"/input/signing.key\"\n",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "suppress_key_server_warning: true\n",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains("database:\n", normalized, StringComparison.Ordinal);
        Assert.Contains("name: psycopg2\n", normalized, StringComparison.Ordinal);
        Assert.Contains("host: postgres\n", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void Port_db_container_uses_a_writable_private_sqlite_work_directory()
    {
        var method = typeof(SynapseConversionService).GetMethod(
            "BuildPortDatabaseDockerArguments",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "BuildPortDatabaseDockerArguments was not found.");

        var arguments = method.Invoke(
            null,
            [
                "conversion-network",
                "conversion-id",
                "/private/sqlite-work",
                "/private/postgres.yaml",
                "/private/signing.key",
                "matrixdotorg/synapse@sha256:test",
                1000
            ]) as IReadOnlyList<string>
            ?? throw new InvalidOperationException(
                "BuildPortDatabaseDockerArguments returned no arguments.");

        Assert.Contains(
            "/private/sqlite-work:/input/sqlite:rw",
            arguments);
        Assert.DoesNotContain(
            "/private/sqlite-work/homeserver.db:/input/homeserver.db:ro",
            arguments);
        Assert.Contains(
            "/private/signing.key:/input/signing.key:ro",
            arguments);

        var sqliteArgumentIndex = Array.IndexOf(arguments.ToArray(), "--sqlite-database");
        Assert.True(sqliteArgumentIndex >= 0);
        Assert.Equal(
            "/input/sqlite/homeserver.db",
            arguments[sqliteArgumentIndex + 1]);
    }
    [Fact]
    public void Postgres_data_cleanup_uses_a_fixed_no_network_root_container()
    {
        var method = typeof(SynapseConversionService).GetMethod(
            "BuildPostgresDataCleanupDockerArguments",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "BuildPostgresDataCleanupDockerArguments was not found.");

        var arguments = method.Invoke(
            null,
            [
                "conversion-id",
                "/private/postgres-data",
                "postgres@sha256:test"
            ]) as IReadOnlyList<string>
            ?? throw new InvalidOperationException(
                "BuildPostgresDataCleanupDockerArguments returned no arguments.");

        Assert.Contains("--network", arguments);
        Assert.Contains("none", arguments);
        Assert.Contains("--read-only", arguments);
        Assert.Contains(
            "/private/postgres-data:/cleanup:rw",
            arguments);
        Assert.Contains("postgres@sha256:test", arguments);
        Assert.Contains(
            "rm -rf -- /cleanup/* /cleanup/.[!.]* /cleanup/..?*",
            arguments);
        Assert.DoesNotContain("--privileged", arguments);
    }

}
