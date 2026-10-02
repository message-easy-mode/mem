using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostAgent.Matrix.Runtime;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Operations;
using Microsoft.Extensions.Logging;
using Modules.Auth.Services.Identity;

namespace HostAgent.Matrix.Federation.PrivateNetwork;

internal sealed record PrivateNetworkOperationInput(
    string RequestHash,
    string ReviewHash,
    string BeforeConfigSha256,
    string Action,
    string CanonicalCidr);

internal sealed record PrivateNetworkConfigSnapshot(
    string OperationDirectory,
    string ActiveConfigPath,
    string BeforeConfigPath,
    string CandidateConfigPath,
    string BeforeConfigSha256,
    string CandidateConfigSha256);

public interface IRuntimeStackPrivateNetworkFederationService
{
    Task<PrivateNetworkFederationInventoryResponse> GetInventoryAsync(CancellationToken ct);

    Task<PrivateNetworkFederationReviewResponse?> ReviewAsync(
        string slugOrId,
        PrivateNetworkFederationReviewRequest request,
        CancellationToken ct);

    Task<PrivateNetworkFederationApplyResponse?> ApplyAsync(
        string slugOrId,
        PrivateNetworkFederationApplyRequest request,
        string requestedBy,
        Guid? actorOperatorId,
        CancellationToken requestCancellation);
}

