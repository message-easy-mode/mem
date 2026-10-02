namespace Modules.Setup.InstallRuns;

public sealed partial class InstallStepExecutor
{
    private static IReadOnlyList<string> GetRequiredVolumeNames(InstallPlan config)
    {
        var volumes = new List<string>();

        if (config.Platform.Postgres.Enabled && config.Platform.Postgres.UseDockerVolume)
        {
            volumes.Add(config.Platform.Postgres.VolumeName);
        }

        if (config.Platform.Ingress.Enabled &&
            string.Equals(config.Platform.Ingress.Provider, "Npm", StringComparison.OrdinalIgnoreCase))
        {
            volumes.Add(NpmDataVolumeName);
            volumes.Add(NpmLetsEncryptVolumeName);
        }

        if (config.SupportTools.Seq.Enabled)
        {
            volumes.Add("mem_seq_data");
        }

        if (config.SupportTools.PgAdmin.Enabled)
        {
            volumes.Add("mem_pgadmin_data");
        }

        if (config.SupportTools.Portainer.Enabled && !config.SupportTools.Portainer.UseExistingIfDetected)
        {
            volumes.Add("portainer_data");
        }

        return volumes
            .Where(volume => !string.IsNullOrWhiteSpace(volume))
            .Select(volume => volume.Trim())
            .ToArray();
    }

    private static async Task<InstallStepResult> PlaceholderAsync(
        string message,
        CancellationToken cancellationToken)
    {
        await Task.Delay(350, cancellationToken);

        return Succeeded(message);
    }

    private static InstallPlan? ReadConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InstallPlan>(configJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ValidateConfig(InstallPlan config)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.General.Mode))
        {
            errors.Add("General mode is required.");
        }

        if (!config.Platform.Postgres.Enabled)
        {
            errors.Add("Postgres must be enabled for the base MEM platform.");
        }

        if (string.IsNullOrWhiteSpace(config.Platform.Postgres.ContainerName))
        {
            errors.Add("Postgres container name is required.");
        }

        if (string.IsNullOrWhiteSpace(config.Platform.Postgres.DatabaseName))
        {
            errors.Add("Postgres database name is required.");
        }

        if (string.IsNullOrWhiteSpace(config.Platform.Postgres.Username))
        {
            errors.Add("Postgres username is required.");
        }

        if (config.Platform.Postgres.UseDockerVolume &&
            string.IsNullOrWhiteSpace(config.Platform.Postgres.VolumeName))
        {
            errors.Add("Postgres volume name is required when Docker volume storage is enabled.");
        }

        if (!SetupReviewPlanFingerprint.Matches(config))
        {
            errors.Add("The frozen Review authority is missing or does not match the reviewed installation plan.");
        }

        if (config.Preflight is null || config.Preflight.BlockingIssueCount > 0)
        {
            errors.Add("A blocker-free persisted Server Check snapshot is required before installation.");
        }

        if (config.PublicAccess.Preparation is null ||
            !string.Equals(config.PublicAccess.Preparation.Status, "Validated", StringComparison.OrdinalIgnoreCase) ||
            !config.PublicAccess.Preparation.ProviderAccessConfirmed ||
            string.IsNullOrWhiteSpace(config.PublicAccess.Zone) ||
            string.IsNullOrWhiteSpace(config.PublicAccess.Domain) ||
            string.IsNullOrWhiteSpace(config.PublicAccess.AcmeEmail))
        {
            errors.Add("A validated Domain plan is required before installation.");
        }

        if (!config.Platform.Ingress.Enabled)
        {
            errors.Add("Ingress must be enabled for the base MEM platform.");
        }

        if (!string.Equals(config.Platform.Ingress.Provider, "Npm", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Only NPM ingress provider is supported in this installer slice.");
        }

        if (string.IsNullOrWhiteSpace(config.Platform.Ingress.ContainerName))
        {
            errors.Add("Ingress container name is required.");
        }

        ValidatePort(config.Platform.Ingress.HttpPort, "Ingress HTTP port", errors);
        ValidatePort(config.Platform.Ingress.HttpsPort, "Ingress HTTPS port", errors);
        ValidatePort(config.Platform.Ingress.AdminPort, "Ingress admin port", errors);

        if (config.SupportTools.Portainer.Enabled)
        {
            if (!string.Equals(
                    config.SupportTools.Portainer.ContainerName,
                    "portainer",
                    StringComparison.Ordinal))
            {
                errors.Add("Portainer container name must remain 'portainer'.");
            }

            ValidatePort(
                config.SupportTools.Portainer.HostPort,
                "Portainer HTTPS host port",
                errors);
        }

        return errors;
    }

    private static bool LooksLikeNpmImage(string inspectOutput)
    {
        return inspectOutput.Contains("nginx-proxy-manager", StringComparison.OrdinalIgnoreCase) ||
               inspectOutput.Contains("jc21", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidatePort(
        int port,
        string label,
        ICollection<string> errors)
    {
        if (port is < 1 or > 65535)
        {
            errors.Add($"{label} must be between 1 and 65535.");
        }
    }

    private static InstallStepResult Succeeded(string message)
    {
        return new InstallStepResult(
            Succeeded: true,
            Message: message);
    }

    private static InstallStepResult Failed(
        string message,
        string? errorMessage)
    {
        return new InstallStepResult(
            Succeeded: false,
            Message: message,
            ErrorMessage: string.IsNullOrWhiteSpace(errorMessage) ? message : errorMessage);
    }

    private static InstallStepResult ActionRequired(
        string message,
        string? detail)
    {
        return new InstallStepResult(
            Succeeded: false,
            Message: message,
            ErrorMessage: detail,
            StepStatus: "WaitingForUser");
    }
}
