// HostAgent/Runtime/Databases/RuntimeStackDatabaseService.cs

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Runtime.Secrets;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Databases;

public interface IRuntimeStackDatabaseService
{
    Task<(RuntimeStackDatabaseProvisioningResult Provisioning, string Password)> ProvisionMatrixDatabaseAsync(
        Guid runtimeStackId,
        string stackSlug,
        CancellationToken ct);

    Task SaveOwnershipAsync(
        RuntimeStackDatabaseProvisioningResult provisioning,
        string password,
        CancellationToken ct);

    Task<RuntimeStackDatabaseDropResult> DropUnregisteredMatrixDatabaseAsync(
        Guid runtimeStackId,
        string stackSlug,
        string databaseName,
        string databaseUsername,
        CancellationToken ct);
}

public sealed class RuntimeStackDatabaseService : IRuntimeStackDatabaseService
{
    public const string MatrixPostgresPasswordSecretKind = "matrix_postgres_password";

    private const string PostgresContainerName = "mem-postgres";
    private const string PostgresSuperuser = "postgres";
    private const string PostgresMaintenanceDatabase = "postgres";
    private const string StackDatabaseHost = "mem-postgres";
    private const int StackDatabasePort = 5432;

    private readonly DockerClient _docker;
    private readonly MemDbContext _db;
    private readonly RuntimeStackSecretService _secretService;
    private readonly ILogger<RuntimeStackDatabaseService> _logger;

    public RuntimeStackDatabaseService(
        DockerClient docker,
        MemDbContext db,
        RuntimeStackSecretService secretService,
        ILogger<RuntimeStackDatabaseService> logger)
    {
        _docker = docker;
        _db = db;
        _secretService = secretService;
        _logger = logger;
    }

    public async Task<(RuntimeStackDatabaseProvisioningResult Provisioning, string Password)> ProvisionMatrixDatabaseAsync(
        Guid runtimeStackId,
        string stackSlug,
        CancellationToken ct)
    {
        if (runtimeStackId == Guid.Empty)
        {
            throw new ArgumentException("Runtime stack id is required.", nameof(runtimeStackId));
        }

        if (string.IsNullOrWhiteSpace(stackSlug))
        {
            throw new ArgumentException("Stack slug is required.", nameof(stackSlug));
        }

        var databaseName = BuildDatabaseName(stackSlug, runtimeStackId);
        var username = BuildUsername(stackSlug, runtimeStackId);

        var existingPassword = await _secretService.GetSecretValueAsync(
            runtimeStackId,
            MatrixPostgresPasswordSecretKind,
            ct);

        var password = string.IsNullOrWhiteSpace(existingPassword)
            ? RuntimeStackSecretService.CreateSecretValue()
            : existingPassword;

        await EnsureRoleAsync(username, password, ct);
        await EnsureDatabaseAsync(databaseName, username, ct);

        _logger.LogInformation(
            "Matrix stack Postgres database created or verified. RuntimeStackId={RuntimeStackId} Database={DatabaseName} Username={Username}",
            runtimeStackId,
            databaseName,
            username);

        return (new RuntimeStackDatabaseProvisioningResult(
            RuntimeStackId: runtimeStackId,
            DatabaseEngine: "postgres",
            DatabaseHost: StackDatabaseHost,
            DatabasePort: StackDatabasePort,
            DatabaseName: databaseName,
            DatabaseUsername: username,
            PasswordSecretKind: MatrixPostgresPasswordSecretKind,
            Status: "active",
            DatabaseCreatedOrVerified: true,
            UserCreatedOrVerified: true), password);
    }

    public async Task<RuntimeStackDatabaseDropResult> DropMatrixDatabaseAsync(
    Guid runtimeStackId,
    bool force,
    CancellationToken ct)
    {
        var row = await _db.RuntimeStackDatabases
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RuntimeStackId == runtimeStackId, ct);

        if (row is null)
        {
            return new RuntimeStackDatabaseDropResult(
                Dropped: false,
                DatabaseName: null,
                DatabaseUsername: null,
                Warnings: [],
                Detail: "No RuntimeStackDatabases row exists for this stack.");
        }

        var warnings = new List<string>();

