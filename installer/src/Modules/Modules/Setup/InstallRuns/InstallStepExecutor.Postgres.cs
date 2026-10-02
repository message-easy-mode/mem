using Core.RuntimeDefinition;
using System.Security.Cryptography;
using System.Text;
using Infrastructure.Docker.Models;

namespace Modules.Setup.InstallRuns;

public sealed partial class InstallStepExecutor
{
    private const string PostgresPasswordDirectory = "/var/lib/postgresql";
    private const string PostgresPasswordFileName = ".mem-postgres-password";
    private const string PostgresPasswordFilePath = PostgresPasswordDirectory + "/" + PostgresPasswordFileName;

    private async Task<InstallStepResult> StartPostgresAsync(
        InstallStepContext context,
        CancellationToken cancellationToken)
    {
        var config = ReadConfig(context.ConfigJson);

        if (config is null)
        {
            return Failed(
                "Postgres start failed.",
                "Installation config could not be read.");
        }

        var postgres = config.Platform.Postgres;

        await BeginProgressPhaseAsync(
            context,
            "postgres.inspect",
            "Inspecting the PostgreSQL runtime and approved image.",
            cancellationToken);

        var approvedPostgresRuntime = await _approvedPostgresRuntimeProvider
            .ResolveForInstallationAsync(cancellationToken);
        var postgresImage = approvedPostgresRuntime.ResolvedImageId;
        var containerName = postgres.ContainerName.Trim();
        var volumeName = postgres.VolumeName.Trim();

        var existing = await _dockerHost.InspectByNameAsync(
            containerName,
            cancellationToken);

        if (existing is not null)
        {
            if (!LooksLikePostgresImage(
                    existing.Image,
                    postgresImage,
                    approvedPostgresRuntime.ApprovedReference))
            {
                return Failed(
                    $"Postgres container '{containerName}' already exists but does not appear to use a Postgres image.",
                    $"Docker API reports image '{existing.Image}'.");
            }

            await EnsureContainerNetworkAsync(containerName, cancellationToken);

            if (existing.Running)
            {
                return Succeeded($"Postgres container '{containerName}' is already running.");
            }

            await BeginProgressPhaseAsync(
                context,
                "postgres.credential",
                "Preparing the protected PostgreSQL bootstrap credential.",
                cancellationToken);

            var existingSecretResult = await MaterialisePostgresPasswordAsync(
                context.InstallationId,
                existing.Id,
                cancellationToken);
            if (!existingSecretResult.Succeeded)
            {
                return existingSecretResult;
            }

            await BeginProgressPhaseAsync(
                context,
                "postgres.container-start",
                "Starting the PostgreSQL container.",
                cancellationToken);

            try
            {
                await _dockerHost.StartContainerAsync(existing.Id, cancellationToken);
                return Succeeded($"Existing Postgres container '{containerName}' started through the Control Plane Docker API.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Failed(
                    $"Failed to start existing Postgres container '{containerName}'.",
                    ex.Message);
            }
        }

        await BeginProgressPhaseAsync(
            context,
            "postgres.credential",
            "Resolving the protected PostgreSQL installation credential.",
            cancellationToken);

        var secretCheck = await ResolvePostgresPasswordAsync(
            context.InstallationId,
            cancellationToken);
        if (!secretCheck.Succeeded)
        {
            return secretCheck.Result!;
        }

        var spec = new DockerContainerSpec(
            Name: containerName,
            Image: postgresImage,
            Environment: new Dictionary<string, string>
            {
                ["POSTGRES_DB"] = postgres.DatabaseName,
                ["POSTGRES_USER"] = postgres.Username,
                ["POSTGRES_PASSWORD_FILE"] = PostgresPasswordFilePath
            },
            Labels: ManagedContainerLabels.ForService(ManagedServiceNames.Postgres, _runtimeContext),
            VolumeMounts:
            [
                new DockerVolumeMount(
                    volumeName,
                    "/var/lib/postgresql/data")
            ],
            NetworkName: NetworkName,
            NetworkAliases: ["postgres"]);

        await BeginProgressPhaseAsync(
            context,
            "postgres.container-start",
            "Creating and starting the PostgreSQL container.",
            cancellationToken);

        string? containerId = null;
        try
        {
            containerId = await _dockerHost.CreateContainerAsync(spec, cancellationToken);

            var passwordBytes = Encoding.UTF8.GetBytes(secretCheck.Password!);
            try
            {
                await _dockerHost.CopyFileToContainerAsync(
                    containerId,
                    PostgresPasswordDirectory,
                    PostgresPasswordFileName,
                    passwordBytes,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }

            await _dockerHost.StartContainerAsync(containerId, cancellationToken);

            return Succeeded(
                $"Postgres container '{containerName}' created and started using approved image '{postgresImage}' and a protected installation credential.");
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
                $"Failed to create or start Postgres container '{containerName}'.",
                ex.Message);
        }
    }

