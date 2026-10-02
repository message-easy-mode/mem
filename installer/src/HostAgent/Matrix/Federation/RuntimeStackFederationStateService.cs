using System.Security.Cryptography;
using System.Text;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Operations;
using Microsoft.Extensions.Logging;

namespace HostAgent.Matrix.Federation;

public interface IRuntimeStackFederationStateService
{
    Task<RuntimeStackFederationStateResponse?> GetAsync(
        string slugOrId,
        CancellationToken ct);
}

public sealed class RuntimeStackFederationStateService
    : IRuntimeStackFederationStateService
{
    private readonly RuntimeStackManifestStore _manifestStore;
    private readonly SynapseFederationConfigReader _configReader;
    private readonly IRuntimeStackFederationContainerInspector _containerInspector;
    private readonly IRuntimeStackFederationIngressInspector _ingressInspector;
    private readonly IRuntimeOperationStore _operationStore;
    private readonly ILogger<RuntimeStackFederationStateService> _logger;

    public RuntimeStackFederationStateService(
        RuntimeStackManifestStore manifestStore,
        SynapseFederationConfigReader configReader,
        IRuntimeStackFederationContainerInspector containerInspector,
        IRuntimeStackFederationIngressInspector ingressInspector,
        IRuntimeOperationStore operationStore,
        ILogger<RuntimeStackFederationStateService> logger)
    {
        _manifestStore = manifestStore;
        _configReader = configReader;
        _containerInspector = containerInspector;
        _ingressInspector = ingressInspector;
        _operationStore = operationStore;
        _logger = logger;
    }

    public async Task<RuntimeStackFederationStateResponse?> GetAsync(
        string slugOrId,
        CancellationToken ct)
    {
        var manifest = await _manifestStore.FindAsync(slugOrId, ct);
        return manifest is null
            ? null
            : await ProjectAsync(manifest, ct);
    }

    internal async Task<RuntimeStackFederationStateResponse> ProjectAsync(
        RuntimeStackManifest manifest,
        CancellationToken ct)
    {
        SynapseFederationConfigReadResult? config = null;
        FederationContainerObservation? container = null;
        FederationIngressObservation? ingress = null;
        var collectionProblems = new List<FederationProblemResponse>();

        var configPath = manifest.Matrix.ConfigPath;
        if (string.IsNullOrWhiteSpace(configPath))
        {
            collectionProblems.Add(new FederationProblemResponse(
                "federation_manifest_incomplete",
                "The Runtime Stack manifest does not contain a Matrix homeserver configuration path."));
        }
        else if (!File.Exists(configPath))
        {
            collectionProblems.Add(new FederationProblemResponse(
                "federation_config_missing",
                "The Matrix homeserver configuration file could not be found at the manifest-owned location."));
        }
        else
        {
            try
            {
                config = await _configReader.ReadAsync(configPath, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex,
                    "Federation state could not read the Matrix configuration. StackId={StackId}",
                    manifest.StackId);
                collectionProblems.Add(new FederationProblemResponse(
                    "federation_config_unavailable",
                    "The Matrix homeserver configuration could not be read."));
            }
        }

        try
        {
            container = await _containerInspector.InspectAsync(manifest.Matrix, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Federation state could not inspect the Matrix container. StackId={StackId}",
                manifest.StackId);
            collectionProblems.Add(new FederationProblemResponse(
                "federation_matrix_container_unavailable",
                "The Matrix container could not be inspected."));
        }

        try
        {
            ingress = await _ingressInspector.InspectAsync(manifest.Matrix, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Federation state could not inspect NPM ingress. StackId={StackId}",
                manifest.StackId);
            collectionProblems.Add(new FederationProblemResponse(
                "federation_ingress_unavailable",
                "The canonical Matrix NPM route could not be inspected."));
        }

        var projected = RuntimeStackFederationStateClassifier.Classify(
            manifest,
            config,
            container,
            ingress,
            collectionProblems);

        try
        {
            var latest = (await _operationStore.ListForStackAsync(
                    manifest.StackId,
                    limit: 20,
                    ct))
                .FirstOrDefault(operation => string.Equals(
                    operation.Operation,
                    RuntimeStackFederationApplyService.OperationName,
                    StringComparison.Ordinal));
            if (latest is not null)
            {
                projected = projected with
                {
                    LatestOperation = new RuntimeStackFederationOperationSummaryResponse(
                        latest.Id,
                        latest.Status,
                        latest.CurrentStep,
                        latest.RequestedAtUtc,
                        latest.CompletedAtUtc)
                };
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Federation state could not read the latest Runtime Operation. StackId={StackId}",
                manifest.StackId);
        }

        return projected;
    }
}

