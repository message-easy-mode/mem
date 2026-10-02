using System.Text.Json;
using System.Text.Json.Serialization;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Operations;

public interface IRuntimeOperationStore
{
    Task<Guid> StartAsync(
        Guid? runtimeStackId,
        string operation,
        string? idempotencyKey,
        string requestedBy,
        string hostMutationLevel,
        object? input,
        CancellationToken ct,
        Guid? restoreAttemptId = null);

    Task UpdateStepAsync(
        Guid operationId,
        string currentStep,
        CancellationToken ct);

    Task CompleteAsync(
        Guid operationId,
        string status,
        string? currentStep,
        object? result,
        object? evidence,
        CancellationToken ct);

    Task FailAsync(
        Guid operationId,
        string? currentStep,
        string error,
        object? evidence,
        CancellationToken ct);

    Task FailAsync(
        Guid operationId,
        string? currentStep,
        string error,
        object? result,
        object? evidence,
        CancellationToken ct);

    Task<RuntimeOperationDetail?> FindByIdempotencyKeyAsync(
        Guid runtimeStackId,
        string operation,
        string idempotencyKey,
        CancellationToken ct);

    Task<RuntimeOperationSummary?> FindActiveMutatingOperationForStackAsync(
        Guid runtimeStackId,
        CancellationToken ct);

    Task<IReadOnlyList<RuntimeOperationSummary>> ListForStackAsync(
        Guid runtimeStackId,
        int limit,
        CancellationToken ct);
}

public sealed class RuntimeOperationStore : IRuntimeOperationStore
{
    private static readonly TimeSpan OperationLockDuration = TimeSpan.FromMinutes(10);
    private readonly MemDbContext _db;
    private readonly IMemDiagnosticEventWriter? _diagnostics;

    public RuntimeOperationStore(
        MemDbContext db,
        IMemDiagnosticEventWriter? diagnostics = null)
    {
        _db = db;
        _diagnostics = diagnostics;
    }

    public async Task<Guid> StartAsync(
        Guid? runtimeStackId,
        string operation,
        string? idempotencyKey,
        string requestedBy,
        string hostMutationLevel,
        object? input,
        CancellationToken ct,
        Guid? restoreAttemptId = null)
    {
        var now = DateTimeOffset.UtcNow;

        var entity = new RuntimeOperationEntity
        {
            Id = Guid.NewGuid(),
            RuntimeStackId = runtimeStackId,
            RestoreAttemptId = restoreAttemptId,

            Operation = operation,
            Status = "running",

            IdempotencyKey = idempotencyKey,

            RequestedBy = requestedBy,
            RequestedByUserId = null,

            RequestedAtUtc = now.UtcDateTime,
            StartedAtUtc = now.UtcDateTime,
            CompletedAtUtc = null,

            CurrentStep = "started",

            AttemptCount = 1,
            LockedUntilUtc = now.Add(OperationLockDuration).UtcDateTime,

            InputJson = input is null
                ? null
                : JsonSerializer.Serialize(input, JsonOptions()),

            ResultJson = null,
            EvidenceJson = null,

            LastError = null,

            HostMutationLevel = hostMutationLevel,

            RequiresConfirmation = false,
            ConfirmedAtUtc = null
        };

        _db.RuntimeOperations.Add(entity);

        await _db.SaveChangesAsync(ct);
        await RecordLifecycleAsync(
            entity,
            eventCode: "runtime.operation.started",
            severity: MemDiagnosticSeverities.Information,
            message: "A MEM runtime operation started.",
            createIncident: false);

        return entity.Id;
    }

