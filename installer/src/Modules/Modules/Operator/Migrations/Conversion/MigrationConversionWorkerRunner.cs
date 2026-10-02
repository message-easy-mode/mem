using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Modules.Operator.Migrations.Runtime;

namespace Modules.Operator.Migrations.Conversion;

public interface IMigrationConversionWorkerRunner
{
    Task<MigrationConversionWorkerRunResult> RunAsync(
        MigrationConversionWorkerRequest request,
        string requestPath,
        string eventsPath,
        string standardErrorPath,
        CancellationToken cancellationToken);
}

public sealed class MigrationConversionWorkerRunner(
    IOptions<MemMigrateRuntimeOptions> runtimeOptions)
    : IMigrationConversionWorkerRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<MigrationConversionWorkerRunResult> RunAsync(
        MigrationConversionWorkerRequest request,
        string requestPath,
        string eventsPath,
        string standardErrorPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(requestPath)!);
        await File.WriteAllTextAsync(
            requestPath,
            JsonSerializer.Serialize(request, JsonOptions),
            cancellationToken);

        var command = runtimeOptions.Value.MemMigrateCommand;

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("worker");
        startInfo.ArgumentList.Add("convert");
        startInfo.ArgumentList.Add("--request");
        startInfo.ArgumentList.Add(requestPath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The mem-migrate conversion worker could not be started.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        await File.WriteAllTextAsync(eventsPath, stdout, cancellationToken);
        await File.WriteAllTextAsync(standardErrorPath, stderr, cancellationToken);

        var events = ParseEvents(stdout, request.OperationId);
        var reportPath = Path.Combine(request.OutputPath, request.ConversionId, "conversion-report.json");
        MigrationConversionReport? report = null;
        if (File.Exists(reportPath))
        {
            report = JsonSerializer.Deserialize<MigrationConversionReport>(
                await File.ReadAllTextAsync(reportPath, cancellationToken),
                JsonOptions);
        }

        return new MigrationConversionWorkerRunResult(
            process.ExitCode,
            reportPath,
            eventsPath,
            standardErrorPath,
            events,
            report);
    }

    private static IReadOnlyList<MigrationConversionWorkerEvent> ParseEvents(
        string stdout,
        string operationId)
    {
        var events = new List<MigrationConversionWorkerEvent>();
        long expectedSequence = 1;
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var workerEvent = JsonSerializer.Deserialize<MigrationConversionWorkerEvent>(line, JsonOptions)
                ?? throw new InvalidDataException("The conversion worker emitted an empty event.");
            if (workerEvent.Schema != "mem-conversion-worker-event" ||
                workerEvent.SchemaVersion != 1 ||
                workerEvent.OperationId != operationId ||
                workerEvent.Sequence != expectedSequence++)
            {
                throw new InvalidDataException("The conversion worker emitted an invalid or out-of-sequence event.");
            }
            events.Add(workerEvent);
        }

        if (events.Count == 0)
        {
            throw new InvalidDataException("The conversion worker emitted no structured events.");
        }

        return events;
    }
}
