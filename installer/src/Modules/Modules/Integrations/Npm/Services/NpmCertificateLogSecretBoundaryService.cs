using Core.RuntimeDefinition;
using Infrastructure.Docker;
using Microsoft.Extensions.Logging;

namespace Modules.Integrations.Npm.Services;

/// <summary>
/// Protects certificate private-key material from the reviewed NPM v2.15.1
/// custom-certificate logging defect before MEM sends any private key to NPM.
///
/// Request cancellation may govern read-only validation. Once MEM begins
/// changing the NPM source/runtime, the hardening lifecycle switches to a
/// bounded server-owned token so a browser disconnect cannot leave ingress
/// stopped or leave a patched-but-not-activated logger boundary behind.
/// </summary>
public sealed class NpmCertificateLogSecretBoundaryService(
    IDockerHost dockerHost,
    ILogger<NpmCertificateLogSecretBoundaryService> logger)
{
    private const string CertificateSourcePath = "/app/internal/certificate.js";
    private const string ActivationMarkerPath = "/data/.mem/npm-certificate-log-boundary-v1";

    private static readonly TimeSpan ExecTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MutationTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(15);

    // Keep these markers exact and deliberately narrow. If the pinned upstream
    // source changes, certificate import must fail closed until MEM reviews the
    // new upstream behavior instead of guessing that secret logging is safe.
    internal const string VulnerableLogMarker =
        "logger.info(\"Writing Custom Certificate:\", certificate);";

    internal const string HardenedLogMarker =
        "logger.info(`Writing Custom Certificate: id=${certificate.id} nice_name=${certificate.nice_name || \"\"}`);";

    private const string InspectScript = """
const fs = require("fs");
const sourcePath = process.argv[1];
const markerPath = process.argv[2];
const expectedContainerId = process.argv[3];
const vulnerable = 'logger.info("Writing Custom Certificate:", certificate);';
const hardened = 'logger.info(`Writing Custom Certificate: id=${certificate.id} nice_name=${certificate.nice_name || ""}`);';

const source = fs.readFileSync(sourcePath, "utf8");
let marker = "";
try {
    marker = fs.readFileSync(markerPath, "utf8").trim();
} catch (_) {
    marker = "";
}

const vulnerableMatches = source.split(vulnerable).length - 1;
const hardenedMatches = source.split(hardened).length - 1;

if (vulnerableMatches === 1 && hardenedMatches === 0) {
    process.stdout.write("vulnerable");
    process.exit(0);
}

if (vulnerableMatches === 0 && hardenedMatches === 1) {
    process.stdout.write(marker === expectedContainerId ? "hardened-active" : "hardened-inactive");
    process.exit(0);
}

process.stderr.write(`unexpected-source:vulnerable=${vulnerableMatches};hardened=${hardenedMatches}`);
process.exit(42);
""";

    private const string PatchScript = """
const fs = require("fs");
const sourcePath = process.argv[1];
const vulnerable = 'logger.info("Writing Custom Certificate:", certificate);';
const hardened = 'logger.info(`Writing Custom Certificate: id=${certificate.id} nice_name=${certificate.nice_name || ""}`);';

const source = fs.readFileSync(sourcePath, "utf8");
const matches = source.split(vulnerable).length - 1;
if (matches !== 1 || source.includes(hardened)) {
    process.stderr.write(`unexpected-source:${matches}`);
    process.exit(42);
}

const updated = source.replace(vulnerable, hardened);
fs.writeFileSync(sourcePath, updated, { encoding: "utf8" });

const verified = fs.readFileSync(sourcePath, "utf8");
if (verified.includes(vulnerable) || !verified.includes(hardened)) {
    process.stderr.write("verification-failed");
    process.exit(43);
}

process.stdout.write("patched");
""";

    private const string ReadinessScript = """
const http = require("http");
const deadline = Date.now() + 20000;

function probe() {
    const request = http.get("http://127.0.0.1:81/api", (response) => {
        response.resume();
        if (response.statusCode >= 200 && response.statusCode < 500) {
            process.stdout.write("ready");
            process.exit(0);
        }
        retry();
    });

    request.setTimeout(1000, () => request.destroy());
    request.on("error", retry);
}

function retry() {
    if (Date.now() >= deadline) {
        process.stderr.write("npm-api-not-ready");
        process.exit(44);
    }
    setTimeout(probe, 250);
}

probe();
""";

    private const string ActivationMarkerScript = """
const fs = require("fs");
const path = require("path");
const markerPath = process.argv[1];
const containerId = process.argv[2];

fs.mkdirSync(path.dirname(markerPath), { recursive: true });
fs.writeFileSync(markerPath, `${containerId}\n`, { encoding: "utf8", mode: 0o600 });
process.stdout.write("activated");
""";

    public async Task<NpmCertificateLogSecretBoundaryResult> EnsureHardenedAsync(
        CancellationToken cancellationToken)
    {
        var container = await dockerHost.InspectByNameAsync(
            ManagedContainerNames.Npm,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "NPM certificate import was blocked because the managed NPM container was not found.");

        if (!container.Running)
        {
            throw new InvalidOperationException(
                "NPM certificate import was blocked because the managed NPM container is not running.");
        }

        if (!NpmRuntimeRelease.IsApprovedImage(container.Image))
        {
            throw new InvalidOperationException(
                $"NPM certificate import was blocked because MEM cannot verify the private-key log boundary for image '{container.Image}'. Expected '{NpmRuntimeRelease.ApprovedImage}'.");
        }

        // Read-only preflight remains request-owned. If the caller has already
        // gone away, no host mutation has been accepted yet.
        var inspected = await dockerHost.ExecAsync(
            container.Id,
            ["node", "-e", InspectScript, CertificateSourcePath, ActivationMarkerPath, container.Id],
            ExecTimeout,
            cancellationToken);

        if (!inspected.Succeeded)
        {
            var reason = inspected.TimedOut
                ? "the bounded NPM source verification timed out"
                : $"the pinned NPM source did not match the reviewed hardening contract (exit {inspected.ExitCode})";

            throw new InvalidOperationException(
                $"NPM certificate import was blocked because {reason}. No certificate private key was sent to NPM.");
        }

        var state = inspected.StandardOutput.Trim();
        if (string.Equals(state, "hardened-active", StringComparison.Ordinal))
        {
            return new NpmCertificateLogSecretBoundaryResult(
                Changed: false,
                Restarted: false,
                Image: container.Image,
                Message: "NPM certificate private-key log hardening is active for the current managed NPM container.");
        }

        if (state is not ("vulnerable" or "hardened-inactive"))
        {
            throw new InvalidOperationException(
                "NPM certificate import was blocked because MEM could not verify the certificate private-key log hardening state. No certificate private key was sent to NPM.");
        }

        // Mutation acceptance boundary. Do not begin patch/restart work if the
        // request was already cancelled. Once accepted, browser/request lifetime
        // no longer owns the NPM host mutation.
        cancellationToken.ThrowIfCancellationRequested();

        using var mutationLifetime = new CancellationTokenSource(MutationTimeout);
        var mutationToken = mutationLifetime.Token;
        var changed = false;

        try
        {
            if (string.Equals(state, "vulnerable", StringComparison.Ordinal))
            {
                var patch = await dockerHost.ExecAsync(
                    container.Id,
                    ["node", "-e", PatchScript, CertificateSourcePath],
                    ExecTimeout,
                    mutationToken);

                if (!patch.Succeeded || !string.Equals(patch.StandardOutput.Trim(), "patched", StringComparison.Ordinal))
                {
                    var reason = patch.TimedOut
                        ? "the bounded NPM source hardening timed out"
                        : $"the pinned NPM source could not be hardened safely (exit {patch.ExitCode})";

                    throw new InvalidOperationException(
                        $"NPM certificate import was blocked because {reason}. No certificate private key was sent to NPM.");
                }

                changed = true;
            }

            // The vulnerable module may already be loaded in the running Node
            // process. A hardened source file without the current-container
            // activation marker is deliberately treated as not active; this also
            // repairs an interrupted previous attempt.
            await dockerHost.StopContainerAsync(container.Id, mutationToken);
            await dockerHost.StartContainerAsync(container.Id, mutationToken);

            var restarted = await dockerHost.InspectByNameAsync(
                ManagedContainerNames.Npm,
                mutationToken);

            if (restarted?.Running != true)
            {
                throw new InvalidOperationException(
                    "NPM did not return to a running state after certificate-log hardening.");
            }

            var ready = await dockerHost.ExecAsync(
                restarted.Id,
                ["node", "-e", ReadinessScript],
                ReadinessTimeout,
                mutationToken);

            if (!ready.Succeeded || !string.Equals(ready.StandardOutput.Trim(), "ready", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The NPM API did not become ready after certificate-log hardening.");
            }

            var activation = await dockerHost.ExecAsync(
                restarted.Id,
                ["node", "-e", ActivationMarkerScript, ActivationMarkerPath, restarted.Id],
                ExecTimeout,
                mutationToken);

            if (!activation.Succeeded || !string.Equals(activation.StandardOutput.Trim(), "activated", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "MEM could not persist the NPM certificate-log activation marker after restart.");
            }

            logger.LogWarning(
                "Applied or activated MEM certificate private-key log hardening for managed NPM runtime {Image}; NPM was restarted before certificate import.",
                container.Image);

            return new NpmCertificateLogSecretBoundaryResult(
                Changed: changed,
                Restarted: true,
                Image: container.Image,
                Message: changed
                    ? "MEM hardened the NPM certificate logging boundary and restarted NPM before certificate import."
                    : "MEM activated previously written NPM certificate-log hardening and restarted NPM before certificate import.");
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            var recovered = await TryRecoverRunningAsync(container.Id);
            var recoveryText = recovered
                ? "MEM restored NPM to a running state."
                : "MEM could not confirm that NPM returned to a running state.";

            throw new InvalidOperationException(
                $"NPM certificate import was blocked because MEM could not complete the bounded certificate-log hardening lifecycle. {recoveryText} No certificate private key was sent to NPM.",
                ex);
        }
    }

    private async Task<bool> TryRecoverRunningAsync(string containerId)
    {
        using var recoveryLifetime = new CancellationTokenSource(RecoveryTimeout);
        var recoveryToken = recoveryLifetime.Token;

        try
        {
            var current = await dockerHost.InspectByNameAsync(
                ManagedContainerNames.Npm,
                recoveryToken);

            if (current?.Running == true)
            {
                return true;
            }

            await dockerHost.StartContainerAsync(containerId, recoveryToken);

            current = await dockerHost.InspectByNameAsync(
                ManagedContainerNames.Npm,
                recoveryToken);

            return current?.Running == true;
        }
        catch (Exception recoveryException) when (
            recoveryException is not StackOverflowException and not OutOfMemoryException)
        {
            logger.LogError(
                recoveryException,
                "MEM could not recover NPM to a running state after certificate-log hardening failed.");
            return false;
        }
    }
}

public sealed record NpmCertificateLogSecretBoundaryResult(
    bool Changed,
    bool Restarted,
    string Image,
    string Message);
