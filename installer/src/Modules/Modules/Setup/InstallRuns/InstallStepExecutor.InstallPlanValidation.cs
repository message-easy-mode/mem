namespace Modules.Setup.InstallRuns;

public sealed partial class InstallStepExecutor
{
    private async Task<InstallStepResult> ValidateInstallPlanAsync(
        InstallStepContext context,
        CancellationToken cancellationToken)
    {
        var config = ReadConfig(context.ConfigJson);

        if (config is null)
        {
            return Failed(
                "Install plan validation failed.",
                "Installation config could not be read. The frozen config JSON is missing or invalid.");
        }

        var errors = ValidateConfig(config);

        if (errors.Count > 0)
        {
            return Failed(
                "Install plan validation failed.",
                string.Join(Environment.NewLine, errors));
        }

        try
        {
            var postgresPassword = await _installationSecretStore.ResolveProtectedAsync(
                context.InstallationId,
                InstallationSecretNames.PlatformCategory,
                InstallationSecretNames.PostgresPassword,
                cancellationToken);
            var dnsCredential = await _installationSecretStore.ResolveProtectedAsync(
                context.InstallationId,
                InstallationSecretNames.DnsCategory,
                InstallationSecretNames.DesecProviderToken,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(postgresPassword) || string.IsNullOrWhiteSpace(dnsCredential))
            {
                return Failed(
                    "Install plan validation failed.",
                    "The reviewed installation credentials are unavailable. Return to Domain, validate the plan again, and accept Review before retrying.");
            }
        }
        catch (InvalidOperationException)
        {
            return Failed(
                "Install plan validation failed.",
                "The reviewed installation credentials could not be recovered from the current Control Plane key ring. Return to Domain, validate the plan again, and accept Review before retrying.");
        }

        SetupDockerSystemInfo dockerInfo;
        try
        {
            dockerInfo = await _dockerRuntimeProbe.GetSystemInfoAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(
                "Install plan validation failed.",
                $"Docker is not reachable through the Control Plane Docker API client. {ex.Message}".Trim());
        }

        var ingressPortCheck = await CheckRequiredPortsAvailableAsync(
            [
                config.Platform.Ingress.HttpPort,
                config.Platform.Ingress.HttpsPort,
                config.Platform.Ingress.AdminPort
            ],
            cancellationToken,
            allowedContainerName: config.Platform.Ingress.UseExistingIfDetected
                ? config.Platform.Ingress.ContainerName.Trim()
                : null);

        if (!ingressPortCheck.Succeeded)
        {
            return ingressPortCheck;
        }

        var dockerVersion = string.IsNullOrWhiteSpace(dockerInfo.ServerVersion)
            ? "unknown"
            : dockerInfo.ServerVersion;

        var snapshot = config.Preflight;
        var snapshotMessage = snapshot is null
            ? "No persisted preflight snapshot was present; critical Docker and port checks were repeated immediately before mutation."
            : $"Preflight snapshot {snapshot.RunId} was recorded at {snapshot.CompletedAtUtc:O}; critical Docker and port checks were repeated immediately before mutation.";

        return Succeeded(
            $"Install plan validation completed. Docker server {dockerVersion} is reachable through the Docker API. {snapshotMessage}");
    }

    private async Task<InstallStepResult> CreateOrVerifyDockerNetworkAsync(
        InstallStepContext context,
        CancellationToken cancellationToken)
    {
        var config = ReadConfig(context.ConfigJson);

        if (config is null)
        {
            return Failed(
                "Docker network verification failed.",
                "Installation config could not be read.");
        }

        try
        {
            await _dockerHost.EnsureNetworkAsync(NetworkName, cancellationToken);

            var controlPlaneNetwork = await EnsureControlPlaneManagedServiceNetworkAsync(
                cancellationToken);
            if (controlPlaneNetwork is not null)
            {
                return controlPlaneNetwork;
            }

            var controlPlaneDetail = _runtimeContext?.RunningInContainer == true
                ? $" The active Control Plane container is connected to '{NetworkName}' for managed-service communication."
                : string.Empty;

            return Succeeded(
                $"Docker network '{NetworkName}' exists or was created through the Control Plane Docker API.{controlPlaneDetail}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(
                $"Failed to create or verify Docker network '{NetworkName}'.",
                ex.Message);
        }
    }

    private async Task<InstallStepResult> CreatePersistentVolumesAsync(
        InstallStepContext context,
        CancellationToken cancellationToken)
    {
        var config = ReadConfig(context.ConfigJson);

        if (config is null)
        {
            return Failed(
                "Persistent volume verification failed.",
                "Installation config could not be read.");
        }

        var volumeNames = GetRequiredVolumeNames(config)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (volumeNames.Length == 0)
        {
            return Succeeded("No Docker volumes were required by this install plan.");
        }

        var results = new List<string>();

        foreach (var volumeName in volumeNames)
        {
            try
            {
                await _dockerHost.EnsureVolumeAsync(volumeName, cancellationToken);
                results.Add(
                    $"Docker volume '{volumeName}' exists or was created through the Control Plane Docker API.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Failed(
                    $"Failed to create or verify Docker volume '{volumeName}'.",
                    ex.Message);
            }
        }

        return Succeeded(string.Join(Environment.NewLine, results));
    }

}
