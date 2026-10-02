using System.Text;
using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Worker;

namespace Mem.Migrate.Cli;

internal static class ConversionWorkerProtocol
{
    private static readonly JsonSerializerOptions EventJsonOptions = new(CaptureJson.Options)
    {
        WriteIndented = false
    };

    public static async Task<ConversionWorkerRequest> ReadRequestAsync(
        string requestPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestPath) || !Path.IsPathFullyQualified(requestPath))
        {
            throw new ArgumentException("The worker request path must be absolute.");
        }

        await using var stream = File.OpenRead(Path.GetFullPath(requestPath));
        return await JsonSerializer.DeserializeAsync<ConversionWorkerRequest>(
            stream,
            CaptureJson.Options,
            cancellationToken)
            ?? throw new InvalidDataException("The conversion worker request is empty or invalid.");
    }

    public static async Task WriteEventAsync(
        TextWriter writer,
        ConversionWorkerEvent workerEvent,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(workerEvent, EventJsonOptions);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    public static ConversionWorkerEvent CreateEvent(
        long sequence,
        ConversionWorkerEventType eventType,
        string operationId,
        string phase,
        string status,
        string message,
        IReadOnlyDictionary<string, string?>? data = null) =>
        new(
            ConversionWorkerEvent.CurrentSchema,
            ConversionWorkerEvent.CurrentSchemaVersion,
            sequence,
            eventType,
            operationId,
            DateTimeOffset.UtcNow,
            phase,
            status,
            message,
            data ?? new Dictionary<string, string?>());
}
