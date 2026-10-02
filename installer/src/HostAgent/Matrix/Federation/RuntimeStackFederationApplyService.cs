using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostAgent.Matrix.Runtime;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Operations;
using Microsoft.Extensions.Logging;
using Modules.Auth.Services.Identity;

namespace HostAgent.Matrix.Federation;


internal sealed record FederationApplyOperationInput(
    string RequestHash,
    string ReviewHash,
    string CurrentStateFingerprint,
    string PreviousMode,
    IReadOnlyList<string> PreviousAllowlist,
    string RequestedMode,
    IReadOnlyList<string> CanonicalAllowlist);

public sealed class FederationApplyBlockedException : InvalidOperationException
{
    public FederationApplyBlockedException(string code, string detail)
        : base(detail)
    {
        Code = code;
    }

    public string Code { get; }
}

public interface IRuntimeStackFederationApplyService
{
    Task<RuntimeStackFederationApplyResponse?> ApplyAsync(
        string slugOrId,
        RuntimeStackFederationApplyRequest request,
        string requestedBy,
        Guid? actorOperatorId,
        CancellationToken requestCancellation);
}

public sealed class RuntimeStackFederationApplyLock
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

public sealed class RuntimeStackFederationApplyService
    : IRuntimeStackFederationApplyService
{
    public const string OperationName = "apply-federation-policy";
    private static readonly TimeSpan ApplyTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RollbackTimeout = TimeSpan.FromMinutes(3);

    private readonly IRuntimeStackFederationManifestResolver _manifestResolver;
    private readonly IRuntimeStackFederationStateService _stateService;
    private readonly FederationPolicyRequestValidator _requestValidator;
    private readonly IRuntimeOperationStore _operations;
    private readonly RuntimeStackFederationApplyLock _applyLock;
    private readonly IRuntimeStackFederationSnapshotService _snapshots;
    private readonly IRuntimeMatrixContainerLifecycleService _matrixLifecycle;
    private readonly ISynapseFederationConfigCandidateValidator _candidateValidator;
    private readonly IRuntimeStackFederationConfigTransaction _configTransaction;
    private readonly IRuntimeStackFederationIngressTransaction _ingressTransaction;
    private readonly IRuntimeStackFederationVerifier _verifier;
    private readonly IMemOperatorAuditService _audit;
    private readonly ILogger<RuntimeStackFederationApplyService> _logger;

    public RuntimeStackFederationApplyService(
        IRuntimeStackFederationManifestResolver manifestResolver,
        IRuntimeStackFederationStateService stateService,
        FederationPolicyRequestValidator requestValidator,
        IRuntimeOperationStore operations,
        RuntimeStackFederationApplyLock applyLock,
        IRuntimeStackFederationSnapshotService snapshots,
        IRuntimeMatrixContainerLifecycleService matrixLifecycle,
        ISynapseFederationConfigCandidateValidator candidateValidator,
        IRuntimeStackFederationConfigTransaction configTransaction,
        IRuntimeStackFederationIngressTransaction ingressTransaction,
        IRuntimeStackFederationVerifier verifier,
        IMemOperatorAuditService audit,
        ILogger<RuntimeStackFederationApplyService> logger)
    {
        _manifestResolver = manifestResolver;
        _stateService = stateService;
        _requestValidator = requestValidator;
        _operations = operations;
        _applyLock = applyLock;
        _snapshots = snapshots;
        _matrixLifecycle = matrixLifecycle;
        _candidateValidator = candidateValidator;
        _configTransaction = configTransaction;
        _ingressTransaction = ingressTransaction;
        _verifier = verifier;
        _audit = audit;
        _logger = logger;
    }

    public async Task<RuntimeStackFederationApplyResponse?> ApplyAsync(
        string slugOrId,
        RuntimeStackFederationApplyRequest request,
        string requestedBy,
        Guid? actorOperatorId,
        CancellationToken requestCancellation)
    {
        ArgumentNullException.ThrowIfNull(request);

        var canonical = _requestValidator.Validate(
            new RuntimeStackFederationPolicyRequest(request.Mode, request.Allowlist));
        var reviewHash = RequireReviewHash(request.ReviewHash);
        var idempotencyKey = RequireIdempotencyKey(request.IdempotencyKey);
        var requestHash = ComputeRequestHash(canonical, reviewHash);

        var manifest = await _manifestResolver.FindAsync(slugOrId, requestCancellation);
        if (manifest is null)
        {
            return null;
        }

        await using var heldLock = await _applyLock.TryAcquireAsync(
            manifest.StackId,
            requestCancellation);
        if (heldLock is null)
        {
            throw new FederationApplyBlockedException(
                "federation_operation_in_progress",
                "Another federation policy operation is already starting for this stack.");
        }

        var replay = await _operations.FindByIdempotencyKeyAsync(
            manifest.StackId,
            OperationName,
            idempotencyKey,
            requestCancellation);
        if (replay is not null)
        {
            return Replay(replay, requestHash);
        }

        var current = await _stateService.GetAsync(
            manifest.StackId.ToString("D"),
            requestCancellation)
            ?? throw new FederationApplyBlockedException(
                "federation_stack_not_found",
                "The Runtime Stack disappeared before federation application began.");
        EnsureApplyable(current, canonical);

        var expectedReviewHash = FederationReviewHash.Compute(
            current.StateFingerprint!,
            canonical);
        if (!FixedEquals(expectedReviewHash, reviewHash))
        {
            throw new FederationApplyBlockedException(
                "federation_review_stale",
                "This stack changed after the review. Review the federation policy again.");
        }

        if (PolicyMatches(current, canonical))
        {
            throw new FederationApplyBlockedException(
                "federation_no_change",
                "The requested federation policy already matches the active stack state.");
        }

        var conflict = await _operations.FindActiveMutatingOperationForStackAsync(
            manifest.StackId,
            requestCancellation);
        if (conflict is not null)
        {
            throw new FederationApplyBlockedException(
                "federation_operation_in_progress",
                "Another mutating Runtime Operation is active for this stack.");
        }

        var container = await _matrixLifecycle.InspectAsync(
            manifest.Matrix,
            requestCancellation);
        EnsureSafeContainer(container);

        var targetIngress = TargetIngress(canonical.Mode);
        var ingressChangeRequired = !string.Equals(
            current.IngressMode,
            targetIngress,
            StringComparison.Ordinal);
        var input = new FederationApplyOperationInput(
            RequestHash: requestHash,
            ReviewHash: reviewHash,
            CurrentStateFingerprint: current.StateFingerprint!,
            PreviousMode: current.Mode,
            PreviousAllowlist: current.Allowlist,
            RequestedMode: canonical.Mode,
            CanonicalAllowlist: canonical.Allowlist);
        var operationId = await _operations.StartAsync(
            manifest.StackId,
            OperationName,
            idempotencyKey,
            NormalizeRequestedBy(requestedBy),
            ingressChangeRequired ? "filesystem,docker,ingress" : "filesystem,docker",
            input,
            requestCancellation);

        using var applyCancellation = new CancellationTokenSource(ApplyTimeout);
        var ct = applyCancellation.Token;
        RuntimeStackFederationSnapshot? snapshot = null;
        SynapseFederationCandidateValidationResult? candidateValidation = null;
        RuntimeStackFederationVerificationResult? verification = null;
        var configMutationStarted = false;
        var ingressMutationStarted = false;
        var failureCode = "federation_snapshot_failed";

        try
        {
            failureCode = "federation_snapshot_failed";
            await StepAsync(operationId, "snapshotting-current-state", ct);
            snapshot = await _snapshots.CreateAsync(
                manifest,
                operationId,
                canonical,
                ct);

            failureCode = "federation_config_validation_failed";
            await StepAsync(operationId, "validating-candidate-config", ct);
            candidateValidation = await _candidateValidator.ValidateAsync(
                manifest,
                container,
                snapshot.CandidateConfigPath,
                ct);
            if (!candidateValidation.Valid)
            {
                var rejected = Outcome(
                    status: "candidate_rejected",
                    operationId,
                    current.Mode,
                    canonical.Mode,
                    current.Mode,
                    rollbackAttempted: false,
                    rollbackSucceeded: null,
                    checks: [],
                    errorCode: "federation_config_validation_failed",
                    detail: "The requested federation policy was not applied because the active Synapse image rejected the candidate configuration.");
                await _operations.FailAsync(
                    operationId,
                    "candidate-rejected",
                    rejected.ErrorCode!,
                    rejected,
                    Evidence(snapshot, candidateValidation, verification, null),
                    ct);
                await TryAuditAsync(
                    "federation.policy.apply.failed",
                    "failed",
                    operationId,
                    actorOperatorId,
                    ct);
                return rejected;
            }

            failureCode = "federation_review_stale";
            await StepAsync(operationId, "validating-review-before-mutation", ct);
            var beforeMutation = await _stateService.GetAsync(
                manifest.StackId.ToString("D"),
                ct);
            if (beforeMutation?.StateFingerprint is null ||
                !FixedEquals(current.StateFingerprint!, beforeMutation.StateFingerprint))
            {
                throw new FederationApplyExecutionException(
                    "federation_review_stale",
                    "This stack changed after the review and before active mutation.");
            }

            if (string.Equals(canonical.Mode, FederationModes.LocalOnly, StringComparison.Ordinal))
            {
                if (ingressChangeRequired)
                {
                    failureCode = "federation_ingress_update_failed";
                    await StepAsync(operationId, "applying-ingress-guard", ct);
                    ingressMutationStarted = true;
                    await _ingressTransaction.ApplyModeAsync(
                        snapshot,
                        FederationModes.LocalOnly,
                        ct);
                }

                failureCode = "federation_ingress_update_failed";
                await StepAsync(operationId, "verifying-ingress-guard", ct);
                var ingressGuard = await _verifier.VerifyLocalOnlyIngressGuardAsync(
                    manifest,
                    ct);
                if (!ingressGuard.Succeeded)
                {
                    verification = ingressGuard;
                    throw new FederationApplyExecutionException(
                        "federation_ingress_update_failed",
                        "Local-only ingress could not be verified before Synapse configuration changed.");
                }
            }

            failureCode = "federation_config_replace_failed";
            await StepAsync(operationId, "replacing-active-config", ct);
            await _configTransaction.ApplyCandidateAsync(snapshot, ct);
            configMutationStarted = true;

            failureCode = "federation_restart_failed";
            await StepAsync(operationId, "restarting-matrix", ct);
            await _matrixLifecycle.RestartAsync(manifest.Matrix, ct);

            if (!string.Equals(canonical.Mode, FederationModes.LocalOnly, StringComparison.Ordinal) &&
                string.Equals(current.IngressMode, FederationIngressModes.LocalOnly, StringComparison.Ordinal))
            {
                failureCode = "federation_client_readiness_failed";
                await StepAsync(operationId, "waiting-for-client-readiness", ct);
                var clientReadiness = await _verifier.VerifyClientReadinessAsync(
                    manifest,
                    ct);
                if (!clientReadiness.Succeeded)
                {
                    verification = clientReadiness;
                    throw new FederationApplyExecutionException(
                        "federation_client_readiness_failed",
                        "Matrix client readiness could not be verified before public federation ingress was restored.");
                }

                failureCode = "federation_ingress_update_failed";
                await StepAsync(operationId, "applying-public-ingress", ct);
                ingressMutationStarted = true;
                await _ingressTransaction.ApplyModeAsync(
                    snapshot,
                    canonical.Mode,
                    ct);
            }
            else if (!string.Equals(canonical.Mode, FederationModes.LocalOnly, StringComparison.Ordinal) &&
                     ingressChangeRequired)
            {
                failureCode = "federation_ingress_update_failed";
                await StepAsync(operationId, "applying-public-ingress", ct);
                ingressMutationStarted = true;
                await _ingressTransaction.ApplyModeAsync(
                    snapshot,
                    canonical.Mode,
                    ct);
            }

            failureCode = "federation_effective_state_mismatch";
            await StepAsync(operationId, "verifying-effective-state", ct);
            verification = await _verifier.VerifyAsync(manifest, canonical, ct);
            if (!verification.Succeeded)
            {
                throw new FederationApplyExecutionException(
                    "federation_effective_state_mismatch",
                    "The requested federation state could not be verified after the guarded transition.");
            }

            var succeeded = Outcome(
                status: "succeeded",
                operationId,
                current.Mode,
                canonical.Mode,
                verification.ObservedMode,
                rollbackAttempted: false,
                rollbackSucceeded: null,
                checks: verification.Checks,
                errorCode: null,
                detail: canonical.Mode switch
                {
                    FederationModes.Public => "Public federation is active.",
                    FederationModes.Restricted => "Restricted federation is active.",
                    FederationModes.LocalOnly => "Local-only federation is active and public federation ingress is blocked.",
                    _ => "The requested federation mode is active."
                });
            await _operations.CompleteAsync(
                operationId,
                "succeeded",
                "completed",
                succeeded,
                Evidence(snapshot, candidateValidation, verification, null),
                ct);
            await TryAuditAsync(
                "federation.policy.apply.succeeded",
                "succeeded",
                operationId,
                actorOperatorId,
                ct);
            return succeeded;
        }
        catch (Exception ex) when (configMutationStarted || ingressMutationStarted)
        {
            _logger.LogWarning(ex,
                "Federation apply failed after active mutation; rollback will be attempted. StackId={StackId} OperationId={OperationId}",
                manifest.StackId,
                operationId);
            return await RollbackAsync(
                manifest,
                current,
                canonical,
                operationId,
                snapshot!,
                candidateValidation,
                verification,
                ex,
                failureCode,
                configMutationStarted,
                ingressMutationStarted,
                actorOperatorId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Federation apply failed before active mutation. StackId={StackId} OperationId={OperationId}",
                manifest.StackId,
                operationId);
            var code = ex switch
            {
                FederationApplyExecutionException execution => execution.Code,
                RuntimeStackFederationConfigStateChangedException => "federation_review_stale",
                RuntimeStackFederationIngressStateChangedException => "federation_review_stale",
                OperationCanceledException => "federation_operation_timeout",
                _ => failureCode
            };
            var failed = Outcome(
                status: "failed",
                operationId,
                current.Mode,
                canonical.Mode,
                current.Mode,
                rollbackAttempted: false,
                rollbackSucceeded: null,
                checks: verification?.Checks ?? [],
                errorCode: code,
                detail: "The requested federation policy was not applied and no active configuration mutation was confirmed.");
            await TryJournalFailureAsync(
                operationId,
                "failed-before-mutation",
                code,
                failed,
                Evidence(snapshot, candidateValidation, verification, ex));
            await TryAuditAsync(
                "federation.policy.apply.failed",
                "failed",
                operationId,
                actorOperatorId,
                CancellationToken.None);
            return failed;
        }
    }

    private async Task<RuntimeStackFederationApplyResponse> RollbackAsync(
        RuntimeStackManifest manifest,
        RuntimeStackFederationStateResponse previousState,
        CanonicalFederationPolicyRequest requested,
        Guid operationId,
        RuntimeStackFederationSnapshot snapshot,
        SynapseFederationCandidateValidationResult? candidateValidation,
        RuntimeStackFederationVerificationResult? applyVerification,
        Exception applyError,
        string applyErrorCode,
        bool configMutationStarted,
        bool ingressMutationStarted,
        Guid? actorOperatorId)
    {
        using var rollbackCancellation = new CancellationTokenSource(RollbackTimeout);
        var ct = rollbackCancellation.Token;
        RuntimeStackFederationVerificationResult? rollbackVerification = null;
        var previousIngressWasLocalOnly = string.Equals(
            previousState.IngressMode,
            FederationIngressModes.LocalOnly,
            StringComparison.Ordinal);

        try
        {
            await TryStepAsync(operationId, "rollback-started", ct);

            if (previousIngressWasLocalOnly && ingressMutationStarted)
            {
                await TryStepAsync(operationId, "restoring-ingress-guard", ct);
                await _ingressTransaction.RestoreBeforeAsync(snapshot, ct);
            }

            if (configMutationStarted)
            {
                await TryStepAsync(operationId, "restoring-config", ct);
                await _configTransaction.RestoreBeforeAsync(snapshot, ct);
                await TryStepAsync(operationId, "restarting-matrix-after-config-restore", ct);
                await _matrixLifecycle.RestartAsync(manifest.Matrix, ct);
            }

            if (!previousIngressWasLocalOnly && ingressMutationStarted)
            {
                await TryStepAsync(operationId, "restoring-ingress", ct);
                await _ingressTransaction.RestoreBeforeAsync(snapshot, ct);
            }

            await TryStepAsync(operationId, "verifying-rollback", ct);
            var previousPolicy = new CanonicalFederationPolicyRequest(
                previousState.Mode,
                previousState.Mode == FederationModes.Restricted
                    ? previousState.Allowlist
                    : []);
            rollbackVerification = await _verifier.VerifyAsync(
                manifest,
                previousPolicy,
                ct);
            if (!rollbackVerification.Succeeded)
            {
                throw new FederationApplyExecutionException(
                    "federation_rollback_failed",
                    "The previous federation state could not be verified after config and ingress restoration.");
            }

            var errorCode = applyError is OperationCanceledException
                ? "federation_operation_timeout"
                : applyError is FederationApplyExecutionException execution
                    ? execution.Code
                    : applyErrorCode;
            var rolledBack = Outcome(
                status: "rolled_back",
                operationId,
                previousState.Mode,
                requested.Mode,
                rollbackVerification.ObservedMode,
                rollbackAttempted: true,
                rollbackSucceeded: true,
                checks: rollbackVerification.Checks,
                errorCode,
                detail: $"The requested federation policy was not applied. MEM restored and verified the previous {ModeLabel(previousState.Mode)} federation state.");
            try
            {
                await _operations.CompleteAsync(
                    operationId,
                    "rolled_back",
                    "rolled-back",
                    rolledBack,
                    Evidence(snapshot, candidateValidation, applyVerification, applyError, rollbackVerification),
                    ct);
            }
            catch (Exception journalError)
            {
                _logger.LogError(journalError,
                    "Federation rollback was verified but the Runtime Operation journal could not be finalised. OperationId={OperationId}",
                    operationId);
            }

            await TryAuditAsync(
                "federation.policy.apply.rolled_back",
                "rolled_back",
                operationId,
                actorOperatorId,
                ct);
            return rolledBack;
        }
        catch (Exception rollbackError)
        {
            _logger.LogError(rollbackError,
                "Federation automatic rollback failed. StackId={StackId} OperationId={OperationId}",
                manifest.StackId,
                operationId);
            var failed = Outcome(
                status: "failed",
                operationId,
                previousState.Mode,
                requested.Mode,
                FederationModes.Unknown,
                rollbackAttempted: true,
                rollbackSucceeded: false,
                checks: rollbackVerification?.Checks ?? [],
                errorCode: "federation_rollback_failed",
                detail: "MEM could not restore a verified Matrix service state automatically. The previous configuration and NPM route snapshots have been retained for immediate recovery.");
            await TryJournalFailureAsync(
                operationId,
                "rollback-failed",
                "federation_rollback_failed",
                failed,
                Evidence(snapshot, candidateValidation, applyVerification, rollbackError, rollbackVerification));
            await TryAuditAsync(
                "federation.policy.apply.failed",
                "failed",
                operationId,
                actorOperatorId,
                CancellationToken.None);
            return failed;
        }
    }

    private async Task TryJournalFailureAsync(
        Guid operationId,
        string step,
        string code,
        RuntimeStackFederationApplyResponse result,
        object evidence)
    {
        try
        {
            using var journalCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _operations.FailAsync(
                operationId,
                step,
                code,
                result,
                evidence,
                journalCancellation.Token);
        }
        catch (Exception journalError)
        {
            _logger.LogError(journalError,
                "Federation Runtime Operation failure outcome could not be journalled. OperationId={OperationId} Step={Step}",
                operationId,
                step);
        }
    }

    private Task StepAsync(Guid operationId, string step, CancellationToken ct) =>
        _operations.UpdateStepAsync(operationId, step, ct);

    private async Task TryStepAsync(Guid operationId, string step, CancellationToken ct)
    {
        try
        {
            await StepAsync(operationId, step, ct);
        }
        catch (Exception journalError)
        {
            _logger.LogError(journalError,
                "Federation rollback step could not be journalled; physical rollback will continue. OperationId={OperationId} Step={Step}",
                operationId,
                step);
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
                    ReasonCode: "operator_policy_change"),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Federation operation audit write failed. OperationId={OperationId} EventType={EventType}",
                operationId,
                eventType);
        }
    }

    private static RuntimeStackFederationApplyResponse Replay(
        RuntimeOperationDetail operation,
        string requestHash)
    {
        var input = Deserialize<FederationApplyOperationInput>(operation.InputJson)
            ?? throw new FederationApplyBlockedException(
                "federation_idempotency_conflict",
                "The existing idempotent operation does not contain a readable canonical input.");
        if (!FixedEquals(input.RequestHash, requestHash))
        {
            throw new FederationApplyBlockedException(
                "federation_idempotency_conflict",
                "The idempotency key was already used for a different federation request.");
        }

        var completed = Deserialize<RuntimeStackFederationApplyResponse>(operation.ResultJson);
        if (completed is not null)
        {
            return completed;
        }

        if (string.Equals(operation.Status, "running", StringComparison.Ordinal))
        {
            return Outcome(
                status: "running",
                operation.Id,
                input.PreviousMode,
                input.RequestedMode,
                input.PreviousMode,
                rollbackAttempted: false,
                rollbackSucceeded: null,
                checks: [],
                errorCode: null,
                detail: "The matching federation operation is still running.");
        }

        throw new FederationApplyBlockedException(
            "federation_idempotency_conflict",
            "The existing idempotent operation does not contain a reusable final result.");
    }

    private static void EnsureApplyable(
        RuntimeStackFederationStateResponse current,
        CanonicalFederationPolicyRequest requested)
    {
        if (string.IsNullOrWhiteSpace(current.StateFingerprint) ||
            string.Equals(current.ConfigurationState, FederationConfigurationStates.Unavailable, StringComparison.Ordinal))
        {
            throw new FederationApplyBlockedException(
                "federation_state_unavailable",
                "The current federation state is unavailable and cannot be changed safely.");
        }

        if (string.Equals(current.ConfigurationState, FederationConfigurationStates.CustomUnsupported, StringComparison.Ordinal))
        {
            throw new FederationApplyBlockedException(
                "federation_config_custom_unsupported",
                "The active federation configuration uses a custom representation that MEM will not rewrite.");
        }

        if (!string.Equals(current.Mode, FederationModes.Public, StringComparison.Ordinal) &&
            !string.Equals(current.Mode, FederationModes.Restricted, StringComparison.Ordinal) &&
            !string.Equals(current.Mode, FederationModes.LocalOnly, StringComparison.Ordinal))
        {
            throw new FederationApplyBlockedException(
                "federation_effective_state_mismatch",
                "The current federation mode could not be determined safely.");
        }

        if (!string.Equals(current.IngressMode, FederationIngressModes.Normal, StringComparison.Ordinal) &&
            !string.Equals(current.IngressMode, FederationIngressModes.LocalOnly, StringComparison.Ordinal))
        {
            throw new FederationApplyBlockedException(
                "federation_ingress_custom_unsupported",
                "The canonical NPM route does not use a supported normal or Local-only ingress template.");
        }

        if (!current.MatrixContainerRunning ||
            current.MatrixDirectHostPortExposed ||
            !current.CanonicalRouteEnabled ||
            !current.CanonicalRouteTargetsMatrix)
        {
            throw new FederationApplyBlockedException(
                current.MatrixDirectHostPortExposed
                    ? "federation_direct_host_port_exposed"
                    : "federation_effective_state_mismatch",
                "The Matrix container and canonical NPM route must pass running, identity, target, and direct-port checks before federation configuration can be changed.");
        }

        if (string.Equals(requested.Mode, FederationModes.LocalOnly, StringComparison.Ordinal) &&
            current.AlternateMatrixRouteDetected)
        {
            throw new FederationApplyBlockedException(
                "federation_alternate_route_detected",
                "Local-only federation cannot be applied while another enabled NPM route forwards to the same Matrix upstream.");
        }
    }

    private static void EnsureSafeContainer(RuntimeMatrixContainerObservation container)
    {
        if (!container.Running ||
            !container.IdentityMatches ||
            !container.DataBindMatches ||
            !container.ExpectedNetworkAttached ||
            container.DirectHostPortExposed)
        {
            throw new FederationApplyBlockedException(
                "federation_matrix_container_identity_mismatch",
                "The manifest-selected Matrix container did not pass identity, bind, network, running-state, and direct-port checks.");
        }
    }

    private static bool PolicyMatches(
        RuntimeStackFederationStateResponse current,
        CanonicalFederationPolicyRequest requested) =>
        string.Equals(current.Mode, requested.Mode, StringComparison.Ordinal) &&
        current.Allowlist.OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(
                requested.Allowlist.OrderBy(x => x, StringComparer.Ordinal),
                StringComparer.Ordinal) &&
        string.Equals(current.IngressMode, TargetIngress(requested.Mode), StringComparison.Ordinal);

    private static string TargetIngress(string mode) =>
        string.Equals(mode, FederationModes.LocalOnly, StringComparison.Ordinal)
            ? FederationIngressModes.LocalOnly
            : FederationIngressModes.Normal;

    private static object Evidence(
        RuntimeStackFederationSnapshot? snapshot,
        SynapseFederationCandidateValidationResult? candidate,
        RuntimeStackFederationVerificationResult? verification,
        Exception? error,
        RuntimeStackFederationVerificationResult? rollbackVerification = null) =>
        new
        {
            schemaVersion = "federation-apply-evidence-v1",
            beforeConfigSha256 = snapshot?.BeforeConfigSha256,
            candidateConfigSha256 = snapshot?.CandidateConfigSha256,
            npmRouteSnapshotSha256 = snapshot?.NpmRouteSnapshotSha256,
            validationImage = candidate?.Image,
            validationImagePinned = candidate?.ImagePinned,
            validationExitCode = candidate?.ExitCode,
            validationResult = candidate?.Valid,
            validationLogTail = candidate?.LogTail,
            verificationChecks = verification?.Checks.Select(x => new { x.Code, x.Status }).ToArray(),
            rollbackChecks = rollbackVerification?.Checks.Select(x => new { x.Code, x.Status }).ToArray(),
            errorCode = error is null ? null : ErrorCodeFor(error)
        };

    private static RuntimeStackFederationApplyResponse Outcome(
        string status,
        Guid operationId,
        string previousMode,
        string requestedMode,
        string observedMode,
        bool rollbackAttempted,
        bool? rollbackSucceeded,
        IReadOnlyList<FederationCheckResponse> checks,
        string? errorCode,
        string detail) =>
        new(
            Source: "control-plane",
            Status: status,
            OperationId: operationId,
            PreviousMode: previousMode,
            RequestedMode: requestedMode,
            ObservedMode: observedMode,
            RollbackAttempted: rollbackAttempted,
            RollbackSucceeded: rollbackSucceeded,
            Checks: checks,
            ErrorCode: errorCode,
            Detail: detail);

    private static string RequireReviewHash(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            !normalized.StartsWith("sha256:", StringComparison.Ordinal) ||
            normalized.Length != 71)
        {
            throw new FederationApplyBlockedException(
                "federation_policy_invalid",
                "A valid server-issued federation review hash is required.");
        }

        return normalized.ToLowerInvariant();
    }

    private static string RequireIdempotencyKey(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 200)
        {
            throw new FederationApplyBlockedException(
                "federation_policy_invalid",
                "An idempotency key between 1 and 200 characters is required.");
        }

        return normalized;
    }

    private static string ComputeRequestHash(
        CanonicalFederationPolicyRequest request,
        string reviewHash)
    {
        var canonical = new StringBuilder()
            .Append(reviewHash).Append('\n')
            .Append(request.Mode).Append('\n');
        foreach (var domain in request.Allowlist)
        {
            canonical.Append(domain).Append('\n');
        }

        return "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static bool FixedEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string ErrorCodeFor(Exception error) => error switch
    {
        FederationApplyExecutionException execution => execution.Code,
        RuntimeStackFederationConfigStateChangedException => "federation_review_stale",
        RuntimeStackFederationIngressStateChangedException => "federation_review_stale",
        OperationCanceledException => "federation_operation_timeout",
        TimeoutException => "federation_restart_failed",
        _ => "federation_restart_failed"
    };

    private static string ModeLabel(string mode) => mode switch
    {
        FederationModes.Public => "Public",
        FederationModes.Restricted => "Restricted",
        FederationModes.LocalOnly => "Local-only",
        _ => "previous"
    };

    private static string NormalizeRequestedBy(string requestedBy) =>
        string.IsNullOrWhiteSpace(requestedBy)
            ? "control-plane-operator"
            : requestedBy.Trim().Length <= 100
                ? requestedBy.Trim()
                : requestedBy.Trim()[..100];

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }


    private sealed class FederationApplyExecutionException : InvalidOperationException
    {
        public FederationApplyExecutionException(string code, string detail)
            : base(detail)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
