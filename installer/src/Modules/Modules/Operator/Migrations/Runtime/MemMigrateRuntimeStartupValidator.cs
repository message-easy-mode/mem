using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Modules.Operator.Migrations.Runtime;

public sealed class MemMigrateRuntimeStartupValidator(
    IOptions<MemMigrateRuntimeOptions> options,
    IHostEnvironment hostEnvironment,
    IMemMigrateRuntimeProbe runtimeProbe,
    ILogger<MemMigrateRuntimeStartupValidator> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var commandPath = ValidateCommandPath(options.Value.MemMigrateCommand, hostEnvironment.IsDevelopment());
        ValidatePayload(commandPath);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        var version = await runtimeProbe.RunAsync(commandPath, ["version"], timeout.Token);
        if (version.ExitCode != 0 || string.IsNullOrWhiteSpace(version.StandardOutput))
        {
            throw new InvalidOperationException(
                $"The configured mem-migrate command failed its version probe. {SafeDetail(version.StandardError)}");
        }

        var workerHelp = await runtimeProbe.RunAsync(
            commandPath,
            ["worker", "convert", "--help"],
            timeout.Token);
        var combined = string.Concat(workerHelp.StandardOutput, "\n", workerHelp.StandardError);
        if (workerHelp.ExitCode != 0 ||
            !combined.Contains("Conversion worker options", StringComparison.Ordinal) ||
            combined.Contains("Unknown command 'worker'", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The configured mem-migrate payload does not expose the required conversion worker contract.");
        }

        logger.LogInformation(
            "MEM migration runtime validated: {CommandPath}; version {Version}",
            commandPath,
            version.StandardOutput.Trim());
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static string ValidateCommandPath(string? value, bool development)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
        {
            throw new InvalidOperationException("Migration:MemMigrateCommand must be an absolute path.");
        }

        var fullPath = Path.GetFullPath(value);
        var expected = development
            ? "/opt/mem/migrate/dev/mem-migrate"
            : "/opt/mem/migrate/current/mem-migrate";

        if (!string.Equals(fullPath, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                development
                    ? "Development mem-migrate must use /opt/mem/migrate/dev/mem-migrate."
                    : "Production mem-migrate must use /opt/mem/migrate/current/mem-migrate.");
        }

        return fullPath;
    }

    internal static void ValidatePayload(string commandPath)
    {
        if (!File.Exists(commandPath))
        {
            throw new InvalidOperationException($"The configured mem-migrate command does not exist: {commandPath}");
        }

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(commandPath);
            var executable = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            if ((mode & executable) == 0)
            {
                throw new InvalidOperationException("The configured mem-migrate command is not executable.");
            }
        }

        var sqliteCompanion = Path.Combine(Path.GetDirectoryName(commandPath)!, "libe_sqlite3.so");
        if (!File.Exists(sqliteCompanion))
        {
            throw new InvalidOperationException(
                $"The mem-migrate publish payload is incomplete. Missing companion file: {sqliteCompanion}");
        }
    }

    private static string SafeDetail(string detail) =>
        string.IsNullOrWhiteSpace(detail) ? "No diagnostic was returned." : detail.Trim();
}
