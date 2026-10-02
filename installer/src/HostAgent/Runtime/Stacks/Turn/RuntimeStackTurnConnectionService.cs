using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostAgent.Matrix.Federation;
using HostAgent.Matrix.Runtime;
using HostAgent.Runtime.Coturn;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Operations;
using Modules.Auth.Services.Identity;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Stacks.Turn;

internal sealed record RuntimeStackTurnConnectOperationInput(
    string RequestHash,
    string ReviewHash,
    string Mode,
    string BeforeConfigSha256,
    string BeforeMetadataSha256);

public interface IRuntimeStackTurnConnectionService
{
    Task<RuntimeStackTurnConnectReviewResponse?> ReviewAsync(
        string slugOrId,
        CancellationToken ct);

    Task<RuntimeStackTurnConnectResponse?> ConnectAsync(
        string slugOrId,
        RuntimeStackTurnConnectRequest request,
        string requestedBy,
        Guid? actorOperatorId,
        CancellationToken requestCancellation);
}

public sealed class RuntimeStackTurnMutationLock
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        Guid runtimeStackId,
        CancellationToken ct)
    {
        var semaphore = _locks.GetOrAdd(runtimeStackId, _ => new SemaphoreSlim(1, 1));
        if (!await semaphore.WaitAsync(TimeSpan.Zero, ct))
        {
            return null;
        }

        return new Releaser(semaphore);
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _released;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}