public static class RuntimeStackFederationStateClassifier
{
    public static RuntimeStackFederationStateResponse Classify(
        RuntimeStackManifest manifest,
        SynapseFederationConfigReadResult? config,
        FederationContainerObservation? container,
        FederationIngressObservation? ingress,
        IReadOnlyList<FederationProblemResponse>? collectionProblems = null)
    {
        var problems = new List<FederationProblemResponse>();
        var warnings = new List<FederationProblemResponse>();
        if (collectionProblems is not null)
        {
            problems.AddRange(collectionProblems);
        }

        if (config?.ProblemCode is not null)
        {
            problems.Add(new FederationProblemResponse(
                config.ProblemCode,
                config.Detail ?? "The Matrix federation configuration is unsupported."));
        }

        if (container?.ProblemCode is not null)
        {
            problems.Add(new FederationProblemResponse(
                container.ProblemCode,
                container.Detail ?? "The Matrix container could not be verified."));
        }

        if (ingress?.ProblemCode is not null)
        {
            problems.Add(new FederationProblemResponse(
                ingress.ProblemCode,
                ingress.Detail ?? "The Matrix NPM route could not be classified."));
        }

        var mode = config?.Mode ?? FederationModes.Unknown;
        var state = FederationConfigurationStates.Healthy;

        var coreUnavailable = config is null ||
            container is null ||
            ingress is null ||
            !container.Exists ||
            !container.IdentityMatches ||
            container.ExpectedNetworkAttached is null ||
            !ingress.Available;

        if (coreUnavailable)
        {
            mode = FederationModes.Unknown;
            state = FederationConfigurationStates.Unavailable;
        }
        else if (!config.Supported ||
                 string.Equals(ingress.IngressMode, FederationIngressModes.CustomUnsupported, StringComparison.Ordinal))
        {
            mode = FederationModes.Unknown;
            state = FederationConfigurationStates.CustomUnsupported;
        }
        else if (string.Equals(config.Mode, FederationModes.LocalOnly, StringComparison.Ordinal))
        {
            if (!string.Equals(ingress.IngressMode, FederationIngressModes.LocalOnly, StringComparison.Ordinal))
            {
                state = FederationConfigurationStates.Incomplete;
                AddProblemOnce(
                    problems,
                    "federation_local_only_incomplete",
                    "Synapse has an empty federation allowlist, but the canonical NPM route does not enforce Local-only ingress.");
            }
        }
        else if (string.Equals(ingress.IngressMode, FederationIngressModes.LocalOnly, StringComparison.Ordinal))
        {
            mode = FederationModes.Unknown;
            state = FederationConfigurationStates.Incomplete;
            AddProblemOnce(
                problems,
                "federation_ingress_policy_mismatch",
                "NPM blocks federation ingress while Synapse is configured for Public or Restricted federation.");
        }
        else if (!string.Equals(ingress.IngressMode, FederationIngressModes.Normal, StringComparison.Ordinal))
        {
            state = FederationConfigurationStates.Incomplete;
        }

        if (container is not null)
        {
            if (!container.Running && !string.Equals(state, FederationConfigurationStates.Unavailable, StringComparison.Ordinal))
            {
                state = FederationConfigurationStates.Incomplete;
                AddProblemOnce(
                    problems,
                    "federation_matrix_not_running",
                    "The Matrix container is not running.");
            }

            if (container.ExpectedNetworkAttached == false &&
                !string.Equals(state, FederationConfigurationStates.Unavailable, StringComparison.Ordinal))
            {
                state = FederationConfigurationStates.Incomplete;
                AddProblemOnce(
                    problems,
                    "federation_matrix_network_mismatch",
                    "The Matrix container is not attached to the runtime network recorded by the manifest.");
            }

            if (container.DirectHostPortExposed &&
                !string.Equals(state, FederationConfigurationStates.Unavailable, StringComparison.Ordinal))
            {
                var directPortProblem = new FederationProblemResponse(
                    "federation_direct_host_port_exposed",
                    "The Matrix container publishes a direct Docker host port that bypasses the canonical NPM route.");

                if (string.Equals(config?.Mode, FederationModes.LocalOnly, StringComparison.Ordinal))
                {
                    state = FederationConfigurationStates.Incomplete;
                    AddProblemOnce(problems, directPortProblem.Code, directPortProblem.Detail);
                }
                else
                {
                    warnings.Add(directPortProblem);
                }
            }
        }

        if (ingress is not null && ingress.Available)
        {
            if (!ingress.CanonicalRouteFound || !ingress.RouteEnabled || !ingress.RouteTargetsExpectedMatrix)
            {
                if (!string.Equals(state, FederationConfigurationStates.CustomUnsupported, StringComparison.Ordinal) &&
                    !string.Equals(state, FederationConfigurationStates.Unavailable, StringComparison.Ordinal))
                {
                    state = FederationConfigurationStates.Incomplete;
                }

                if (!ingress.CanonicalRouteFound)
                {
                    AddProblemOnce(
                        problems,
                        "federation_ingress_missing",
                        "The canonical Matrix NPM route is missing.");
                }
                else if (!ingress.RouteTargetsExpectedMatrix)
                {
                    AddProblemOnce(
                        problems,
                        "federation_ingress_target_mismatch",
                        "The canonical Matrix NPM route does not target the manifest-owned Matrix upstream.");
                }
                else if (!ingress.RouteEnabled)
                {
                    AddProblemOnce(
                        problems,
                        "federation_ingress_disabled",
                        "The canonical Matrix NPM route is not enabled and online.");
                }
            }

            if (ingress.AlternateMatrixRouteDetected)
            {
                if (string.Equals(config?.Mode, FederationModes.LocalOnly, StringComparison.Ordinal))
                {
                    state = FederationConfigurationStates.Incomplete;
                    AddProblemOnce(
                        problems,
                        "federation_alternate_route_detected",
                        "Another enabled NPM proxy host forwards to the same Matrix upstream and could bypass Local-only ingress.");
                }
                else
                {
                    warnings.Add(new FederationProblemResponse(
                        "federation_alternate_route_detected",
                        "Another enabled NPM proxy host forwards to the same Matrix upstream."));
                }
            }

            if (!ingress.CertificatePresent)
            {
                warnings.Add(new FederationProblemResponse(
                    "federation_certificate_not_observed",
                    "The canonical Matrix route does not currently show forced SSL with an NPM certificate."));
            }
        }

        var checks = BuildChecks(config, container, ingress);
        var fingerprint = ComputeFingerprint(manifest, config, container, ingress, mode);

        return new RuntimeStackFederationStateResponse(
            Source: "control-plane",
            Status: "ok",
            RuntimeStackId: manifest.StackId,
            Slug: manifest.Slug,
            Mode: mode,
            ConfigurationState: state,
            Allowlist: config?.Allowlist ?? [],
            EnforcementKind: ResolveEnforcementKind(mode),
            MatrixContainerRunning: container?.Running == true,
            MatrixDirectHostPortExposed: container?.DirectHostPortExposed == true,
            IngressMode: ingress?.IngressMode ?? FederationIngressModes.Unavailable,
            ServerWellKnownPublished: ingress?.ServerWellKnownPublished == true,
            FederationPathsPubliclyForwarded: ingress?.FederationPathsPubliclyForwarded == true,
            SigningKeyPathsPubliclyForwarded: ingress?.SigningKeyPathsPubliclyForwarded == true,
            CanonicalRouteEnabled: ingress?.RouteEnabled == true,
            CanonicalRouteTargetsMatrix: ingress?.RouteTargetsExpectedMatrix == true,
            CanonicalCertificatePresent: ingress?.CertificatePresent == true,
            AlternateMatrixRouteDetected: ingress?.AlternateMatrixRouteDetected == true,
            StateFingerprint: fingerprint,
            LatestOperation: null,
            Checks: checks,
            Warnings: warnings,
            Problems: problems);
    }

