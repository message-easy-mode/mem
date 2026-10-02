using Modules.Setup.Platform.Coturn;

namespace Modules.Setup.InstallRuns;

public sealed partial class InstallStepExecutor
{
    private async Task<InstallStepResult> InstallSharedPlatformTurnAsync(
        InstallStepContext context,
        CancellationToken cancellationToken)
    {
        if (_platformCoturnSetupService is null)
        {
            return Failed(
                "Shared platform TURN installation is unavailable.",
                "The Coturn Setup service is not registered in the active Control Plane runtime.");
        }

        await BeginProgressPhaseAsync(
            context,
            "coturn.install",
            "Installing or reconciling the shared platform TURN runtime.",
            cancellationToken);

        PlatformCoturnSetupResult result;
        try
        {
            result = await _platformCoturnSetupService.EnsureInstalledAsync(
                new PlatformCoturnSetupRequest(
                    ExternalIp: null,
                    ForceRecreate: false),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(
                "Shared platform TURN installation failed.",
                "The shared platform TURN runtime rejected installation. Review the Coturn workspace and Diagnostics for safe evidence.");
        }

        if (!result.Ready)
        {
            return Failed(
                "Shared platform TURN installation did not reach Ready state.",
                SafeCoturnFailureDetail(result));
        }

        await BeginProgressPhaseAsync(
            context,
            "coturn.installed",
            "The shared platform TURN runtime is structurally ready.",
            cancellationToken);

        return Succeeded(SafeCoturnSuccessMessage(
            "Shared platform TURN installed or reconciled successfully.",
            result));
    }

    private async Task<InstallStepResult> VerifySharedPlatformTurnAsync(
        InstallStepContext context,
        CancellationToken cancellationToken)
    {
        if (_platformCoturnSetupService is null)
        {
            return Failed(
                "Shared platform TURN verification is unavailable.",
                "The Coturn Setup service is not registered in the active Control Plane runtime.");
        }

        await BeginProgressPhaseAsync(
            context,
            "coturn.verify",
            "Verifying shared platform TURN ownership, image, protected state, and published ports.",
            cancellationToken);

        PlatformCoturnSetupResult result;
        try
        {
            result = await _platformCoturnSetupService.InspectAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(
                "Shared platform TURN verification failed.",
                "The shared platform TURN runtime could not be inspected safely. Review the Coturn workspace and Diagnostics.");
        }

        if (!result.Ready)
        {
            return Failed(
                "Shared platform TURN is not ready.",
                SafeCoturnFailureDetail(result));
        }

        return Succeeded(SafeCoturnSuccessMessage(
            "Shared platform TURN verification completed successfully.",
            result));
    }

    private static string SafeCoturnSuccessMessage(
        string headline,
        PlatformCoturnSetupResult result)
    {
        var lines = new List<string>
        {
            headline,
            $"Container: {result.ContainerName}",
            $"Public host: {result.PublicHost}",
            $"Realm: {result.Realm}",
            $"Readiness: {result.Readiness}",
            $"Approved image: {(result.ImageApproved ? "yes" : "no")}",
            $"Ownership verified: {(result.OwnershipVerified ? "yes" : "no")}",
            $"Protected secret present: {(result.SecretPresent ? "yes" : "no")}",
            $"Relay ports published: {(result.RelayPortsPublished ? "yes" : "no")}",
            $"Security policy applied: {(result.SecurityPolicyApplied ? "yes" : "no")}",
            $"Published ports: {string.Join(", ", result.PublishedPorts)}"
        };

        if (result.Warnings.Count > 0)
        {
            lines.Add("Warnings:");
            lines.AddRange(result.Warnings.Select(warning => $"- {warning}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string SafeCoturnFailureDetail(PlatformCoturnSetupResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.Detail)
            ? "The shared platform TURN runtime did not satisfy its structural readiness contract."
            : result.Detail.Trim();

        return $"{detail} Container={result.ContainerName}; Readiness={result.Readiness}; " +
               $"Running={result.Running}; OwnershipVerified={result.OwnershipVerified}; " +
               $"ImageApproved={result.ImageApproved}; SecretPresent={result.SecretPresent}; " +
               $"RelayPortsPublished={result.RelayPortsPublished}; SecurityPolicyApplied={result.SecurityPolicyApplied}.";
    }
}
