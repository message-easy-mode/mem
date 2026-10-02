using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostAgent.Runtime.Operations;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HostAgent.Runtime.Coturn;

public sealed class CoturnPlatformMaintenanceOperationService
{
    public const string OperationName = "maintain-platform-coturn";
    private const int MaximumIdempotencyKeyLength = 80;
    private static readonly TimeSpan FailureJournalTimeout = TimeSpan.FromSeconds(5);

    private readonly MemDbContext _db;
    private readonly RuntimeOperationStore _operations;
    private readonly ICoturnPlatformMaintenanceDispatcher _dispatcher;
    private readonly ICoturnPlatformMaintenanceRuntime _runtime;
    private readonly CoturnPlatformMutationAdmissionGate _admissionGate;

    public CoturnPlatformMaintenanceOperationService(
        MemDbContext db,
        RuntimeOperationStore operations,
        ICoturnPlatformMaintenanceDispatcher dispatcher,
        ICoturnPlatformMaintenanceRuntime runtime,
        CoturnPlatformMutationAdmissionGate admissionGate)
    {
        _db = db;
        _operations = operations;
        _dispatcher = dispatcher;
        _runtime = runtime;
        _admissionGate = admissionGate;
    }

    public async Task<CoturnPlatformMaintenanceAcceptedResponse> AcceptAsync(
        CoturnPlatformMaintenanceRequest request,
        string requestedBy,
        CancellationToken requestCancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        requestCancellation.ThrowIfCancellationRequested();

        var action = CoturnPlatformMaintenanceActions.Normalize(request.Action);
        if (action == CoturnPlatformMaintenanceActions.RestartVerify &&
            !string.IsNullOrWhiteSpace(request.ExternalIp))
        {
            // A restart is intentionally lower authority than protected repair.
            // Never silently discard a configuration edit or upgrade a restart
            // into a mutation that bypasses the repair endpoint's recent step-up.
            throw new CoturnPlatformMaintenanceException(
                "coturn_restart_configuration_not_allowed",
                "Restart & verify does not change the external IP. Use Apply & verify (protected repair) to apply network settings.",
                StatusCodes.Status400BadRequest);
        }

        var externalIp = action == CoturnPlatformMaintenanceActions.Repair
            ? NormalizeRepairExternalIp(request.ExternalIp)
            : null;
        var clientIdempotencyKey = RequireIdempotencyKey(request.IdempotencyKey);
        var durableIdempotencyKey = BuildDurableIdempotencyKey(
            action,
            clientIdempotencyKey);
        var requestHash = ComputeRequestHash(action, externalIp);
        var effectiveRequest = request with
        {
            Action = action,
            ExternalIp = externalIp,
            IdempotencyKey = clientIdempotencyKey
        };

        using var admission = await _admissionGate.EnterAsync(requestCancellation);

        var replay = await _db.RuntimeOperations
            .AsNoTracking()
            .Where(x =>
                x.Operation == OperationName &&
                x.IdempotencyKey == durableIdempotencyKey)
            .OrderByDescending(x => x.RequestedAtUtc)
            .FirstOrDefaultAsync(requestCancellation);

        if (replay is not null)
        {
            EnsureMatchingReplay(replay.InputJson, requestHash);
            return Accepted(
                replay.Id,
                action,
                reusedExistingOperation: true);
        }

        // Detect an already accepted shared-Coturn mutation before inspecting
        // transient runtime state. During a real repair the container may be
        // stopped or temporarily absent; a second request must report the
        // active durable operation rather than misclassify that transient state.
        var active = await FindRunningMutationAsync(requestCancellation);
        if (active is not null)
        {
            throw new CoturnPlatformMaintenanceException(
                "coturn_operation_in_progress",
                "Another shared platform TURN mutation is already running. Wait for it to finish before starting another maintenance action.",
                StatusCodes.Status409Conflict);
        }

        var preflight = await ValidatePreconditionsAsync(
            action,
            requestCancellation);

        // Authority, idempotency, and runtime preconditions are now accepted.
        // RuntimeOperation creation and queue journaling deliberately stop using
        // HttpContext.RequestAborted so transport loss cannot create an ambiguous
        // half-accepted maintenance request.
        using var acceptanceJournal = new CancellationTokenSource(FailureJournalTimeout);
        var operationId = await _operations.StartAsync(
            runtimeStackId: null,
            operation: OperationName,
            idempotencyKey: durableIdempotencyKey,
            requestedBy: string.IsNullOrWhiteSpace(requestedBy)
                ? "platform-owner"
                : requestedBy.Trim(),
            hostMutationLevel: action == CoturnPlatformMaintenanceActions.Repair
                ? "docker,platform-turn,protected-config,host-ports"
                : "docker,platform-turn",
            input: new CoturnPlatformMaintenanceOperationInput(
                Action: action,
                RequestHash: requestHash,
                ExternalIpConfigured: !string.IsNullOrWhiteSpace(externalIp),
                PreflightContainerId: preflight.ContainerId,
                PreflightStartedAtUtc: preflight.DockerRuntime?.StartedAtUtc,
                PreflightRestartCount: preflight.DockerRuntime?.RestartCount,
                PreflightRuntimeExact: preflight.RuntimeExact,
                RequestAbortCancelsMutation: false,
                FunctionalVerificationRequired: true),
            acceptanceJournal.Token);

        // From this point forward the durable operation already exists. Queue
        // journaling deliberately uses a bounded server-owned token rather than
        // HttpContext.RequestAborted.
        using var journal = new CancellationTokenSource(FailureJournalTimeout);
        await _operations.UpdateStepAsync(
            operationId,
            "queued",
            journal.Token);

        if (!_dispatcher.TryEnqueue(operationId, effectiveRequest))
        {
            using var failureJournal = new CancellationTokenSource(FailureJournalTimeout);
            await _operations.FailAsync(
                operationId,
                currentStep: "queued",
                error: "The platform TURN maintenance queue is full.",
                evidence: new
                {
                    action,
                    failureKind = "coturn-maintenance-background-queue-full"
                },
                failureJournal.Token);

            throw new CoturnPlatformMaintenanceException(
                "coturn_maintenance_queue_full",
                "MEM could not accept another shared platform TURN maintenance operation at this time.",
                StatusCodes.Status503ServiceUnavailable);
        }

        return Accepted(
            operationId,
            action,
            reusedExistingOperation: false);
    }