public sealed class RuntimeStackTurnConnectionService
    : IRuntimeStackTurnConnectionService
{
    public const string OperationName = "connect-stack-turn";
    private static readonly TimeSpan ApplyTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RollbackTimeout = TimeSpan.FromMinutes(3);

    private readonly RuntimeStackManifestStore _manifestStore;
    private readonly IRuntimeStackTurnInspectionService _inspectionService;
    private readonly IRuntimeStackTurnPlatformConfigurationProvider _platformConfiguration;
    private readonly SynapseTurnConfigReader _reader;
    private readonly RuntimeStackTurnConfigTransaction _configTransaction;
    private readonly IRuntimeOperationStore _operations;
    private readonly RuntimeStackTurnMutationLock _mutationLock;
    private readonly IRuntimeMatrixContainerLifecycleService _matrixLifecycle;
    private readonly ISynapseFederationConfigCandidateValidator _candidateValidator;
    private readonly IMemOperatorAuditService _audit;
    private readonly ILogger<RuntimeStackTurnConnectionService> _logger;

    public RuntimeStackTurnConnectionService(
        RuntimeStackManifestStore manifestStore,
        IRuntimeStackTurnInspectionService inspectionService,
        IRuntimeStackTurnPlatformConfigurationProvider platformConfiguration,
        SynapseTurnConfigReader reader,
        RuntimeStackTurnConfigTransaction configTransaction,
        IRuntimeOperationStore operations,
        RuntimeStackTurnMutationLock mutationLock,
        IRuntimeMatrixContainerLifecycleService matrixLifecycle,
        ISynapseFederationConfigCandidateValidator candidateValidator,
        IMemOperatorAuditService audit,
        ILogger<RuntimeStackTurnConnectionService> logger)
    {
        _manifestStore = manifestStore;
        _inspectionService = inspectionService;
        _platformConfiguration = platformConfiguration;
        _reader = reader;
        _configTransaction = configTransaction;
        _operations = operations;
        _mutationLock = mutationLock;
        _matrixLifecycle = matrixLifecycle;
        _candidateValidator = candidateValidator;
        _audit = audit;
        _logger = logger;
    }

    public async Task<RuntimeStackTurnConnectReviewResponse?> ReviewAsync(
        string slugOrId,
        CancellationToken ct)
    {
        var manifest = await _manifestStore.FindAsync(slugOrId, ct);
        if (manifest is null)
        {
            return null;
        }

        var inspection = await RequireInspectionAsync(manifest, ct);
        var plan = RuntimeStackTurnConnectPlanner.Plan(inspection);
        var platform = await RequirePlatformConfigurationAsync(ct);
        var reviewHash = ComputeReviewHash(manifest, inspection, platform, plan.Mode);

        return new RuntimeStackTurnConnectReviewResponse(
            Source: "control-plane",
            Status: plan.Mode == RuntimeStackTurnConnectModes.NoChange
                ? RuntimeStackTurnConnectStatuses.NoChange
                : RuntimeStackTurnConnectStatuses.Ready,
            RuntimeStackId: manifest.StackId,
            Slug: manifest.Slug,
            MatrixServerName: manifest.Matrix.ServerName ?? manifest.Matrix.PublicHost ?? manifest.Slug,
            Mode: plan.Mode,
            ConfigurationChangeRequired: plan.ConfigurationChangeRequired,
            RestartRequired: plan.RestartRequired,
            PlatformPublicHost: platform.PublicHost,
            TurnUris: platform.TurnUris,
            UserLifetime: platform.UserLifetime,
            AllowGuests: platform.AllowGuests,
            CurrentConfigurationSha256: inspection.LiveConfiguration?.FileSha256 ?? "unavailable",
            ReviewHash: reviewHash,
            ConfirmationText: ConnectConfirmationText(manifest, platform, plan.Mode),
            Consequences: ConnectConsequences(plan.Mode));
    }

    public async Task<RuntimeStackTurnConnectResponse?> ConnectAsync(
        string slugOrId,
        RuntimeStackTurnConnectRequest request,
        string requestedBy,
        Guid? actorOperatorId,
        CancellationToken requestCancellation)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmConnectToPlatformTurn)
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_connect_confirmation_required",
                "Confirm that this stack should be connected to the MEM-managed platform TURN service.");
        }

        var reviewHash = RequireToken(
            request.ReviewHash,
            "turn_connect_review_hash_required");
        var idempotencyKey = RequireToken(
            request.IdempotencyKey,
            "turn_connect_idempotency_key_required");

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
                "turn_connect_operation_in_progress",
                "Another TURN connection operation is already starting for this stack.");
        }

        var requestHash = ComputeRequestHash(reviewHash, request.ConfirmReplaceExternalTurn);
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
        var plan = RuntimeStackTurnConnectPlanner.Plan(inspection);
        if (plan.Mode == RuntimeStackTurnConnectModes.NoChange)
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_connect_no_change",
                "This stack is already connected to MEM-managed TURN.");
        }

        if (plan.Mode == RuntimeStackTurnConnectModes.ReplaceExternal &&
            !request.ConfirmReplaceExternalTurn)
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_connect_external_replacement_confirmation_required",
                "Replacing an external TURN configuration requires the explicit external-replacement confirmation from the reviewed operation.");
        }

        var platform = await RequirePlatformConfigurationAsync(requestCancellation);
        var expectedReviewHash = ComputeReviewHash(
            manifest,
            inspection,
            platform,
            plan.Mode);
        if (!FixedEquals(expectedReviewHash, reviewHash))
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_connect_review_stale",
                "The stack or platform TURN configuration changed after review. Review the connection again.");
        }

        var conflict = await _operations.FindActiveMutatingOperationForStackAsync(
            manifest.StackId,
            requestCancellation);
        if (conflict is not null)
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_connect_operation_in_progress",
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
            plan.ConfigurationChangeRequired ? "filesystem,docker" : "filesystem",
            new RuntimeStackTurnConnectOperationInput(
                requestHash,
                reviewHash,
                plan.Mode,
                beforeConfigHash,
                beforeMetadataHash),
            requestCancellation);

        using var timeout = new CancellationTokenSource(ApplyTimeout);
        var ct = timeout.Token;
        RuntimeStackTurnConfigSnapshot? snapshot = null;
        var configurationChanged = false;
        var matrixRestarted = false;
        var metadataChanged = false;
        var failureCode = "turn_connect_operation_failed";

        try
        {
            if (plan.ConfigurationChangeRequired)
            {
                failureCode = "turn_connect_snapshot_failed";
                await StepAsync(operationId, "snapshotting-current-state", ct);
                snapshot = plan.Mode == RuntimeStackTurnConnectModes.ReplaceExternal
                    ? await _configTransaction.CreateReplaceExternalSnapshotAsync(
                        manifest,
                        operationId,
                        platform,
                        beforeConfigHash,
                        ct)
                    : await _configTransaction.CreateConnectSnapshotAsync(
                        manifest,
                        operationId,
                        platform,
                        ct);

                failureCode = "turn_connect_candidate_validation_failed";
                await StepAsync(operationId, "validating-candidate-config", ct);
                var validation = await _candidateValidator.ValidateAsync(
                    manifest,
                    container,
                    snapshot.CandidateConfigPath,
                    ct);
                if (!validation.Valid)
                {
                    var rejected = Outcome(
                        RuntimeStackTurnConnectStatuses.CandidateRejected,
                        operationId,
                        manifest,
                        plan.Mode,
                        configurationChanged: false,
                        matrixRestarted: false,
                        rollbackAttempted: false,
                        rollbackSucceeded: null,
                        stateAfter: inspection.State,
                        errorCode: "turn_connect_candidate_validation_failed",
                        detail: "The active Synapse image rejected the TURN configuration candidate before mutation.");
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
                        "runtime.turn.connect.failed",
                        "failed",
                        operationId,
                        actorOperatorId,
                        ct);
                    return rejected;
                }

                failureCode = "turn_connect_review_stale";
                await StepAsync(operationId, "validating-review-before-mutation", ct);
                var observed = await _reader.ReadAsync(snapshot.ActiveConfigPath, ct);
                if (!FixedEquals(observed.FileSha256, snapshot.BeforeConfigSha256))
                {
                    throw new RuntimeStackTurnConnectionException(
                        "turn_connect_review_stale",
                        "The active Synapse configuration changed after review and before mutation.");
                }

                failureCode = "turn_connect_config_replace_failed";
                await StepAsync(operationId, "replacing-active-config", ct);
                await RuntimeStackTurnConfigTransaction.ReplaceAsync(
                    snapshot.CandidateConfigPath,
                    snapshot.ActiveConfigPath,
                    snapshot.BeforeConfigSha256,
                    ct);
                configurationChanged = true;

                failureCode = "turn_connect_restart_failed";
                await StepAsync(operationId, "restarting-matrix", ct);
                await _matrixLifecycle.RestartAsync(manifest.Matrix, ct);
                matrixRestarted = true;

                failureCode = "turn_connect_effective_state_mismatch";
                await StepAsync(operationId, "verifying-live-turn-config", ct);
                var live = await _reader.ReadAsync(snapshot.ActiveConfigPath, ct);
                EnsureMatchesPlatform(live, platform, requireManagedMarker: true);
            }

            failureCode = "turn_connect_manifest_update_failed";
            await StepAsync(operationId, "recording-turn-association", ct);
            var activePath = RequireOwnedConfigPath(manifest.Matrix);
            var effective = await _reader.ReadAsync(activePath, ct);
            EnsureMatchesPlatform(
                effective,
                platform,
                requireManagedMarker: plan.Mode != RuntimeStackTurnConnectModes.AdoptExisting);
            var connectedMetadata = BuildConnectedMetadata(
                previousMetadata,
                effective,
                platform,
                operationId,
                plan.Mode);
            var updated = await _manifestStore.ReplaceMatrixRuntimeMetadataAsync(
                manifest.StackId,
                connectedMetadata,
                previousMetadata,
                ct);
            if (updated is null)
            {
                throw new RuntimeStackTurnConnectionException(
                    "turn_connect_stack_not_found",
                    "The Runtime Stack disappeared while the TURN association was being recorded.");
            }
            metadataChanged = true;

            failureCode = "turn_connect_final_verification_failed";
            await StepAsync(operationId, "verifying-connected-state", ct);
            var finalInspection = await _inspectionService.InspectAsync(
                manifest.StackId.ToString("D"),
                ct);
            if (finalInspection is null ||
                !string.Equals(
                    finalInspection.State,
                    RuntimeStackTurnStates.Connected,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    finalInspection.Management,
                    RuntimeStackTurnManagementKinds.MemManaged,
                    StringComparison.Ordinal))
            {
                throw new RuntimeStackTurnConnectionException(
                    "turn_connect_final_verification_failed",
                    "The final stack TURN state could not be verified as connected and MEM-managed.");
            }

            var succeeded = Outcome(
                RuntimeStackTurnConnectStatuses.Succeeded,
                operationId,
                manifest,
                plan.Mode,
                configurationChanged,
                matrixRestarted,
                rollbackAttempted: false,
                rollbackSucceeded: null,
                stateAfter: finalInspection.State,
                errorCode: null,
                detail: plan.Mode switch
                {
                    RuntimeStackTurnConnectModes.AdoptExisting =>
                        "The existing verified Synapse TURN configuration was recorded as the MEM-managed stack association without a restart.",
                    RuntimeStackTurnConnectModes.ReplaceExternal =>
                        "The reviewed external TURN configuration was replaced with MEM-managed platform TURN and Matrix returned to a verified running state.",
                    _ =>
                        "The stack was connected to the MEM-managed platform TURN service and Matrix returned to a verified running state."
                });
            await _operations.CompleteAsync(
                operationId,
                RuntimeStackTurnConnectStatuses.Succeeded,
                "completed",
                succeeded,
                new
                {
                    mode = plan.Mode,
                    beforeConfigSha256 = beforeConfigHash,
                    afterConfigSha256 = finalInspection.LiveConfiguration?.FileSha256,
                    beforeMetadataSha256 = beforeMetadataHash,
                    turnUris = platform.TurnUris,
                    matrixRestarted
                },
                ct);
            await TryAuditAsync(
                "runtime.turn.connect.succeeded",
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
                "Stack TURN connection failed after mutation; rollback will be attempted. StackId={StackId} OperationId={OperationId}",
                manifest.StackId,
                operationId);
            return await RollbackAsync(
                manifest,
                operationId,
                plan.Mode,
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
                    ? "turn_connect_operation_timeout"
                    : failureCode;
            var failed = Outcome(
                RuntimeStackTurnConnectStatuses.Failed,
                operationId,
                manifest,
                plan.Mode,
                configurationChanged: false,
                matrixRestarted: false,
                rollbackAttempted: false,
                rollbackSucceeded: null,
                stateAfter: inspection.State,
                errorCode: code,
                detail: "The stack TURN connection was not applied and no active configuration mutation was confirmed.");
            await TryFailAsync(
                operationId,
                "failed-before-mutation",
                code,
                failed,
                ex);
            await TryAuditAsync(
                "runtime.turn.connect.failed",
                "failed",
                operationId,
                actorOperatorId,
                CancellationToken.None);
            return failed;
        }
    }

    private async Task<RuntimeStackTurnConnectResponse> RollbackAsync(
        RuntimeStackManifest manifest,
        Guid operationId,
        string mode,
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
                await RuntimeStackTurnConfigTransaction.ReplaceAsync(
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
                RuntimeStackTurnConnectStatuses.RolledBack,
                operationId,
                manifest,
                mode,
                configurationChanged,
                restarted,
                rollbackAttempted: true,
                rollbackSucceeded: true,
                stateAfter: null,
                errorCode: applyErrorCode,
                detail: "The TURN connection was not completed. MEM restored and verified the previous stack state.");
            await _operations.CompleteAsync(
                operationId,
                RuntimeStackTurnConnectStatuses.RolledBack,
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
                "runtime.turn.connect.rolled_back",
                "rolled_back",
                operationId,
                actorOperatorId,
                ct);
            return rolledBack;
        }
        catch (Exception rollbackError)
        {
            var unresolved = Outcome(
                RuntimeStackTurnConnectStatuses.Unresolved,
                operationId,
                manifest,
                mode,
                configurationChanged,
                restarted,
                rollbackAttempted: true,
                rollbackSucceeded: false,
                stateAfter: null,
                errorCode: "turn_connect_rollback_failed",
                detail: "MEM could not restore a verified previous state automatically. The private operation snapshots were retained for recovery.");
            await TryFailAsync(
                operationId,
                "rollback-failed",
                unresolved.ErrorCode!,
                unresolved,
                rollbackError);
            await TryAuditAsync(
                "runtime.turn.connect.failed",
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
            "turn_connect_state_unavailable",
            "The current stack TURN state could not be inspected.");

    private async Task<CoturnSynapseConfig> RequirePlatformConfigurationAsync(
        CancellationToken ct) =>
        await _platformConfiguration.GetSynapseConfigAsync(ct)
        ?? throw new RuntimeStackTurnConnectionException(
            "turn_connect_platform_not_ready",
            "The platform TURN service is not ready to provide a safe Synapse configuration.");

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
                "turn_connect_matrix_runtime_unsafe",
                "The active Matrix container does not match the safe Runtime Stack ownership boundary.");
        }
    }

    private static void EnsureMatchesPlatform(
        SynapseTurnConfigReadResult live,
        CoturnSynapseConfig platform,
        bool requireManagedMarker)
    {
        if (!live.Supported ||
            !live.AnyTurnSettings ||
            (requireManagedMarker && !live.MemManagedMarkerPresent) ||
            !SetsEqual(live.TurnUris, platform.TurnUris) ||
            !string.Equals(
                live.CredentialMechanism,
                "inline-shared-secret",
                StringComparison.Ordinal) ||
            !FixedEquals(live.SharedSecretValue, platform.SharedSecret) ||
            !string.Equals(
                live.UserLifetime,
                platform.UserLifetime,
                StringComparison.Ordinal) ||
            live.AllowGuests != platform.AllowGuests)
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_connect_effective_state_mismatch",
                "The effective Synapse TURN configuration does not match the reviewed MEM platform configuration.");
        }
    }

    private static IReadOnlyDictionary<string, string?> BuildConnectedMetadata(
        IReadOnlyDictionary<string, string?> previous,
        SynapseTurnConfigReadResult effective,
        CoturnSynapseConfig platform,
        Guid operationId,
        string mode)
    {
        var metadata = previous.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        metadata["turnConfigured"] = "true";
        metadata["turnPublicHost"] = platform.PublicHost;
        metadata["turnRealm"] = platform.Realm;
        metadata["turnUris"] = string.Join(", ", platform.TurnUris);
        metadata["turnRelayPortsPublished"] = platform.RelayPortsPublished ? "true" : "false";
        metadata["turnSharedSecretPresent"] = effective.SharedSecretPresent ? "true" : "false";
        metadata["turnUserLifetime"] = effective.UserLifetime;
        metadata["turnAllowGuests"] = effective.AllowGuests.HasValue
            ? (effective.AllowGuests.Value ? "true" : "false")
            : null;
        metadata["turnConfigurationSource"] = "platform-coturn";
        metadata["turnManagement"] = RuntimeStackTurnManagementKinds.MemManaged;
        metadata["turnConfigurationSha256"] = effective.FileSha256;
        metadata["turnLastOperationId"] = operationId.ToString("D");
        metadata["turnLastOperationMode"] = mode;
        metadata["turnLastInspectedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");

        return metadata;
    }

    private static string RequireOwnedConfigPath(RuntimeStackServiceManifest matrix)
    {
        if (string.IsNullOrWhiteSpace(matrix.DataPath) ||
            string.IsNullOrWhiteSpace(matrix.ConfigPath))
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_connect_config_path_missing",
                "The Runtime Stack manifest does not contain the owned Synapse configuration path.");
        }

        var expected = Path.GetFullPath(Path.Combine(matrix.DataPath, "homeserver.yaml"));
        var actual = Path.GetFullPath(matrix.ConfigPath);
        if (!string.Equals(expected, actual, StringComparison.Ordinal) ||
            !File.Exists(actual))
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_connect_config_path_unowned",
                "The Synapse configuration is not present at the manifest-owned homeserver.yaml path.");
        }

        return actual;
    }

    private static string ComputeReviewHash(
        RuntimeStackManifest manifest,
        RuntimeStackTurnInspectionResponse inspection,
        CoturnSynapseConfig platform,
        string mode)
    {
        var metadataFingerprint = HashMetadata(manifest.Matrix.RuntimeMetadata);
        var platformFingerprint = Hash(
            string.Join(
                "\n",
                platform.PublicHost,
                platform.Realm,
                string.Join("\n", platform.TurnUris.Select(NormalizeUri).OrderBy(x => x, StringComparer.Ordinal)),
                RuntimeStackTurnConfigTransaction.HashText(platform.SharedSecret),
                platform.UserLifetime,
                platform.AllowGuests ? "true" : "false",
                platform.RelayPortsPublished ? "true" : "false"));

        return Hash(string.Join(
            "\n",
            manifest.StackId.ToString("D"),
            inspection.State,
            inspection.Management,
            mode,
            inspection.LiveConfiguration?.FileSha256 ?? "unavailable",
            metadataFingerprint,
            platformFingerprint));
    }

    private static string ConnectConfirmationText(
        RuntimeStackManifest manifest,
        CoturnSynapseConfig platform,
        string mode) =>
        mode switch
        {
            RuntimeStackTurnConnectModes.AdoptExisting =>
                $"Record the existing verified {platform.PublicHost} TURN configuration as the MEM-managed association for {manifest.Slug}.",
            RuntimeStackTurnConnectModes.ReplaceExternal =>
                $"Replace the reviewed external TURN configuration for {manifest.Slug} with {platform.PublicHost}. Synapse will restart and active calls may be interrupted.",
            _ =>
                $"Connect {manifest.Slug} to {platform.PublicHost}. Synapse will restart and active calls may be interrupted."
        };

    private static IReadOnlyList<string> ConnectConsequences(string mode) =>
        mode switch
        {
            RuntimeStackTurnConnectModes.AdoptExisting =>
            [
                "The live Synapse configuration will not be rewritten.",
                "The Matrix container will not restart.",
                "MEM will record the already matching platform TURN association.",
                "Matrix identity, users, rooms, messages, and media remain unchanged."
            ],
            RuntimeStackTurnConnectModes.ReplaceExternal =>
            [
                "The exact reviewed external homeserver.yaml will be retained in a private rollback snapshot.",
                "The external TURN settings will be replaced atomically with the current MEM platform TURN settings.",
                "The Matrix container will restart and active voice or video calls may be interrupted.",
                "If verification fails after mutation, MEM will restore the exact previous configuration and stack metadata.",
                "Matrix identity, users, rooms, messages, and media remain unchanged."
            ],
            _ =>
            [
                "Synapse configuration will be changed atomically.",
                "The Matrix container will restart.",
                "Active voice or video calls may be interrupted.",
                "Matrix identity, users, rooms, messages, and media remain unchanged."
            ]
        };

    private static string ComputeRequestHash(
        string reviewHash,
        bool confirmReplaceExternalTurn) =>
        confirmReplaceExternalTurn
            ? Hash(string.Join(
                "\n",
                "connect",
                reviewHash,
                "confirmed",
                "replace-external-confirmed"))
            : Hash(string.Join(
                "\n",
                "connect",
                reviewHash,
                "confirmed"));

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

    private static bool SetsEqual(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right) =>
        left.Select(NormalizeUri)
            .OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(
                right.Select(NormalizeUri)
                    .OrderBy(value => value, StringComparer.Ordinal),
                StringComparer.Ordinal);

    private static string NormalizeUri(string value) =>
        value.Trim().ToLowerInvariant();

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

    private static RuntimeStackTurnConnectResponse Replay(
        RuntimeOperationDetail replay,
        string requestHash,
        RuntimeStackManifest manifest)
    {
        var mode = RuntimeStackTurnConnectModes.Configure;
        if (!string.IsNullOrWhiteSpace(replay.InputJson))
        {
            using var input = JsonDocument.Parse(replay.InputJson);
            var observedHash = input.RootElement.TryGetProperty("requestHash", out var hash)
                ? hash.GetString()
                : null;
            if (!FixedEquals(observedHash, requestHash))
            {
                throw new RuntimeStackTurnConnectionException(
                    "turn_connect_idempotency_conflict",
                    "The idempotency key was already used with a different TURN connection request.");
            }

            if (input.RootElement.TryGetProperty("mode", out var modeElement) &&
                modeElement.GetString() is { Length: > 0 } observedMode)
            {
                mode = observedMode;
            }
        }

        if (!string.IsNullOrWhiteSpace(replay.ResultJson))
        {
            var result = JsonSerializer.Deserialize<RuntimeStackTurnConnectResponse>(
                replay.ResultJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (result is not null)
            {
                return result;
            }
        }

        return Outcome(
            replay.Status == "running"
                ? RuntimeStackTurnConnectStatuses.Running
                : RuntimeStackTurnConnectStatuses.Failed,
            replay.Id,
            manifest,
            mode,
            configurationChanged: false,
            matrixRestarted: false,
            rollbackAttempted: false,
            rollbackSucceeded: null,
            stateAfter: null,
            errorCode: replay.LastError,
            detail: replay.Status == "running"
                ? "The TURN connection operation is still running."
                : "The existing TURN connection operation outcome could not be reconstructed safely.");
    }

    private static RuntimeStackTurnConnectResponse Outcome(
        string status,
        Guid operationId,
        RuntimeStackManifest manifest,
        string mode,
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
            Mode: mode,
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
        RuntimeStackTurnConnectResponse result,
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
                    ReasonCode: "runtime_stack_turn_connection"),
                ct);
        }
        catch
        {
            // The durable Runtime Operation remains authoritative.
        }
    }
}
