using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace HostAgent.Runtime.Databases;

public interface IRuntimeStackPostgresQueryExecutor
{
    Task<RuntimeStackPostgresQueryResult> QueryAsync(
        string databaseName,
        string sql,
        CancellationToken ct);
}

public sealed record RuntimeStackPostgresQueryResult(
    string Stdout);

public sealed class RuntimeStackPostgresQueryExecutor : IRuntimeStackPostgresQueryExecutor
{
    private const string PostgresContainerName = "mem-postgres";
    private const string PostgresSuperuser = "postgres";

    private static readonly Regex SafeDatabaseName = new(
        "^[A-Za-z_][A-Za-z0-9_]{0,62}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly DockerClient _docker;

    public RuntimeStackPostgresQueryExecutor(DockerClient docker)
    {
        _docker = docker;
    }

    public async Task<RuntimeStackPostgresQueryResult> QueryAsync(
        string databaseName,
        string sql,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(databaseName) ||
            !SafeDatabaseName.IsMatch(databaseName))
        {
            throw new InvalidOperationException(
                "The persisted runtime-stack database name is not a safe PostgreSQL identifier.");
        }

        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ArgumentException("A PostgreSQL query is required.", nameof(sql));
        }

        var exec = await _docker.Exec.ExecCreateContainerAsync(
            PostgresContainerName,
            new ContainerExecCreateParameters
            {
                AttachStdout = true,
                AttachStderr = true,
                Cmd =
                [
                    "psql",
                    "-X",
                    "-v", "ON_ERROR_STOP=1",
                    "-U", PostgresSuperuser,
                    "-d", databaseName,
                    "-A",
                    "-t",
                    "-q",
                    "-c", sql
                ]
            },
            ct);

        using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(
            exec.ID,
            tty: false,
            cancellationToken: ct);

        var output = await stream.ReadOutputToEndAsync(ct);
        var inspect = await _docker.Exec.InspectContainerExecAsync(exec.ID, ct);

        if (inspect.ExitCode != 0)
        {
            throw new RuntimeStackPostgresQueryException(
                "matrix_user_inventory_query_failed",
                "The Matrix user inventory could not be read from the owned PostgreSQL database.");
        }

        return new RuntimeStackPostgresQueryResult(output.stdout ?? string.Empty);
    }
}

public sealed class RuntimeStackPostgresQueryException : Exception
{
    public RuntimeStackPostgresQueryException(
        string code,
        string safeDetail,
        Exception? innerException = null)
        : base(safeDetail, innerException)
    {
        Code = code;
        SafeDetail = safeDetail;
    }

    public string Code { get; }
    public string SafeDetail { get; }
}