public sealed class RuntimeStackPrivateNetworkFederationService
    : IRuntimeStackPrivateNetworkFederationService
{
    public const string OperationName = "update-private-network-federation-exception";
    private static readonly TimeSpan ApplyTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RollbackTimeout = TimeSpan.FromMinutes(3);

    private readonly RuntimeStackManifestStore _manifestStore;
    private readonly IRuntimeStackFederationManifestResolver _manifestResolver;
    private readonly IRuntimeStackFederationStateService _federationState;
    private readonly SynapsePrivateNetworkConfigReader _reader;
    private readonly SynapsePrivateNetworkConfigEditor _editor;
    private readonly PrivateNetworkAddressValidator _addressValidator;
    private readonly IRuntimeOperationStore _operations;
    private readonly RuntimeStackFederationApplyLock _applyLock;
    private readonly IRuntimeMatrixContainerLifecycleService _matrixLifecycle;
    private readonly ISynapseFederationConfigCandidateValidator _candidateValidator;
    private readonly IRuntimeStackFederationVerifier _federationVerifier;
    private readonly IMemOperatorAuditService _audit;
    private readonly ILogger<RuntimeStackPrivateNetworkFederationService> _logger;

    public RuntimeStackPrivateNetworkFederationService(
        RuntimeStackManifestStore manifestStore,
        IRuntimeStackFederationManifestResolver manifestResolver,
        IRuntimeStackFederationStateService federationState,
        SynapsePrivateNetworkConfigReader reader,
        SynapsePrivateNetworkConfigEditor editor,
        PrivateNetworkAddressValidator addressValidator,
        IRuntimeOperationStore operations,
        RuntimeStackFederationApplyLock applyLock,
        IRuntimeMatrixContainerLifecycleService matrixLifecycle,
        ISynapseFederationConfigCandidateValidator candidateValidator,
        IRuntimeStackFederationVerifier federationVerifier,
        IMemOperatorAuditService audit,
        ILogger<RuntimeStackPrivateNetworkFederationService> logger)
    {
        _manifestStore = manifestStore;
        _manifestResolver = manifestResolver;
        _federationState = federationState;
        _reader = reader;
        _editor = editor;
        _addressValidator = addressValidator;
        _operations = operations;
        _applyLock = applyLock;
        _matrixLifecycle = matrixLifecycle;
        _candidateValidator = candidateValidator;
        _federationVerifier = federationVerifier;
        _audit = audit;
        _logger = logger;
    }

    public async Task<PrivateNetworkFederationInventoryResponse> GetInventoryAsync(
        CancellationToken ct)
    {
        var manifests = await _manifestStore.ListAsync(ct);
        var stacks = new List<PrivateNetworkFederationStackStateResponse>();

        foreach (var manifest in manifests)
        {
            stacks.Add(await ReadStackStateAsync(manifest, ct));
        }

        return new PrivateNetworkFederationInventoryResponse(
            Source: "control-plane",
            Status: "ok",
            Stacks: stacks);
    }

    public async Task<PrivateNetworkFederationReviewResponse?> ReviewAsync(
        string slugOrId,
        PrivateNetworkFederationReviewRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var manifest = await _manifestResolver.FindAsync(slugOrId, ct);
        if (manifest is null)
        {
            return null;
        }

        var action = ValidateAction(request.Action);
        var address = ValidateAddress(request.Address, action);
        var path = RequireOwnedConfigPath(manifest);
        var current = await _reader.ReadAsync(path, ct);
        EnsureConfigSupported(current);
        var federation = await RequireApplyableFederationStateAsync(manifest, action, ct);
        var proposed = Proposed(current.Entries, address.Cidr, action);
        var noChange = current.Entries.SequenceEqual(proposed, StringComparer.Ordinal);
        var reviewHash = ComputeReviewHash(
            manifest.StackId,
            current.FileSha256,
            federation.StateFingerprint!,
            action,
            address.Cidr);

        return new PrivateNetworkFederationReviewResponse(
            Source: "control-plane",
            Status: noChange ? "no_change" : "ready",
            RuntimeStackId: manifest.StackId,
            Slug: manifest.Slug,
            MatrixServerName: manifest.Matrix.ServerName ?? manifest.Matrix.PublicHost ?? manifest.Slug,
            Action: action,
            CanonicalAddress: address.Address,
            CanonicalCidr: address.Cidr,
            CurrentExceptions: current.Entries,
            ProposedExceptions: proposed,
            RestartRequired: !noChange,
            NoChange: noChange,
            ReviewHash: reviewHash,
            ConfirmationText: action == PrivateNetworkExceptionActions.Add
                ? $"Allow Synapse on {manifest.Slug} to contact the exact private address {address.Cidr} and restart Matrix."
                : $"Remove the exact private address exception {address.Cidr} from {manifest.Slug} and restart Matrix.");
    }

    public async Task<PrivateNetworkFederationApplyResponse?> ApplyAsync(
        string slugOrId,
        PrivateNetworkFederationApplyRequest request,
        string requestedBy,
        Guid? actorOperatorId,
        CancellationToken requestCancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        var action = ValidateAction(request.Action);
        var address = ValidateAddress(request.Address, action);
        var reviewHash = RequireToken(request.ReviewHash, "private_network_review_hash_required");
        var idempotencyKey = RequireToken(request.IdempotencyKey, "private_network_idempotency_key_required");
        var manifest = await _manifestResolver.FindAsync(slugOrId, requestCancellation);
        if (manifest is null)
        {
            return null;
        }

        await using var heldLock = await _applyLock.TryAcquireAsync(manifest.StackId, requestCancellation);
        if (heldLock is null)
        {
            throw new PrivateNetworkFederationException(
                "private_network_operation_in_progress",
                "Another federation configuration operation is already starting for this stack.");
        }

        var requestHash = ComputeRequestHash(action, address.Cidr, reviewHash);
        var replay = await _operations.FindByIdempotencyKeyAsync(
            manifest.StackId,
            OperationName,
            idempotencyKey,
            requestCancellation);
        if (replay is not null)
        {
            return Replay(replay, requestHash, manifest, action, address.Cidr);
        }

        var path = RequireOwnedConfigPath(manifest);
        var current = await _reader.ReadAsync(path, requestCancellation);
        EnsureConfigSupported(current);
        var federation = await RequireApplyableFederationStateAsync(manifest, action, requestCancellation);
        var expectedReviewHash = ComputeReviewHash(
            manifest.StackId,
            current.FileSha256,
            federation.StateFingerprint!,
            action,
            address.Cidr);
        if (!FixedEquals(expectedReviewHash, reviewHash))
        {
            throw new PrivateNetworkFederationException(
                "private_network_review_stale",
                "This stack changed after the review. Review the private network exception again.");
        }

        var proposed = Proposed(current.Entries, address.Cidr, action);
        if (current.Entries.SequenceEqual(proposed, StringComparer.Ordinal))
        {
            throw new PrivateNetworkFederationException(
                "private_network_no_change",
                "The exact private network exception already matches the requested state.");
        }

        var conflict = await _operations.FindActiveMutatingOperationForStackAsync(
            manifest.StackId,
            requestCancellation);
        if (conflict is not null)
        {
            throw new PrivateNetworkFederationException(
                "private_network_operation_in_progress",
                "Another mutating Runtime Operation is active for this stack.");
        }

        var container = await _matrixLifecycle.InspectAsync(manifest.Matrix, requestCancellation);
        EnsureSafeContainer(container);
        var operationId = await _operations.StartAsync(
            manifest.StackId,
            OperationName,
            idempotencyKey,
            string.IsNullOrWhiteSpace(requestedBy) ? "control-plane-operator" : requestedBy.Trim(),
            "filesystem,docker",
            new PrivateNetworkOperationInput(
                requestHash,
                reviewHash,
                current.FileSha256,
                action,
                address.Cidr),
            requestCancellation);

        using var timeout = new CancellationTokenSource(ApplyTimeout);
        var ct = timeout.Token;
        PrivateNetworkConfigSnapshot? snapshot = null;
        var mutationStarted = false;
        var failureCode = "private_network_snapshot_failed";

        try
        {
            await StepAsync(operationId, "snapshotting-current-state", ct);
            snapshot = await CreateSnapshotAsync(
                manifest,
                operationId,
                current,
                address.Cidr,
                action == PrivateNetworkExceptionActions.Add,
                ct);

            failureCode = "private_network_config_validation_failed";
            await StepAsync(operationId, "validating-candidate-config", ct);
            var validation = await _candidateValidator.ValidateAsync(
                manifest,
                container,
                snapshot.CandidateConfigPath,
                ct);
            if (!validation.Valid)
            {
                var rejected = Outcome(
                    "candidate_rejected",
                    operationId,
                    manifest,
                    action,
                    address.Cidr,
                    current.Entries,
                    false,
                    null,
                    "private_network_config_validation_failed",
                    "The active Synapse image rejected the private network exception candidate before mutation.");
                await _operations.FailAsync(
                    operationId,
                    "candidate-rejected",
                    rejected.ErrorCode!,
                    rejected,
                    new { snapshot.BeforeConfigSha256, snapshot.CandidateConfigSha256, validation.ExitCode, validation.Image, validation.ImagePinned },
                    ct);
                await TryAuditAsync("security.private_network_exception.apply.failed", "failed", operationId, actorOperatorId, ct);
                return rejected;
            }

            failureCode = "private_network_review_stale";
            await StepAsync(operationId, "validating-review-before-mutation", ct);
            var observed = await _reader.ReadAsync(path, ct);
            if (!FixedEquals(observed.FileSha256, current.FileSha256))
            {
                throw new PrivateNetworkFederationException(
                    "private_network_review_stale",
                    "The active Synapse configuration changed after review and before mutation.");
            }

            failureCode = "private_network_config_replace_failed";
            await StepAsync(operationId, "replacing-active-config", ct);
            await ReplaceAsync(
                snapshot.CandidateConfigPath,
                snapshot.ActiveConfigPath,
                snapshot.BeforeConfigSha256,
                ct);
            mutationStarted = true;

            failureCode = "private_network_restart_failed";
            await StepAsync(operationId, "restarting-matrix", ct);
            await _matrixLifecycle.RestartAsync(manifest.Matrix, ct);

            failureCode = "private_network_effective_state_mismatch";
            await StepAsync(operationId, "verifying-effective-state", ct);
            var after = await _reader.ReadAsync(path, ct);
            EnsureDesired(after, address.Cidr, action);
            var federationVerification = await _federationVerifier.VerifyAsync(
                manifest,
                new CanonicalFederationPolicyRequest(federation.Mode, federation.Allowlist),
                ct);
            if (!federationVerification.Succeeded)
            {
                throw new PrivateNetworkFederationException(
                    "private_network_effective_state_mismatch",
                    "The existing Public or Restricted federation state did not return to a verified healthy state after restart.");
            }

            var succeeded = Outcome(
                "succeeded",
                operationId,
                manifest,
                action,
                address.Cidr,
                after.Entries,
                false,
                null,
                null,
                action == PrivateNetworkExceptionActions.Add
                    ? "The exact private network exception is active and Matrix returned to a verified healthy federation state."
                    : "The exact private network exception was removed and Matrix returned to a verified healthy federation state.");
            await _operations.CompleteAsync(
                operationId,
                "succeeded",
                "completed",
                succeeded,
                new { snapshot.BeforeConfigSha256, snapshot.CandidateConfigSha256, federationVerification.Checks },
                ct);
            await TryAuditAsync("security.private_network_exception.apply.succeeded", "succeeded", operationId, actorOperatorId, ct);
            return succeeded;
        }
        catch (Exception ex) when (mutationStarted && snapshot is not null)
        {
            _logger.LogWarning(ex,
                "Private network exception apply failed after mutation; rollback will be attempted. StackId={StackId} OperationId={OperationId}",
                manifest.StackId,
                operationId);
            return await RollbackAsync(
                manifest,
                federation,
                current,
                action,
                address.Cidr,
                operationId,
                snapshot!,
                failureCode,
                ex,
                actorOperatorId);
        }
        catch (Exception ex)
        {
            var code = ex is PrivateNetworkFederationException known
                ? known.Code
                : ex is OperationCanceledException
                    ? "private_network_operation_timeout"
                    : failureCode;
            var failed = Outcome(
                "failed",
                operationId,
                manifest,
                action,
                address.Cidr,
                current.Entries,
                false,
                null,
                code,
                "The private network exception was not applied and no active configuration mutation was confirmed.");
            await TryFailAsync(operationId, "failed-before-mutation", code, failed, ex);
            await TryAuditAsync("security.private_network_exception.apply.failed", "failed", operationId, actorOperatorId, CancellationToken.None);
            return failed;
        }
    }

    private async Task<PrivateNetworkFederationApplyResponse> RollbackAsync(
        RuntimeStackManifest manifest,
        RuntimeStackFederationStateResponse federation,
        SynapsePrivateNetworkConfigReadResult previous,
        string action,
        string cidr,
        Guid operationId,
        PrivateNetworkConfigSnapshot snapshot,
        string applyErrorCode,
        Exception applyError,
        Guid? actorOperatorId)
    {
        using var timeout = new CancellationTokenSource(RollbackTimeout);
        var ct = timeout.Token;
        try
        {
            await TryStepAsync(operationId, "rollback-started", ct);
            await TryStepAsync(operationId, "restoring-config", ct);
            await ReplaceAsync(snapshot.BeforeConfigPath, snapshot.ActiveConfigPath, null, ct);
            await TryStepAsync(operationId, "restarting-matrix-after-config-restore", ct);
            await _matrixLifecycle.RestartAsync(manifest.Matrix, ct);
            await TryStepAsync(operationId, "verifying-rollback", ct);
            var restored = await _reader.ReadAsync(snapshot.ActiveConfigPath, ct);
            var exactConfigRestored = restored.Supported &&
                restored.Entries.SequenceEqual(previous.Entries, StringComparer.Ordinal);
            var federationVerification = await _federationVerifier.VerifyAsync(
                manifest,
                new CanonicalFederationPolicyRequest(federation.Mode, federation.Allowlist),
                ct);
            if (!exactConfigRestored || !federationVerification.Succeeded)
            {
                throw new InvalidOperationException("The previous Synapse state could not be verified after rollback.");
            }

            var rolledBack = Outcome(
                "rolled_back",
                operationId,
                manifest,
                action,
                cidr,
                restored.Entries,
                true,
                true,
                applyErrorCode,
                "The requested private network exception change was not applied. MEM restored and verified the exact previous Synapse configuration.");
            await _operations.CompleteAsync(
                operationId,
                "rolled_back",
                "rolled-back",
                rolledBack,
                new { snapshot.BeforeConfigSha256, snapshot.CandidateConfigSha256, applyErrorCode, applyError = applyError.GetType().Name },
                ct);
            await TryAuditAsync("security.private_network_exception.apply.rolled_back", "rolled_back", operationId, actorOperatorId, ct);
            return rolledBack;
        }
        catch (Exception rollbackError)
        {
            var failed = Outcome(
                "failed",
                operationId,
                manifest,
                action,
                cidr,
                [],
                true,
                false,
                "private_network_rollback_failed",
                "MEM could not restore a verified Matrix service state automatically. The private operation snapshots have been retained for recovery.");
            await TryFailAsync(
                operationId,
                "rollback-failed",
                "private_network_rollback_failed",
                failed,
                rollbackError);
            await TryAuditAsync("security.private_network_exception.apply.failed", "failed", operationId, actorOperatorId, CancellationToken.None);
            return failed;
        }
    }

    private async Task<PrivateNetworkFederationStackStateResponse> ReadStackStateAsync(
        RuntimeStackManifest manifest,
        CancellationToken ct)
    {
        try
        {
            var path = RequireOwnedConfigPath(manifest);
            var config = await _reader.ReadAsync(path, ct);
            var federation = await _federationState.GetAsync(manifest.StackId.ToString("D"), ct);
            RuntimeMatrixContainerObservation? container = null;
            try
            {
                container = await _matrixLifecycle.InspectAsync(manifest.Matrix, ct);
            }
            catch
            {
                // Inventory remains useful when container inspection is unavailable.
            }

            var latest = (await _operations.ListForStackAsync(manifest.StackId, 20, ct))
                .FirstOrDefault(x => string.Equals(x.Operation, OperationName, StringComparison.Ordinal));
            return new PrivateNetworkFederationStackStateResponse(
                manifest.StackId,
                manifest.Slug,
                manifest.Matrix.ServerName ?? manifest.Matrix.PublicHost ?? manifest.Slug,
                federation?.Mode ?? FederationModes.Unknown,
                federation?.ConfigurationState ?? FederationConfigurationStates.Unavailable,
                config.Supported ? PrivateNetworkConfigurationStates.Healthy : PrivateNetworkConfigurationStates.CustomUnsupported,
                config.Entries,
                container?.Running == true,
                latest?.Id,
                latest?.Status,
                latest?.CurrentStep,
                config.ProblemCode,
                config.Detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new PrivateNetworkFederationStackStateResponse(
                manifest.StackId,
                manifest.Slug,
                manifest.Matrix.ServerName ?? manifest.Matrix.PublicHost ?? manifest.Slug,
                FederationModes.Unknown,
                FederationConfigurationStates.Unavailable,
                PrivateNetworkConfigurationStates.Unavailable,
                [],
                false,
                null,
                null,
                null,
                "private_network_state_unavailable",
                "MEM could not inspect the stack's private network exceptions safely.");
        }
    }

    private async Task<RuntimeStackFederationStateResponse> RequireApplyableFederationStateAsync(
        RuntimeStackManifest manifest,
        string action,
        CancellationToken ct)
    {
        var state = await _federationState.GetAsync(manifest.StackId.ToString("D"), ct)
            ?? throw new PrivateNetworkFederationException("private_network_stack_not_found", "The Runtime Stack disappeared during inspection.");
        if (!IsFederationStateEligibleForAction(
                state.ConfigurationState,
                state.Mode,
                action))
        {
            throw new PrivateNetworkFederationException(
                "private_network_federation_state_unsupported",
                action == PrivateNetworkExceptionActions.Remove
                    ? "Existing exact private network exceptions can be removed only while the stack has a healthy Public, Restricted, or Local-only federation configuration."
                    : "New exact private network exceptions can be added only while the stack has a healthy Public or Restricted federation configuration. Existing exact exceptions may still be removed while Local-only is healthy.");
        }

        if (string.IsNullOrWhiteSpace(state.StateFingerprint))
        {
            throw new PrivateNetworkFederationException("private_network_state_unavailable", "The stack state fingerprint is unavailable.");
        }

        return state;
    }

    internal static bool IsFederationStateEligibleForAction(
        string configurationState,
        string mode,
        string action)
    {
        if (!string.Equals(
                configurationState,
                FederationConfigurationStates.Healthy,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(mode, FederationModes.Public, StringComparison.Ordinal) ||
            string.Equals(mode, FederationModes.Restricted, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(action, PrivateNetworkExceptionActions.Remove, StringComparison.Ordinal) &&
            string.Equals(mode, FederationModes.LocalOnly, StringComparison.Ordinal);
    }

    private async Task<PrivateNetworkConfigSnapshot> CreateSnapshotAsync(
        RuntimeStackManifest manifest,
        Guid operationId,
        SynapsePrivateNetworkConfigReadResult current,
        string cidr,
        bool enabled,
        CancellationToken ct)
    {
        var active = RequireOwnedConfigPath(manifest);
        var dataPath = Path.GetFullPath(manifest.Matrix.DataPath!);
        var directory = Path.Combine(dataPath, ".mem", "private-network", operationId.ToString("D"));
        Directory.CreateDirectory(directory);
        ApplyDirectoryMode(directory);
        var before = Path.Combine(directory, "homeserver.before.yaml");
        var candidate = Path.Combine(directory, "homeserver.candidate.yaml");
        var beforeBytes = await File.ReadAllBytesAsync(active, ct);
        if (!FixedEquals(Hash(beforeBytes), current.FileSha256))
        {
            throw new PrivateNetworkFederationException("private_network_review_stale", "The active Synapse configuration changed before snapshot capture.");
        }

        var candidateBytes = _editor.Render(beforeBytes, cidr, enabled);
        await WritePrivateFileAsync(before, beforeBytes, ct);
        await WritePrivateFileAsync(candidate, candidateBytes, ct);
        return new PrivateNetworkConfigSnapshot(
            directory,
            active,
            before,
            candidate,
            Hash(beforeBytes),
            Hash(candidateBytes));
    }

    private static string RequireOwnedConfigPath(RuntimeStackManifest manifest)
    {
        var dataPathValue = manifest.Matrix.DataPath;
        var configPathValue = manifest.Matrix.ConfigPath;
        if (string.IsNullOrWhiteSpace(dataPathValue) || string.IsNullOrWhiteSpace(configPathValue))
        {
            throw new PrivateNetworkFederationException("private_network_manifest_incomplete", "The Runtime Stack manifest does not contain the Matrix data and config paths.");
        }

        var dataPath = Path.GetFullPath(dataPathValue);
        var configPath = Path.GetFullPath(configPathValue);
        var expected = Path.GetFullPath(Path.Combine(dataPath, "homeserver.yaml"));
        if (!string.Equals(configPath, expected, StringComparison.Ordinal) || !File.Exists(configPath))
        {
            throw new PrivateNetworkFederationException("private_network_config_missing", "The manifest-owned homeserver.yaml file is unavailable.");
        }

        return configPath;
    }

    private static void EnsureConfigSupported(SynapsePrivateNetworkConfigReadResult current)
    {
        if (!current.Supported)
        {
            throw new PrivateNetworkFederationException(
                current.ProblemCode ?? "private_network_config_custom_unsupported",
                current.Detail ?? "The current ip_range_whitelist representation is not safe for automatic editing.");
        }
    }

    private static void EnsureSafeContainer(RuntimeMatrixContainerObservation container)
    {
        if (!container.Running || !container.IdentityMatches || !container.DataBindMatches ||
            !container.ExpectedNetworkAttached || container.DirectHostPortExposed)
        {
            throw new PrivateNetworkFederationException(
                "private_network_container_unsafe",
                "The Matrix container identity, data bind, network, running state, or direct-port posture is not safe for this operation.");
        }
    }

    private static void EnsureDesired(
        SynapsePrivateNetworkConfigReadResult observed,
        string cidr,
        string action)
    {
        EnsureConfigSupported(observed);
        var present = observed.Entries.Contains(cidr, StringComparer.Ordinal);
        if ((action == PrivateNetworkExceptionActions.Add && !present) ||
            (action == PrivateNetworkExceptionActions.Remove && present))
        {
            throw new PrivateNetworkFederationException(
                "private_network_effective_state_mismatch",
                "The active ip_range_whitelist does not match the reviewed exact exception state.");
        }
    }

    private static IReadOnlyList<string> Proposed(
        IReadOnlyList<string> current,
        string cidr,
        string action)
    {
        var entries = current.ToHashSet(StringComparer.Ordinal);
        if (action == PrivateNetworkExceptionActions.Add)
        {
            entries.Add(cidr);
        }
        else
        {
            entries.Remove(cidr);
        }

        return entries.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }


    private CanonicalPrivateNetworkAddress ValidateAddress(string? value, string action) =>
        action == PrivateNetworkExceptionActions.Add
            ? _addressValidator.ValidateExactPrivateAddress(value)
            : _addressValidator.ValidateExactAddressForRemoval(value);

    private static string ValidateAction(string? action)
    {
        var canonical = action?.Trim().ToLowerInvariant();
        return canonical switch
        {
            PrivateNetworkExceptionActions.Add => PrivateNetworkExceptionActions.Add,
            PrivateNetworkExceptionActions.Remove => PrivateNetworkExceptionActions.Remove,
            _ => throw new PrivateNetworkFederationException(
                "private_network_action_invalid",
                "Choose whether to add or remove the exact private network exception.")
        };
    }

    private static string RequireToken(string? value, string code)
    {
        var token = value?.Trim();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256)
        {
            throw new PrivateNetworkFederationException(code, "The reviewed request token is missing or invalid.");
        }

        return token;
    }

    private static string ComputeReviewHash(
        Guid stackId,
        string configHash,
        string stateFingerprint,
        string action,
        string cidr) =>
        Hash(Encoding.UTF8.GetBytes(string.Join("\n", stackId.ToString("D"), configHash, stateFingerprint, action, cidr)));

    private static string ComputeRequestHash(string action, string cidr, string reviewHash) =>
        Hash(Encoding.UTF8.GetBytes(string.Join("\n", action, cidr, reviewHash)));

    private static PrivateNetworkFederationApplyResponse Replay(
        RuntimeOperationDetail replay,
        string requestHash,
        RuntimeStackManifest manifest,
        string action,
        string cidr)
    {
        if (!string.IsNullOrWhiteSpace(replay.InputJson))
        {
            using var input = JsonDocument.Parse(replay.InputJson);
            var observed = input.RootElement.TryGetProperty("requestHash", out var hash)
                ? hash.GetString()
                : null;
            if (!FixedEquals(observed, requestHash))
            {
                throw new PrivateNetworkFederationException(
                    "private_network_idempotency_conflict",
                    "The idempotency key was already used with a different exact private network request.");
            }
        }

        if (!string.IsNullOrWhiteSpace(replay.ResultJson))
        {
            var result = JsonSerializer.Deserialize<PrivateNetworkFederationApplyResponse>(
                replay.ResultJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result is not null)
            {
                return result;
            }
        }

        return Outcome(
            replay.Status == "running" ? "running" : "failed",
            replay.Id,
            manifest,
            action,
            cidr,
            [],
            false,
            null,
            replay.LastError,
            replay.Status == "running"
                ? "The exact private network exception operation is still running."
                : "The existing operation outcome could not be reconstructed safely.");
    }

    private static PrivateNetworkFederationApplyResponse Outcome(
        string status,
        Guid operationId,
        RuntimeStackManifest manifest,
        string action,
        string cidr,
        IReadOnlyList<string> observed,
        bool rollbackAttempted,
        bool? rollbackSucceeded,
        string? errorCode,
        string detail) =>
        new(
            "control-plane",
            status,
            operationId,
            manifest.StackId,
            manifest.Slug,
            action,
            cidr,
            observed,
            rollbackAttempted,
            rollbackSucceeded,
            errorCode,
            detail);

    private async Task StepAsync(Guid operationId, string step, CancellationToken ct) =>
        await _operations.UpdateStepAsync(operationId, step, ct);

    private async Task TryStepAsync(Guid operationId, string step, CancellationToken ct)
    {
        try { await _operations.UpdateStepAsync(operationId, step, ct); } catch { }
    }

    private async Task TryFailAsync(
        Guid operationId,
        string step,
        string code,
        PrivateNetworkFederationApplyResponse result,
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
        catch { }
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
                    ReasonCode: "private_network_exception_change"),
                ct);
        }
        catch { }
    }

    private static async Task ReplaceAsync(
        string sourcePath,
        string activePath,
        string? expectedActiveSha256,
        CancellationToken ct)
    {
        if (!File.Exists(sourcePath) || !File.Exists(activePath))
        {
            throw new FileNotFoundException("The private network configuration transaction source or active file is missing.");
        }

        if (!string.IsNullOrWhiteSpace(expectedActiveSha256))
        {
            var observed = Hash(await File.ReadAllBytesAsync(activePath, ct));
            if (!FixedEquals(observed, expectedActiveSha256))
            {
                throw new PrivateNetworkFederationException("private_network_review_stale", "The active Synapse configuration changed before atomic replacement.");
            }
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(activePath))
            ?? throw new InvalidOperationException("The active Synapse config directory could not be resolved.");
        var temporary = Path.Combine(directory, $".homeserver.yaml.mem-private-network-{Guid.NewGuid():N}.tmp");
        var mode = TryGetMode(activePath);
        try
        {
            var bytes = await File.ReadAllBytesAsync(sourcePath, ct);
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
                stream.Flush(true);
            }

            if (mode is not null)
            {
                File.SetUnixFileMode(temporary, mode.Value);
            }

            File.Move(temporary, activePath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task WritePrivateFileAsync(string path, byte[] bytes, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
        stream.Flush(true);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void ApplyDirectoryMode(string path)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static UnixFileMode? TryGetMode(string path) =>
        OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            ? File.GetUnixFileMode(path)
            : null;

    private static string Hash(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null) return false;
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