    public async Task<CoturnPlatformMaintenanceActiveResponse> FindActiveAsync(
        CancellationToken cancellationToken)
    {
        var operation = await _db.RuntimeOperations
            .AsNoTracking()
            .Where(x =>
                x.Operation == OperationName &&
                x.Status == "running" &&
                x.CompletedAtUtc == null)
            .OrderByDescending(x => x.RequestedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (operation is null)
        {
            return new CoturnPlatformMaintenanceActiveResponse(
                Source: "control-plane",
                Active: false,
                Operation: null);
        }

        var input = DeserializeInput(operation.InputJson);
        var action = input?.Action;
        if (action is not (
                CoturnPlatformMaintenanceActions.RestartVerify or
                CoturnPlatformMaintenanceActions.Repair))
        {
            action = null;
        }

        if (action is null)
        {
            throw new CoturnPlatformMaintenanceException(
                "coturn_maintenance_operation_unreadable",
                "The active Coturn maintenance operation does not contain readable canonical input. Review Diagnostics before starting another maintenance action.",
                StatusCodes.Status409Conflict);
        }

        var terminal = operation.CompletedAtUtc.HasValue;
        return new CoturnPlatformMaintenanceActiveResponse(
            Source: "control-plane",
            Active: !terminal,
            Operation: new CoturnPlatformMaintenanceOperationResponse(
                OperationId: operation.Id,
                Action: action,
                Status: operation.Status,
                CurrentStep: operation.CurrentStep,
                RequestedAtUtc: ToDateTimeOffset(operation.RequestedAtUtc),
                StartedAtUtc: ToNullableDateTimeOffset(operation.StartedAtUtc),
                CompletedAtUtc: ToNullableDateTimeOffset(operation.CompletedAtUtc),
                LastError: operation.LastError,
                Terminal: terminal,
                Succeeded: terminal && string.Equals(
                    operation.Status,
                    "succeeded",
                    StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<Infrastructure.Data.Entities.RuntimeOperationEntity?> FindRunningMutationAsync(
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        return await _db.RuntimeOperations
            .AsNoTracking()
            .Where(x =>
                (x.Operation == OperationName ||
                 x.Operation == CoturnPlatformInstallOperationService.OperationName ||
                 x.Operation == CoturnStartupSupervisionProcessor.OperationName) &&
                x.Status == "running" &&
                x.CompletedAtUtc == null &&
                (x.LockedUntilUtc == null || x.LockedUntilUtc > now))
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<CoturnRuntimeResponse> ValidatePreconditionsAsync(
        string action,
        CancellationToken cancellationToken)
    {
        var current = await _runtime.InspectAsync(cancellationToken);

        if (string.Equals(
                current.ProtectedEvidenceAccess,
                CoturnProtectedEvidenceAccess.Restricted,
                StringComparison.Ordinal))
        {
            throw new CoturnPlatformMaintenanceException(
                "coturn_maintenance_authority_required",
                "Protected Coturn maintenance is unavailable in this host-native execution mode. Switch to Containerized Development or a production-shaped runtime.",
                StatusCodes.Status409Conflict);
        }

        if (current.ContainerExists && !current.OwnershipVerified)
        {
            throw new CoturnPlatformMaintenanceException(
                "coturn_maintenance_ownership_conflict",
                "The Coturn container is not verified as MEM-owned. MEM will not restart, remove, or replace it.",
                StatusCodes.Status409Conflict);
        }

        if (action == CoturnPlatformMaintenanceActions.RestartVerify)
        {
            if (!current.ContainerExists)
            {
                throw new CoturnPlatformMaintenanceException(
                    "coturn_restart_not_deployed",
                    "Restart & Verify requires an existing MEM-owned Coturn runtime.",
                    StatusCodes.Status409Conflict);
            }

            if (!CoturnPlatformMaintenancePolicy.CanRestartAndVerify(current))
            {
                throw new CoturnPlatformMaintenanceException(
                    "coturn_restart_requires_safe_runtime",
                    "Restart & Verify requires a healthy owned Coturn service or restart-policy-only drift. Use Repair for other runtime or protected setup drift.",
                    StatusCodes.Status409Conflict);
            }

            return current;
        }

        // Repair is an operational boundary: it may recreate an owned or missing
        // Coturn container, but it may never pull an image. Inspection resolves
        // the approved immutable image locally; reject before accepting a
        // destructive operation if that local image is unavailable.
        if (string.IsNullOrWhiteSpace(current.ResolvedImageId))
        {
            throw new CoturnPlatformMaintenanceException(
                "coturn_repair_approved_image_unavailable",
                "The MEM-approved Coturn runtime image is not available locally. Use the installation boundary to provision the approved image before repairing the service.",
                StatusCodes.Status409Conflict);
        }

        return current;
    }

    private static string? NormalizeRepairExternalIp(string? value)
    {
        try
        {
            return CoturnRuntimePolicy.NormalizeExternalIp(value);
        }
        catch (InvalidOperationException ex)
        {
            throw new CoturnPlatformMaintenanceException(
                "coturn_external_ip_invalid",
                ex.Message,
                StatusCodes.Status400BadRequest);
        }
    }

    private static string RequireIdempotencyKey(string? rawKey)
    {
        var key = rawKey?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaximumIdempotencyKeyLength)
        {
            throw new CoturnPlatformMaintenanceException(
                "coturn_maintenance_idempotency_key_invalid",
                $"A Coturn maintenance idempotency key between 1 and {MaximumIdempotencyKeyLength} characters is required.",
                StatusCodes.Status400BadRequest);
        }

        return key;
    }

    private static string BuildDurableIdempotencyKey(
        string action,
        string clientIdempotencyKey) =>
        $"coturn-maintenance:{action}:{clientIdempotencyKey}";

    private static string ComputeRequestHash(
        string action,
        string? externalIp)
    {
        var canonical = $"{action}\n{externalIp ?? "<automatic>"}\n";
        return "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static void EnsureMatchingReplay(
        string? inputJson,
        string requestHash)
    {
        var input = DeserializeInput(inputJson);
        if (input is null || !FixedEquals(input.RequestHash, requestHash))
        {
            throw new CoturnPlatformMaintenanceException(
                "coturn_maintenance_idempotency_conflict",
                "The Coturn maintenance idempotency key was already used for a different request.",
                StatusCodes.Status409Conflict);
        }
    }

    private static CoturnPlatformMaintenanceOperationInput? DeserializeInput(
        string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CoturnPlatformMaintenanceOperationInput>(
                inputJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool FixedEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToNullableDateTimeOffset(DateTime? value) =>
        value.HasValue ? ToDateTimeOffset(value.Value) : null;

    private static CoturnPlatformMaintenanceAcceptedResponse Accepted(
        Guid operationId,
        string action,
        bool reusedExistingOperation)
    {
        var pollUrl = $"/internal/host-agent/operations/{operationId:D}";
        return new CoturnPlatformMaintenanceAcceptedResponse(
            operationId,
            action,
            Status: "accepted",
            PollUrl: pollUrl,
            ReusedExistingOperation: reusedExistingOperation);
    }

    private sealed record CoturnPlatformMaintenanceOperationInput(
        string Action,
        string RequestHash,
        bool ExternalIpConfigured,
        string? PreflightContainerId,
        DateTimeOffset? PreflightStartedAtUtc,
        long? PreflightRestartCount,
        bool PreflightRuntimeExact,
        bool RequestAbortCancelsMutation,
        bool FunctionalVerificationRequired);
}
