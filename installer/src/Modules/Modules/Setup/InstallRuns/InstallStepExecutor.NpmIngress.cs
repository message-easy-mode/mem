using Core.RuntimeDefinition;
using Infrastructure.Docker.Models;

namespace Modules.Setup.InstallRuns;

public sealed partial class InstallStepExecutor
{
    private async Task<InstallStepResult> StartNpmIngressAsync(
        InstallStepContext context,
        CancellationToken cancellationToken)
    {
        var config = ReadConfig(context.ConfigJson);

        if (config is null)
        {
            return Failed(
                "NPM / ingress start failed.",
                "Installation config could not be read.");
        }

        var ingress = config.Platform.Ingress;

        await BeginProgressPhaseAsync(
            context,
            "npm.inspect",
            "Inspecting the Nginx Proxy Manager runtime and ingress configuration.",
            cancellationToken);

        if (!ingress.Enabled)
        {
            return Failed(
                "NPM / ingress start failed.",
                "Ingress is disabled, but the base MEM platform requires ingress.");
        }

        if (!string.Equals(ingress.Provider, "Npm", StringComparison.OrdinalIgnoreCase))
        {
            return Failed(
                "NPM / ingress start failed.",
                $"Unsupported ingress provider '{ingress.Provider}'. Only 'Npm' is supported in this installer slice.");
        }

        await BeginProgressPhaseAsync(
            context,
            "npm.control-plane-network",
            "Ensuring the Control Plane can reach Nginx Proxy Manager on the managed Docker network.",
            cancellationToken);

        var controlPlaneNetwork = await EnsureControlPlaneManagedServiceNetworkAsync(
            cancellationToken);
        if (controlPlaneNetwork is not null)
        {
            return controlPlaneNetwork;
        }

        var containerName = ingress.ContainerName.Trim();
        var existing = await _dockerHost.InspectByNameAsync(
            containerName,
            cancellationToken);

        if (existing is not null)
        {
            if (!LooksLikeNpmImage(existing.Image))
            {
                return Failed(
                    $"Ingress container '{containerName}' already exists but does not appear to use an Nginx Proxy Manager image.",
                    $"Docker API reports image '{existing.Image}'.");
            }

            await EnsureContainerNetworkAsync(containerName, cancellationToken);

            if (!existing.Running)
            {
                await BeginProgressPhaseAsync(
                    context,
                    "npm.container-start",
                    "Starting the existing Nginx Proxy Manager container.",
                    cancellationToken);

                try
                {
                    await _dockerHost.StartContainerAsync(existing.Id, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return Failed(
                        $"Failed to start existing NPM / ingress container '{containerName}'.",
                        ex.Message);
                }
            }

            await BeginProgressPhaseAsync(
                context,
                "npm.readiness",
                "Waiting for the Nginx Proxy Manager administration API.",
                cancellationToken);

            var readiness = await WaitForNpmReadinessAsync(
                context,
                containerName,
                ingress.AdminPort,
                cancellationToken);

            if (!readiness.Succeeded)
            {
                return readiness;
            }

            var bootstrap = await EnsureNpmAdministratorReadyAsync(
                context,
                ingress,
                cancellationToken);
            if (!bootstrap.Succeeded)
            {
                return bootstrap;
            }

            return Succeeded(
                $"NPM / ingress container '{containerName}' {(existing.Running ? "is already running" : "was started through the Control Plane Docker API")}.{Environment.NewLine}{readiness.Message}{Environment.NewLine}{bootstrap.Message}");
        }

        var portCheck = await CheckRequiredPortsAvailableAsync(
            [
                ingress.HttpPort,
                ingress.HttpsPort,
                ingress.AdminPort
            ],
            cancellationToken);

        if (!portCheck.Succeeded)
        {
            return portCheck;
        }

        var spec = new DockerContainerSpec(
            Name: containerName,
            Image: NpmImage,
            Environment: new Dictionary<string, string>
            {
                ["DB_SQLITE_FILE"] = "/data/database.sqlite"
            },
            Labels: ManagedContainerLabels.ForService(ManagedServiceNames.Npm, _runtimeContext),
            PortBindings: new Dictionary<string, string>
            {
                ["80/tcp"] = ingress.HttpPort.ToString(),
                ["443/tcp"] = ingress.HttpsPort.ToString(),
                ["81/tcp"] = ingress.AdminPort.ToString()
            },
            VolumeMounts:
            [
                new DockerVolumeMount(NpmDataVolumeName, "/data"),
                new DockerVolumeMount(NpmLetsEncryptVolumeName, "/etc/letsencrypt")
            ],
            NetworkName: NetworkName,
            NetworkAliases: ["npm"]);

        await BeginProgressPhaseAsync(
            context,
            "npm.container-start",
            "Creating and starting the Nginx Proxy Manager container.",
            cancellationToken);

        string? containerId = null;
        try
        {
            containerId = await _dockerHost.CreateContainerAsync(spec, cancellationToken);
            await _dockerHost.StartContainerAsync(containerId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(containerId))
            {
                await RemoveContainerBestEffortAsync(containerId, cancellationToken);
            }

            return Failed(
                $"Failed to create or start NPM / ingress container '{containerName}'.",
                ex.Message);
        }

        await BeginProgressPhaseAsync(
            context,
            "npm.readiness",
            "Waiting for the Nginx Proxy Manager administration API.",
            cancellationToken);

        var readinessAfterCreate = await WaitForNpmReadinessAsync(
            context,
            containerName,
            ingress.AdminPort,
            cancellationToken);

        if (!readinessAfterCreate.Succeeded)
        {
            return readinessAfterCreate;
        }

        var bootstrapAfterCreate = await EnsureNpmAdministratorReadyAsync(
            context,
            ingress,
            cancellationToken);
        if (!bootstrapAfterCreate.Succeeded)
        {
            return bootstrapAfterCreate;
        }

        return Succeeded(
            $"NPM / ingress container '{containerName}' created and started using image '{NpmImage}' through the Control Plane Docker API.{Environment.NewLine}{readinessAfterCreate.Message}{Environment.NewLine}{bootstrapAfterCreate.Message}");
    }

    private async Task<InstallStepResult> WaitForNpmReadinessAsync(
        InstallStepContext context,
        string containerName,
        int adminPort,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 30;
        string? lastProbeDetail = null;
        string? lastRouteKind = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await HeartbeatProgressAsync(
                context,
                "npm.readiness",
                $"Nginx Proxy Manager readiness check {attempt} of {maxAttempts}.",
                cancellationToken);

            var inspection = await _dockerHost.InspectByNameAsync(
                containerName,
                cancellationToken);

            if (inspection is null)
            {
                return Failed(
                    $"NPM / ingress readiness check failed for container '{containerName}'.",
                    "Docker API inspection did not find the container.");
            }

            if (!inspection.Running)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }

            var adminProbe = await _npmAdminProbe.ProbeAsync(
                containerName,
                adminPort,
                cancellationToken);

            if (adminProbe.Reachable)
            {
                return Succeeded(
                    $"NPM / ingress admin UI responded through {adminProbe.RouteKind} authority after {attempt} readiness check attempt(s).");
            }

            lastRouteKind = adminProbe.RouteKind;
            lastProbeDetail = adminProbe.Error;

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        var routeDetail = string.IsNullOrWhiteSpace(lastRouteKind)
            ? "unknown authority"
            : lastRouteKind;
        var errorDetail = string.IsNullOrWhiteSpace(lastProbeDetail)
            ? "No additional probe detail was recorded."
            : lastProbeDetail;

        return Failed(
            $"NPM / ingress container '{containerName}' did not become ready in time.",
            $"The runtime-aware NPM administration probe did not succeed after {maxAttempts} attempts. Last route={routeDetail}. {errorDetail}");
    }
}
