using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostAgent.Matrix.Federation;
using HostAgent.Matrix.Runtime;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Operations;
using Microsoft.Extensions.Logging;
using Modules.Auth.Services.Identity;

namespace HostAgent.Runtime.Stacks.Turn;

internal sealed record RuntimeStackTurnDisconnectOperationInput(
    string RequestHash,
    string ReviewHash,
    string BeforeConfigSha256,
    string BeforeMetadataSha256);

public interface IRuntimeStackTurnDisconnectionService
{
    Task<RuntimeStackTurnDisconnectReviewResponse?> ReviewAsync(
        string slugOrId,
        CancellationToken ct);

    Task<RuntimeStackTurnDisconnectResponse?> DisconnectAsync(
        string slugOrId,
        RuntimeStackTurnDisconnectRequest request,
        string requestedBy,
        Guid? actorOperatorId,
        CancellationToken requestCancellation);
}

public sealed class RuntimeStackTurnDisconnectionService
    : IRuntimeStackTurnDisconnectionService
{
    public const string OperationName = "disconnect-stack-turn";
    private static readonly TimeSpan ApplyTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RollbackTimeout = TimeSpan.FromMinutes(3);

    private readonly RuntimeStackManifestStore _manifestStore;
    private readonly IRuntimeStackTurnInspectionService _inspectionService;
    private readonly SynapseTurnConfigReader _reader;
    private readonly RuntimeStackTurnConfigTransaction _configTransaction;
    private readonly IRuntimeOperationStore _operations;
    private readonly RuntimeStackTurnMutationLock _mutationLock;
    private readonly IRuntimeMatrixContainerLifecycleService _matrixLifecycle;
    private readonly ISynapseFederationConfigCandidateValidator _candidateValidator;
    private readonly IMemOperatorAuditService _audit;
    private readonly ILogger<RuntimeStackTurnDisconnectionService> _logger;

    public RuntimeStackTurnDisconnectionService(
        RuntimeStackManifestStore manifestStore,
        IRuntimeStackTurnInspectionService inspectionService,
        SynapseTurnConfigReader reader,
        RuntimeStackTurnConfigTransaction configTransaction,
        IRuntimeOperationStore operations,
        RuntimeStackTurnMutationLock mutationLock,
        IRuntimeMatrixContainerLifecycleService matrixLifecycle,
        ISynapseFederationConfigCandidateValidator candidateValidator,
        IMemOperatorAuditService audit,
        ILogger<RuntimeStackTurnDisconnectionService> logger)
    {
        _manifestStore = manifestStore;
        _inspectionService = inspectionService;
        _reader = reader;
        _configTransaction = configTransaction;
        _operations = operations;
        _mutationLock = mutationLock;
        _matrixLifecycle = matrixLifecycle;
        _candidateValidator = candidateValidator;
        _audit = audit;
        _logger = logger;
    }

    public async Task<RuntimeStackTurnDisconnectReviewResponse?> ReviewAsync(
        string slugOrId,
        CancellationToken ct)
    {
        var manifest = await _manifestStore.FindAsync(slugOrId, ct);
        if (manifest is null)
        {
            return null;
        }

        var inspection = await RequireInspectionAsync(manifest, ct);
        var plan = RuntimeStackTurnDisconnectPlanner.Plan(inspection);
        var live = inspection.LiveConfiguration;
        var reviewHash = ComputeReviewHash(manifest, inspection);
        IReadOnlyList<string> consequences = plan.RestartRequired
            ?
            [
                "Only the verified MEM-managed TURN settings will be removed.",
                "The Matrix container will restart.",
                "Voice and video may be less reliable behind restrictive NAT or firewalls.",
                "Matrix identity, users, rooms, messages, and media remain unchanged."
            ]
            :
            [
                "No Synapse configuration change or Matrix restart is required."
            ];

        return new RuntimeStackTurnDisconnectReviewResponse(
            Source: "control-plane",
            Status: plan.ConfigurationChangeRequired
                ? RuntimeStackTurnDisconnectStatuses.Ready
                : RuntimeStackTurnDisconnectStatuses.NoChange,
            RuntimeStackId: manifest.StackId,
            Slug: manifest.Slug,
            MatrixServerName: manifest.Matrix.ServerName ?? manifest.Matrix.PublicHost ?? manifest.Slug,
            ConfigurationChangeRequired: plan.ConfigurationChangeRequired,
            RestartRequired: plan.RestartRequired,
            PlatformPublicHost: live?.PublicHost,
            TurnUris: live?.TurnUris ?? [],
            CurrentConfigurationSha256: live?.FileSha256 ?? "unavailable",
            ReviewHash: reviewHash,
            ConfirmationText: plan.RestartRequired
                ? $"Disconnect {manifest.Slug} from MEM-managed TURN. Synapse will restart and active calls may be interrupted."
                : $"{manifest.Slug} is already disconnected from TURN.",
            Consequences: consequences);
    }

    public async Task<RuntimeStackTurnDisconnectResponse?> DisconnectAsync(
        string slugOrId,
        RuntimeStackTurnDisconnectRequest request,
        string requestedBy,
        Guid? actorOperatorId,
        CancellationToken requestCancellation)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmDisconnectFromPlatformTurn)
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_disconnect_confirmation_required",
                "Confirm that this stack should be disconnected from the MEM-managed platform TURN service.");
        }

        var reviewHash = RequireToken(
            request.ReviewHash,
            "turn_disconnect_review_hash_required");
        var idempotencyKey = RequireToken(
            request.IdempotencyKey,
            "turn_disconnect_idempotency_key_required");

        var manifest = await _manifestStore.FindAsync(slugOrId, requestCancellation);
        if (manifest is null)
        {
            return null;
        }

        await using var heldLock = await _mutationLock.TryAcquireAsync(
            manifest.StackId,
            requestCancellation);
        if (heldLock is null)
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_disconnect_operation_in_progress",
                "Another TURN operation is already starting for this stack.");
        }

        var requestHash = ComputeRequestHash(reviewHash);
        var replay = await _operations.FindByIdempotencyKeyAsync(
            manifest.StackId,
            OperationName,
            idempotencyKey,
            requestCancellation);
        if (replay is not null)
        {
            return Replay(replay, requestHash, manifest);
        }

        var inspection = await RequireInspectionAsync(manifest, requestCancellation);
        var plan = RuntimeStackTurnDisconnectPlanner.Plan(inspection);
        if (!plan.ConfigurationChangeRequired)
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_disconnect_no_change",
                "This stack is already disconnected from TURN.");
        }

        var expectedReviewHash = ComputeReviewHash(manifest, inspection);
        if (!FixedEquals(expectedReviewHash, reviewHash))
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_disconnect_review_stale",
                "The stack TURN configuration changed after review. Review the disconnection again.");
        }

        var conflict = await _operations.FindActiveMutatingOperationForStackAsync(
            manifest.StackId,
            requestCancellation);
        if (conflict is not null)
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_disconnect_operation_in_progress",
                "Another mutating Runtime Operation is active for this stack.");
        }

        var container = await _matrixLifecycle.InspectAsync(
            manifest.Matrix,
            requestCancellation);
        EnsureSafeContainer(container);

        var previousMetadata = manifest.Matrix.RuntimeMetadata.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        var beforeMetadataHash = HashMetadata(previousMetadata);
        var beforeConfigHash = inspection.LiveConfiguration?.FileSha256 ?? "unavailable";
        var operationId = await _operations.StartAsync(
            manifest.StackId,
            OperationName,
            idempotencyKey,
            string.IsNullOrWhiteSpace(requestedBy)
                ? "control-plane-operator"
                : requestedBy.Trim(),
            "filesystem,docker",
            new RuntimeStackTurnDisconnectOperationInput(
                requestHash,
                reviewHash,
                beforeConfigHash,
                beforeMetadataHash),
            requestCancellation);

        using var timeout = new CancellationTokenSource(ApplyTimeout);
        var ct = timeout.Token;
        RuntimeStackTurnConfigSnapshot? snapshot = null;
        var configurationChanged = false;
        var matrixRestarted = false;
        var metadataChanged = false;
        var failureCode = "turn_disconnect_operation_failed";

        try
        {
            failureCode = "turn_disconnect_snapshot_failed";
            await StepAsync(operationId, "snapshotting-current-state", ct);
            snapshot = await _configTransaction.CreateDisconnectSnapshotAsync(
                manifest,
                operationId,
                ct);

            failureCode = "turn_disconnect_candidate_validation_failed";
            await StepAsync(operationId, "validating-candidate-config", ct);
            var validation = await _candidateValidator.ValidateAsync(
                manifest,
                container,
                snapshot.CandidateConfigPath,
                ct);
            if (!validation.Valid)
            {
                var rejected = Outcome(
                    RuntimeStackTurnDisconnectStatuses.CandidateRejected,
                    operationId,
                    manifest,
                    configurationChanged: false,
                    matrixRestarted: false,
                    rollbackAttempted: false,
                    rollbackSucceeded: null,
                    stateAfter: inspection.State,
                    errorCode: "turn_disconnect_candidate_validation_failed",
                    detail: "The active Synapse image rejected the TURN removal candidate before mutation.");
                await _operations.FailAsync(
                    operationId,
                    "candidate-rejected",
                    rejected.ErrorCode!,
                    rejected,
                    new
                    {
                        snapshot.BeforeConfigSha256,
                        snapshot.CandidateConfigSha256,
                        validation.ExitCode,
                        validation.Image,
                        validation.ImagePinned
                    },
                    ct);
                await TryAuditAsync(
                    "runtime.turn.disconnect.failed",
                    "failed",
                    operationId,
                    actorOperatorId,
                    ct);
                return rejected;
            }

            failureCode = "turn_disconnect_review_stale";
            await StepAsync(operationId, "validating-review-before-mutation", ct);
            var observed = await _reader.ReadAsync(snapshot.ActiveConfigPath, ct);
            if (!FixedEquals(observed.FileSha256, snapshot.BeforeConfigSha256) ||
                !observed.Supported ||
                !observed.AnyTurnSettings ||
                !observed.MemManagedMarkerPresent)
            {
                throw new RuntimeStackTurnConnectionException(
                    "turn_disconnect_review_stale",
                    "The active Synapse TURN configuration changed after review and before mutation.");
            }

            failureCode = "turn_disconnect_config_replace_failed";
            await StepAsync(operationId, "replacing-active-config", ct);
            await RuntimeStackTurnConfigTransaction.ReplaceForDisconnectAsync(
                snapshot.CandidateConfigPath,
                snapshot.ActiveConfigPath,
                snapshot.BeforeConfigSha256,
                ct);
            configurationChanged = true;

            failureCode = "turn_disconnect_restart_failed";
            await StepAsync(operationId, "restarting-matrix", ct);
            await _matrixLifecycle.RestartAsync(manifest.Matrix, ct);
            matrixRestarted = true;

            failureCode = "turn_disconnect_effective_state_mismatch";
            await StepAsync(operationId, "verifying-live-turn-removal", ct);
            var effective = await _reader.ReadAsync(snapshot.ActiveConfigPath, ct);
            EnsureDisconnected(effective);

            failureCode = "turn_disconnect_manifest_update_failed";
            await StepAsync(operationId, "recording-disconnected-state", ct);
            var disconnectedMetadata = BuildDisconnectedMetadata(
                previousMetadata,
                effective,
                operationId);
            var updated = await _manifestStore.ReplaceMatrixRuntimeMetadataAsync(
                manifest.StackId,
                disconnectedMetadata,
                previousMetadata,
                ct);
            if (updated is null)
            {
                throw new RuntimeStackTurnConnectionException(
                    "turn_disconnect_stack_not_found",
                    "The Runtime Stack disappeared while the disconnected state was being recorded.");
            }
            metadataChanged = true;

            failureCode = "turn_disconnect_final_verification_failed";
            await StepAsync(operationId, "verifying-disconnected-state", ct);
            var finalInspection = await _inspectionService.InspectAsync(
                manifest.StackId.ToString("D"),
                ct);
            if (finalInspection is null ||
                !string.Equals(
                    finalInspection.State,
                    RuntimeStackTurnStates.NotConnected,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    finalInspection.Management,
                    RuntimeStackTurnManagementKinds.None,
                    StringComparison.Ordinal))
            {
                throw new RuntimeStackTurnConnectionException(
                    "turn_disconnect_final_verification_failed",
                    "The final stack TURN state could not be verified as disconnected.");
            }

            var succeeded = Outcome(
                RuntimeStackTurnDisconnectStatuses.Succeeded,
                operationId,
                manifest,
                configurationChanged,
                matrixRestarted,
                rollbackAttempted: false,
                rollbackSucceeded: null,
                stateAfter: finalInspection.State,
                errorCode: null,
                detail: "The MEM-managed TURN settings were removed and Matrix returned to a verified running state.");
            await _operations.CompleteAsync(
                operationId,
                RuntimeStackTurnDisconnectStatuses.Succeeded,
                "completed",
                succeeded,
                new
                {
                    beforeConfigSha256 = beforeConfigHash,
                    afterConfigSha256 = finalInspection.LiveConfiguration?.FileSha256,
                    beforeMetadataSha256 = beforeMetadataHash,
                    matrixRestarted
                },
                ct);
            await TryAuditAsync(
                "runtime.turn.disconnect.succeeded",
                "succeeded",
                operationId,
                actorOperatorId,
                ct);
            return succeeded;
        }
        catch (Exception ex) when (configurationChanged || metadataChanged)
        {
            _logger.LogWarning(
                ex,
                "Stack TURN disconnection failed after mutation; rollback will be attempted. StackId={StackId} OperationId={OperationId}",
                manifest.StackId,
                operationId);
            return await RollbackAsync(
                manifest,
                operationId,
                snapshot,
                previousMetadata,
                configurationChanged,
                metadataChanged,
                failureCode,
                ex,
                actorOperatorId);
        }
        catch (Exception ex)
        {
            var code = ex is RuntimeStackTurnConnectionException known
                ? known.Code
                : ex is OperationCanceledException
                    ? "turn_disconnect_operation_timeout"
                    : failureCode;
            var failed = Outcome(
                RuntimeStackTurnDisconnectStatuses.Failed,
                operationId,
                manifest,
                configurationChanged: false,
                matrixRestarted: false,
                rollbackAttempted: false,
                rollbackSucceeded: null,
                stateAfter: inspection.State,
                errorCode: code,
                detail: "The stack TURN disconnection was not applied and no active configuration mutation was confirmed.");
            await TryFailAsync(
                operationId,
                "failed-before-mutation",
                code,
                failed,
                ex);
            await TryAuditAsync(
                "runtime.turn.disconnect.failed",
                "failed",
                operationId,
                actorOperatorId,
                CancellationToken.None);
            return failed;
        }
    }

    private async Task<RuntimeStackTurnDisconnectResponse> RollbackAsync(
        RuntimeStackManifest manifest,
        Guid operationId,
        RuntimeStackTurnConfigSnapshot? snapshot,
        IReadOnlyDictionary<string, string?> previousMetadata,
        bool configurationChanged,
        bool metadataChanged,
        string applyErrorCode,
        Exception applyError,
        Guid? actorOperatorId)
    {
        using var timeout = new CancellationTokenSource(RollbackTimeout);
        var ct = timeout.Token;
        var restarted = false;

        try
        {
            await TryStepAsync(operationId, "rollback-started", ct);

            if (configurationChanged)
            {
                if (snapshot is null)
                {
                    throw new InvalidOperationException(
                        "The TURN configuration snapshot is unavailable for rollback.");
                }

                await TryStepAsync(operationId, "restoring-synapse-config", ct);
                await RuntimeStackTurnConfigTransaction.ReplaceForDisconnectAsync(
                    snapshot.BeforeConfigPath,
                    snapshot.ActiveConfigPath,
                    expectedActiveSha256: null,
                    ct);
                await TryStepAsync(operationId, "restarting-matrix-after-restore", ct);
                await _matrixLifecycle.RestartAsync(manifest.Matrix, ct);
                restarted = true;
                await TryStepAsync(operationId, "verifying-rollback", ct);
                var restoredBytes = await File.ReadAllBytesAsync(
                    snapshot.ActiveConfigPath,
                    ct);
                if (!FixedEquals(
                        RuntimeStackTurnConfigTransaction.Hash(restoredBytes),
                        snapshot.BeforeConfigSha256))
                {
                    throw new InvalidOperationException(
                        "The exact previous Synapse configuration could not be verified after rollback.");
                }
            }

            if (metadataChanged)
            {
                await TryStepAsync(operationId, "restoring-stack-metadata", ct);
                var restoredManifest = await _manifestStore.ReplaceMatrixRuntimeMetadataAsync(
                    manifest.StackId,
                    previousMetadata,
                    ct);
                if (restoredManifest is null)
                {
                    throw new InvalidOperationException(
                        "The previous Runtime Stack metadata could not be restored.");
                }
            }

            var rolledBack = Outcome(
                RuntimeStackTurnDisconnectStatuses.RolledBack,
                operationId,
                manifest,
                configurationChanged,
                restarted,
                rollbackAttempted: true,
                rollbackSucceeded: true,
                stateAfter: RuntimeStackTurnStates.Connected,
                errorCode: applyErrorCode,
                detail: "The TURN disconnection was not completed. MEM restored and verified the previous stack state.");
            await _operations.CompleteAsync(
                operationId,
                RuntimeStackTurnDisconnectStatuses.RolledBack,
                "rolled-back",
                rolledBack,
                new
                {
                    applyErrorCode,
                    applyError = applyError.GetType().Name,
                    configurationChanged,
                    metadataChanged
                },
                ct);
            await TryAuditAsync(
                "runtime.turn.disconnect.rolled_back",
                "rolled_back",
                operationId,
                actorOperatorId,
                ct);
            return rolledBack;
        }
        catch (Exception rollbackError)
        {
            var unresolved = Outcome(
                RuntimeStackTurnDisconnectStatuses.Unresolved,
                operationId,
                manifest,
                configurationChanged,
                restarted,
                rollbackAttempted: true,
                rollbackSucceeded: false,
                stateAfter: null,
                errorCode: "turn_disconnect_rollback_failed",
                detail: "MEM could not restore a verified previous state automatically. The private operation snapshots were retained for recovery.");
            await TryFailAsync(
                operationId,
                "rollback-failed",
                unresolved.ErrorCode!,
                unresolved,
                rollbackError);
            await TryAuditAsync(
                "runtime.turn.disconnect.failed",
                "failed",
                operationId,
                actorOperatorId,
                CancellationToken.None);
            return unresolved;
        }
    }

    private async Task<RuntimeStackTurnInspectionResponse> RequireInspectionAsync(
        RuntimeStackManifest manifest,
        CancellationToken ct) =>
        await _inspectionService.InspectAsync(
            manifest.StackId.ToString("D"),
            ct)
        ?? throw new RuntimeStackTurnConnectionException(
            "turn_disconnect_state_unavailable",
            "The current stack TURN state could not be inspected.");

    private static void EnsureSafeContainer(
        RuntimeMatrixContainerObservation observation)
    {
        if (!observation.Running ||
            !observation.IdentityMatches ||
            !observation.DataBindMatches ||
            !observation.ExpectedNetworkAttached ||
            observation.DirectHostPortExposed)
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_disconnect_matrix_runtime_unsafe",
                "The active Matrix container does not match the safe Runtime Stack ownership boundary.");
        }
    }

    private static void EnsureDisconnected(SynapseTurnConfigReadResult live)
    {
        if (!live.Supported || live.AnyTurnSettings)
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_disconnect_effective_state_mismatch",
                "The effective Synapse configuration still contains TURN settings after the reviewed removal.");
        }
    }

    private static IReadOnlyDictionary<string, string?> BuildDisconnectedMetadata(
        IReadOnlyDictionary<string, string?> previous,
        SynapseTurnConfigReadResult effective,
        Guid operationId)
    {
        var metadata = previous.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);

        foreach (var key in new[]
        {
            "turnPublicHost",
            "turnRealm",
            "turnUris",
            "turnRelayPortsPublished",
            "turnSharedSecretPresent",
            "turnUserLifetime",
            "turnAllowGuests"
        })
        {
            metadata.Remove(key);
        }

        metadata["turnConfigured"] = "false";
        metadata["turnConfigurationSource"] = "operator-disconnected";
        metadata["turnManagement"] = RuntimeStackTurnManagementKinds.None;
        metadata["turnConfigurationSha256"] = effective.FileSha256;
        metadata["turnLastOperationId"] = operationId.ToString("D");
        metadata["turnLastOperationMode"] = "disconnect";
        metadata["turnLastInspectedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");

        return metadata;
    }

    private static string ComputeReviewHash(
        RuntimeStackManifest manifest,
        RuntimeStackTurnInspectionResponse inspection) =>
        Hash(string.Join(
            "\n",
            manifest.StackId.ToString("D"),
            inspection.State,
            inspection.Management,
            inspection.LiveConfiguration?.FileSha256 ?? "unavailable",
            HashMetadata(manifest.Matrix.RuntimeMetadata)));

    private static string ComputeRequestHash(string reviewHash) =>
        Hash(string.Join("\n", "disconnect", reviewHash, "confirmed"));

    private static string HashMetadata(
        IReadOnlyDictionary<string, string?> metadata) =>
        Hash(string.Join(
            "\n",
            metadata
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}")));

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string RequireToken(string? value, string code)
    {
        var token = value?.Trim();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256)
        {
            throw new RuntimeStackTurnConnectionException(
                code,
                "The reviewed request token is missing or invalid.");
        }

        return token;
    }

    private static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static RuntimeStackTurnDisconnectResponse Replay(
        RuntimeOperationDetail replay,
        string requestHash,
        RuntimeStackManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(replay.InputJson))
        {
            using var input = JsonDocument.Parse(replay.InputJson);
            var observedHash = input.RootElement.TryGetProperty("requestHash", out var hash)
                ? hash.GetString()
                : null;
            if (!FixedEquals(observedHash, requestHash))
            {
                throw new RuntimeStackTurnConnectionException(
                    "turn_disconnect_idempotency_conflict",
                    "The idempotency key was already used with a different TURN disconnection request.");
            }
        }

        if (!string.IsNullOrWhiteSpace(replay.ResultJson))
        {
            var result = JsonSerializer.Deserialize<RuntimeStackTurnDisconnectResponse>(
                replay.ResultJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (result is not null)
            {
                return result;
            }
        }

        return Outcome(
            replay.Status == "running"
                ? RuntimeStackTurnDisconnectStatuses.Running
                : RuntimeStackTurnDisconnectStatuses.Failed,
            replay.Id,
            manifest,
            configurationChanged: false,
            matrixRestarted: false,
            rollbackAttempted: false,
            rollbackSucceeded: null,
            stateAfter: null,
            errorCode: replay.LastError,
            detail: replay.Status == "running"
                ? "The TURN disconnection operation is still running."
                : "The existing TURN disconnection outcome could not be reconstructed safely.");
    }

    private static RuntimeStackTurnDisconnectResponse Outcome(
        string status,
        Guid operationId,
        RuntimeStackManifest manifest,
        bool configurationChanged,
        bool matrixRestarted,
        bool rollbackAttempted,
        bool? rollbackSucceeded,
        string? stateAfter,
        string? errorCode,
        string detail) =>
        new(
            Source: "control-plane",
            Status: status,
            OperationId: operationId,
            RuntimeStackId: manifest.StackId,
            Slug: manifest.Slug,
            ConfigurationChanged: configurationChanged,
            MatrixRestarted: matrixRestarted,
            RollbackAttempted: rollbackAttempted,
            RollbackSucceeded: rollbackSucceeded,
            StateAfter: stateAfter,
            ErrorCode: errorCode,
            Detail: detail);

    private async Task StepAsync(
        Guid operationId,
        string step,
        CancellationToken ct) =>
        await _operations.UpdateStepAsync(operationId, step, ct);

    private async Task TryStepAsync(
        Guid operationId,
        string step,
        CancellationToken ct)
    {
        try
        {
            await _operations.UpdateStepAsync(operationId, step, ct);
        }
        catch
        {
            // Best-effort evidence update only.
        }
    }

    private async Task TryFailAsync(
        Guid operationId,
        string step,
        string code,
        RuntimeStackTurnDisconnectResponse result,
        Exception error)
    {
        try
        {
            await _operations.FailAsync(
                operationId,
                step,
                code,
                result,
                new { error = error.GetType().Name },
                CancellationToken.None);
        }
        catch
        {
            // Preserve the original operation outcome.
        }
    }

    private async Task TryAuditAsync(
        string eventType,
        string outcome,
        Guid operationId,
        Guid? actorOperatorId,
        CancellationToken ct)
    {
        try
        {
            await _audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: eventType,
                    Outcome: outcome,
                    ActorOperatorId: actorOperatorId,
                    CorrelationId: operationId.ToString("D"),
                    ReasonCode: "runtime_stack_turn_disconnection"),
                ct);
        }
        catch
        {
            // The durable Runtime Operation remains authoritative.
        }
    }
}
