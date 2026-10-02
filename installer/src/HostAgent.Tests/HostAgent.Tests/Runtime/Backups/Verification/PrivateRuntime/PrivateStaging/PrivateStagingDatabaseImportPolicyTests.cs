using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;

namespace HostAgent.Tests.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;

public sealed class PrivateStagingDatabaseImportPolicyTests
{
    [Fact]
    public void Custom_dump_uses_pg_restore_with_safe_flags()
    {
        var plan = PrivateStagingDatabaseImportPolicy.Resolve(
            PrivateStagingDatabaseDumpFormats.PostgreSqlCustom);

        var command = plan.BuildCommand("staging_user", "staging_database");

        Assert.Equal("pg_restore", command[0]);
        Assert.Contains("--exit-on-error", command);
        Assert.Contains("--no-owner", command);
        Assert.Contains("--no-privileges", command);
        Assert.Contains("staging_user", command);
        Assert.Contains("staging_database", command);
        Assert.Equal("/restore/database/synapse.dump", command[^1]);
    }

    [Fact]
    public void Plain_sql_dump_uses_psql_with_on_error_stop()
    {
        var plan = PrivateStagingDatabaseImportPolicy.Resolve(
            PrivateStagingDatabaseDumpFormats.PostgreSqlPlainSql);

        var command = plan.BuildCommand("staging_user", "staging_database");

        Assert.Equal("psql", command[0]);
        Assert.Contains("ON_ERROR_STOP=1", command);
        Assert.Equal("/restore/database/synapse.sql", command[^1]);
    }

    [Fact]
    public void Custom_dump_requires_PGDMP_signature()
    {
        using var fixture = new TempDump("not-a-custom-dump"u8.ToArray());
        var plan = PrivateStagingDatabaseImportPolicy.Resolve(
            PrivateStagingDatabaseDumpFormats.PostgreSqlCustom);

        var error = Assert.Throws<InvalidDataException>(() =>
            PrivateStagingDatabaseImportPolicy.ValidateCopiedDump(plan, fixture.Path));

        Assert.Contains("PGDMP", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plain_sql_rejects_custom_dump_signature()
    {
        using var fixture = new TempDump("PGDMP"u8.ToArray());
        var plan = PrivateStagingDatabaseImportPolicy.Resolve(
            PrivateStagingDatabaseDumpFormats.PostgreSqlPlainSql);

        var error = Assert.Throws<InvalidDataException>(() =>
            PrivateStagingDatabaseImportPolicy.ValidateCopiedDump(plan, fixture.Path));

        Assert.Contains("plain-SQL", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_dump_accepts_PGDMP_signature()
    {
        using var fixture = new TempDump("PGDMPpayload"u8.ToArray());
        var plan = PrivateStagingDatabaseImportPolicy.Resolve(
            PrivateStagingDatabaseDumpFormats.PostgreSqlCustom);

        PrivateStagingDatabaseImportPolicy.ValidateCopiedDump(plan, fixture.Path);
    }

    [Fact]
    public void Failed_database_import_blocks_Synapse_start()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            PrivateStagingDatabaseImportPolicy.EnsureImportSucceeded(false));

        Assert.Contains("Synapse was not started", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Successful_database_import_allows_Synapse_start()
    {
        PrivateStagingDatabaseImportPolicy.EnsureImportSucceeded(true);
    }

    [Fact]
    public void Synapse_conversion_candidate_declares_custom_dump_format()
    {
        var format = PrivateStagingDatabaseImportPolicy
            .ResolveMigrationCandidateDumpFormat("synapse-postgresql-conversion");

        Assert.Equal(PrivateStagingDatabaseDumpFormats.PostgreSqlCustom, format);
    }

    [Fact]
    public void Unknown_migration_candidate_kind_fails_closed()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            PrivateStagingDatabaseImportPolicy.ResolveMigrationCandidateDumpFormat("mem-stack-export"));

        Assert.Contains("does not declare", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_dump_format_fails_closed()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            PrivateStagingDatabaseImportPolicy.Resolve("postgresql-mystery"));

        Assert.Contains("unsupported", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TempDump : IDisposable
    {
        private readonly string _directory;

        public TempDump(byte[] bytes)
        {
            _directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "mem-private-staging-import-policy",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "database.dump");
            File.WriteAllBytes(Path, bytes);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
