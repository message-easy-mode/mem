using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Runtime.Manifests;

namespace HostAgent.Matrix.Federation;

public sealed record RuntimeStackFederationVerificationResult(
    bool Succeeded,
    string ObservedMode,
    IReadOnlyList<FederationCheckResponse> Checks);

public interface IRuntimeStackFederationVerifier
{
    Task<RuntimeStackFederationVerificationResult> VerifyAsync(
        RuntimeStackManifest manifest,
        CanonicalFederationPolicyRequest expected,
        CancellationToken ct);

    Task<RuntimeStackFederationVerificationResult> VerifyLocalOnlyIngressGuardAsync(
        RuntimeStackManifest manifest,
        CancellationToken ct);

    Task<RuntimeStackFederationVerificationResult> VerifyClientReadinessAsync(
        RuntimeStackManifest manifest,
        CancellationToken ct);
}

public sealed class RuntimeStackFederationVerifier
    : IRuntimeStackFederationVerifier
{
    internal const string NpmContainerName = "mem-npm";
    internal const string PublicProbeIp = "127.0.0.1";
    private static readonly TimeSpan VerificationTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private readonly DockerClient _docker;
    private readonly IRuntimeStackFederationStateService _stateService;

    public RuntimeStackFederationVerifier(
        DockerClient docker,
        IRuntimeStackFederationStateService stateService)
    {
        _docker = docker;
        _stateService = stateService;
    }

    public Task<RuntimeStackFederationVerificationResult> VerifyAsync(
        RuntimeStackManifest manifest,
        CanonicalFederationPolicyRequest expected,
        CancellationToken ct) =>
        RetryAsync(() => VerifyOnceAsync(manifest, expected, ct), ct);

    public Task<RuntimeStackFederationVerificationResult> VerifyLocalOnlyIngressGuardAsync(
        RuntimeStackManifest manifest,
        CancellationToken ct) =>
        RetryAsync(() => VerifyLocalOnlyIngressGuardOnceAsync(manifest, ct), ct);

    public Task<RuntimeStackFederationVerificationResult> VerifyClientReadinessAsync(
        RuntimeStackManifest manifest,
        CancellationToken ct) =>
        RetryAsync(() => VerifyClientReadinessOnceAsync(manifest, ct), ct);

    private static async Task<RuntimeStackFederationVerificationResult> RetryAsync(
        Func<Task<RuntimeStackFederationVerificationResult>> verify,
        CancellationToken ct)
    {
        RuntimeStackFederationVerificationResult? last = null;
        var startedAt = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - startedAt < VerificationTimeout)
        {
            ct.ThrowIfCancellationRequested();
            last = await verify();
            if (last.Succeeded)
            {
                return last;
            }

            await Task.Delay(RetryDelay, ct);
        }

        return last ?? new RuntimeStackFederationVerificationResult(
            Succeeded: false,
            ObservedMode: FederationModes.Unknown,
            Checks:
            [
                new FederationCheckResponse(
                    "federation.verification.unavailable",
                    FederationCheckStatuses.Failed,
                    "Federation verification did not produce a result before the bounded timeout.")
            ]);
    }

    private async Task<RuntimeStackFederationVerificationResult> VerifyOnceAsync(
        RuntimeStackManifest manifest,
        CanonicalFederationPolicyRequest expected,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(expected);

        var checks = new List<FederationCheckResponse>();
        var state = await _stateService.GetAsync(manifest.StackId.ToString("D"), ct);
        var localOnly = string.Equals(
            expected.Mode,
            FederationModes.LocalOnly,
            StringComparison.Ordinal);
        var expectedIngress = localOnly
            ? FederationIngressModes.LocalOnly
            : FederationIngressModes.Normal;

        var stateMatches = state is not null &&
            string.Equals(state.ConfigurationState, FederationConfigurationStates.Healthy, StringComparison.Ordinal) &&
            string.Equals(state.Mode, expected.Mode, StringComparison.Ordinal) &&
            string.Equals(state.IngressMode, expectedIngress, StringComparison.Ordinal) &&
            state.MatrixContainerRunning &&
            !state.MatrixDirectHostPortExposed &&
            !state.AlternateMatrixRouteDetected &&
            state.CanonicalRouteEnabled &&
            state.CanonicalRouteTargetsMatrix &&
            (localOnly
                ? !state.ServerWellKnownPublished &&
                  !state.FederationPathsPubliclyForwarded &&
                  !state.SigningKeyPathsPubliclyForwarded
                : state.ServerWellKnownPublished &&
                  state.FederationPathsPubliclyForwarded &&
                  state.SigningKeyPathsPubliclyForwarded) &&
            AllowlistsEqual(state.Allowlist, expected.Allowlist);
        checks.Add(new FederationCheckResponse(
            "federation.effective_state.matches",
            stateMatches ? FederationCheckStatuses.Passed : FederationCheckStatuses.Failed,
            stateMatches
                ? "The active config, Matrix container, and NPM ingress match the requested federation mode."
                : "The active federation state does not yet match the requested mode."));

        if (!TryResolveUrls(manifest, checks, out var internalBase, out var publicBase))
        {
            return new RuntimeStackFederationVerificationResult(
                false,
                state?.Mode ?? FederationModes.Unknown,
                checks);
        }

        checks.Add(await ProbeFromNpmAsync(
            "federation.matrix.client_ready",
            internalBase + "/_matrix/client/versions",
            ct));
        checks.Add(await ProbePublicAsync(
            "federation.public_client.available",
            publicBase + "/_matrix/client/versions",
            status => status is >= 200 and < 400,
            "The Matrix client endpoint is available through the local NPM HTTPS route.",
            ct));

        if (localOnly)
        {
            checks.Add(await ProbePublicAsync(
                "federation.server_well_known.blocked",
                publicBase + "/.well-known/matrix/server",
                status => status == 404,
                "Matrix server discovery is not published in Local-only mode.",
                ct));
            checks.Add(await ProbePublicAsync(
                "federation.public_endpoint.blocked",
                publicBase + "/_matrix/federation/v1/version",
                status => status == 404,
                "The public federation endpoint is blocked in Local-only mode.",
                ct));
            checks.Add(await ProbePublicAsync(
                "federation.signing_key.blocked",
                publicBase + "/_matrix/key/v2/server",
                status => status == 404,
                "The public signing-key endpoint is blocked in Local-only mode.",
                ct));
        }
        else
        {
            checks.Add(await ProbePublicAsync(
                "federation.server_well_known.available",
                publicBase + "/.well-known/matrix/server",
                status => status is >= 200 and < 400,
                "Matrix server discovery is available through the local NPM HTTPS route.",
                ct));
            checks.Add(await ProbePublicAsync(
                "federation.public_endpoint.available",
                publicBase + "/_matrix/federation/v1/version",
                status => status is >= 200 and < 400,
                "The public federation endpoint is available through the local NPM HTTPS route.",
                ct));
            checks.Add(await ProbePublicAsync(
                "federation.signing_key.available",
                publicBase + "/_matrix/key/v2/server",
                status => status is >= 200 and < 400,
                "The public signing-key endpoint is available through the local NPM HTTPS route.",
                ct));
        }

        return new RuntimeStackFederationVerificationResult(
            Succeeded: checks.All(IsPassed),
            ObservedMode: state?.Mode ?? FederationModes.Unknown,
            Checks: checks);
    }

    private async Task<RuntimeStackFederationVerificationResult> VerifyLocalOnlyIngressGuardOnceAsync(
        RuntimeStackManifest manifest,
        CancellationToken ct)
    {
        var checks = new List<FederationCheckResponse>();
        var state = await _stateService.GetAsync(manifest.StackId.ToString("D"), ct);
        var guardMatches = state is not null &&
            string.Equals(state.IngressMode, FederationIngressModes.LocalOnly, StringComparison.Ordinal) &&
            state.MatrixContainerRunning &&
            !state.MatrixDirectHostPortExposed &&
            !state.AlternateMatrixRouteDetected &&
            state.CanonicalRouteEnabled &&
            state.CanonicalRouteTargetsMatrix &&
            !state.ServerWellKnownPublished &&
            !state.FederationPathsPubliclyForwarded &&
            !state.SigningKeyPathsPubliclyForwarded;
        checks.Add(new FederationCheckResponse(
            "federation.local_only.ingress_guard",
            guardMatches ? FederationCheckStatuses.Passed : FederationCheckStatuses.Failed,
            guardMatches
                ? "The canonical NPM route blocks server discovery, federation, and signing-key ingress without a bypass."
                : "The canonical NPM route does not yet provide a verified Local-only ingress guard."));

        if (!TryResolveUrls(manifest, checks, out _, out var publicBase))
        {
            return new RuntimeStackFederationVerificationResult(
                false,
                state?.Mode ?? FederationModes.Unknown,
                checks);
        }

        checks.Add(await ProbePublicAsync(
            "federation.public_client.available",
            publicBase + "/_matrix/client/versions",
            status => status is >= 200 and < 400,
            "The Matrix client endpoint remains available while Local-only ingress is active.",
            ct));
        checks.Add(await ProbePublicAsync(
            "federation.server_well_known.blocked",
            publicBase + "/.well-known/matrix/server",
            status => status == 404,
            "Matrix server discovery is blocked before Synapse is changed.",
            ct));
        checks.Add(await ProbePublicAsync(
            "federation.public_endpoint.blocked",
            publicBase + "/_matrix/federation/v1/version",
            status => status == 404,
            "The public federation endpoint is blocked before Synapse is changed.",
            ct));
        checks.Add(await ProbePublicAsync(
            "federation.signing_key.blocked",
            publicBase + "/_matrix/key/v2/server",
            status => status == 404,
            "The public signing-key endpoint is blocked before Synapse is changed.",
            ct));

        return new RuntimeStackFederationVerificationResult(
            checks.All(IsPassed),
            state?.Mode ?? FederationModes.Unknown,
            checks);
    }

    private async Task<RuntimeStackFederationVerificationResult> VerifyClientReadinessOnceAsync(
        RuntimeStackManifest manifest,
        CancellationToken ct)
    {
        var checks = new List<FederationCheckResponse>();
        var state = await _stateService.GetAsync(manifest.StackId.ToString("D"), ct);
        var runtimeReady = state is not null &&
            state.MatrixContainerRunning &&
            !state.MatrixDirectHostPortExposed &&
            state.CanonicalRouteEnabled &&
            state.CanonicalRouteTargetsMatrix;
        checks.Add(new FederationCheckResponse(
            "federation.matrix.runtime_ready",
            runtimeReady ? FederationCheckStatuses.Passed : FederationCheckStatuses.Failed,
            runtimeReady
                ? "The manifest-selected Matrix runtime and canonical route remain healthy."
                : "The Matrix runtime or canonical route is not healthy."));

        if (!TryResolveUrls(manifest, checks, out var internalBase, out var publicBase))
        {
            return new RuntimeStackFederationVerificationResult(
                false,
                state?.Mode ?? FederationModes.Unknown,
                checks);
        }

        checks.Add(await ProbeFromNpmAsync(
            "federation.matrix.client_ready",
            internalBase + "/_matrix/client/versions",
            ct));
        checks.Add(await ProbePublicAsync(
            "federation.public_client.available",
            publicBase + "/_matrix/client/versions",
            status => status is >= 200 and < 400,
            "The Matrix client endpoint is available before federation ingress is reopened.",
            ct));

        return new RuntimeStackFederationVerificationResult(
            checks.All(IsPassed),
            state?.Mode ?? FederationModes.Unknown,
            checks);
    }

    private static bool TryResolveUrls(
        RuntimeStackManifest manifest,
        ICollection<FederationCheckResponse> checks,
        out string internalBase,
        out string publicBase)
    {
        internalBase = manifest.Matrix.InternalBaseUrl?.TrimEnd('/') ?? string.Empty;
        publicBase = manifest.Matrix.PublicBaseUrl?.TrimEnd('/') ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(internalBase) &&
            !string.IsNullOrWhiteSpace(publicBase))
        {
            return true;
        }

        checks.Add(new FederationCheckResponse(
            "federation.manifest.urls",
            FederationCheckStatuses.Failed,
            "The Runtime Stack manifest does not contain the Matrix internal and public base URLs."));
        return false;
    }

    private async Task<FederationCheckResponse> ProbeFromNpmAsync(
        string code,
        string url,
        CancellationToken ct)
    {
        try
        {
            var exec = await _docker.Exec.ExecCreateContainerAsync(
                NpmContainerName,
                new ContainerExecCreateParameters
                {
                    AttachStdout = true,
                    AttachStderr = true,
                    Cmd =
                    [
                        "curl",
                        "-sS",
                        "--max-time",
                        "10",
                        "-o",
                        "/dev/null",
                        "-w",
                        "%{http_code}",
                        url
                    ]
                },
                ct);
            using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(
                exec.ID,
                tty: false,
                ct);
            var output = await stream.ReadOutputToEndAsync(ct);
            var inspect = await _docker.Exec.InspectContainerExecAsync(exec.ID, ct);
            var status = TryParseStatus(output.stdout);
            var success = inspect.ExitCode == 0 && status is >= 200 and < 400;
            return new FederationCheckResponse(
                code,
                success ? FederationCheckStatuses.Passed : FederationCheckStatuses.Failed,
                success
                    ? "The Matrix client endpoint is reachable from NPM on the MEM runtime network."
                    : $"The Matrix client endpoint was not ready from NPM (HTTP {status?.ToString() ?? "unknown"}).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new FederationCheckResponse(
                code,
                FederationCheckStatuses.Failed,
                "The Matrix client endpoint could not be reached from NPM.");
        }
    }

    private async Task<FederationCheckResponse> ProbePublicAsync(
        string code,
        string url,
        Func<int, bool> expected,
        string successDetail,
        CancellationToken ct)
    {
        try
        {
            var exec = await _docker.Exec.ExecCreateContainerAsync(
                NpmContainerName,
                new ContainerExecCreateParameters
                {
                    AttachStdout = true,
                    AttachStderr = true,
                    Cmd = BuildPublicHttpsProbeCommand(url)
                },
                ct);
            using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(
                exec.ID,
                tty: false,
                ct);
            var output = await stream.ReadOutputToEndAsync(ct);
            var inspect = await _docker.Exec.InspectContainerExecAsync(exec.ID, ct);
            var status = TryParseStatus(output.stdout);
            var success = inspect.ExitCode == 0 &&
                status is not null &&
                expected(status.Value);
            return new FederationCheckResponse(
                code,
                success ? FederationCheckStatuses.Passed : FederationCheckStatuses.Failed,
                success
                    ? successDetail
                    : $"The endpoint returned unexpected HTTP {status?.ToString() ?? "unknown"} through the local NPM HTTPS route.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new FederationCheckResponse(
                code,
                FederationCheckStatuses.Failed,
                "The endpoint could not be reached through the local NPM HTTPS route.");
        }
    }

    internal static string[] BuildPublicHttpsProbeCommand(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.IdnHost))
        {
            throw new ArgumentException(
                "Federation public probes require an absolute HTTPS URL.",
                nameof(url));
        }

        var port = uri.IsDefaultPort ? 443 : uri.Port;
        var resolve = $"{uri.IdnHost}:{port}:{PublicProbeIp}";

        return
        [
            "curl",
            "-k",
            "-sS",
            "--max-time",
            "10",
            "--resolve",
            resolve,
            "-o",
            "/dev/null",
            "-w",
            "%{http_code}",
            url
        ];
    }

    private static int? TryParseStatus(string? value) =>
        int.TryParse(value?.Trim(), out var status) ? status : null;

    private static bool IsPassed(FederationCheckResponse check) =>
        string.Equals(check.Status, FederationCheckStatuses.Passed, StringComparison.Ordinal);

    private static bool AllowlistsEqual(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right) =>
        left.OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(
                right.OrderBy(x => x, StringComparer.Ordinal),
                StringComparer.Ordinal);
}
