// HostAgent/Runtime/Coturn/CoturnRuntimeService.cs

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Docker;
using HostAgent.Docker.Models;
using HostAgent.Platform;
using HostAgent.Runtime.ServiceRuntime;
using Microsoft.Extensions.Options;
using Modules.Shared.RuntimeImages;
using Modules.Setup.Platform.Coturn;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Coturn;

public sealed class CoturnRuntimeService : IPlatformCoturnSetupService, ICoturnPlatformMaintenanceRuntime, ICoturnStartupSupervisionRuntime
{
    private readonly DockerClient _dockerClient;
    private readonly IDockerHost _dockerHost;
    private readonly PlatformDomainResolver _domainResolver;
    private readonly RuntimeNetworkOptions _networkOptions;
    private readonly IApprovedCoturnRuntimeProvider _approvedImageProvider;
    private readonly CoturnRuntimeFileStore _fileStore;
    private readonly CoturnCheckEvidenceStore _checkEvidenceStore;
    private readonly TimeProvider _timeProvider;
    private readonly IMemDiagnosticEventWriter? _diagnostics;

    public CoturnRuntimeService(
        DockerClient dockerClient,
        IDockerHost dockerHost,
        PlatformDomainResolver domainResolver,
        IOptions<RuntimeNetworkOptions> networkOptions,
        IApprovedCoturnRuntimeProvider approvedImageProvider,
        CoturnRuntimeFileStore fileStore,
        CoturnCheckEvidenceStore checkEvidenceStore,
        TimeProvider timeProvider,
        IMemDiagnosticEventWriter? diagnostics = null)
    {
        _dockerClient = dockerClient;
        _dockerHost = dockerHost;
        _domainResolver = domainResolver;
        _networkOptions = networkOptions.Value;
        _approvedImageProvider = approvedImageProvider;
        _fileStore = fileStore;
        _checkEvidenceStore = checkEvidenceStore;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
    }

    public async Task<CoturnRuntimeResponse> InspectAsync(CancellationToken ct)
    {
        var domain = await _domainResolver.ResolveMainPlatformDomainAsync(
            requestedDomainId: null,
            ct);

        var expectedBaseDomain = NormalizeDomain(domain.BaseDomain);
        var expectedPublicHost = BuildPublicHost(expectedBaseDomain);
        var files = await _fileStore.ReadAsync(ct);
        var container = await FindContainerAsync(ct);
        var warnings = new List<string>();

        ApprovedCoturnRuntimeDescriptor? approvedImage = null;
        string? approvedImageError = null;
        try
        {
            approvedImage = await _approvedImageProvider.ResolveForOperationAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            approvedImageError = ex.Message;
            warnings.Add(ex.Message);
        }

        var dockerInspection = await TryInspectOwnedContainerAsync(container, ct);
        var dockerEvidence = BuildDockerRuntimeEvidence(container, dockerInspection);
        var state = Observe(
            container,
            dockerInspection,
            dockerEvidence,
            files,
            approvedImage,
            expectedBaseDomain);

        AddInspectionWarnings(
            warnings,
            state,
            dockerEvidence,
            files,
            expectedBaseDomain,
            TryGetLabel(container, "mem.coturn.baseDomain"),
            approvedImageError);

        return BuildResponse(
            container,
            dockerInspection,
            dockerEvidence,
            files,
            approvedImage,
            expectedBaseDomain,
            expectedPublicHost,
            recreated: false,
            warnings,
            detail: BuildInspectionDetail(state.Evaluation));
    }

    private static string BuildInspectionDetail(
        CoturnReadinessEvaluation evaluation)
    {
        if (evaluation.Ready)
        {
            return "Coturn is running with exact MEM-managed Docker runtime configuration and verified protected setup evidence.";
        }

        if (string.Equals(
                evaluation.OperatorStatus,
                CoturnOperatorStatuses.VerificationLimited,
                StringComparison.Ordinal))
        {
            return "Coturn Docker runtime configuration is exact, but protected host-file verification is intentionally restricted in this execution context.";
        }

        return "Coturn is not yet runtime-ready. Review the operator status and exact Docker evidence.";
    }

    public Task<CoturnRuntimeResponse> EnsureStartedAsync(
        CoturnEnsureRequest request,
        CancellationToken ct) =>
        EnsureAsync(
            request,
            CoturnRuntimeImageBoundary.Operation,
            ct);

