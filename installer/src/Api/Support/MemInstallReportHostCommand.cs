using Api.Runtime;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Integrations;
using Modules.Operator.Diagnostics.Services;
using Modules.Setup;
using Modules.Setup.SupportReports;
using Shared.ControlPlane.Runtime;
using Shared.Exceptions;
using HostAgent.DependencyInjection;

namespace Api.Support;

/// <summary>
/// Host/container-authoritative support-report entry point for Setup failures.
/// It intentionally requires local process execution authority and never opens
/// an HTTP listener or prompts for credentials.
/// </summary>
public static class MemInstallReportHostCommand
{
    private const string Command = "support";
    private const string Action = "install-report";

    public static bool IsRequested(string[] args) =>
        args.Length >= 2 &&
        string.Equals(args[0], Command, StringComparison.Ordinal) &&
        string.Equals(args[1], Action, StringComparison.Ordinal);

    public static async Task<int> RunAsync(string[] args)
    {
        if (!TryParse(args, out var parsed, out var parseError))
        {
            if (!string.IsNullOrWhiteSpace(parseError))
            {
                Console.Error.WriteLine($"ERROR: {parseError}");
            }
            WriteUsage();
            return 2;
        }

        try
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    Args = Array.Empty<string>()
                });
            builder.Logging.ClearProviders();

            var commandRuntimeContext = MemRuntimeContextBootstrap.Create(
                builder.Configuration,
                builder.Environment);
            var runtimeContext = MemActiveApiProcessStateStore.ResolveForHostCommand(
                commandRuntimeContext);
            builder.Services.AddSingleton(runtimeContext);

            // The host report command does not start the normal ASP.NET host, but
            // Diagnostics pagination still constructs MemDiagnosticCursorCodec,
            // which requires IDataProtectionProvider. A command-scoped ephemeral
            // provider is sufficient because every cursor is produced and consumed
            // within this one trusted local process. Avoid creating or mutating the
            // Control Plane's persistent authentication key ring from a read-only
            // support command.
            builder.Services.AddSingleton<IDataProtectionProvider>(
                new EphemeralDataProtectionProvider());

            var sqlitePath = MemControlPlaneSqlitePathResolver.Resolve(
                builder.Configuration,
                builder.Environment);
            if (!File.Exists(sqlitePath))
            {
                Console.Error.WriteLine(
                    "ERROR: The MEM Control Plane database was not found. " +
                    "The support command will not create a new database.");
                return 4;
            }

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = sqlitePath,
                Cache = SqliteCacheMode.Shared,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();
            builder.Services.AddDbContext<MemDbContext>(
                options => options.UseSqlite(connectionString));

            builder.Services.AddHostAgent(builder.Configuration);
            builder.Services.AddIntegrations(builder.Configuration);

            var diagnosticsOptions = new DiagnosticsApiOptions();
            builder.Configuration
                .GetSection(DiagnosticsApiOptions.SafeEventsSectionName)
                .Bind(diagnosticsOptions);
            DiagnosticsApiOptions.Validate(diagnosticsOptions);
            builder.Services.AddSingleton(diagnosticsOptions);

            builder.Services.AddSetupApplication(builder.Configuration);

            await using var provider = builder.Services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider
                .GetRequiredService<SetupInstallationSupportReportService>();
            var formatter = scope.ServiceProvider
                .GetRequiredService<SetupInstallationSupportReportFormatter>();

            var request = new SetupInstallationSupportReportRequest(
                TraceId: parsed.TraceId,
                IncludeDockerEvidence: parsed.IncludeDockerEvidence,
                Format: parsed.Format);
            var report = await service.GenerateAsync(
                parsed.InstallationId,
                request,
                CancellationToken.None);
            var document = formatter.Format(report, parsed.Format);
            Console.Out.Write(document.Content);
            if (!document.Content.EndsWith('\n'))
            {
                Console.Out.WriteLine();
            }
            return 0;
        }
        catch (MemProblemException exception) when (
            exception.StatusCode == StatusCodes.Status404NotFound)
        {
            Console.Error.WriteLine($"ERROR: {exception.SafeDetail}");
            return 5;
        }
        catch (MemProblemException exception)
        {
            Console.Error.WriteLine($"ERROR: {exception.SafeDetail}");
            return 6;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"ERROR: Installation support-report generation failed ({exception.GetType().Name}). " +
                "No credential or authentication secret was written to the console.");
            return 6;
        }
    }

    public static bool TryParse(
        string[] args,
        out MemInstallReportHostCommandOptions parsed,
        out string? error)
    {
        parsed = new MemInstallReportHostCommandOptions(
            InstallationId: null,
            TraceId: null,
            UseLatest: true,
            Format: SetupSupportReportFormats.Json,
            IncludeDockerEvidence: true);
        error = null;

        if (!IsRequested(args))
        {
            error = "The command must begin with 'support install-report'.";
            return false;
        }

        Guid? installationId = null;
        string? traceId = null;
        var latest = false;
        var format = SetupSupportReportFormats.Json;
        var includeDockerEvidence = true;

        for (var index = 2; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--latest":
                    latest = true;
                    break;
                case "--installation-id":
                    if (!TryReadValue(args, ref index, out var idValue) ||
                        !Guid.TryParse(idValue, out var id))
                    {
                        error = "--installation-id requires a valid installation GUID.";
                        return false;
                    }
                    installationId = id;
                    break;
                case "--trace-id":
                    if (!TryReadValue(args, ref index, out var traceValue) ||
                        string.IsNullOrWhiteSpace(traceValue))
                    {
                        error = "--trace-id requires a technical trace reference.";
                        return false;
                    }
                    traceId = traceValue.Trim();
                    break;
                case "--format":
                    if (!TryReadValue(args, ref index, out var formatValue) ||
                        !SetupSupportReportFormats.IsSupported(formatValue))
                    {
                        error = "--format must be 'json' or 'text'.";
                        return false;
                    }
                    format = SetupSupportReportFormats.Normalize(formatValue);
                    break;
                case "--include-docker-evidence":
                    includeDockerEvidence = true;
                    break;
                case "--no-docker-evidence":
                    includeDockerEvidence = false;
                    break;
                default:
                    error = $"Unknown option '{argument}'.";
                    return false;
            }
        }

        var selectors = (installationId is null ? 0 : 1) +
                        (traceId is null ? 0 : 1) +
                        (latest ? 1 : 0);
        if (selectors > 1)
        {
            error = "Use only one of --latest, --installation-id, or --trace-id.";
            return false;
        }

        parsed = new MemInstallReportHostCommandOptions(
            installationId,
            traceId,
            UseLatest: selectors == 0 || latest,
            format,
            includeDockerEvidence);
        return true;
    }

    private static bool TryReadValue(
        IReadOnlyList<string> args,
        ref int index,
        out string value)
    {
        value = string.Empty;
        if (index + 1 >= args.Count)
        {
            return false;
        }

        index++;
        value = args[index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static void WriteUsage()
    {
        Console.Error.WriteLine(
            "Usage: dotnet Api.dll support install-report [--latest | --installation-id <id> | --trace-id <id>] [--format json|text] [--include-docker-evidence | --no-docker-evidence]");
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            "The report is written to stdout. Operational errors are written to stderr.");
        Console.Error.WriteLine(
            "Run this only from a trusted shell with local MEM Control Plane execution authority.");
    }
}

public sealed record MemInstallReportHostCommandOptions(
    Guid? InstallationId,
    string? TraceId,
    bool UseLatest,
    string Format,
    bool IncludeDockerEvidence);
