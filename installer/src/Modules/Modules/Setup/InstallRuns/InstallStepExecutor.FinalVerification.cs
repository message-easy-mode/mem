namespace Modules.Setup.InstallRuns;

public sealed partial class InstallStepExecutor
{
    private async Task<InstallStepResult> RunFinalVerificationChecksAsync(
        InstallStepContext context,
        CancellationToken cancellationToken)
    {
        var config = ReadConfig(context.ConfigJson);

        if (config is null)
        {
            return Failed(
                "Final verification failed.",
                "Installation config could not be read.");
        }

        var errors = new List<string>();
        var warnings = new List<string>();
        var evidence = new List<string>();

        await BeginProgressPhaseAsync(
            context,
            "verification.postgres",
            "Verifying the PostgreSQL runtime.",
            cancellationToken);

        await VerifyContainerRunningAsync(
            config.Platform.Postgres.ContainerName,
            "Postgres",
            errors,
            evidence,
            cancellationToken);

        await BeginProgressPhaseAsync(
            context,
            "verification.npm",
            "Verifying the Nginx Proxy Manager runtime.",
            cancellationToken);

        await VerifyContainerRunningAsync(
            config.Platform.Ingress.ContainerName,
            "NPM / ingress",
            errors,
            evidence,
            cancellationToken);

        await BeginProgressPhaseAsync(
            context,
            "verification.certificate",
            "Verifying the managed TLS certificate and Nginx Proxy Manager import.",
            cancellationToken);

        var resolved = await ResolvePlatformPublicAccessAsync(
            cancellationToken);

        if (resolved.Error is not null)
        {
            errors.Add(resolved.Error.ErrorMessage ?? resolved.Error.Message);
        }
        else
        {
            var access = resolved.Access!;

            evidence.Add($"MEM-managed certificate resolved. CertificateId={access.CertificateId}.");
            evidence.Add($"NPM can see the MEM-managed certificate. NpmCertificateId={access.NpmCertificateId}.");
        }

        await BeginProgressPhaseAsync(
            context,
            "verification.coturn",
            "Verifying the shared platform TURN runtime.",
            cancellationToken);

        if (_platformCoturnSetupService is null)
        {
            errors.Add("The shared platform TURN verification service is unavailable.");
        }
        else
        {
            try
            {
                var coturn = await _platformCoturnSetupService.InspectAsync(cancellationToken);
                if (!coturn.Ready)
                {
                    errors.Add(SafeCoturnFailureDetail(coturn));
                }
                else
                {
                    evidence.Add(
                        $"Shared platform TURN '{coturn.ContainerName}' is structurally ready at {coturn.PublicHost}. " +
                        $"Ownership={coturn.OwnershipVerified}; ImageApproved={coturn.ImageApproved}; " +
                        $"RelayPortsPublished={coturn.RelayPortsPublished}; SecurityPolicyApplied={coturn.SecurityPolicyApplied}.");
                }

                warnings.AddRange(coturn.Warnings);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                errors.Add("Shared platform TURN could not be verified. Review the Coturn workspace and Diagnostics for safe evidence.");
            }
        }

        if (config.PublicAccess?.UseStaging == true)
        {
            warnings.Add(
                "The selected certificate was issued from the Let's Encrypt staging environment. Browser HTTPS trust is expected to fail until a production certificate is issued.");
        }

        if (errors.Count > 0)
        {
            return Failed(
                "Final verification failed.",
                string.Join(Environment.NewLine, errors));
        }

        await BeginProgressPhaseAsync(
            context,
            "verification.complete",
            "Finalizing platform verification evidence.",
            cancellationToken);

        var messageParts = new List<string>
        {
            "Final platform dependency verification completed successfully.",
            "",
            "Verified:"
        };

        messageParts.AddRange(evidence.Select(item => $"- {item}"));

        if (warnings.Count > 0)
        {
            messageParts.Add("");
            messageParts.Add("Warnings:");
            messageParts.AddRange(warnings.Select(item => $"- {item}"));
        }

        return Succeeded(string.Join(Environment.NewLine, messageParts));
    }

    private async Task VerifyContainerRunningAsync(
        string containerName,
        string label,
        ICollection<string> errors,
        ICollection<string> evidence,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(containerName))
        {
            errors.Add($"{label} container name is missing.");
            return;
        }

        var inspection = await _dockerHost.InspectByNameAsync(
            containerName.Trim(),
            cancellationToken);

        if (inspection is null)
        {
            errors.Add($"{label} container '{containerName}' could not be inspected through the Docker API.");
            return;
        }

        if (!inspection.Running)
        {
            errors.Add(
                $"{label} container '{containerName}' is not running. Docker API state: {inspection.State}.");
            return;
        }

        evidence.Add(
            $"{label} container '{containerName}' is running. Image={inspection.Image}; state={inspection.State}.");
    }

}