    private async Task<InstallStepResult> MaterialisePostgresPasswordAsync(
        Guid installationId,
        string containerId,
        CancellationToken cancellationToken)
    {
        var secretCheck = await ResolvePostgresPasswordAsync(
            installationId,
            cancellationToken);
        if (!secretCheck.Succeeded)
        {
            return secretCheck.Result!;
        }

        var passwordBytes = Encoding.UTF8.GetBytes(secretCheck.Password!);
        try
        {
            await _dockerHost.CopyFileToContainerAsync(
                containerId,
                PostgresPasswordDirectory,
                PostgresPasswordFileName,
                passwordBytes,
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(
                "Postgres start was blocked because the protected bootstrap credential could not be materialised inside the stopped container.",
                ex.Message);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }

        return Succeeded("Protected Postgres bootstrap credential materialised inside the stopped container.");
    }

    private async Task<(bool Succeeded, string? Password, InstallStepResult? Result)> ResolvePostgresPasswordAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        string? password;
        try
        {
            password = await _installationSecretStore.ResolveProtectedAsync(
                installationId,
                InstallationSecretNames.PlatformCategory,
                InstallationSecretNames.PostgresPassword,
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return (
                false,
                null,
                Failed(
                    "Postgres start was blocked because the protected installation credential could not be resolved.",
                    "Return to the Domain/Review setup flow so MEM can recreate the protected installation credential."));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return (
                false,
                null,
                Failed(
                    "Postgres start was blocked because the protected installation credential is missing.",
                    "Return to the Domain/Review setup flow so MEM can create the protected installation credential."));
        }

        return (true, password, null);
    }

    private async Task<InstallStepResult> WaitForPostgresReadinessAsync(
        InstallStepContext context,
        CancellationToken cancellationToken)
    {
        var config = ReadConfig(context.ConfigJson);

        if (config is null)
        {
            return Failed(
                "Postgres readiness check failed.",
                "Installation config could not be read.");
        }

        var postgres = config.Platform.Postgres;
        var containerName = postgres.ContainerName.Trim();

        const int maxAttempts = 30;
        string? lastError = null;

        await BeginProgressPhaseAsync(
            context,
            "postgres.readiness",
            "Waiting for PostgreSQL to accept database connections.",
            cancellationToken);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await HeartbeatProgressAsync(
                context,
                "postgres.readiness",
                $"PostgreSQL readiness check {attempt} of {maxAttempts}.",
                cancellationToken);

            var result = await _dockerHost.ExecAsync(
                containerName,
                [
                    "pg_isready",
                    "-U", postgres.Username,
                    "-d", postgres.DatabaseName
                ],
                TimeSpan.FromSeconds(15),
                cancellationToken);

            if (result.Succeeded)
            {
                return Succeeded(
                    $"Postgres container '{containerName}' is ready after {attempt} readiness check attempt(s).");
            }

            lastError = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        return Failed(
            $"Postgres container '{containerName}' did not become ready in time.",
            $"pg_isready did not succeed after {maxAttempts} attempts. {lastError}".Trim());
    }

    private async Task RemoveContainerBestEffortAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dockerHost.RemoveContainerAsync(
                containerId,
                force: true,
                removeVolumes: false,
                ct: cancellationToken);
        }
        catch
        {
            // Best-effort rollback. The original step failure remains authoritative.
        }
    }

    private static bool LooksLikePostgresImage(
        string actualImage,
        string resolvedImage,
        string approvedReference) =>
        actualImage.Contains("postgres", StringComparison.OrdinalIgnoreCase) ||
        actualImage.Contains(resolvedImage, StringComparison.OrdinalIgnoreCase) ||
        actualImage.Contains(approvedReference, StringComparison.OrdinalIgnoreCase);
}