    private static IReadOnlyList<FederationCheckResponse> BuildChecks(
        SynapseFederationConfigReadResult? config,
        FederationContainerObservation? container,
        FederationIngressObservation? ingress)
    {
        var checks = new List<FederationCheckResponse>
        {
            new(
                "federation.config.supported",
                config is null
                    ? FederationCheckStatuses.Unknown
                    : config.Supported
                        ? FederationCheckStatuses.Passed
                        : FederationCheckStatuses.Failed,
                config is null
                    ? "The active Synapse configuration could not be inspected."
                    : config.Supported
                        ? "The active Synapse federation configuration uses a supported representation."
                        : "The active Synapse federation configuration uses an unsupported custom representation."),
            new(
                "federation.matrix.running",
                container is null
                    ? FederationCheckStatuses.Unknown
                    : container.Running
                        ? FederationCheckStatuses.Passed
                        : FederationCheckStatuses.Failed,
                container?.Running == true
                    ? "The Matrix container is running."
                    : "The Matrix container is not confirmed running."),
            new(
                "federation.direct_host_port.absent",
                container is null
                    ? FederationCheckStatuses.Unknown
                    : container.DirectHostPortExposed
                        ? FederationCheckStatuses.Failed
                        : FederationCheckStatuses.Passed,
                container is null
                    ? "The Matrix container could not be inspected for direct host-port exposure."
                    : container.DirectHostPortExposed
                        ? "A direct Matrix Docker host port is published."
                        : "No direct Matrix Docker host port is published."),
            new(
                "federation.ingress.canonical_route",
                ingress is null
                    ? FederationCheckStatuses.Unknown
                    : ingress.CanonicalRouteFound && ingress.RouteEnabled && ingress.RouteTargetsExpectedMatrix
                        ? FederationCheckStatuses.Passed
                        : FederationCheckStatuses.Failed,
                ingress is null
                    ? "The canonical Matrix NPM route could not be inspected."
                    : ingress.CanonicalRouteFound && ingress.RouteEnabled && ingress.RouteTargetsExpectedMatrix
                        ? "The canonical Matrix NPM route is enabled and targets the manifest-owned Matrix upstream."
                        : "The canonical Matrix NPM route is missing, disabled, or targets a different upstream."),
            new(
                "federation.ingress.alternate_route.absent",
                ingress is null
                    ? FederationCheckStatuses.Unknown
                    : ingress.AlternateMatrixRouteDetected
                        ? FederationCheckStatuses.Warning
                        : FederationCheckStatuses.Passed,
                ingress is null
                    ? "NPM routes could not be inspected for an alternate Matrix upstream route."
                    : ingress.AlternateMatrixRouteDetected
                        ? "Another enabled NPM proxy host forwards to the same Matrix upstream."
                        : "No alternate NPM route to the same Matrix upstream was detected.")
        };

        return checks;
    }

