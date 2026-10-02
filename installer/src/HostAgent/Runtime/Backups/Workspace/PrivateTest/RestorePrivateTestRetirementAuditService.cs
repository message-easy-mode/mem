using System.Globalization;
using System.Text.Json;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.Observability;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostAgent.Runtime.Backups.Workspace.PrivateTest;

/// <summary>
/// Resolves the Restore Workspace that owns a retained Private Restore Test
/// staging runtime and appends bounded retirement lifecycle evidence to the
/// restore-scoped durable event stream.
///
/// The staging id is matched only against server-owned private-test operation
/// evidence. Callers do not supply or assert Restore Workspace ownership.
/// </summary>
public sealed class RestorePrivateTestRetirementAuditService
{
    private const string PrivateTestOperation = "restore.private-test";
    private const string DestroyRequestedEventCode = "restore.private-test.destroy.requested";
    private const string DestroyedEventCode = "restore.private-test.destroyed";

    private static readonly JsonSerializerOptions EvidenceJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly MemDbContext _db;
    private readonly RestoreStructuredLogService _logs;

    public RestorePrivateTestRetirementAuditService(
        MemDbContext db,
        RestoreStructuredLogService logs)
    {
        _db = db;
        _logs = logs;
    }

    public async Task<RestorePrivateTestRetirementAuditContext?> ResolveAsync(
        string stagingId,
        CancellationToken ct)
    {
        var normalizedStagingId = NormalizeStagingId(stagingId);

        var candidates = await _db.RuntimeOperations
            .AsNoTracking()
            .Where(operation =>
                operation.Operation == PrivateTestOperation &&
                operation.RestoreAttemptId != null &&
                operation.EvidenceJson != null &&
                operation.EvidenceJson!.Contains(normalizedStagingId))
            .OrderByDescending(operation => operation.RequestedAtUtc)
            .Select(operation => new
            {
                operation.Id,
                operation.RestoreAttemptId,
                operation.EvidenceJson
            })
            .ToArrayAsync(ct);

        RestorePrivateTestRetirementAuditContext? match = null;

        foreach (var candidate in candidates)
        {
            RestorePrivateTestEvidence? evidence;
            try
            {
                evidence = JsonSerializer.Deserialize<RestorePrivateTestEvidence>(
                    candidate.EvidenceJson!,
                    EvidenceJsonOptions);
            }
            catch (JsonException)
            {
                // A malformed historical operation must not be mistaken for the
                // owner of a destructive retirement request.
                continue;
            }

            if (!string.Equals(
                    evidence?.StagingId,
                    normalizedStagingId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var restoreAttemptId = candidate.RestoreAttemptId!.Value;
            var restoreSessionId = await _db.RestoreAttempts
                .AsNoTracking()
                .Where(attempt => attempt.Id == restoreAttemptId)
                .Select(attempt => attempt.RestoreSessionId)
                .FirstOrDefaultAsync(ct);

            if (string.IsNullOrWhiteSpace(restoreSessionId))
            {
                continue;
            }

            var resolved = new RestorePrivateTestRetirementAuditContext(
                RestoreAttemptId: restoreAttemptId,
                RestoreSessionId: restoreSessionId,
                PrivateTestOperationId: candidate.Id,
                StagingId: normalizedStagingId);

            if (match is not null &&
                match.RestoreAttemptId != resolved.RestoreAttemptId)
            {
                throw new InvalidOperationException(
                    "Private staging retirement ownership is ambiguous across Restore Workspaces.");
            }

            match ??= resolved;
        }

        return match;
    }

    public Task RecordDestroyRequestedAsync(
        RestorePrivateTestRetirementAuditContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        return _logs.RecordAsync(
            context.RestoreAttemptId,
            context.PrivateTestOperationId,
            stage: RestoreAttemptStages.PrivateTest,
            severity: RestoreLogSeverities.Information,
            eventCode: DestroyRequestedEventCode,
            message: $"Private restore test staging '{context.StagingId}' retirement was requested.",
            details: new Dictionary<string, string?>
            {
                ["stagingId"] = context.StagingId
            },
            ct: ct,
            updateAttemptSummary: true);
    }

    public async Task RecordDestroyedIfMissingAsync(
        RestorePrivateTestRetirementAuditContext context,
        PrivateStagingDestroySummary? destroy,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (await HasDestroyedEventAsync(context, ct))
        {
            return;
        }

        await _logs.RecordAsync(
            context.RestoreAttemptId,
            context.PrivateTestOperationId,
            stage: RestoreAttemptStages.PrivateTest,
            severity: RestoreLogSeverities.Information,
            eventCode: DestroyedEventCode,
            message: $"Private restore test staging '{context.StagingId}' was explicitly destroyed. Disposable staging resources were removed; production resources were not touched.",
            details: new Dictionary<string, string?>
            {
                ["stagingId"] = context.StagingId,
                ["destroyedAtUtc"] = destroy?.DestroyedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                ["elementContainerRemoved"] = ToBooleanText(destroy?.ElementContainerRemoved),
                ["synapseContainerRemoved"] = ToBooleanText(destroy?.SynapseContainerRemoved),
                ["postgresContainerRemoved"] = ToBooleanText(destroy?.PostgresContainerRemoved),
                ["networkRemoved"] = ToBooleanText(destroy?.NetworkRemoved),
                ["workspaceRemoved"] = ToBooleanText(destroy?.WorkspaceRemoved),
                ["warningCount"] = destroy?.Warnings.Count.ToString(CultureInfo.InvariantCulture)
            },
            ct: ct,
            updateAttemptSummary: true);
    }

    private async Task<bool> HasDestroyedEventAsync(
        RestorePrivateTestRetirementAuditContext context,
        CancellationToken ct)
    {
        var history = await _logs.ReadAllForSupportAsync(
            context.RestoreSessionId,
            ct);

        return history?.Events.Any(@event =>
            string.Equals(
                @event.EventCode,
                DestroyedEventCode,
                StringComparison.Ordinal) &&
            @event.Details is not null &&
            @event.Details.TryGetValue("stagingId", out var eventStagingId) &&
            string.Equals(
                eventStagingId,
                context.StagingId,
                StringComparison.Ordinal)) == true;
    }

    private static string NormalizeStagingId(string stagingId)
    {
        if (string.IsNullOrWhiteSpace(stagingId))
        {
            throw new InvalidOperationException("Private staging id is required for retirement audit.");
        }

        return stagingId.Trim();
    }

    private static string? ToBooleanText(bool? value) =>
        value switch
        {
            true => "true",
            false => "false",
            null => null
        };
}

public sealed record RestorePrivateTestRetirementAuditContext(
    Guid RestoreAttemptId,
    string RestoreSessionId,
    Guid PrivateTestOperationId,
    string StagingId);