    public async Task UpdateStepAsync(
        Guid operationId,
        string currentStep,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currentStep))
        {
            throw new ArgumentException("A runtime operation step is required.", nameof(currentStep));
        }

        var entity = await _db.RuntimeOperations
            .FirstOrDefaultAsync(x => x.Id == operationId, ct);

        if (entity is null || !string.Equals(entity.Status, "running", StringComparison.Ordinal))
        {
            return;
        }

        entity.CurrentStep = currentStep.Trim();
        entity.LockedUntilUtc = DateTimeOffset.UtcNow
            .Add(OperationLockDuration)
            .UtcDateTime;

        await _db.SaveChangesAsync(ct);
        await RecordLifecycleAsync(
            entity,
            eventCode: "runtime.operation.step_changed",
            severity: MemDiagnosticSeverities.Information,
            message: "A MEM runtime operation advanced to another step.",
            createIncident: false);
    }

    public async Task CompleteAsync(
        Guid operationId,
        string status,
        string? currentStep,
        object? result,
        object? evidence,
        CancellationToken ct)
    {
        var entity = await _db.RuntimeOperations
            .FirstOrDefaultAsync(x => x.Id == operationId, ct);

        if (entity is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        entity.Status = status;
        entity.CurrentStep = currentStep;
        entity.CompletedAtUtc = now.UtcDateTime;
        entity.LockedUntilUtc = null;

        entity.ResultJson = result is null
            ? null
            : JsonSerializer.Serialize(result, JsonOptions());

        entity.EvidenceJson = evidence is null
            ? null
            : JsonSerializer.Serialize(evidence, JsonOptions());

        entity.LastError = null;

        await _db.SaveChangesAsync(ct);

        var succeeded = string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "passed", StringComparison.OrdinalIgnoreCase);
        await RecordLifecycleAsync(
            entity,
            eventCode: "runtime.operation.completed",
            severity: succeeded
                ? MemDiagnosticSeverities.Information
                : MemDiagnosticSeverities.Warning,
            message: succeeded
                ? "A MEM runtime operation completed successfully."
                : "A MEM runtime operation completed without a successful terminal state.",
            createIncident: false);
    }

    public async Task CancelIfRunningAsync(
        Guid operationId,
        string? currentStep,
        object? result,
        object? evidence,
        CancellationToken ct)
    {
        var entity = await _db.RuntimeOperations
            .FirstOrDefaultAsync(x => x.Id == operationId, ct);

        if (entity is null ||
            !string.Equals(entity.Status, "running", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        entity.Status = "cancelled";
        entity.CurrentStep = currentStep;
        entity.CompletedAtUtc = now.UtcDateTime;
        entity.LockedUntilUtc = null;
        entity.LastError = null;

        entity.ResultJson = result is null
            ? null
            : JsonSerializer.Serialize(result, JsonOptions());

        entity.EvidenceJson = evidence is null
            ? null
            : JsonSerializer.Serialize(evidence, JsonOptions());

        await _db.SaveChangesAsync(ct);
        await RecordLifecycleAsync(
            entity,
            eventCode: "runtime.operation.cancelled",
            severity: MemDiagnosticSeverities.Warning,
            message: "A MEM runtime operation was cancelled before completion.",
            createIncident: false);
    }

    public Task FailAsync(
        Guid operationId,
        string? currentStep,
        string error,
        object? evidence,
        CancellationToken ct) =>
        FailAsync(
            operationId,
            currentStep,
            error,
            result: null,
            evidence: evidence,
            ct: ct);

    public async Task FailAsync(
        Guid operationId,
        string? currentStep,
        string error,
        object? result,
        object? evidence,
        CancellationToken ct)
    {
        var entity = await _db.RuntimeOperations
            .FirstOrDefaultAsync(x => x.Id == operationId, ct);

        if (entity is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        entity.Status = "failed";
        entity.CurrentStep = currentStep;
        entity.CompletedAtUtc = now.UtcDateTime;
        entity.LockedUntilUtc = null;
        entity.LastError = error;

        entity.ResultJson = result is null
            ? null
            : JsonSerializer.Serialize(result, JsonOptions());

        entity.EvidenceJson = evidence is null
            ? null
            : JsonSerializer.Serialize(evidence, JsonOptions());

        await _db.SaveChangesAsync(ct);
        await RecordLifecycleAsync(
            entity,
            eventCode: "runtime.operation.failed",
            severity: MemDiagnosticSeverities.Error,
            message: "A MEM runtime operation failed.",
            createIncident: true);
    }

    public async Task<RuntimeOperationDetail?> FindByIdAsync(
        Guid operationId,
        CancellationToken ct)
    {
        var entity = await _db.RuntimeOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == operationId, ct);

        return entity is null ? null : ToDetail(entity);
    }

    public async Task<RuntimeOperationDetail?> FindByIdempotencyKeyAsync(
        Guid runtimeStackId,
        string operation,
        string idempotencyKey,
        CancellationToken ct)
    {
        var entity = await _db.RuntimeOperations
            .AsNoTracking()
            .Where(x =>
                x.RuntimeStackId == runtimeStackId &&
                x.Operation == operation &&
                x.IdempotencyKey == idempotencyKey)
            .OrderByDescending(x => x.RequestedAtUtc)
            .FirstOrDefaultAsync(ct);

        return entity is null ? null : ToDetail(entity);
    }

    public async Task<RuntimeOperationSummary?> FindActiveMutatingOperationForStackAsync(
        Guid runtimeStackId,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        var entity = await _db.RuntimeOperations
            .AsNoTracking()
            .Where(x =>
                x.RuntimeStackId == runtimeStackId &&
                x.Status == "running" &&
                x.LockedUntilUtc != null &&
                x.LockedUntilUtc > now &&
                x.HostMutationLevel != null &&
                x.HostMutationLevel != "" &&
                x.HostMutationLevel != "none")
            .OrderByDescending(x => x.RequestedAtUtc)
            .FirstOrDefaultAsync(ct);

        return entity is null ? null : ToSummary(entity);
    }

    public async Task AttachRuntimeStackAsync(
        Guid operationId,
        Guid runtimeStackId,
        CancellationToken ct)
    {
        var entity = await _db.RuntimeOperations
            .FirstOrDefaultAsync(x => x.Id == operationId, ct);

        if (entity is null)
        {
            return;
        }

        entity.RuntimeStackId = runtimeStackId;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RuntimeOperationSummary>> ListForStackAsync(
        Guid runtimeStackId,
        int limit,
        CancellationToken ct)
    {
        var safeLimit = Math.Clamp(limit, 1, 100);

        var entities = await _db.RuntimeOperations
            .AsNoTracking()
            .Where(x => x.RuntimeStackId == runtimeStackId)
            .OrderByDescending(x => x.RequestedAtUtc)
            .Take(safeLimit)
            .ToArrayAsync(ct);

        return entities.Select(ToSummary).ToArray();
    }

    public async Task<IReadOnlyList<RuntimeOperationSummary>> ListForRestoreAttemptAsync(
        Guid restoreAttemptId,
        int limit,
        CancellationToken ct)
    {
        var safeLimit = Math.Clamp(limit, 1, 100);

        var entities = await _db.RuntimeOperations
            .AsNoTracking()
            .Where(x => x.RestoreAttemptId == restoreAttemptId)
            .OrderByDescending(x => x.RequestedAtUtc)
            .Take(safeLimit)
            .ToArrayAsync(ct);

        return entities.Select(ToSummary).ToArray();
    }

    private async Task RecordLifecycleAsync(
        RuntimeOperationEntity entity,
        string eventCode,
        string severity,
        string message,
        bool createIncident)
    {
        if (_diagnostics is null)
        {
            return;
        }

        try
        {
            var feature = ResolveFeature(entity.Operation);
            var resource = await ResolveResourceAsync(entity);
            var details = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["operation"] = entity.Operation,
                ["status"] = entity.Status,
                ["hostMutationLevel"] = entity.HostMutationLevel,
                ["failureSummary"] = createIncident ? entity.LastError : null
            };
            var observed = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["status"] = entity.Status,
                ["currentStep"] = entity.CurrentStep
            };

            await _diagnostics.TryWriteWorkflowEventAsync(new MemDiagnosticWriteRequest(
                Severity: severity,
                EventCode: eventCode,
                Source: "host-agent.runtime-operation",
                Feature: feature,
                Stage: entity.CurrentStep,
                Message: message,
                IncidentId: createIncident ? OperationIncidentId(entity.Id) : null,
                CreateIncident: createIncident,
                OperationId: entity.Id,
                Resource: resource,
                Expected: createIncident
                    ? new Dictionary<string, string?>
                    {
                        ["terminalStatus"] = "succeeded"
                    }
                    : null,
                Observed: observed,
                Details: details,
                SuggestedAction: createIncident
                    ? "Open the owning workspace and review the correlated operation evidence before retrying."
                    : null,
                Retryable: createIncident));
        }
        catch (Exception exception) when (exception is not OperationCanceledException and
                                          not StackOverflowException and
                                          not OutOfMemoryException)
        {
            // Diagnostics are deliberately best effort. A projection or resource lookup
            // failure must never change the result of the operation already persisted.
        }
    }

    private static string OperationIncidentId(Guid operationId) =>
        $"inc_op_{operationId:N}";

    private async Task<MemDiagnosticResource> ResolveResourceAsync(
        RuntimeOperationEntity entity)
    {
        if (entity.RestoreAttemptId is Guid restoreAttemptId)
        {
            var attempt = await _db.RestoreAttempts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == restoreAttemptId,
                    CancellationToken.None);
            if (attempt is not null)
            {
                return new MemDiagnosticResource(
                    Kind: "restore",
                    Id: attempt.RestoreSessionId,
                    DisplayName: attempt.SourceDisplayNameSnapshot,
                    StackSlug: attempt.SourceStackSlugSnapshot,
                    WorkspacePath: $"/restores/{Uri.EscapeDataString(attempt.RestoreSessionId)}");
            }
        }

        if (entity.RuntimeStackId is Guid runtimeStackId)
        {
            var stack = await _db.RuntimeStacks
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == runtimeStackId,
                    CancellationToken.None);
            if (stack is not null)
            {
                var kind = ResolveFeature(entity.Operation) switch
                {
                    "federation" => "federation",
                    "turn" => "turn",
                    _ => "stack"
                };
                var service = kind is "federation" or "turn" ? "synapse" : null;
                return new MemDiagnosticResource(
                    Kind: kind,
                    Id: stack.Slug,
                    DisplayName: stack.DisplayName ?? stack.Slug,
                    StackId: stack.Id.ToString("D"),
                    StackSlug: stack.Slug,
                    Service: service,
                    WorkspacePath: kind switch
                    {
                        "federation" => $"/stacks/{Uri.EscapeDataString(stack.Slug)}/federation",
                        "turn" => $"/stacks/{Uri.EscapeDataString(stack.Slug)}/services",
                        _ => $"/stacks/{Uri.EscapeDataString(stack.Slug)}"
                    });
            }
        }

        return new MemDiagnosticResource(
            Kind: "runtime-operation",
            Id: entity.Id.ToString("D"),
            DisplayName: entity.Operation);
    }

    private static string ResolveFeature(string operation)
    {
        var normalized = operation.Trim().ToLowerInvariant();
        if (normalized.Contains("federation", StringComparison.Ordinal)) return "federation";
        if (normalized.Contains("turn", StringComparison.Ordinal)) return "turn";
        if (normalized.Contains("restore", StringComparison.Ordinal)) return "restore";
        if (normalized.Contains("migration", StringComparison.Ordinal)) return "migration";
        if (normalized.Contains("backup", StringComparison.Ordinal)) return "backup";
        if (normalized.Contains("stack", StringComparison.Ordinal) ||
            normalized.Contains("user", StringComparison.Ordinal) ||
            normalized.Contains("doctor", StringComparison.Ordinal)) return "stack";
        return "runtime";
    }

    private static RuntimeOperationSummary ToSummary(RuntimeOperationEntity entity) =>
        new(
            entity.Id,
            entity.RuntimeStackId,
            entity.Operation,
            entity.Status,
            entity.IdempotencyKey,
            entity.RequestedBy,
            entity.HostMutationLevel,
            entity.CurrentStep,
            ToDateTimeOffset(entity.RequestedAtUtc),
            ToNullableDateTimeOffset(entity.StartedAtUtc),
            ToNullableDateTimeOffset(entity.CompletedAtUtc),
            entity.LastError);

    private static RuntimeOperationDetail ToDetail(RuntimeOperationEntity entity) =>
        new(
            entity.Id,
            entity.RuntimeStackId,
            entity.Operation,
            entity.Status,
            entity.IdempotencyKey,
            entity.RequestedBy,
            entity.HostMutationLevel,
            entity.CurrentStep,
            ToDateTimeOffset(entity.RequestedAtUtc),
            ToNullableDateTimeOffset(entity.StartedAtUtc),
            ToNullableDateTimeOffset(entity.CompletedAtUtc),
            entity.LastError,
            entity.InputJson,
            entity.ResultJson,
            entity.EvidenceJson,
            ToNullableDateTimeOffset(entity.LockedUntilUtc));

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToNullableDateTimeOffset(DateTime? value) =>
        value.HasValue ? ToDateTimeOffset(value.Value) : null;

    private static JsonSerializerOptions JsonOptions() =>
        new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
}

public sealed record RuntimeOperationSummary(
    Guid Id,
    Guid? RuntimeStackId,
    string Operation,
    string Status,
    string? IdempotencyKey,
    string? RequestedBy,
    string? HostMutationLevel,
    string? CurrentStep,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? LastError);

public sealed record RuntimeOperationDetail(
    Guid Id,
    Guid? RuntimeStackId,
    string Operation,
    string Status,
    string? IdempotencyKey,
    string? RequestedBy,
    string? HostMutationLevel,
    string? CurrentStep,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? LastError,
    string? InputJson,
    string? ResultJson,
    string? EvidenceJson,
    DateTimeOffset? LockedUntilUtc);