        try
        {
            await ExecPsqlAsync(
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = {QuoteLiteral(row.DatabaseName)};",
                ct);

            await ExecPsqlAsync(
                $"DROP DATABASE IF EXISTS {QuoteIdentifier(row.DatabaseName)};",
                ct);

            await ExecPsqlAsync(
                $"DROP ROLE IF EXISTS {QuoteIdentifier(row.DatabaseUsername)};",
                ct);

            _logger.LogInformation(
                "Matrix stack Postgres database and role dropped. RuntimeStackId={RuntimeStackId} Database={DatabaseName} Username={Username}",
                runtimeStackId,
                row.DatabaseName,
                row.DatabaseUsername);

            return new RuntimeStackDatabaseDropResult(
                Dropped: true,
                DatabaseName: row.DatabaseName,
                DatabaseUsername: row.DatabaseUsername,
                Warnings: warnings,
                Detail: "Matrix Postgres database and role were dropped.");
        }
        catch (Exception ex) when (CanContinueAfterDatabaseDropFailure(force, ex))
        {
            warnings.Add(
                $"Database drop failed, but force=true allowed destroy to continue: {ex.Message}");

            return new RuntimeStackDatabaseDropResult(
                Dropped: false,
                DatabaseName: row.DatabaseName,
                DatabaseUsername: row.DatabaseUsername,
                Warnings: warnings,
                Detail: ex.Message);
        }
    }

    internal static bool CanContinueAfterDatabaseDropFailure(
        bool force,
        Exception exception) =>
        force &&
        exception is not OperationCanceledException &&
        exception is not StackOverflowException &&
        exception is not OutOfMemoryException;

    public async Task SaveOwnershipAsync(
        RuntimeStackDatabaseProvisioningResult provisioning,
        string password,
        CancellationToken ct)
    {
        var stackExists = await _db.RuntimeStacks
            .AnyAsync(x => x.Id == provisioning.RuntimeStackId, ct);

        if (!stackExists)
        {
            throw new InvalidOperationException(
                $"RuntimeStack '{provisioning.RuntimeStackId}' was not found. Save the RuntimeStack before attaching database ownership.");
        }

        var now = DateTime.UtcNow;

        var row = await _db.RuntimeStackDatabases
            .FirstOrDefaultAsync(x => x.RuntimeStackId == provisioning.RuntimeStackId, ct);

        if (row is null)
        {
            row = new RuntimeStackDatabaseEntity
            {
                Id = Guid.NewGuid(),
                RuntimeStackId = provisioning.RuntimeStackId,
                CreatedAtUtc = now
            };

            _db.RuntimeStackDatabases.Add(row);
        }

        row.DatabaseEngine = provisioning.DatabaseEngine;
        row.DatabaseHost = provisioning.DatabaseHost;
        row.DatabasePort = provisioning.DatabasePort;
        row.DatabaseName = provisioning.DatabaseName;
        row.DatabaseUsername = provisioning.DatabaseUsername;
        row.PasswordSecretKind = provisioning.PasswordSecretKind;
        row.Status = provisioning.Status;
        row.UpdatedAtUtc = now;
        row.MetadataJson = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["purpose"] = "synapse-database",
            ["ownership"] = "one-postgres-database-per-runtime-stack"
        });

        await _db.SaveChangesAsync(ct);

        await _secretService.UpsertSecretValueAsync(
            provisioning.RuntimeStackId,
            MatrixPostgresPasswordSecretKind,
            password,
            metadataJson: JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["purpose"] = "synapse-postgres-password",
                ["note"] = "Stored in plain SQLite for current dev slice. Protect/encrypt before production."
            }),
            ct);
    }

    /// <summary>
    /// Returns the only database and role identity that may be created for a
    /// given runtime stack identity. Pre-registration cleanup uses this to avoid
    /// ever dropping a database name supplied by a request or arbitrary history.
    /// </summary>
    public RuntimeStackDatabaseIdentity GetExpectedMatrixDatabaseIdentity(
        Guid runtimeStackId,
        string stackSlug)
    {
        if (runtimeStackId == Guid.Empty)
        {
            throw new ArgumentException("Runtime stack id is required.", nameof(runtimeStackId));
        }

        if (string.IsNullOrWhiteSpace(stackSlug))
        {
            throw new ArgumentException("Stack slug is required.", nameof(stackSlug));
        }

        return new RuntimeStackDatabaseIdentity(
            DatabaseName: BuildDatabaseName(stackSlug, runtimeStackId),
            DatabaseUsername: BuildUsername(stackSlug, runtimeStackId));
    }

    /// <summary>
    /// Removes a database and role created by a workflow that never reached
    /// normal runtime-stack registration. Inputs are re-derived and
    /// verified against the runtime stack identity before any SQL is executed.
    /// </summary>
    public async Task<RuntimeStackDatabaseDropResult> DropUnregisteredMatrixDatabaseAsync(
        Guid runtimeStackId,
        string stackSlug,
        string databaseName,
        string databaseUsername,
        CancellationToken ct)
    {
        var expected = GetExpectedMatrixDatabaseIdentity(runtimeStackId, stackSlug);

        var runtimeStackExists = await _db.RuntimeStacks
            .AsNoTracking()
            .AnyAsync(x => x.Id == runtimeStackId || x.Slug == stackSlug, ct);

        var ownershipExists = await _db.RuntimeStackDatabases
            .AsNoTracking()
            .AnyAsync(x => x.RuntimeStackId == runtimeStackId, ct);

        EnsureUnregisteredMatrixDatabaseCleanupAllowed(
            expected,
            databaseName,
            databaseUsername,
            runtimeStackExists,
            ownershipExists);

        await ExecPsqlAsync(
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = {QuoteLiteral(expected.DatabaseName)};",
            ct);

        await ExecPsqlAsync(
            $"DROP DATABASE IF EXISTS {QuoteIdentifier(expected.DatabaseName)};",
            ct);

        await ExecPsqlAsync(
            $"DROP ROLE IF EXISTS {QuoteIdentifier(expected.DatabaseUsername)};",
            ct);

        _logger.LogInformation(
            "Unregistered Matrix Postgres database and role were dropped after failed pre-registration. RuntimeStackId={RuntimeStackId} Database={DatabaseName} Username={Username}",
            runtimeStackId,
            expected.DatabaseName,
            expected.DatabaseUsername);

        return new RuntimeStackDatabaseDropResult(
            Dropped: true,
            DatabaseName: expected.DatabaseName,
            DatabaseUsername: expected.DatabaseUsername,
            Warnings: [],
            Detail: "Unregistered Matrix Postgres database and role were dropped after a failed pre-registration workflow.");
    }

    internal static void EnsureUnregisteredMatrixDatabaseCleanupAllowed(
        RuntimeStackDatabaseIdentity expected,
        string databaseName,
        string databaseUsername,
        bool runtimeStackExists,
        bool ownershipExists)
    {
        if (!string.Equals(databaseName, expected.DatabaseName, StringComparison.Ordinal) ||
            !string.Equals(databaseUsername, expected.DatabaseUsername, StringComparison.Ordinal))
        {
            throw new RuntimeStackDatabaseCleanupRefusedException(
                "Refusing to drop an unregistered Matrix database because its recorded identity does not match the expected runtime stack identity.");
        }

        if (runtimeStackExists)
        {
            throw new RuntimeStackDatabaseCleanupRefusedException(
                "Refusing to drop an unregistered Matrix database because the target is represented by a runtime stack record.");
        }

        if (ownershipExists)
        {
            throw new RuntimeStackDatabaseCleanupRefusedException(
                "Refusing to drop an unregistered Matrix database because normal database ownership has already been recorded.");
        }
    }

    private async Task EnsureRoleAsync(
        string username,
        string password,
        CancellationToken ct)
    {
        await ExecPsqlAsync(
            BuildEnsureRoleSql(username, password),
            ct);
    }

    internal static string BuildEnsureRoleSql(
        string username,
        string password) =>
        "DO $$ BEGIN " +
        $"IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = {QuoteLiteral(username)}) THEN " +
        $"CREATE ROLE {QuoteIdentifier(username)} LOGIN PASSWORD {QuoteLiteral(password)}; " +
        "ELSE " +
        $"ALTER ROLE {QuoteIdentifier(username)} WITH LOGIN PASSWORD {QuoteLiteral(password)}; " +
        "END IF; " +
        "END $$;";

    private async Task EnsureDatabaseAsync(
        string databaseName,
        string ownerUsername,
        CancellationToken ct)
    {
        var existsSql = $"SELECT 1 FROM pg_database WHERE datname = {QuoteLiteral(databaseName)};";
        var exists = await ExecPsqlAsync(existsSql, ct);

        if (!exists.Stdout.Contains("1", StringComparison.Ordinal))
        {
            await ExecPsqlAsync(
                $"CREATE DATABASE {QuoteIdentifier(databaseName)} OWNER {QuoteIdentifier(ownerUsername)} ENCODING 'UTF8' LC_COLLATE 'C' LC_CTYPE 'C' TEMPLATE template0;",
                ct);
        }

        await ExecPsqlAsync(
            $"ALTER DATABASE {QuoteIdentifier(databaseName)} OWNER TO {QuoteIdentifier(ownerUsername)};",
            ct);
    }

    private async Task<(string Stdout, string Stderr, long ExitCode)> ExecPsqlAsync(
        string sql,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ArgumentException("A PostgreSQL provisioning command is required.", nameof(sql));
        }

        var exec = await _docker.Exec.ExecCreateContainerAsync(
            PostgresContainerName,
            CreatePsqlExecParameters(),
            ct);

        using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(
            exec.ID,
            tty: false,
            cancellationToken: ct);

        var outputTask = stream.ReadOutputToEndAsync(ct);
        var inputBytes = EncodeSqlStandardInput(sql);
        try
        {
            await stream.WriteAsync(
                inputBytes,
                0,
                inputBytes.Length,
                ct);
            stream.CloseWrite();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(inputBytes);
        }

        var output = await outputTask;

        var inspect = await _docker.Exec.InspectContainerExecAsync(
            exec.ID,
            ct);

        var stdout = output.stdout ?? string.Empty;
        var stderr = output.stderr ?? string.Empty;

        if (inspect.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Postgres provisioning command failed with exit code {inspect.ExitCode}.");
        }

        return (stdout, stderr, inspect.ExitCode);
    }

    internal static ContainerExecCreateParameters CreatePsqlExecParameters() =>
        new()
        {
            AttachStdin = true,
            AttachStdout = true,
            AttachStderr = true,
            Cmd =
            [
                "psql",
                "-X",
                "-v", "ON_ERROR_STOP=1",
                "-U", PostgresSuperuser,
                "-d", PostgresMaintenanceDatabase
            ]
        };

    internal static byte[] EncodeSqlStandardInput(string sql)
    {
        var byteCount = Encoding.UTF8.GetByteCount(sql);
        var buffer = new byte[byteCount + 1];
        _ = Encoding.UTF8.GetBytes(sql, buffer);
        buffer[byteCount] = (byte)'\n';
        return buffer;
    }

    private static string BuildDatabaseName(string stackSlug, Guid runtimeStackId) =>
        BuildSafeName("matrix", stackSlug, runtimeStackId);

    private static string BuildUsername(string stackSlug, Guid runtimeStackId) =>
        BuildSafeName("mxu", stackSlug, runtimeStackId);

    private static string BuildSafeName(
        string prefix,
        string stackSlug,
        Guid runtimeStackId)
    {
        var safeSlug = new string(stackSlug
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray());

        safeSlug = string.Join(
            '_',
            safeSlug.Split('_', StringSplitOptions.RemoveEmptyEntries));

        if (safeSlug.Length == 0)
        {
            safeSlug = "stack";
        }

        if (safeSlug.Length > 32)
        {
            safeSlug = safeSlug[..32].Trim('_');
        }

        var suffix = runtimeStackId.ToString("N")[..8];

        return $"{prefix}_{safeSlug}_{suffix}";
    }

    private static string QuoteIdentifier(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string QuoteLiteral(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

}

public sealed record RuntimeStackDatabaseDropResult(
    bool Dropped,
    string? DatabaseName,
    string? DatabaseUsername,
    IReadOnlyList<string> Warnings,
    string Detail);

public sealed record RuntimeStackDatabaseIdentity(
    string DatabaseName,
    string DatabaseUsername);

public sealed class RuntimeStackDatabaseCleanupRefusedException : InvalidOperationException
{
    public RuntimeStackDatabaseCleanupRefusedException(string message)
        : base(message)
    {
    }
}
