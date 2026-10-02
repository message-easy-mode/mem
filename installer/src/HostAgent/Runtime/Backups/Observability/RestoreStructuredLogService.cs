using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HostAgent.Runtime.Backups.Coordination;
using Infrastructure.Data.Entities;
using Mem.Localization;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Backups.Observability;

/// <summary>
/// Stores safe restore events as append-friendly NDJSON. SQLite remains the
/// authority for restore coordination; this service only maintains concise
/// summary fields and writes the human-inspectable event stream.
/// </summary>
public sealed class RestoreStructuredLogService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> AppendLocks = new(StringComparer.Ordinal);
    private static readonly byte[] NewLine = Encoding.UTF8.GetBytes(Environment.NewLine);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly MemDbContext _db;
    private readonly RestoreAttemptWorkspaceStore _workspaceStore;
    private readonly ILogger<RestoreStructuredLogService> _logger;
    private readonly IMemDiagnosticEventWriter? _diagnostics;

    public RestoreStructuredLogService(
        MemDbContext db,
        RestoreAttemptWorkspaceStore workspaceStore,
        ILogger<RestoreStructuredLogService> logger,
        IMemDiagnosticEventWriter? diagnostics = null)
    {
        _db = db;
        _workspaceStore = workspaceStore;
        _logger = logger;
        _diagnostics = diagnostics;
    }

    public async Task RecordAsync(
        Guid restoreAttemptId,
        Guid? operationId,
        string stage,
        string severity,
        string eventCode,
        string message,
        IReadOnlyDictionary<string, string?>? details,
        CancellationToken ct,
        bool updateAttemptSummary = true)
    {
        var attempt = await _db.RestoreAttempts
            .FirstOrDefaultAsync(x => x.Id == restoreAttemptId, ct);

        if (attempt is null)
        {
            _logger.LogWarning(
                "Restore log event skipped because attempt was not found. RestoreAttemptId={RestoreAttemptId} EventCode={EventCode}",
                restoreAttemptId,
                eventCode);
            return;
        }

        await RecordForTrackedAttemptAsync(
            attempt,
            operationId,
            stage,
            severity,
            eventCode,
            message,
            details,
            ct,
            updateAttemptSummary);
    }

    /// <summary>
    /// Forward-only overload for new restore events. The descriptor is the
    /// stable machine contract; <paramref name="message"/> remains concise
    /// operator/audit prose and is redacted by the existing event pipeline.
    /// </summary>
    public Task RecordAsync(
        Guid restoreAttemptId,
        Guid? operationId,
        string stage,
        string severity,
        LocalizedMessage eventMessage,
        string message,
        CancellationToken ct,
        bool updateAttemptSummary = true)
    {
        ArgumentNullException.ThrowIfNull(eventMessage);

        return RecordAsync(
            restoreAttemptId,
            operationId,
            stage,
            severity,
            eventMessage.Code,
            message,
            ToDetailValues(eventMessage.Arguments),
            ct,
            updateAttemptSummary);
    }

    public async Task<RestoreLogPage?> ListAsync(
        string restoreSessionId,
        RestoreLogQuery query,
        CancellationToken ct)
    {
        var attempt = await FindAttemptAsync(restoreSessionId, ct);
        if (attempt is null)
        {
            return null;
        }

        var read = await ReadAllEventsAsync(attempt, ct);
        var normalizedSeverity = NormalizeOptionalFilter(query.Severity);
        var normalizedStage = NormalizeOptionalFilter(query.Stage);
        var normalizedSearch = RestoreDiagnosticRedactor.RedactText(query.Search, 200).ToLowerInvariant();

        var filtered = read.Events
            .Where(x => string.IsNullOrWhiteSpace(normalizedSeverity) ||
                        string.Equals(x.Severity, normalizedSeverity, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(normalizedStage) ||
                        string.Equals(x.Stage, normalizedStage, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(normalizedSearch) || MatchesSearch(x, normalizedSearch))
            .OrderByDescending(x => x.TimestampUtc)
            .ThenByDescending(x => x.EventId, StringComparer.Ordinal)
            .ToArray();

        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Length / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);
        var events = filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        var summary = BuildSummary(read.Events, attempt.WarningCount, attempt.ErrorCount);
        return new RestoreLogPage(
            attempt.RestoreSessionId,
            page,
            pageSize,
            filtered.Length,
            totalPages,
            summary,
            events,
            read.Warnings);
    }

    internal async Task<RestoreLogReadResult?> ReadAllForSupportAsync(
        string restoreSessionId,
        CancellationToken ct)
    {
        var attempt = await FindAttemptAsync(restoreSessionId, ct);
        return attempt is null ? null : await ReadAllEventsAsync(attempt, ct);
    }

    internal async Task<RestoreAttemptEntity?> FindAttemptAsync(
        string restoreSessionId,
        CancellationToken ct)
    {
        var normalizedRestoreSessionId = RestoreSourceKeyFactory.NormalizePathSegment(
            restoreSessionId,
            "Restore session id is required.");

        return await _db.RestoreAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RestoreSessionId == normalizedRestoreSessionId, ct);
    }

    internal static RestoreLogSummary BuildSummary(
        IReadOnlyList<RestoreLogEvent> events,
        int warningCount,
        int errorCount)
    {
        var ordered = events
            .OrderByDescending(x => x.TimestampUtc)
            .ThenByDescending(x => x.EventId, StringComparer.Ordinal)
            .ToArray();

        return new RestoreLogSummary(
            TotalEvents: events.Count,
            WarningCount: warningCount,
            ErrorCount: errorCount,
            LatestEvent: ordered.FirstOrDefault(),
            LatestWarningOrError: ordered.FirstOrDefault(x => x.Severity is RestoreLogSeverities.Warning or RestoreLogSeverities.Error or RestoreLogSeverities.Critical));
    }

    private static IReadOnlyDictionary<string, string?>? ToDetailValues(
        IReadOnlyDictionary<string, object?> arguments)
    {
        if (arguments.Count == 0)
        {
            return null;
        }

        var details = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var pair in arguments)
        {
            details[pair.Key] = pair.Value switch
            {
                null => null,
                IFormattable formattable => formattable.ToString(
                    null,
                    CultureInfo.InvariantCulture),
                _ => pair.Value.ToString()
            };
        }

        return details;
    }

    private async Task RecordForTrackedAttemptAsync(
        RestoreAttemptEntity attempt,
        Guid? operationId,
        string stage,
        string severity,
        string eventCode,
        string message,
        IReadOnlyDictionary<string, string?>? details,
        CancellationToken ct,
        bool updateAttemptSummary)
    {
        var normalizedStage = NormalizeRequired(stage, "Restore log stage is required.", 120);
        var normalizedSeverity = NormalizeRequired(severity, "Restore log severity is required.", 32).ToLowerInvariant();
        if (!RestoreLogSeverities.IsKnown(normalizedSeverity))
        {
            throw new InvalidOperationException($"Unknown restore log severity '{severity}'.");
        }

        var @event = RestoreDiagnosticRedactor.RedactEvent(new RestoreLogEvent(
            SchemaVersion: 1,
            EventId: Guid.NewGuid().ToString("N"),
            TimestampUtc: DateTimeOffset.UtcNow,
            RestoreSessionId: attempt.RestoreSessionId,
            OperationId: operationId,
            Stage: normalizedStage,
            Severity: normalizedSeverity,
            EventCode: NormalizeRequired(eventCode, "Restore log event code is required.", 200),
            Message: RestoreDiagnosticRedactor.RedactText(message, 1500),
            Details: RestoreDiagnosticRedactor.RedactDetails(details)));

        await _workspaceStore.EnsureWorkspaceAsync(ToSnapshot(attempt), ct);
        var eventPath = GetEventsPath(attempt.RestoreSessionId);
        await AppendAsync(eventPath, @event, ct);

        if (updateAttemptSummary)
        {
            attempt.LastEventAtUtc = @event.TimestampUtc.UtcDateTime;
            attempt.UpdatedAtUtc = @event.TimestampUtc.UtcDateTime;

            if (normalizedSeverity == RestoreLogSeverities.Warning)
            {
                attempt.WarningCount++;
            }

            if (RestoreLogSeverities.IsError(normalizedSeverity))
            {
                attempt.ErrorCount++;
                attempt.LastErrorCode = @event.EventCode;
                attempt.LastErrorSummary = @event.Message;
            }

            await _db.SaveChangesAsync(ct);
            await _workspaceStore.EnsureWorkspaceAsync(ToSnapshot(attempt), ct);
        }

        await BridgeToGlobalDiagnosticsAsync(attempt, @event);
    }

    private async Task BridgeToGlobalDiagnosticsAsync(
        RestoreAttemptEntity attempt,
        RestoreLogEvent @event)
    {
        if (_diagnostics is null || !ShouldBridge(@event))
        {
            return;
        }

        var isError = RestoreLogSeverities.IsError(@event.Severity);
        var operationIncidentId = @event.OperationId is Guid operationId
            ? $"inc_op_{operationId:N}"
            : null;
        var createIncident = isError && operationIncidentId is null;
        await _diagnostics.TryWriteWorkflowEventAsync(new MemDiagnosticWriteRequest(
            Severity: @event.Severity,
            EventCode: @event.EventCode,
            Source: "host-agent.restore",
            Feature: "restore",
            Stage: @event.Stage,
            Message: @event.Message,
            IncidentId: isError ? operationIncidentId : null,
            CreateIncident: createIncident,
            OperationId: @event.OperationId,
            Resource: new MemDiagnosticResource(
                Kind: "restore",
                Id: attempt.RestoreSessionId,
                DisplayName: attempt.SourceDisplayNameSnapshot,
                StackSlug: attempt.SourceStackSlugSnapshot,
                WorkspacePath: $"/restores/{Uri.EscapeDataString(attempt.RestoreSessionId)}"),
            Observed: new Dictionary<string, string?>
            {
                ["status"] = attempt.Status,
                ["stage"] = attempt.CurrentStage
            },
            Details: @event.Details?.ToDictionary(
                pair => pair.Key,
                pair => (string?)pair.Value,
                StringComparer.Ordinal),
            SuggestedAction: isError
                ? "Open the Restore Workspace and review the correlated restore evidence before retrying."
                : null,
            Retryable: isError));
    }

    private static bool ShouldBridge(RestoreLogEvent @event)
    {
        if (@event.Severity is RestoreLogSeverities.Warning or
            RestoreLogSeverities.Error or
            RestoreLogSeverities.Critical)
        {
            return true;
        }

        return @event.EventCode is
            "restore.attempt.created" or
            "restore.private-test.requested" or
            "restore.private-test.passed" or
            "restore.standard-recreate.completed" or
            "restore.standard-recreate.completed-needs-verification" or
            "restore.cancelled" or
            "restore.handover.completed" or
            "restore.standard-recreate.cleanup.completed";
    }

    private async Task<RestoreLogReadResult> ReadAllEventsAsync(
        RestoreAttemptEntity attempt,
        CancellationToken ct)
    {
        var eventPath = GetEventsPath(attempt.RestoreSessionId);
        if (!File.Exists(eventPath))
        {
            return new RestoreLogReadResult(Array.Empty<RestoreLogEvent>(), Array.Empty<string>());
        }

        var events = new List<RestoreLogEvent>();
        var warnings = new List<string>();
        try
        {
            await using var stream = new FileStream(
                eventPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 16 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            var lineNumber = 0;
            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(ct);
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var parsed = JsonSerializer.Deserialize<RestoreLogEvent>(line, JsonOptions);
                    if (parsed is null || parsed.SchemaVersion != 1 ||
                        string.IsNullOrWhiteSpace(parsed.EventId) ||
                        string.IsNullOrWhiteSpace(parsed.RestoreSessionId))
                    {
                        warnings.Add($"Ignored malformed structured restore log event at line {lineNumber}.");
                        continue;
                    }

                    events.Add(RestoreDiagnosticRedactor.RedactEvent(parsed));
                }
                catch (JsonException)
                {
                    warnings.Add($"Ignored malformed structured restore log event at line {lineNumber}.");
                }
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not read restore event stream. RestoreSessionId={RestoreSessionId}", attempt.RestoreSessionId);
            warnings.Add("The structured event stream could not be read completely.");
        }

        return new RestoreLogReadResult(events, warnings);
    }

    private async Task AppendAsync(string eventPath, RestoreLogEvent @event, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(eventPath)
            ?? throw new InvalidOperationException("Restore event path has no parent directory.");
        Directory.CreateDirectory(directory);

        var gate = AppendLocks.GetOrAdd(eventPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await using var stream = new FileStream(
                eventPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 16 * 1024,
                options: FileOptions.Asynchronous | FileOptions.WriteThrough);

            await JsonSerializer.SerializeAsync(stream, @event, JsonOptions, ct);
            await stream.WriteAsync(NewLine, ct);
            await stream.FlushAsync(ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private string GetEventsPath(string restoreSessionId) =>
        Path.Combine(_workspaceStore.GetSessionDirectoryPath(restoreSessionId), "logs", "events.ndjson");

    private static bool MatchesSearch(RestoreLogEvent @event, string normalizedSearch)
    {
        if (@event.EventCode.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
            @event.Message.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return @event.Details?.Any(x =>
            x.Key.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
            x.Value.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string NormalizeRequired(string? value, string error, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException(error);
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? NormalizeOptionalFilter(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static RestoreAttemptSnapshot ToSnapshot(RestoreAttemptEntity entity) =>
        new(
            entity.Id,
            entity.RestoreSessionId,
            entity.SourceKind,
            entity.SourceKey,
            entity.SourceCatalogEntryIdSnapshot,
            entity.SourceDisplayNameSnapshot,
            entity.SourceOriginKindSnapshot,
            entity.SourceStackSlugSnapshot,
            entity.SourceBackupIdSnapshot,
            entity.Status,
            entity.CurrentStage,
            ToOffset(entity.CreatedAtUtc),
            ToOffset(entity.UpdatedAtUtc),
            ToOffset(entity.TerminalAtUtc),
            ToOffset(entity.LastEventAtUtc),
            entity.LastErrorCode,
            entity.LastErrorSummary,
            entity.WarningCount,
            entity.ErrorCount,
            entity.SessionDirectoryPath,
            entity.LogDirectoryPath,
            entity.SupportReportPath,
            entity.RuntimeOperationId);

    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value.HasValue ? ToOffset(value.Value) : null;
}

internal sealed record RestoreLogReadResult(
    IReadOnlyList<RestoreLogEvent> Events,
    IReadOnlyList<string> Warnings);