    public async Task<CoturnRuntimeResponse> RestartOwnedAsync(
        CancellationToken ct)
    {
        var before = await InspectAsync(ct);

        if (string.Equals(
                before.ProtectedEvidenceAccess,
                CoturnProtectedEvidenceAccess.Restricted,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Restart & Verify requires protected Coturn authority that is unavailable in this host-native execution mode.");
        }

        if (!before.ContainerExists ||
            !before.OwnershipVerified ||
            string.IsNullOrWhiteSpace(before.ContainerId))
        {
            throw new InvalidOperationException(
                "Restart & Verify requires an existing MEM-owned Coturn container.");
        }

        if (!CoturnPlatformMaintenancePolicy.CanRestartAndVerify(before))
        {
            throw new InvalidOperationException(
                "Restart & Verify requires a healthy owned Coturn service or restart-policy-only drift. Use Repair for other runtime or protected setup drift.");
        }

        var current = await FindContainerAsync(ct);
        if (current is null ||
            !CoturnRuntimePolicy.IsOwned(current.Labels) ||
            !string.Equals(current.ID, before.ContainerId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Coturn runtime identity changed after maintenance preflight. Refresh the workspace before retrying.");
        }

        // A restart-policy mismatch is safe to correct in-place. Do this before
        // stopping the relay, and only when the maintenance policy proved that
        // no other runtime/configuration drift is present.
        if (before.RuntimeDrift.Count == 1 &&
            string.Equals(
                before.RuntimeDrift[0],
                "restart-policy",
                StringComparison.Ordinal))
        {
            await _dockerClient.Containers.UpdateContainerAsync(
                current.ID,
                new ContainerUpdateParameters
                {
                    RestartPolicy = new RestartPolicy
                    {
                        Name = RestartPolicyKind.UnlessStopped
                    }
                },
                ct);

            var corrected = await InspectAsync(ct);
            if (!corrected.RuntimeExact ||
                !string.Equals(
                    corrected.OperatorStatus,
                    CoturnOperatorStatuses.RuntimeReady,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    corrected.Detail ??
                    "Coturn restart-policy correction did not restore exact Runtime ready state.");
            }
        }

        try
        {
            await _dockerHost.StopContainerAsync(current.ID, ct);
            await _dockerHost.StartContainerAsync(current.ID, ct);

            var deadline = _timeProvider.GetUtcNow().AddSeconds(30);
            CoturnRuntimeResponse? last = null;
            while (_timeProvider.GetUtcNow() <= deadline)
            {
                ct.ThrowIfCancellationRequested();
                last = await InspectAsync(ct);

                if (last.Running &&
                    last.RuntimeExact &&
                    string.Equals(
                        last.OperatorStatus,
                        CoturnOperatorStatuses.RuntimeReady,
                        StringComparison.Ordinal))
                {
                    // Give the listener a short bounded settling window before the
                    // operation processor performs the credentialed allocation probe.
                    await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
                    return last;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            }

            throw new InvalidOperationException(
                last?.Detail ??
                "Coturn did not return to exact Runtime ready state within 30 seconds of restart.");
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            // Once the stop phase begins, cancellation or a later verification
            // failure must not casually strand the shared relay in a stopped
            // state. Attempt a short recovery under a fresh server-owned token,
            // then preserve the original failure for operation evidence.
            var recovered = await TryRecoverCoturnRunningAsync(current.ID);
            if (!recovered)
            {
                throw new InvalidOperationException(
                    "Coturn Restart & Verify failed and MEM could not confirm that the owned Coturn container returned to a running state.",
                    ex);
            }

            throw;
        }
    }

    private async Task<bool> TryRecoverCoturnRunningAsync(string containerId)
    {
        using var recoveryLifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var recoveryToken = recoveryLifetime.Token;

        try
        {
            var current = await FindContainerAsync(recoveryToken);
            if (current is null ||
                !CoturnRuntimePolicy.IsOwned(current.Labels) ||
                !string.Equals(current.ID, containerId, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.Equals(current.State, "running", StringComparison.OrdinalIgnoreCase))
            {
                await _dockerHost.StartContainerAsync(containerId, recoveryToken);
            }

            current = await FindContainerAsync(recoveryToken);
            return current is not null &&
                CoturnRuntimePolicy.IsOwned(current.Labels) &&
                string.Equals(current.ID, containerId, StringComparison.Ordinal) &&
                string.Equals(current.State, "running", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            return false;
        }
    }

    public async Task<CoturnStartupRecoveryMutationResult> RecoverStartupAsync(
        string decision,
        string expectedContainerId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expectedContainerId))
        {
            throw new InvalidOperationException(
                "Coturn startup recovery requires the exact preflight container identity.");
        }

        if (decision is not (
                CoturnStartupSupervisionDecisions.StartStopped or
                CoturnStartupSupervisionDecisions.CorrectRestartPolicy))
        {
            throw new InvalidOperationException(
                "Coturn startup recovery only supports safe stopped-container start or restart-policy correction.");
        }

        var before = await InspectAsync(ct);
        var currentDecision = CoturnStartupSupervisionPolicy.Evaluate(before);

        if (string.Equals(
                currentDecision,
                CoturnStartupSupervisionDecisions.Ready,
                StringComparison.Ordinal))
        {
            return new CoturnStartupRecoveryMutationResult(
                before,
                MutationPerformed: false,
                ContainerStarted: false,
                RestartPolicyCorrected: false);
        }

        if (!string.Equals(currentDecision, decision, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Coturn startup runtime state changed after supervision preflight. MEM will not mutate an ambiguous target.");
        }

        if (!before.OwnershipVerified ||
            !string.Equals(before.ContainerId, expectedContainerId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Coturn startup recovery target identity no longer matches the exact MEM-owned preflight container.");
        }

        var current = await FindContainerAsync(ct);
        if (current is null ||
            !CoturnRuntimePolicy.IsOwned(current.Labels) ||
            !string.Equals(current.ID, expectedContainerId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Coturn startup recovery could not re-resolve the exact MEM-owned container.");
        }

        var restartPolicyCorrected = false;
        if (before.DockerRuntime?.RestartPolicyMatches == false)
        {
            await _dockerClient.Containers.UpdateContainerAsync(
                current.ID,
                new ContainerUpdateParameters
                {
                    RestartPolicy = new RestartPolicy
                    {
                        Name = RestartPolicyKind.UnlessStopped
                    }
                },
                ct);
            restartPolicyCorrected = true;
        }

        var containerStarted = false;
        if (string.Equals(
                decision,
                CoturnStartupSupervisionDecisions.StartStopped,
                StringComparison.Ordinal))
        {
            await _dockerHost.StartContainerAsync(current.ID, ct);
            containerStarted = true;
        }

        var deadline = _timeProvider.GetUtcNow().AddSeconds(30);
        CoturnRuntimeResponse? last = null;
        while (_timeProvider.GetUtcNow() <= deadline)
        {
            ct.ThrowIfCancellationRequested();
            last = await InspectAsync(ct);

            if (last.Running &&
                last.RuntimeExact &&
                string.Equals(
                    last.OperatorStatus,
                    CoturnOperatorStatuses.RuntimeReady,
                    StringComparison.Ordinal))
            {
                return new CoturnStartupRecoveryMutationResult(
                    last,
                    MutationPerformed: restartPolicyCorrected || containerStarted,
                    ContainerStarted: containerStarted,
                    RestartPolicyCorrected: restartPolicyCorrected);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }

        throw new InvalidOperationException(
            last?.Detail ??
            "Coturn did not reach exact Runtime ready state within the bounded startup recovery window.");
    }

    public Task<CoturnRuntimeResponse> RepairAsync(
        string? externalIp,
        CancellationToken cancellationToken) =>
        EnsureAsync(
            new CoturnEnsureRequest(
                ExternalIp: externalIp,
                ForceRecreate: true,
                PublishRelayPorts: true),
            CoturnRuntimeImageBoundary.Operation,
            cancellationToken);

    public async Task<PlatformCoturnSetupResult> EnsureInstalledAsync(
        PlatformCoturnSetupRequest request,
        CancellationToken cancellationToken)
    {
        var response = await EnsureAsync(
            CreateSetupEnsureRequest(request),
            CoturnRuntimeImageBoundary.Installation,
            cancellationToken);

        return ToSetupResult(response);
    }

    internal static CoturnEnsureRequest CreateSetupEnsureRequest(
        PlatformCoturnSetupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new CoturnEnsureRequest(
            request.ExternalIp,
            request.ForceRecreate,
            PublishRelayPorts: true);
    }

    async Task<PlatformCoturnSetupResult> IPlatformCoturnSetupService.InspectAsync(
        CancellationToken cancellationToken) =>
        ToSetupResult(await InspectAsync(cancellationToken));

    private async Task<CoturnRuntimeResponse> EnsureAsync(
        CoturnEnsureRequest request,
        CoturnRuntimeImageBoundary imageBoundary,
        CancellationToken ct)
    {
        var domain = await _domainResolver.ResolveMainPlatformDomainAsync(
            requestedDomainId: null,
            ct);

        var expectedBaseDomain = NormalizeDomain(domain.BaseDomain);
        var expectedPublicHost = BuildPublicHost(expectedBaseDomain);
        var normalizedExternalIp = CoturnRuntimePolicy.NormalizeExternalIp(
            request.ExternalIp);

        var approvedImage = await ResolveApprovedImageAsync(
            _approvedImageProvider,
            imageBoundary,
            ct);
        var existing = await FindContainerAsync(ct);

        if (existing is not null && !CoturnRuntimePolicy.IsOwned(existing.Labels))
        {
            throw new InvalidOperationException(
                $"A container named '{CoturnRuntimePolicy.ContainerName}' exists but is not owned by MEM. " +
                "MEM will not inspect, remove, or replace the conflicting container.");
        }

        var files = await _fileStore.EnsureAsync(
            expectedBaseDomain,
            expectedBaseDomain,
            normalizedExternalIp,
            ct);

        if (!files.SecretPresent || string.IsNullOrWhiteSpace(files.ConfigSha256))
        {
            throw new InvalidOperationException(
                "Coturn secret and protected configuration could not be prepared.");
        }

        var shouldRecreate = existing is not null &&
            (request.ForceRecreate ||
             !ContainerMatchesDesiredState(
                 existing,
                 approvedImage,
                 expectedBaseDomain,
                 normalizedExternalIp,
                 request.PublishRelayPorts,
                 files.ConfigSha256));

        var recreated = false;
        if (existing is not null && shouldRecreate)
        {
            await _dockerClient.Containers.RemoveContainerAsync(
                existing.ID,
                new ContainerRemoveParameters
                {
                    Force = true,
                    RemoveVolumes = false
                },
                ct);

            existing = null;
            recreated = true;
        }

        if (existing is null)
        {
            var spec = BuildContainerSpec(
                approvedImage,
                expectedBaseDomain,
                expectedPublicHost,
                normalizedExternalIp,
                request.PublishRelayPorts,
                files.ConfigSha256,
                _fileStore.ConfigPath,
                _networkOptions.GatewayNetworkName);

            var containerId = await _dockerHost.CreateContainerAsync(spec, ct);
            await _dockerHost.StartContainerAsync(containerId, ct);
            existing = await FindContainerAsync(ct);
        }
        else if (!string.Equals(existing.State, "running", StringComparison.OrdinalIgnoreCase))
        {
            await _dockerClient.Containers.StartContainerAsync(
                existing.ID,
                new ContainerStartParameters(),
                ct);

            existing = await FindContainerAsync(ct);
        }

        if (existing is not null && !CoturnRuntimePolicy.IsOwned(existing.Labels))
        {
            throw new InvalidOperationException(
                "Docker returned a Coturn container that does not carry the required MEM ownership labels.");
        }

        var dockerInspection = await TryInspectOwnedContainerAsync(existing, ct);
        var dockerEvidence = BuildDockerRuntimeEvidence(existing, dockerInspection);
        var state = Observe(
            existing,
            dockerInspection,
            dockerEvidence,
            files,
            approvedImage,
            expectedBaseDomain);

        var warnings = new List<string>
        {
            $"DNS must point {expectedPublicHost} to this server's public IP."
        };

        if (normalizedExternalIp is null)
        {
            warnings.Add(
                "No external IP is configured. Hosts behind NAT normally require an explicit public IP.");
        }

        if (request.PublishRelayPorts)
        {
            warnings.Add(
                $"Firewall must allow {CoturnRuntimePolicy.TurnPort}/udp, " +
                $"{CoturnRuntimePolicy.TurnPort}/tcp, and UDP relay range " +
                $"{CoturnRuntimePolicy.RelayMinPort}-{CoturnRuntimePolicy.RelayMaxPort}.");
        }
        else
        {
            warnings.Add(
                "Relay ports were deliberately omitted. This development-only posture is not TURN-ready.");
        }

        AddInspectionWarnings(
            warnings,
            state,
            dockerEvidence,
            files,
            expectedBaseDomain,
            TryGetLabel(existing, "mem.coturn.baseDomain"),
            approvedImageError: null);

        return BuildResponse(
            existing,
            dockerInspection,
            dockerEvidence,
            files,
            approvedImage,
            expectedBaseDomain,
            expectedPublicHost,
            recreated,
            warnings,
            detail: state.Evaluation.Ready
                ? "Coturn platform service is running with exact MEM-managed Docker runtime configuration."
                : "Coturn was ensured, but one or more runtime-readiness requirements are not satisfied.");
    }

    public async Task<CoturnCheckResponse> CheckAsync(CancellationToken ct)
    {
        var checkStartedAtUtc = _timeProvider.GetUtcNow();
        var previousEvidence = await _checkEvidenceStore.ReadLatestAsync(ct);
        var domain = await _domainResolver.ResolveMainPlatformDomainAsync(
            requestedDomainId: null,
            ct);

        var expectedBaseDomain = NormalizeDomain(domain.BaseDomain);
        var expectedPublicHost = BuildPublicHost(expectedBaseDomain);
        var files = await _fileStore.ReadAsync(ct);
        var container = await FindContainerAsync(ct);

        ApprovedCoturnRuntimeDescriptor? approvedImage = null;
        string? approvedImageError = null;
        try
        {
            approvedImage = await _approvedImageProvider.ResolveForOperationAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            approvedImageError = ex.Message;
        }

        var dockerInspection = await TryInspectOwnedContainerAsync(container, ct);
        var dockerEvidence = BuildDockerRuntimeEvidence(container, dockerInspection);
        var state = Observe(
            container,
            dockerInspection,
            dockerEvidence,
            files,
            approvedImage,
            expectedBaseDomain);
        var checks = new List<CoturnCheckItem>();

        checks.Add(new CoturnCheckItem(
            Key: "container",
            Status: state.Observation.ContainerExists &&
                    state.OwnershipVerified &&
                    state.Observation.Running
                ? CoturnCheckStatuses.Passed
                : CoturnCheckStatuses.Failed,
            Summary: state.Observation.ContainerExists
                ? state.OwnershipVerified
                    ? state.Observation.Running
                        ? "The MEM-owned Coturn container is running."
                        : "The MEM-owned Coturn container is not running."
                    : "A container named mem-coturn exists but is not owned by MEM."
                : "The Coturn container is not deployed."));

        checks.Add(new CoturnCheckItem(
            Key: "docker-runtime",
            Status: dockerEvidence is not null &&
                    state.Observation.RuntimeStateHealthy
                ? CoturnCheckStatuses.Passed
                : CoturnCheckStatuses.Failed,
            Summary: dockerEvidence is null
                ? "Exact Docker runtime inspection is unavailable."
                : state.Observation.RuntimeStateHealthy
                    ? "Docker reports a stable Coturn runtime state with no restart, OOM, dead, or state-error flags."
                    : "Docker reports a degraded Coturn runtime state. Review restart, OOM, exit, and state evidence."));

        checks.Add(new CoturnCheckItem(
            Key: "restart-policy",
            Status: dockerEvidence?.RestartPolicyMatches == true
                ? CoturnCheckStatuses.Passed
                : CoturnCheckStatuses.Failed,
            Summary: dockerEvidence?.RestartPolicyMatches == true
                ? $"Docker restart policy is {CoturnRuntimePolicy.ExpectedRestartPolicy}."
                : $"Docker restart policy does not match the required {CoturnRuntimePolicy.ExpectedRestartPolicy} policy.",
            Detail: dockerEvidence is null
                ? null
                : $"Observed restart policy: {dockerEvidence.RestartPolicy}; restart count: {dockerEvidence.RestartCount}."));

        checks.Add(new CoturnCheckItem(
            Key: "gateway-network",
            Status: dockerEvidence is not null &&
                    dockerEvidence.ExpectedNetworkAttached &&
                    dockerEvidence.NetworkAliasesMatch
                ? CoturnCheckStatuses.Passed
                : CoturnCheckStatuses.Failed,
            Summary: dockerEvidence is not null &&
                     dockerEvidence.ExpectedNetworkAttached &&
                     dockerEvidence.NetworkAliasesMatch
                ? "Coturn is attached to the expected MEM gateway network with the required aliases."
                : "Coturn gateway-network attachment or aliases do not match the MEM runtime contract."));

        checks.Add(new CoturnCheckItem(
            Key: "config-mount",
            Status: dockerEvidence is not null &&
                    dockerEvidence.ConfigMountPresent &&
                    dockerEvidence.ConfigMountReadOnly &&
                    dockerEvidence.ConfigMountSourceMatches &&
                    dockerEvidence.ConfigMountDestinationMatches
                ? CoturnCheckStatuses.Passed
                : CoturnCheckStatuses.Failed,
            Summary: dockerEvidence is not null &&
                     dockerEvidence.ConfigMountPresent &&
                     dockerEvidence.ConfigMountReadOnly &&
                     dockerEvidence.ConfigMountSourceMatches &&
                     dockerEvidence.ConfigMountDestinationMatches
                ? "The protected Coturn configuration is mounted from the expected host source to the expected read-only container destination."
                : "The Coturn configuration mount does not match the expected protected read-only binding."));

        checks.Add(new CoturnCheckItem(
            Key: "approved-image",
            Status: state.ImageApproved
                ? CoturnCheckStatuses.Passed
                : CoturnCheckStatuses.Failed,
            Summary: state.ImageApproved
                ? "Coturn is running the locally resolved MEM-approved immutable image."
                : "Coturn is not running the locally resolved MEM-approved immutable image.",
            Detail: approvedImageError));

        var protectedEvidenceRestricted = string.Equals(
            files.ProtectedEvidenceAccess,
            CoturnProtectedEvidenceAccess.Restricted,
            StringComparison.Ordinal);
        var protectedEvidenceUnavailable = string.Equals(
            files.ProtectedEvidenceAccess,
            CoturnProtectedEvidenceAccess.Unavailable,
            StringComparison.Ordinal);

        var configurationReady =
            files.ConfigPresent &&
            state.Observation.ConfigHashMatches &&
            state.SecurityPolicyApplied;
        checks.Add(new CoturnCheckItem(
            Key: "configuration",
            Status: protectedEvidenceRestricted || protectedEvidenceUnavailable
                ? CoturnCheckStatuses.NotRun
                : configurationReady
                    ? CoturnCheckStatuses.Passed
                    : CoturnCheckStatuses.Failed,
            Summary: protectedEvidenceRestricted
                ? "Protected Coturn configuration verification is restricted in this execution context."
                : protectedEvidenceUnavailable
                    ? "Protected Coturn configuration verification is unavailable."
                    : configurationReady
                        ? "The protected Coturn configuration matches the container and release security policy."
                        : "The protected Coturn configuration is missing, drifted, or does not satisfy the release security policy."));

        checks.Add(new CoturnCheckItem(
            Key: "shared-secret",
            Status: protectedEvidenceRestricted || protectedEvidenceUnavailable
                ? CoturnCheckStatuses.NotRun
                : files.SecretPresent
                    ? CoturnCheckStatuses.Passed
                    : CoturnCheckStatuses.Failed,
            Summary: protectedEvidenceRestricted
                ? "Protected TURN shared-secret verification is restricted in this execution context."
                : protectedEvidenceUnavailable
                    ? "Protected TURN shared-secret verification is unavailable."
                    : files.SecretPresent
                        ? "The protected platform TURN shared secret is available."
                        : "The protected platform TURN shared secret is unavailable."));

        var listenerPortsPublished =
            container is not null &&
            HasRequiredListenerPorts(container);
        checks.Add(new CoturnCheckItem(
            Key: "listener-ports",
            Status: listenerPortsPublished
                ? CoturnCheckStatuses.Passed
                : CoturnCheckStatuses.Failed,
            Summary: listenerPortsPublished
                ? $"TURN listener ports {CoturnRuntimePolicy.TurnPort}/tcp and {CoturnRuntimePolicy.TurnPort}/udp are published."
                : $"TURN listener ports {CoturnRuntimePolicy.TurnPort}/tcp and {CoturnRuntimePolicy.TurnPort}/udp are not both published."));

        checks.Add(new CoturnCheckItem(
            Key: "relay-ports",
            Status: state.RelayPortsPublished
                ? CoturnCheckStatuses.Passed
                : CoturnCheckStatuses.Failed,
            Summary: state.RelayPortsPublished
                ? $"UDP relay range {CoturnRuntimePolicy.RelayMinPort}-{CoturnRuntimePolicy.RelayMaxPort} is published."
                : $"UDP relay range {CoturnRuntimePolicy.RelayMinPort}-{CoturnRuntimePolicy.RelayMaxPort} is not fully published."));

        checks.Add(new CoturnCheckItem(
            Key: "platform-domain",
            Status: state.DomainDriftDetected
                ? CoturnCheckStatuses.Failed
                : CoturnCheckStatuses.Passed,
            Summary: state.DomainDriftDetected
                ? "Coturn is configured for a different platform domain."
                : "Coturn matches the current main platform domain."));

        var hostDnsResolved = false;
        var resolvedAddresses = Array.Empty<string>();
        try
        {
            resolvedAddresses = (await Dns.GetHostAddressesAsync(expectedPublicHost, ct))
                .Distinct()
                .Select(address => address.ToString())
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            hostDnsResolved = resolvedAddresses.Any(value =>
                IPAddress.TryParse(value, out var address) &&
                !IPAddress.IsLoopback(address));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            resolvedAddresses = Array.Empty<string>();
        }

        var hostDnsDetail = resolvedAddresses.Length == 0
            ? "The host resolver returned no addresses. This check does not query authoritative public DNS."
            : "Host resolver addresses: " +
              string.Join(", ", resolvedAddresses) +
              ". This may reflect MEM local split DNS and is not authoritative public-DNS proof.";

        checks.Add(new CoturnCheckItem(
            Key: "host-dns",
            Status: hostDnsResolved
                ? CoturnCheckStatuses.Passed
                : CoturnCheckStatuses.Failed,
            Summary: hostDnsResolved
                ? $"The host resolver returned a non-loopback address for {expectedPublicHost}."
                : $"The host resolver did not return a non-loopback address for {expectedPublicHost}.",
            Detail: hostDnsDetail));

        var externalIp = TryGetLabel(container, "mem.coturn.externalIp");

        CoturnCheckItem probeDnsCheck;
        CoturnAllocationProbeResponse allocation;
        if (!state.Evaluation.Ready)
        {
            probeDnsCheck = BuildProbeDnsNotRunCheck(
                "Probe-runtime DNS resolution was not evaluated because the platform service is not structurally ready.");
            allocation = new CoturnAllocationProbeResponse(
                Status: CoturnCheckStatuses.NotRun,
                Transport: "udp",
                Summary: "The local TURN allocation probe was not run because the platform service is not structurally ready.",
                LogTail: null);
        }
        else if (!hostDnsResolved)
        {
            probeDnsCheck = BuildProbeDnsNotRunCheck(
                "Probe-runtime DNS resolution was not evaluated because the TURN host did not resolve through the Control Plane resolver.");
            allocation = new CoturnAllocationProbeResponse(
                Status: CoturnCheckStatuses.NotRun,
                Transport: "udp",
                Summary: "The local TURN allocation probe was not run because the TURN host did not resolve through the host resolver.",
                LogTail: null);
        }
        else if (container is null ||
                 string.IsNullOrWhiteSpace(files.SecretValue))
        {
            probeDnsCheck = BuildProbeDnsNotRunCheck(
                "Probe-runtime DNS resolution was not evaluated because the owned container or protected credential was unavailable.");
            allocation = new CoturnAllocationProbeResponse(
                Status: CoturnCheckStatuses.NotRun,
                Transport: "udp",
                Summary: "The local TURN allocation probe was not run because the owned container or protected credential was unavailable.",
                LogTail: null);
        }
        else
        {
            probeDnsCheck = await CheckProbeRuntimeDnsAsync(
                container.ID,
                expectedPublicHost,
                ct);

            if (!string.Equals(
                    probeDnsCheck.Status,
                    CoturnCheckStatuses.Passed,
                    StringComparison.Ordinal))
            {
                allocation = new CoturnAllocationProbeResponse(
                    Status: CoturnCheckStatuses.NotRun,
                    Transport: "udp",
                    Summary: "The local TURN allocation probe was not run because DNS resolution was unavailable in the Coturn probe runtime.",
                    LogTail: null);
            }
            else
            {
                allocation = await RunAllocationProbeAsync(
                    container.ID,
                    expectedPublicHost,
                    files.SecretValue,
                    checkStartedAtUtc,
                    ct);
            }
        }

        checks.Add(probeDnsCheck);
        checks.Add(BuildExternalIpCheck(externalIp, allocation));

        var warnings = new List<string>();
        AddInspectionWarnings(
            warnings,
            state,
            dockerEvidence,
            files,
            expectedBaseDomain,
            TryGetLabel(container, "mem.coturn.baseDomain"),
            approvedImageError);

        if (!string.IsNullOrWhiteSpace(previousEvidence.WarningCode))
        {
            warnings.Add(
                "Previous Coturn functional-check evidence could not be read. The current check result remains authoritative.");
        }

        var status = CoturnDiagnosticsPolicy.SummarizeStatus(checks, allocation);
        var detail = status switch
        {
            CoturnCheckStatuses.Failed =>
                "Coturn needs attention. Review the failed checks before connecting stacks.",
            CoturnCheckStatuses.Warning =>
                "Coturn completed the available platform checks with warnings. Review them before connecting stacks.",
            _ =>
                "Coturn passed the platform structural, host-resolution, and local allocation checks. External client reachability is still a separate proof."
        };
        var checkedAtUtc = _timeProvider.GetUtcNow();

        var result = new CoturnCheckResponse(
            Source: "control-plane",
            Status: status,
            CheckedAtUtc: checkedAtUtc,
            FreshUntilUtc: checkedAtUtc.AddSeconds(
                CoturnDiagnosticsPolicy.FunctionalCheckFreshForSeconds),
            ContainerState: state.Evaluation.ContainerState,
            Readiness: state.Evaluation.Readiness,
            PublicHost: expectedPublicHost,
            RuntimeContainerId: container?.ID,
            RuntimeStartedAtUtc: dockerInspection?.StartedAtUtc,
            RuntimeRestartCount: dockerInspection?.RestartCount,
            Checks: checks,
            Allocation: allocation,
            Warnings: warnings.Distinct(StringComparer.Ordinal).ToArray(),
            Detail: detail,
            EvidencePersisted: false,
            IncidentId: null);

        var diagnostic = await RecordFunctionalCheckDiagnosticAsync(
            result,
            previousEvidence.Result);

        var diagnosticWarnings = string.IsNullOrWhiteSpace(diagnostic.WarningCode)
            ? result.Warnings
            : result.Warnings
                .Append(
                    "Coturn functional-check diagnostic evidence could not be recorded. The check result remains available.")
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        result = result with
        {
            IncidentId = diagnostic.IncidentId,
            Warnings = diagnosticWarnings
        };

        var persistedResult = result with
        {
            EvidencePersisted = true
        };
        var writeResult = await _checkEvidenceStore.WriteLatestAsync(
            persistedResult,
            ct);

        if (writeResult.Stored)
        {
            return persistedResult;
        }

        var persistenceWarnings = result.Warnings
            .Append(
                "The latest Coturn functional-check result could not be persisted. The current browser result remains available for this request.")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return result with
        {
            Warnings = persistenceWarnings,
            EvidencePersisted = false
        };
    }

    internal static CoturnCheckItem BuildExternalIpCheck(
        string? externalIp,
        CoturnAllocationProbeResponse allocation) =>
        CoturnRelayAddressPolicy.Evaluate(externalIp, allocation);

    public async Task<CoturnLatestCheckResponse> GetLatestCheckAsync(
        CancellationToken ct)
    {
        var observedAtUtc = _timeProvider.GetUtcNow();
        var evidence = await _checkEvidenceStore.ReadLatestAsync(ct);
        var evidenceUnavailable = !string.IsNullOrWhiteSpace(
            evidence.WarningCode);
        var runtimeChanged = false;

        if (evidence.Result is not null && !evidenceUnavailable)
        {
            try
            {
                var currentContainer = await FindContainerAsync(ct);
                var currentInspection = await TryInspectOwnedContainerAsync(
                    currentContainer,
                    ct);

                if (evidence.Result.RuntimeContainerId is not null &&
                    currentContainer is not null &&
                    CoturnRuntimePolicy.IsOwned(currentContainer.Labels) &&
                    currentInspection is null)
                {
                    evidenceUnavailable = true;
                }
                else
                {
                    runtimeChanged = RuntimeChangedSinceCheck(
                        evidence.Result,
                        currentContainer?.ID,
                        currentContainer is not null &&
                        CoturnRuntimePolicy.IsOwned(currentContainer.Labels),
                        currentInspection?.StartedAtUtc,
                        currentInspection?.RestartCount,
                        currentInspection is not null);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                evidenceUnavailable = true;
            }
        }

        var freshness = CoturnDiagnosticsPolicy.EvaluateFreshness(
            evidence.Result,
            observedAtUtc,
            evidenceUnavailable,
            runtimeChanged);

        var warnings = evidenceUnavailable
            ? new[]
            {
                "The latest Coturn functional-check evidence could not be read from the Control Plane state root."
            }
            : Array.Empty<string>();

        var detail = freshness.Freshness switch
        {
            CoturnCheckFreshnessStatuses.Fresh =>
                "The latest Coturn functional check is fresh.",
            CoturnCheckFreshnessStatuses.Stale =>
                "The latest Coturn functional check is stale. Run Check now before relying on it as current service-health evidence.",
            CoturnCheckFreshnessStatuses.RuntimeChanged =>
                "The Coturn runtime changed after the latest functional check. Run Check now before relying on that evidence.",
            CoturnCheckFreshnessStatuses.NotChecked =>
                "No persisted Coturn functional check is available yet.",
            _ =>
                "Coturn functional-check freshness could not be established."
        };

        return new CoturnLatestCheckResponse(
            Source: "control-plane",
            Freshness: freshness.Freshness,
            Fresh: freshness.Fresh,
            FreshForSeconds: CoturnDiagnosticsPolicy.FunctionalCheckFreshForSeconds,
            ObservedAtUtc: observedAtUtc,
            CheckedAtUtc: evidence.Result?.CheckedAtUtc,
            FreshUntilUtc: freshness.FreshUntilUtc,
            IncidentId: evidence.Result?.IncidentId,
            Result: evidence.Result,
            Warnings: warnings,
            Detail: detail);
    }

    internal static bool RuntimeChangedSinceCheck(
        CoturnCheckResponse result,
        string? currentContainerId,
        bool currentOwned,
        DateTimeOffset? currentStartedAtUtc,
        long? currentRestartCount,
        bool currentInspectionAvailable)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.RuntimeContainerId is null)
        {
            return currentContainerId is not null;
        }

        if (!currentOwned || !currentInspectionAvailable)
        {
            return true;
        }

        if (!string.Equals(
                result.RuntimeContainerId,
                currentContainerId,
                StringComparison.Ordinal))
        {
            return true;
        }

        if (result.RuntimeRestartCount != currentRestartCount)
        {
            return true;
        }

        return result.RuntimeStartedAtUtc != currentStartedAtUtc;
    }

    private async Task<CoturnFunctionalCheckDiagnosticWriteResult> RecordFunctionalCheckDiagnosticAsync(
        CoturnCheckResponse result,
        CoturnCheckResponse? previous)
    {
        var plan = CoturnDiagnosticsPolicy.PlanFunctionalCheckDiagnostic(
            result,
            previous);

        var diagnosticResult = await _diagnostics.TryWriteWorkflowEventAsync(
            new MemDiagnosticWriteRequest(
                Severity: plan.Severity,
                EventCode: plan.EventCode,
                Source: "host-agent.coturn",
                Feature: "coturn",
                Stage: "functional-check",
                Message: string.Equals(
                        plan.EventCode,
                        CoturnFunctionalCheckEventCodes.Failed,
                        StringComparison.Ordinal)
                    ? "Coturn functional verification failed."
                    : string.Equals(
                        plan.EventCode,
                        CoturnFunctionalCheckEventCodes.Recovered,
                        StringComparison.Ordinal)
                        ? "Coturn functional verification recovered."
                        : string.Equals(
                            plan.EventCode,
                            CoturnFunctionalCheckEventCodes.Warning,
                            StringComparison.Ordinal)
                            ? "Coturn functional verification completed with warnings."
                            : "Coturn functional verification passed.",
                IncidentId: plan.IncidentId,
                CreateIncident: plan.CreateIncident,
                Resource: new MemDiagnosticResource(
                    Kind: "service",
                    Id: CoturnRuntimePolicy.ServiceKey,
                    DisplayName: "Coturn TURN server",
                    Service: CoturnRuntimePolicy.ServiceKey,
                    WorkspacePath: "/services/coturn"),
                Expected: new Dictionary<string, string?>
                {
                    ["checkStatus"] = CoturnCheckStatuses.Passed,
                    ["allocationStatus"] = CoturnCheckStatuses.Passed
                },
                Observed: new Dictionary<string, string?>
                {
                    ["checkStatus"] = result.Status,
                    ["allocationStatus"] = result.Allocation.Status
                },
                Details: new Dictionary<string, string?>
                {
                    ["checkedAtUtc"] = result.CheckedAtUtc.ToString("O"),
                    ["publicHost"] = result.PublicHost,
                    ["containerState"] = result.ContainerState,
                    ["readiness"] = result.Readiness
                },
                SuggestedAction: string.Equals(
                        plan.EventCode,
                        CoturnFunctionalCheckEventCodes.Failed,
                        StringComparison.Ordinal)
                    ? "Open Services > Coturn, review the failed checks and bounded recent logs, then correct the underlying runtime or network condition before retrying."
                    : null,
                Retryable: string.Equals(
                    plan.EventCode,
                    CoturnFunctionalCheckEventCodes.Failed,
                    StringComparison.Ordinal)));

        var incidentId = diagnosticResult?.IncidentId ?? plan.IncidentId;
        var warningCode = diagnosticResult is { Stored: false }
            ? diagnosticResult.WarningCode
            : null;

        return new CoturnFunctionalCheckDiagnosticWriteResult(
            IncidentId: incidentId,
            WarningCode: warningCode);
    }

    public async Task<CoturnLogsResponse> GetRecentLogsAsync(
        int? tail,
        CancellationToken ct)
    {
        var requestedTail = CoturnDiagnosticsPolicy.NormalizeLogTail(tail);
        var container = await FindContainerAsync(ct);

        if (container is null)
        {
            return new CoturnLogsResponse(
                Source: "control-plane",
                Status: "not-available",
                RetrievedAtUtc: DateTimeOffset.UtcNow,
                ContainerName: CoturnRuntimePolicy.ContainerName,
                RequestedTail: requestedTail,
                ReturnedLines: 0,
                Truncated: false,
                Content: string.Empty,
                Warnings: ["Coturn is not deployed, so no container logs are available."]);
        }

        if (!CoturnRuntimePolicy.IsOwned(container.Labels))
        {
            throw new InvalidOperationException(
                $"A container named '{CoturnRuntimePolicy.ContainerName}' exists but is not owned by MEM. " +
                "MEM will not read logs from the conflicting container.");
        }

        var files = await _fileStore.ReadAsync(ct);
        var raw = await _dockerHost.GetLogsAsync(
            container.ID,
            requestedTail,
            ct);
        var sanitized = CoturnDiagnosticsPolicy.SanitizeDiagnosticText(
            raw,
            files.SecretValue);

        var warnings = sanitized.Truncated
            ? new[] { "The returned log view was bounded and truncated." }
            : Array.Empty<string>();

        return new CoturnLogsResponse(
            Source: "control-plane",
            Status: "ok",
            RetrievedAtUtc: DateTimeOffset.UtcNow,
            ContainerName: CoturnRuntimePolicy.ContainerName,
            RequestedTail: requestedTail,
            ReturnedLines: sanitized.ReturnedLines,
            Truncated: sanitized.Truncated,
            Content: sanitized.Content,
            Warnings: warnings);
    }

    private async Task<CoturnCheckItem> CheckProbeRuntimeDnsAsync(
        string containerId,
        string publicHost,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var exec = await _dockerClient.Exec.ExecCreateContainerAsync(
                containerId,
                CreateProbeDnsExecParameters(publicHost),
                timeout.Token);

            using var stream = await _dockerClient.Exec.StartAndAttachContainerExecAsync(
                exec.ID,
                tty: false,
                timeout.Token);

            var output = await stream.ReadOutputToEndAsync(timeout.Token);
            var inspection = await _dockerClient.Exec.InspectContainerExecAsync(
                exec.ID,
                timeout.Token);
            var rawOutput = string.Join(
                "\n",
                new[] { output.stdout, output.stderr }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            var sanitized = CoturnDiagnosticsPolicy.SanitizeDiagnosticText(rawOutput);

            return BuildProbeRuntimeDnsCheck(
                publicHost,
                inspection.ExitCode,
                sanitized.Content);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new CoturnCheckItem(
                Key: "probe-dns",
                Status: CoturnCheckStatuses.Warning,
                Summary: $"The Coturn probe runtime did not complete DNS resolution for {publicHost} within the bounded prerequisite window.",
                Detail: "The authenticated TURN allocation probe was not attempted. A probe-runtime DNS timeout is prerequisite uncertainty, not evidence that Coturn rejected an allocation.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var sanitized = CoturnDiagnosticsPolicy.SanitizeDiagnosticText(ex.Message);
            return new CoturnCheckItem(
                Key: "probe-dns",
                Status: CoturnCheckStatuses.Warning,
                Summary: $"The Coturn probe runtime could not establish DNS resolution for {publicHost}.",
                Detail: string.IsNullOrWhiteSpace(sanitized.Content)
                    ? "The authenticated TURN allocation probe was not attempted because its resolver prerequisite could not be established."
                    : sanitized.Content);
        }
    }

    internal static ContainerExecCreateParameters CreateProbeDnsExecParameters(
        string publicHost)
    {
        if (string.IsNullOrWhiteSpace(publicHost))
        {
            throw new ArgumentException(
                "A Coturn public host is required for the probe-runtime DNS prerequisite.",
                nameof(publicHost));
        }

        return new ContainerExecCreateParameters
        {
            AttachStdout = true,
            AttachStderr = true,
            Cmd =
            [
                "/bin/sh",
                "-eu",
                "-c",
                "exec getent ahosts \"$1\"",
                "mem-coturn-probe-dns",
                publicHost.Trim()
            ]
        };
    }

    internal static CoturnCheckItem BuildProbeRuntimeDnsCheck(
        string publicHost,
        long exitCode,
        string? output)
    {
        var resolvedAddresses = (output ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(
                new[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault())
            .Where(value =>
                !string.IsNullOrWhiteSpace(value) &&
                IPAddress.TryParse(value, out _))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var nonLoopbackResolved = resolvedAddresses.Any(value =>
            IPAddress.TryParse(value, out var address) &&
            !IPAddress.IsLoopback(address));

        if (exitCode == 0 && nonLoopbackResolved)
        {
            return new CoturnCheckItem(
                Key: "probe-dns",
                Status: CoturnCheckStatuses.Passed,
                Summary: $"The Coturn probe runtime resolved a non-loopback address for {publicHost}.",
                Detail: "Probe-runtime resolver addresses: " +
                        string.Join(", ", resolvedAddresses) +
                        ". This verifies the resolver context used by the authenticated allocation probe; it is not authoritative public-DNS or external-client reachability proof.");
        }

        var detail = resolvedAddresses.Length == 0
            ? "The Coturn probe runtime returned no usable addresses. The authenticated TURN allocation probe was not attempted."
            : "Probe-runtime resolver addresses: " +
              string.Join(", ", resolvedAddresses) +
              ". Only loopback addresses were available, so the authenticated TURN allocation probe was not attempted.";

        return new CoturnCheckItem(
            Key: "probe-dns",
            Status: CoturnCheckStatuses.Warning,
            Summary: $"The Coturn probe runtime could not resolve a non-loopback address for {publicHost}.",
            Detail: detail);
    }

    private static CoturnCheckItem BuildProbeDnsNotRunCheck(string summary) =>
        new(
            Key: "probe-dns",
            Status: CoturnCheckStatuses.NotRun,
            Summary: summary,
            Detail: null);

    private async Task<CoturnAllocationProbeResponse> RunAllocationProbeAsync(
        string containerId,
        string publicHost,
        string sharedSecret,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var credential = CoturnDiagnosticsPolicy.CreateTemporaryCredential(
            sharedSecret,
            now);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            var exec = await _dockerClient.Exec.ExecCreateContainerAsync(
                containerId,
                CreateAllocationProbeExecParameters(publicHost),
                timeout.Token);

            using var stream = await _dockerClient.Exec.StartAndAttachContainerExecAsync(
                exec.ID,
                tty: false,
                timeout.Token);

            var outputTask = stream.ReadOutputToEndAsync(timeout.Token);
            var credentialBytes = EncodeAllocationProbeStandardInput(
                credential.Username,
                credential.Password);
            try
            {
                await stream.WriteAsync(
                    credentialBytes,
                    0,
                    credentialBytes.Length,
                    timeout.Token);
                stream.CloseWrite();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(credentialBytes);
            }

            var output = await outputTask;
            var inspection = await _dockerClient.Exec.InspectContainerExecAsync(
                exec.ID,
                timeout.Token);
            var rawOutput = string.Join(
                "\n",
                new[] { output.stdout, output.stderr }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            var sanitized = CoturnDiagnosticsPolicy.SanitizeDiagnosticText(
                rawOutput,
                sharedSecret,
                credential.Username,
                credential.Password);
            var passed = inspection.ExitCode == 0;

            return new CoturnAllocationProbeResponse(
                Status: passed
                    ? CoturnCheckStatuses.Passed
                    : CoturnCheckStatuses.Failed,
                Transport: "udp",
                Summary: passed
                    ? "Coturn accepted temporary credentials and completed a local UDP allocation probe. This does not prove the external client path."
                    : "The local UDP allocation probe reached the allocation stage but failed. This does not prove the external client path.",
                LogTail: string.IsNullOrWhiteSpace(sanitized.Content)
                    ? null
                    : sanitized.Content)
            {
                RelayAddressEvidence = CoturnRelayAddressPolicy.Parse(rawOutput)
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new CoturnAllocationProbeResponse(
                Status: CoturnCheckStatuses.Failed,
                Transport: "udp",
                Summary: "The local UDP allocation probe reached the allocation stage but timed out.",
                LogTail: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var sanitized = CoturnDiagnosticsPolicy.SanitizeDiagnosticText(
                ex.Message,
                sharedSecret,
                credential.Username,
                credential.Password);

            return new CoturnAllocationProbeResponse(
                Status: CoturnCheckStatuses.Failed,
                Transport: "udp",
                Summary: "The local UDP allocation probe could not be executed.",
                LogTail: string.IsNullOrWhiteSpace(sanitized.Content)
                    ? null
                    : sanitized.Content);
        }
    }

    internal static ContainerExecCreateParameters CreateAllocationProbeExecParameters(
        string publicHost)
    {
        if (string.IsNullOrWhiteSpace(publicHost))
        {
            throw new ArgumentException(
                "A Coturn public host is required for the allocation probe.",
                nameof(publicHost));
        }

        return new ContainerExecCreateParameters
        {
            AttachStdin = true,
            AttachStdout = true,
            AttachStderr = true,
            Cmd =
            [
                "/bin/sh",
                "-eu",
                "-c",
                AllocationProbeShellScript,
                "mem-coturn-allocation-probe",
                CoturnRuntimePolicy.TurnPort.ToString(),
                publicHost.Trim()
            ]
        };
    }

    internal static byte[] EncodeAllocationProbeStandardInput(
        string username,
        string password)
    {
        ValidateAllocationProbeCredentialPart(username, nameof(username));
        ValidateAllocationProbeCredentialPart(password, nameof(password));

        return Encoding.UTF8.GetBytes($"{username}\n{password}\n");
    }

    internal static Task<ApprovedCoturnRuntimeDescriptor> ResolveApprovedImageAsync(
        IApprovedCoturnRuntimeProvider provider,
        CoturnRuntimeImageBoundary boundary,
        CancellationToken cancellationToken) =>
        boundary switch
        {
            CoturnRuntimeImageBoundary.Installation =>
                provider.ResolveForInstallationAsync(cancellationToken),
            CoturnRuntimeImageBoundary.Operation =>
                provider.ResolveForOperationAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };

    private static void ValidateAllocationProbeCredentialPart(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Contains('\r') ||
            value.Contains('\n'))
        {
            throw new ArgumentException(
                "Coturn allocation probe credentials must be non-empty single-line values.",
                parameterName);
        }
    }

    private const string AllocationProbeShellScript =
        "IFS= read -r turn_username\n" +
        "IFS= read -r turn_password\n" +
        "exec turnutils_uclient -v -y -c -n 1 " +
        "-p \"$1\" -u \"$turn_username\" -w \"$turn_password\" \"$2\"";

    public async Task<CoturnSynapseConfig?> GetSynapseConfigAsync(
        CancellationToken ct)
    {
        var domain = await _domainResolver.ResolveMainPlatformDomainAsync(
            requestedDomainId: null,
            ct);

        var expectedBaseDomain = NormalizeDomain(domain.BaseDomain);
        var expectedPublicHost = BuildPublicHost(expectedBaseDomain);
        var files = await _fileStore.ReadAsync(ct);

        if (!files.SecretPresent || string.IsNullOrWhiteSpace(files.SecretValue))
        {
            return null;
        }

        ApprovedCoturnRuntimeDescriptor approvedImage;
        try
        {
            approvedImage = await _approvedImageProvider.ResolveForOperationAsync(ct);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        var container = await FindContainerAsync(ct);
        var dockerInspection = await TryInspectOwnedContainerAsync(container, ct);
        var dockerEvidence = BuildDockerRuntimeEvidence(container, dockerInspection);
        var state = Observe(
            container,
            dockerInspection,
            dockerEvidence,
            files,
            approvedImage,
            expectedBaseDomain);

        if (!CoturnRuntimePolicy.CanProvideSynapseConfiguration(state.Evaluation))
        {
            return null;
        }

        return new CoturnSynapseConfig(
            PublicHost: expectedPublicHost,
            Realm: expectedBaseDomain,
            TurnUris: CoturnRuntimePolicy.BuildTurnUris(expectedPublicHost),
            SharedSecret: files.SecretValue,
            UserLifetime: "1h",
            AllowGuests: true,
            RelayPortsPublished: true,
            ExpectedBaseDomain: expectedBaseDomain);
    }

    internal static DockerContainerSpec BuildContainerSpec(
        ApprovedCoturnRuntimeDescriptor approvedImage,
        string baseDomain,
        string publicHost,
        string? externalIp,
        bool publishRelayPorts,
        string configSha256,
        string configPath,
        string gatewayNetwork)
    {
        var labels = new Dictionary<string, string>
        {
            ["mem.component"] = CoturnRuntimePolicy.ComponentLabel,
            ["mem.service"] = CoturnRuntimePolicy.ServiceKey,
            ["mem.coturn.baseDomain"] = baseDomain,
            ["mem.coturn.publicHost"] = publicHost,
            ["mem.coturn.realm"] = baseDomain,
            ["mem.coturn.turnPort"] = CoturnRuntimePolicy.TurnPort.ToString(),
            ["mem.coturn.relayMinPort"] = CoturnRuntimePolicy.RelayMinPort.ToString(),
            ["mem.coturn.relayMaxPort"] = CoturnRuntimePolicy.RelayMaxPort.ToString(),
            ["mem.coturn.relayPortsPublished"] = publishRelayPorts ? "true" : "false",
            ["mem.coturn.securityPolicyVersion"] = CoturnRuntimePolicy.SecurityPolicyVersion,
            ["mem.coturn.startupPrivilegeModel"] = "root-then-drop-to-nobody",
            ["mem.coturn.peerPolicyMode"] = "deny-private-reserved-allow-self",
            ["mem.coturn.externalIpMode"] = externalIp is null ? "auto-detect" : "explicit",
            ["mem.coturn.configSha256"] = configSha256,
            ["mem.coturn.approvedImageReference"] = approvedImage.ApprovedReference,
            ["mem.coturn.resolvedImageId"] = approvedImage.ResolvedImageId
        };

        if (externalIp is not null)
        {
            labels["mem.coturn.externalIp"] = externalIp;
        }

        return new DockerContainerSpec(
            Name: CoturnRuntimePolicy.ContainerName,
            Image: approvedImage.ResolvedImageId,
            Env: null,
            Labels: labels,
            Cmd: CoturnRuntimePolicy.BuildContainerCommand(externalIp),
            PortBindings: CoturnRuntimePolicy.BuildPortBindings(publishRelayPorts),
            AutoRemove: false,
            BindMounts:
            [
                new BindMount(
                    configPath,
                    CoturnRuntimePolicy.ContainerConfigPath,
                    ReadOnly: true)
            ],
            // The approved upstream image defaults to nobody:nogroup. MEM starts the
            // entrypoint as root only so it can read the root-owned 0600 mounted
            // configuration. Coturn then drops to nobody:nogroup through the
            // proc-user/proc-group settings in that configuration.
            User: CoturnRuntimePolicy.StartupUser,
            RestartPolicy: new DockerRestartPolicy(
                DockerRestartPolicyName.UnlessStopped),
            NetworkName: gatewayNetwork,
            NetworkAliases:
            [
                "coturn",
                CoturnRuntimePolicy.ContainerName
            ]);
    }

    private async Task<DockerContainerInspection?> TryInspectOwnedContainerAsync(
        ContainerListResponse? container,
        CancellationToken ct)
    {
        if (container is null || !CoturnRuntimePolicy.IsOwned(container.Labels))
        {
            return null;
        }

        try
        {
            return await _dockerHost.InspectAsync(container.ID, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The list-level ownership/runtime projection remains useful even if
            // Docker's full inspect cannot be completed. Callers must surface an
            // Unknown/Needs-attention state rather than losing the whole Services
            // or Home surface to one inspection failure.
            return null;
        }
    }

    private CoturnDockerRuntimeEvidence? BuildDockerRuntimeEvidence(
        ContainerListResponse? container,
        DockerContainerInspection? inspection)
    {
        if (container is null ||
            inspection is null ||
            !CoturnRuntimePolicy.IsOwned(container.Labels))
        {
            return null;
        }

        var expectedNetwork = _networkOptions.GatewayNetworkName.Trim();
        var expectedAliases = new[]
        {
            "coturn",
            CoturnRuntimePolicy.ContainerName
        };
        var expectedNetworkAttachment = inspection.Networks.FirstOrDefault(network =>
            string.Equals(network.Name, expectedNetwork, StringComparison.OrdinalIgnoreCase));
        var observedAliases = expectedNetworkAttachment?.Aliases ?? [];
        var expectedNetworkAttached = expectedNetworkAttachment is not null;
        var networkModeMatches = string.Equals(
            inspection.NetworkMode,
            expectedNetwork,
            StringComparison.OrdinalIgnoreCase);
        var networkAliasesMatch = expectedNetworkAttached &&
            expectedAliases.All(expectedAlias => observedAliases.Contains(
                expectedAlias,
                StringComparer.OrdinalIgnoreCase));

        var configMount = inspection.BindMounts.FirstOrDefault(mount =>
            string.Equals(
                mount.Destination,
                CoturnRuntimePolicy.ContainerConfigPath,
                StringComparison.Ordinal));
        var configMountPresent = configMount is not null;
        var configMountSourceMatches = configMountPresent &&
            PathsEqual(configMount!.Source, _fileStore.ConfigPath);
        var configMountDestinationMatches = configMountPresent &&
            string.Equals(
                configMount!.Destination,
                CoturnRuntimePolicy.ContainerConfigPath,
                StringComparison.Ordinal);

        var expectedExternalIp = TryGetLabel(container, "mem.coturn.externalIp");
        var expectedCommand = CoturnRuntimePolicy.BuildContainerCommand(expectedExternalIp);
        var commandMatches = inspection.Command.SequenceEqual(
            expectedCommand,
            StringComparer.Ordinal);

        return new CoturnDockerRuntimeEvidence(
            RestartPolicy: inspection.RestartPolicy,
            ExpectedRestartPolicy: CoturnRuntimePolicy.ExpectedRestartPolicy,
            RestartPolicyMatches: string.Equals(
                inspection.RestartPolicy,
                CoturnRuntimePolicy.ExpectedRestartPolicy,
                StringComparison.OrdinalIgnoreCase),
            RestartCount: inspection.RestartCount,
            Restarting: inspection.Restarting,
            Paused: inspection.Paused,
            ExitCode: inspection.ExitCode,
            OomKilled: inspection.OomKilled,
            Dead: inspection.Dead,
            StateErrorPresent: inspection.StateErrorPresent,
            HealthStatus: NormalizeOptional(inspection.HealthStatus),
            StartedAtUtc: inspection.StartedAtUtc,
            FinishedAtUtc: inspection.FinishedAtUtc,
            NetworkMode: NormalizeOptional(inspection.NetworkMode),
            ExpectedNetwork: expectedNetwork,
            NetworkModeMatches: networkModeMatches,
            AttachedNetworks: inspection.Networks
                .Select(network => network.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ExpectedNetworkAliases: expectedAliases,
            ObservedExpectedNetworkAliases: observedAliases
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ExpectedNetworkAttached: expectedNetworkAttached,
            NetworkAliasesMatch: networkAliasesMatch,
            ConfigMountPresent: configMountPresent,
            ConfigMountReadOnly: configMount?.ReadOnly ?? false,
            ConfigMountSourceMatches: configMountSourceMatches,
            ConfigMountDestinationMatches: configMountDestinationMatches,
            CommandMatches: commandMatches,
            StartupUserMatches: string.Equals(
                inspection.User,
                CoturnRuntimePolicy.StartupUser,
                StringComparison.Ordinal));
    }

    private CoturnObservedState Observe(
        ContainerListResponse? container,
        DockerContainerInspection? dockerInspection,
        CoturnDockerRuntimeEvidence? dockerEvidence,
        CoturnRuntimeFiles files,
        ApprovedCoturnRuntimeDescriptor? approvedImage,
        string expectedBaseDomain)
    {
        var containerExists = container is not null;
        var ownershipVerified = containerExists &&
            CoturnRuntimePolicy.IsOwned(container!.Labels);
        var configuredBaseDomain = TryGetLabel(container, "mem.coturn.baseDomain");
        var domainDriftDetected = containerExists &&
            !string.Equals(
                NormalizeLabelDomain(configuredBaseDomain),
                expectedBaseDomain,
                StringComparison.Ordinal);
        var observedImageId = dockerInspection?.ImageId ?? container?.ImageID;
        var imageApproved = approvedImage is not null && containerExists &&
            string.Equals(
                NormalizeImageId(observedImageId),
                approvedImage.ResolvedImageId,
                StringComparison.Ordinal);
        var relayPortsPublished = containerExists && HasRequiredPublishedPorts(container!);
        var configHashMatches = containerExists &&
            !string.IsNullOrWhiteSpace(files.ConfigSha256) &&
            string.Equals(
                TryGetLabel(container, "mem.coturn.configSha256"),
                files.ConfigSha256,
                StringComparison.OrdinalIgnoreCase);
        var securityPolicyApplied = containerExists &&
            files.PermissionsApplied &&
            CoturnRuntimePolicy.HasRequiredSecurityPolicy(files.Configuration) &&
            string.Equals(
                TryGetLabel(container, "mem.coturn.startupPrivilegeModel"),
                "root-then-drop-to-nobody",
                StringComparison.Ordinal) &&
            string.Equals(
                TryGetLabel(container, "mem.coturn.peerPolicyMode"),
                "deny-private-reserved-allow-self",
                StringComparison.Ordinal) &&
            string.Equals(
                TryGetLabel(container, "mem.coturn.securityPolicyVersion"),
                CoturnRuntimePolicy.SecurityPolicyVersion,
                StringComparison.Ordinal);
        var running = dockerInspection?.Running ??
            string.Equals(container?.State, "running", StringComparison.OrdinalIgnoreCase);
        var runtimeStateHealthy = dockerInspection is not null &&
            dockerInspection.Running &&
            !dockerInspection.Restarting &&
            !dockerInspection.Paused &&
            !dockerInspection.OomKilled &&
            !dockerInspection.Dead &&
            !dockerInspection.StateErrorPresent;
        var networkAttachmentMatches = dockerEvidence?.ExpectedNetworkAttached == true;
        var configMountMatches = dockerEvidence is not null &&
            dockerEvidence.ConfigMountPresent &&
            dockerEvidence.ConfigMountReadOnly &&
            dockerEvidence.ConfigMountSourceMatches &&
            dockerEvidence.ConfigMountDestinationMatches;

        var observation = new CoturnRuntimeObservation(
            ContainerExists: containerExists,
            Running: running,
            OwnershipVerified: ownershipVerified,
            DockerInspectionAvailable: dockerInspection is not null,
            RuntimeStateHealthy: runtimeStateHealthy,
            RestartPolicyMatches: dockerEvidence?.RestartPolicyMatches == true,
            NetworkAttachmentMatches: networkAttachmentMatches,
            NetworkAliasesMatch: dockerEvidence?.NetworkAliasesMatch == true,
            ConfigMountMatches: configMountMatches,
            CommandMatches: dockerEvidence?.CommandMatches == true,
            StartupUserMatches: dockerEvidence?.StartupUserMatches == true,
            ProtectedEvidenceAccess: files.ProtectedEvidenceAccess,
            ImageApproved: imageApproved,
            SecretPresent: files.SecretPresent,
            ConfigPresent: files.ConfigPresent,
            ConfigHashMatches: configHashMatches,
            SecurityPolicyApplied: securityPolicyApplied,
            RelayPortsPublished: relayPortsPublished,
            DomainDriftDetected: domainDriftDetected);

        return new CoturnObservedState(
            Observation: observation,
            Evaluation: CoturnRuntimePolicy.Evaluate(observation),
            RelayPortsPublished: relayPortsPublished,
            SecurityPolicyApplied: securityPolicyApplied,
            ImageApproved: imageApproved,
            OwnershipVerified: ownershipVerified,
            DomainDriftDetected: domainDriftDetected);
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static PlatformCoturnSetupResult ToSetupResult(
        CoturnRuntimeResponse response) =>
        new(
            Ready: string.Equals(
                response.Readiness,
                "ready",
                StringComparison.Ordinal),
            Status: response.Status,
            Readiness: response.Readiness,
            ContainerState: response.ContainerState,
            ContainerName: response.ContainerName,
            PublicHost: response.PublicHost,
            Realm: response.Realm,
            ApprovedImageReference: response.ApprovedImageReference,
            ResolvedImageId: response.ResolvedImageId,
            ContainerExists: response.ContainerExists,
            Running: response.Running,
            OwnershipVerified: response.OwnershipVerified,
            ImageApproved: response.ImageApproved,
            SecretPresent: response.SecretPresent,
            SecretFilePermissionsApplied: response.SecretFilePermissionsApplied,
            RelayPortsPublished: response.RelayPortsPublished,
            SecurityPolicyApplied: response.SecurityPolicyApplied,
            Recreated: response.Recreated,
            PublishedPorts: response.PublishedPorts,
            RequiredProductionFirewallPorts: response.RequiredProductionFirewallPorts,
            Warnings: response.Warnings,
            Detail: response.Detail);

    private CoturnRuntimeResponse BuildResponse(
        ContainerListResponse? container,
        DockerContainerInspection? dockerInspection,
        CoturnDockerRuntimeEvidence? dockerEvidence,
        CoturnRuntimeFiles files,
        ApprovedCoturnRuntimeDescriptor? approvedImage,
        string expectedBaseDomain,
        string publicHost,
        bool recreated,
        IReadOnlyList<string> warnings,
        string? detail)
    {
        var state = Observe(
            container,
            dockerInspection,
            dockerEvidence,
            files,
            approvedImage,
            expectedBaseDomain);
        var policy = _approvedImageProvider.GetPolicy();
        var configuredBaseDomain = TryGetLabel(container, "mem.coturn.baseDomain");
        var runtimeDrift = BuildRuntimeDrift(state, dockerEvidence);

        return new CoturnRuntimeResponse(
            Source: "control-plane",
            Status: state.Evaluation.Status,
            ContainerState: state.Evaluation.ContainerState,
            Readiness: state.Evaluation.Readiness,
            ServiceKey: CoturnRuntimePolicy.ServiceKey,
            ContainerName: CoturnRuntimePolicy.ContainerName,
            Image: dockerInspection?.ConfiguredImage ?? container?.Image ?? policy.ApprovedReference,
            ApprovedImageReference: policy.ApprovedReference,
            ResolvedImageId: approvedImage?.ResolvedImageId,
            ImageApproved: state.ImageApproved,
            ContainerExists: container is not null,
            Running: state.Observation.Running,
            OwnershipVerified: state.OwnershipVerified,
            ContainerId: container?.ID,
            DockerState: dockerInspection?.Status ?? container?.State,
            OperatorStatus: state.Evaluation.OperatorStatus,
            RuntimeExact: state.Evaluation.RuntimeExact,
            DockerRuntime: dockerEvidence,
            RuntimeDrift: runtimeDrift,
            ProtectedEvidenceAccess: files.ProtectedEvidenceAccess,
            Realm: expectedBaseDomain,
            PublicHost: publicHost,
            TurnPort: CoturnRuntimePolicy.TurnPort,
            RelayMinPort: CoturnRuntimePolicy.RelayMinPort,
            RelayMaxPort: CoturnRuntimePolicy.RelayMaxPort,
            TurnUris: CoturnRuntimePolicy.BuildTurnUris(publicHost),
            SecretPresent: files.SecretPresent,
            SecretSource: files.SecretSource,
            SecretStorage: "protected-local-file",
            SecretFilePermissionsApplied: files.PermissionsApplied,
            ExpectedBaseDomain: expectedBaseDomain,
            ConfiguredBaseDomain: configuredBaseDomain,
            DomainDriftDetected: state.DomainDriftDetected,
            Recreated: recreated,
            ExternalIp: TryGetLabel(container, "mem.coturn.externalIp"),
            RelayPortsPublished: state.RelayPortsPublished,
            SecurityPolicyApplied: state.SecurityPolicyApplied,
            SecurityPolicyVersion: CoturnRuntimePolicy.SecurityPolicyVersion,
            PublishedPorts: CoturnRuntimePolicy.BuildPublishedPorts(state.RelayPortsPublished),
            RequiredProductionFirewallPorts: CoturnRuntimePolicy.BuildRequiredProductionFirewallPorts(),
            Warnings: warnings.Distinct(StringComparer.Ordinal).ToArray(),
            Detail: detail)
        {
            ConfigurationPresent = files.ConfigPresent,
            ConfigurationExact =
                files.ConfigPresent &&
                state.Observation.ConfigHashMatches &&
                files.PermissionsApplied &&
                state.SecurityPolicyApplied
        };
    }

    private static IReadOnlyList<string> BuildRuntimeDrift(
        CoturnObservedState state,
        CoturnDockerRuntimeEvidence? dockerEvidence)
    {
        if (!state.Observation.ContainerExists || !state.OwnershipVerified)
        {
            return [];
        }

        var drift = new List<string>();
        if (dockerEvidence is null)
        {
            drift.Add("docker-inspection");
            return drift;
        }

        if (!state.Observation.RuntimeStateHealthy)
        {
            drift.Add("runtime-state");
        }

        if (!dockerEvidence.RestartPolicyMatches)
        {
            drift.Add("restart-policy");
        }

        if (!dockerEvidence.ExpectedNetworkAttached)
        {
            drift.Add("gateway-network");
        }

        if (!dockerEvidence.NetworkAliasesMatch)
        {
            drift.Add("network-aliases");
        }

        if (!dockerEvidence.ConfigMountPresent ||
            !dockerEvidence.ConfigMountReadOnly ||
            !dockerEvidence.ConfigMountSourceMatches ||
            !dockerEvidence.ConfigMountDestinationMatches)
        {
            drift.Add("config-mount");
        }

        if (!dockerEvidence.CommandMatches)
        {
            drift.Add("command");
        }

        if (!dockerEvidence.StartupUserMatches)
        {
            drift.Add("startup-user");
        }

        return drift;
    }

    private static bool ContainerMatchesDesiredState(
        ContainerListResponse container,
        ApprovedCoturnRuntimeDescriptor approvedImage,
        string expectedBaseDomain,
        string? expectedExternalIp,
        bool expectedRelayPortsPublished,
        string expectedConfigSha256)
    {
        return CoturnRuntimePolicy.IsOwned(container.Labels) &&
               string.Equals(
                   NormalizeImageId(container.ImageID),
                   approvedImage.ResolvedImageId,
                   StringComparison.Ordinal) &&
               string.Equals(
                   NormalizeLabelDomain(TryGetLabel(container, "mem.coturn.baseDomain")),
                   expectedBaseDomain,
                   StringComparison.Ordinal) &&
               StringEqualsNullable(
                   TryGetLabel(container, "mem.coturn.externalIp"),
                   expectedExternalIp) &&
               ReadRelayPortsPublishedLabel(container) == expectedRelayPortsPublished &&
               (!expectedRelayPortsPublished || HasRequiredPublishedPorts(container)) &&
               string.Equals(
                   TryGetLabel(container, "mem.coturn.securityPolicyVersion"),
                   CoturnRuntimePolicy.SecurityPolicyVersion,
                   StringComparison.Ordinal) &&
               string.Equals(
                   TryGetLabel(container, "mem.coturn.startupPrivilegeModel"),
                   "root-then-drop-to-nobody",
                   StringComparison.Ordinal) &&
               string.Equals(
                   TryGetLabel(container, "mem.coturn.peerPolicyMode"),
                   "deny-private-reserved-allow-self",
                   StringComparison.Ordinal) &&
               string.Equals(
                   TryGetLabel(container, "mem.coturn.externalIpMode"),
                   expectedExternalIp is null ? "auto-detect" : "explicit",
                   StringComparison.Ordinal) &&
               string.Equals(
                   TryGetLabel(container, "mem.coturn.configSha256"),
                   expectedConfigSha256,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void AddInspectionWarnings(
        ICollection<string> warnings,
        CoturnObservedState state,
        CoturnDockerRuntimeEvidence? dockerEvidence,
        CoturnRuntimeFiles files,
        string expectedBaseDomain,
        string? configuredBaseDomain,
        string? approvedImageError)
    {
        if (!state.Observation.ContainerExists)
        {
            warnings.Add("Coturn container does not exist.");
            return;
        }

        if (!state.OwnershipVerified)
        {
            warnings.Add(
                $"A container named '{CoturnRuntimePolicy.ContainerName}' exists without the required MEM ownership labels.");
            return;
        }

        if (!state.Observation.Running)
        {
            warnings.Add("Coturn container is not running.");
        }

        if (state.Observation.Running && dockerEvidence is null)
        {
            warnings.Add("Exact Docker runtime inspection could not be completed for the MEM-owned Coturn container.");
        }

        if (dockerEvidence is not null)
        {
            if (!state.Observation.RuntimeStateHealthy)
            {
                warnings.Add("Docker reports Coturn in a degraded runtime state. Review restart, OOM, exit, and state evidence.");
            }

            if (!dockerEvidence.RestartPolicyMatches)
            {
                warnings.Add(
                    $"Coturn restart policy is '{dockerEvidence.RestartPolicy}', but MEM requires '{dockerEvidence.ExpectedRestartPolicy}'.");
            }

            if (!dockerEvidence.ExpectedNetworkAttached)
            {
                warnings.Add(
                    $"Coturn is not attached to the expected MEM gateway network '{dockerEvidence.ExpectedNetwork}'.");
            }

            if (!dockerEvidence.NetworkAliasesMatch)
            {
                warnings.Add("Coturn does not expose the required aliases on the MEM gateway network.");
            }

            if (!dockerEvidence.ConfigMountPresent ||
                !dockerEvidence.ConfigMountReadOnly ||
                !dockerEvidence.ConfigMountSourceMatches ||
                !dockerEvidence.ConfigMountDestinationMatches)
            {
                warnings.Add("Coturn protected configuration mount does not match the expected read-only MEM binding.");
            }

            if (!dockerEvidence.CommandMatches)
            {
                warnings.Add("Coturn container command does not match the MEM-managed runtime contract.");
            }

            if (!dockerEvidence.StartupUserMatches)
            {
                warnings.Add("Coturn container startup user does not match the required MEM privilege-drop contract.");
            }
        }

        if (approvedImageError is not null || !state.ImageApproved)
        {
            warnings.Add("Coturn is not running the locally resolved MEM-approved immutable image.");
        }

        if (string.Equals(
                files.ProtectedEvidenceAccess,
                CoturnProtectedEvidenceAccess.Unavailable,
                StringComparison.Ordinal))
        {
            warnings.Add("Protected Coturn setup evidence could not be inspected in this execution context.");
        }
        else if (string.Equals(
                     files.ProtectedEvidenceAccess,
                     CoturnProtectedEvidenceAccess.Available,
                     StringComparison.Ordinal))
        {
            if (!files.SecretPresent)
            {
                warnings.Add("Coturn shared secret is missing or invalid.");
            }

            if (!files.ConfigPresent)
            {
                warnings.Add("Protected Coturn configuration is missing.");
            }

            if (!files.PermissionsApplied)
            {
                warnings.Add("Coturn secret-bearing files do not have verified owner-only Unix permissions.");
            }

            if (!state.Observation.ConfigHashMatches)
            {
                warnings.Add("Coturn configuration does not match the hash bound to the container.");
            }

            if (!state.SecurityPolicyApplied)
            {
                warnings.Add("Coturn minimum security policy is absent or could not be verified.");
            }
        }

        if (!state.RelayPortsPublished)
        {
            warnings.Add(
                $"UDP relay range {CoturnRuntimePolicy.RelayMinPort}-{CoturnRuntimePolicy.RelayMaxPort} is not fully published.");
        }

        if (state.DomainDriftDetected)
        {
            warnings.Add(
                $"Coturn is configured for '{configuredBaseDomain ?? "<missing>"}', " +
                $"but the current main platform domain is '{expectedBaseDomain}'.");
        }
    }

    private async Task<ContainerListResponse?> FindContainerAsync(CancellationToken ct)
    {
        var containers = await _dockerClient.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool>
                    {
                        [CoturnRuntimePolicy.ContainerName] = true
                    }
                }
            },
            ct);

        return containers.FirstOrDefault(container =>
            container.Names.Any(name =>
                string.Equals(
                    name.TrimStart('/'),
                    CoturnRuntimePolicy.ContainerName,
                    StringComparison.OrdinalIgnoreCase)));
    }

    private static bool HasRequiredListenerPorts(ContainerListResponse container) =>
        HasPublishedPort(container, CoturnRuntimePolicy.TurnPort, "tcp") &&
        HasPublishedPort(container, CoturnRuntimePolicy.TurnPort, "udp");

    private static bool HasRequiredPublishedPorts(ContainerListResponse container)
    {
        if (!HasPublishedPort(container, CoturnRuntimePolicy.TurnPort, "tcp") ||
            !HasPublishedPort(container, CoturnRuntimePolicy.TurnPort, "udp"))
        {
            return false;
        }

        for (var port = CoturnRuntimePolicy.RelayMinPort;
             port <= CoturnRuntimePolicy.RelayMaxPort;
             port++)
        {
            if (!HasPublishedPort(container, port, "udp"))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasPublishedPort(
        ContainerListResponse container,
        int port,
        string protocol)
    {
        return container.Ports?.Any(binding =>
            binding.PrivatePort == port &&
            binding.PublicPort == port &&
            string.Equals(binding.Type, protocol, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static bool ReadRelayPortsPublishedLabel(ContainerListResponse container) =>
        string.Equals(
            TryGetLabel(container, "mem.coturn.relayPortsPublished"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static string? TryGetLabel(
        ContainerListResponse? container,
        string key)
    {
        if (container?.Labels is null)
        {
            return null;
        }

        return container.Labels.TryGetValue(key, out var value)
            ? value
            : null;
    }

    private static string BuildPublicHost(string baseDomain) =>
        $"turn.{NormalizeDomain(baseDomain)}";

    private static string NormalizeDomain(string value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .Trim('.')
            .ToLowerInvariant();

        if (normalized.Length == 0)
        {
            throw new InvalidOperationException(
                "Coturn requires a non-empty platform base domain.");
        }

        return normalized;
    }

    private static string? NormalizeLabelDomain(string? value)
    {
        var normalized = value?
            .Trim()
            .Trim('.')
            .ToLowerInvariant();

        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized;
    }

    private static string? NormalizeImageId(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized;
    }

    private static bool StringEqualsNullable(string? left, string? right) =>
        string.Equals(
            NormalizeOptional(left),
            NormalizeOptional(right),
            StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized;
    }

    private sealed record CoturnObservedState(
        CoturnRuntimeObservation Observation,
        CoturnReadinessEvaluation Evaluation,
        bool RelayPortsPublished,
        bool SecurityPolicyApplied,
        bool ImageApproved,
        bool OwnershipVerified,
        bool DomainDriftDetected);
}

internal enum CoturnRuntimeImageBoundary
{
    Installation,
    Operation
}

internal sealed record CoturnFunctionalCheckDiagnosticWriteResult(
    string? IncidentId,
    string? WarningCode);
