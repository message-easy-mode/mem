using System.Text.Json;
using System.Text.Json.Serialization;
using HostAgent.Runtime.Backups.Coordination;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Runtime.Backups.Observability;

/// <summary>
/// Generates a compact redacted JSON report suitable for support, an issue
/// handoff, or AI-assisted troubleshooting. It intentionally does not package
/// private artifacts or expose raw runtime-operation evidence.
/// </summary>
public sealed class RestoreSupportReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly MemDbContext _db;
    private readonly RestoreStructuredLogService _logs;
    private readonly RestoreAttemptWorkspaceStore _workspaceStore;
    private readonly MemControlPlaneRuntimeContext _runtimeContext;
    private readonly ILogger<RestoreSupportReportService> _logger;

    public RestoreSupportReportService(
        MemDbContext db,
        RestoreStructuredLogService logs,
        RestoreAttemptWorkspaceStore workspaceStore,
        MemControlPlaneRuntimeContext runtimeContext,
        ILogger<RestoreSupportReportService> logger)
    {
        _db = db;
        _logs = logs;
        _workspaceStore = workspaceStore;
        _runtimeContext = runtimeContext;
        _logger = logger;
    }

    public async Task<RestoreSupportReportGenerationResult?> GenerateAsync(
        string restoreSessionId,
        CancellationToken ct)
    {
        var attempt = await _logs.FindAttemptAsync(restoreSessionId, ct);
        if (attempt is null)
        {
            return null;
        }

        var read = await _logs.ReadAllForSupportAsync(attempt.RestoreSessionId, ct)
            ?? new RestoreLogReadResult(Array.Empty<RestoreLogEvent>(), Array.Empty<string>());

        var claims = await _db.RestoreTargetClaims
            .AsNoTracking()
            .Where(x => x.RestoreAttemptId == attempt.Id)
            .OrderByDescending(x => x.ClaimedAtUtc)
            .ToListAsync(ct);

        var operations = await _db.RuntimeOperations
            .AsNoTracking()
            .Where(x => x.RestoreAttemptId == attempt.Id)
            .OrderByDescending(x => x.RequestedAtUtc)
            .Take(50)
            .ToListAsync(ct);

        var targets = claims
            .GroupBy(x => x.ResourceType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().ResourceValue, StringComparer.OrdinalIgnoreCase);

        var recentEvents = read.Events
            .OrderByDescending(x => x.TimestampUtc)
            .ThenByDescending(x => x.EventId, StringComparer.Ordinal)
            .Take(50)
            .Select(RestoreDiagnosticRedactor.RedactEvent)
            .ToArray();

        var report = new RestoreSupportReport(
            SchemaVersion: 1,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            MemVersion: _runtimeContext.Version,
            RestoreSessionId: attempt.RestoreSessionId,
            Attempt: new RestoreSupportReportAttempt(
                attempt.Status,
                attempt.CurrentStage,
                ToOffset(attempt.CreatedAtUtc),
                ToOffset(attempt.UpdatedAtUtc),
                ToOffset(attempt.TerminalAtUtc),
                RestoreDiagnosticRedactor.RedactText(attempt.LastErrorCode, 200),
                RestoreDiagnosticRedactor.RedactText(attempt.LastErrorSummary, 1500),
                attempt.WarningCount,
                attempt.ErrorCount),
            Source: new RestoreSupportReportSource(
                attempt.SourceKind,
                attempt.SourceKey,
                attempt.SourceCatalogEntryIdSnapshot,
                attempt.SourceDisplayNameSnapshot,
                attempt.SourceOriginKindSnapshot,
                attempt.SourceStackSlugSnapshot,
                attempt.SourceBackupIdSnapshot,
                attempt.BackupCatalogEntryId is null),
            Target: new RestoreSupportReportTarget(
                GetTarget(targets, RestoreTargetResourceTypes.StackSlug),
                GetTarget(targets, RestoreTargetResourceTypes.MatrixHost),
                GetTarget(targets, RestoreTargetResourceTypes.ElementHost)),
            Logs: RestoreStructuredLogService.BuildSummary(read.Events, attempt.WarningCount, attempt.ErrorCount),
            Operations: operations.Select(x => new RestoreSupportReportOperation(
                x.Id,
                x.Operation,
                x.Status,
                RestoreDiagnosticRedactor.RedactText(x.CurrentStep, 200),
                ToOffset(x.RequestedAtUtc),
                ToOffset(x.StartedAtUtc),
                ToOffset(x.CompletedAtUtc),
                RestoreDiagnosticRedactor.RedactText(x.LastError, 1500))).ToArray(),
            RecentEvents: recentEvents,
            Warnings: read.Warnings
                .Select(x => RestoreDiagnosticRedactor.RedactText(x, 500))
                .Concat(new[]
                {
                    "This report intentionally excludes database dumps, secrets, private keys, credentials, and unrestricted raw command output."
                })
                .Distinct(StringComparer.Ordinal)
                .ToArray())
        {
            MemCommit = _runtimeContext.Commit
        };

        var reportPath = Path.Combine(
            _workspaceStore.GetSessionDirectoryPath(attempt.RestoreSessionId),
            "support",
            "support-report.json");

        await WriteJsonAtomicallyAsync(reportPath, report, ct);

        var tracked = await _db.RestoreAttempts
            .FirstOrDefaultAsync(x => x.Id == attempt.Id, ct);
        if (tracked is not null)
        {
            tracked.SupportReportPath = reportPath;
            tracked.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await _workspaceStore.EnsureWorkspaceAsync(ToSnapshot(tracked), ct);
        }

        return new RestoreSupportReportGenerationResult(
            attempt.RestoreSessionId,
            reportPath,
            report);
    }

    public async Task<RestoreSupportReport?> GetAsync(
        string restoreSessionId,
        CancellationToken ct)
    {
        var attempt = await _logs.FindAttemptAsync(restoreSessionId, ct);
        if (attempt is null || string.IsNullOrWhiteSpace(attempt.SupportReportPath))
        {
            return null;
        }

        var expectedPath = Path.Combine(
            _workspaceStore.GetSessionDirectoryPath(attempt.RestoreSessionId),
            "support",
            "support-report.json");

        if (!string.Equals(Path.GetFullPath(attempt.SupportReportPath), Path.GetFullPath(expectedPath), StringComparison.Ordinal) ||
            !File.Exists(expectedPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                expectedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 16 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            var report = await JsonSerializer.DeserializeAsync<RestoreSupportReport>(stream, JsonOptions, ct);
            return report is null ? null : SanitizeReport(report);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse support report. RestoreSessionId={RestoreSessionId}", attempt.RestoreSessionId);
            return null;
        }
    }

    private static RestoreSupportReport SanitizeReport(RestoreSupportReport report) =>
        report with
        {
            Attempt = report.Attempt with
            {
                LastErrorCode = RestoreDiagnosticRedactor.RedactText(report.Attempt.LastErrorCode, 200),
                LastErrorSummary = RestoreDiagnosticRedactor.RedactText(report.Attempt.LastErrorSummary, 1500)
            },
            Operations = report.Operations
                .Select(x => x with
                {
                    CurrentStep = RestoreDiagnosticRedactor.RedactText(x.CurrentStep, 200),
                    LastError = RestoreDiagnosticRedactor.RedactText(x.LastError, 1500)
                })
                .ToArray(),
            RecentEvents = report.RecentEvents.Select(RestoreDiagnosticRedactor.RedactEvent).ToArray(),
            Warnings = report.Warnings.Select(x => RestoreDiagnosticRedactor.RedactText(x, 500)).ToArray()
        };

    private static string? GetTarget(
        IReadOnlyDictionary<string, string> targets,
        string resourceType) =>
        targets.TryGetValue(resourceType, out var value) ? value : null;

    private static async Task WriteJsonAtomicallyAsync<T>(string finalPath, T value, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(finalPath)
            ?? throw new InvalidOperationException("Support report path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(finalPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, ct);
                await stream.FlushAsync(ct);
            }

            File.Move(temporaryPath, finalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
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