    private static string ResolveEnforcementKind(string mode) => mode switch
    {
        FederationModes.Public => "synapse_unrestricted",
        FederationModes.Restricted => "synapse_exact_domain_allowlist",
        FederationModes.LocalOnly => "synapse_empty_allowlist_and_npm_ingress",
        _ => "unknown"
    };

    private static string? ComputeFingerprint(
        RuntimeStackManifest manifest,
        SynapseFederationConfigReadResult? config,
        FederationContainerObservation? container,
        FederationIngressObservation? ingress,
        string mode)
    {
        if (config is null || container is null || ingress is null)
        {
            return null;
        }

        var canonical = string.Join("\n",
        [
            manifest.StackId.ToString("D"),
            container.ContainerId ?? string.Empty,
            container.Image ?? string.Empty,
            config.FileSha256,
            ingress.RouteSnapshotSha256 ?? string.Empty,
            manifest.Matrix.PublicHost ?? string.Empty,
            manifest.Matrix.InternalHost ?? string.Empty,
            manifest.Matrix.InternalBaseUrl ?? string.Empty,
            mode,
            container.DirectHostPortExposed ? "direct-port" : "no-direct-port"
        ]);

        return "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static void AddProblemOnce(
        ICollection<FederationProblemResponse> problems,
        string code,
        string detail)
    {
        if (problems.Any(problem => string.Equals(problem.Code, code, StringComparison.Ordinal)))
        {
            return;
        }

        problems.Add(new FederationProblemResponse(code, detail));
    }
}
