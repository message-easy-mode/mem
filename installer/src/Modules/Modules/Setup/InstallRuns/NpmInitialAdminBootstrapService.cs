using Core.RuntimeDefinition;
using System.Net;
using Infrastructure.Docker;
using Infrastructure.Docker.Models;
using Microsoft.Extensions.Logging;
using Modules.Integrations.Npm.Services;
using Modules.Setup.Secrets;
using Shared.ControlPlane.Runtime;

namespace Modules.Setup.InstallRuns;

/// <summary>
/// Establishes the first administrator for a fresh MEM-managed Nginx Proxy
/// Manager instance. Bootstrap credentials exist in the environment of one
/// bounded initialization container only. The service then recreates NPM
/// against the same persistent volumes without INITIAL_ADMIN_* and verifies
/// the selected account again.
///
/// This service owns only the bounded NPM bootstrap primitive. Installation
/// step/WaitingForUser state remains owned by the durable installer.
/// </summary>
public sealed class NpmInitialAdminBootstrapService(
    IDockerHost dockerHost,
    InstallNpmAdminProbe npmAdminProbe,
    IMemManagedServiceAuthorityResolver authorityResolver,
    NpmApiClient npmApiClient,
    NpmAdminCredentialService credentialService,
    TimeProvider timeProvider,
    ILogger<NpmInitialAdminBootstrapService> logger,
    MemControlPlaneRuntimeContext? runtimeContext = null,
    InstallProgressReporter? progressReporter = null) : INpmInitialAdminBootstrapService
{
    private const string InitialAdminEmailEnvironment = "INITIAL_ADMIN_EMAIL";
    private const string InitialAdminPasswordEnvironment = "INITIAL_ADMIN_PASSWORD";
    private const int ReadinessAttempts = 30;

    public async Task<NpmInitialAdminBootstrapResult> BootstrapAsync(
        Guid installationId,
        NpmInitialAdminBootstrapTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateTarget(target);

        await ProgressPhaseAsync(
            installationId,
            "npm.bootstrap.inspect",
            "Inspecting Nginx Proxy Manager first-administrator state.",
            cancellationToken);

        var credential = await credentialService.ResolveAsync(
            installationId,
            cancellationToken);
        if (credential is null)
        {
            return Result(
                succeeded: false,
                status: "CredentialRequired",
                message: "A protected Nginx Proxy Manager administrator credential is required before first-admin initialization can continue.",
                errorCode: "NpmAdminCredentialRequired");
        }

        logger.LogInformation(
            "Checking NPM first-admin bootstrap state for installation {InstallationId}, container {ContainerName}, image {Image}",
            installationId,
            target.ContainerName,
            target.Image);

        var existing = await dockerHost.InspectByNameAsync(
            target.ContainerName,
            cancellationToken);

        if (existing is not null && !UsesExpectedImage(existing, target.Image))
        {
            return Result(
                succeeded: false,
                status: "UnsafeExistingState",
                message: $"Container '{target.ContainerName}' exists with an unexpected image. MEM will not inject NPM bootstrap credentials into it.",
                errorCode: "NpmBootstrapUnexpectedImage");
        }

        if (existing is null)
        {
            var cleanCreate = await CreateAndStartCleanAsync(target, cancellationToken);
            if (!cleanCreate.Succeeded)
                return cleanCreate;

            existing = await dockerHost.InspectByNameAsync(
                target.ContainerName,
                cancellationToken);
        }
        else
        {
            await dockerHost.EnsureNetworkAsync(target.NetworkName, cancellationToken);
            await dockerHost.ConnectContainerToNetworkAsync(
                target.ContainerName,
                target.NetworkName,
                cancellationToken);

            if (!existing.Running)
            {
                try
                {
                    await dockerHost.StartContainerAsync(existing.Id, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return Failed(
                        "NPM could not be started before first-admin bootstrap inspection.",
                        "NpmBootstrapContainerStartFailed",
                        ex);
                }
            }
        }

        var ready = await WaitForReadinessAsync(target, cancellationToken);
        if (!ready.Succeeded)
            return ready;

        string baseUrl;
        try
        {
            baseUrl = ResolveApiBaseUrl(target);
        }
        catch (Exception ex)
        {
            return Failed(
                "MEM could not resolve the runtime-aware NPM administration authority.",
                "NpmApiUnavailable",
                ex);
        }

        var setupState = await WaitForSetupStateAsync(
            baseUrl,
            expectedState: null,
            unavailableMessage: "MEM could not determine whether NPM already has an administrator.",
            unavailableErrorCode: "NpmSetupStateUnavailable",
            unexpectedStateMessage: null,
            unexpectedStateErrorCode: null,
            cancellationToken);
        if (setupState.Error is not null)
            return setupState.Error;

        var isSetup = setupState.IsSetup;

        existing = await dockerHost.InspectByNameAsync(
            target.ContainerName,
            cancellationToken);
        if (existing is null)
        {
            return Result(
                succeeded: false,
                status: "Failed",
                message: "NPM disappeared while MEM was checking its first-admin state.",
                errorCode: "NpmBootstrapContainerMissing");
        }

        if (isSetup)
        {
            return await VerifyInitializedAsync(
                installationId,
                target,
                existing,
                credential,
                baseUrl,
                cancellationToken);
        }

        return await BootstrapFreshAsync(
            installationId,
            target,
            existing,
            credential,
            baseUrl,
            cancellationToken);
    }

    private async Task<NpmInitialAdminBootstrapResult> VerifyInitializedAsync(
        Guid installationId,
        NpmInitialAdminBootstrapTarget target,
        DockerContainerInspection existing,
        NpmAdminCredential credential,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        await ProgressPhaseAsync(
            installationId,
            "npm.bootstrap.verify-initial-login",
            "Verifying the protected Nginx Proxy Manager administrator credential.",
            cancellationToken);

        var login = await TryLoginAsync(baseUrl, credential, cancellationToken);
        if (!login.Succeeded)
            return login.Result!;

        var recreated = false;
        if (HasBootstrapEnvironment(existing))
        {
            await ProgressPhaseAsync(
                installationId,
                "npm.bootstrap.recreate-clean",
                "Recreating Nginx Proxy Manager without bootstrap-only credentials.",
                cancellationToken);

            var clean = await RecreateCleanAsync(target, existing, cancellationToken);
            if (!clean.Succeeded)
                return clean;

            recreated = true;

            var setupAfterRecreation = await ReadSetupStateAfterCleanRecreationAsync(
                baseUrl,
                cancellationToken);
            if (setupAfterRecreation.Error is not null)
                return setupAfterRecreation.Error;
            if (!setupAfterRecreation.IsSetup)
            {
                return Result(
                    succeeded: false,
                    status: "Failed",
                    message: "NPM lost its initialized administrator state after the clean container recreation.",
                    errorCode: "NpmBootstrapStateNotPersistent",
                    initialLoginVerified: true,
                    recreated: true,
                    bootstrapEnvironmentRemoved: true);
            }

            await ProgressPhaseAsync(
                installationId,
                "npm.bootstrap.verify-final-login",
                "Verifying the Nginx Proxy Manager administrator after clean recreation.",
                cancellationToken);

            var finalLogin = await TryLoginAsync(baseUrl, credential, cancellationToken);
            if (!finalLogin.Succeeded)
            {
                return finalLogin.Result! with
                {
                    InitialLoginVerified = true,
                    Recreated = true,
                    BootstrapEnvironmentRemoved = true
                };
            }
        }

        var finalInspection = await dockerHost.InspectByNameAsync(
            target.ContainerName,
            cancellationToken);
        if (finalInspection is null)
        {
            return Result(
                succeeded: false,
                status: "Failed",
                message: "NPM could not be inspected after administrator verification.",
                errorCode: "NpmBootstrapContainerMissing",
                initialLoginVerified: true,
                recreated: recreated);
        }

        var environmentRemoved = !HasBootstrapEnvironment(finalInspection);
        if (!environmentRemoved)
        {
            return Result(
                succeeded: false,
                status: "Failed",
                message: "NPM is initialized, but bootstrap-only environment variables remain on the running container.",
                errorCode: "NpmBootstrapEnvironmentStillPresent",
                initialLoginVerified: true,
                recreated: recreated,
                finalLoginVerified: recreated,
                bootstrapEnvironmentRemoved: false);
        }

        await credentialService.MarkVerifiedAsync(
            installationId,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        logger.LogInformation(
            "NPM administrator credential verified for installation {InstallationId}; bootstrap environment is absent",
            installationId);

        return Result(
            succeeded: true,
            status: "AlreadyInitialized",
            message: recreated
                ? "NPM was already initialized. MEM verified the protected administrator credential and recreated the container without bootstrap-only environment variables."
                : "NPM was already initialized and accepted the protected MEM administrator credential.",
            errorCode: null,
            initialLoginVerified: true,
            recreated: recreated,
            finalLoginVerified: true,
            bootstrapEnvironmentRemoved: true);
    }

    private async Task<NpmInitialAdminBootstrapResult> BootstrapFreshAsync(
        Guid installationId,
        NpmInitialAdminBootstrapTarget target,
        DockerContainerInspection existing,
        NpmAdminCredential credential,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Starting bounded NPM first-admin initialization for installation {InstallationId}, container {ContainerName}",
            installationId,
            target.ContainerName);

        await ProgressPhaseAsync(
            installationId,
            "npm.bootstrap.create-admin",
            "Creating the first Nginx Proxy Manager administrator in a bounded bootstrap runtime.",
            cancellationToken);

        var removeExisting = await RemoveContainerPreservingVolumesAsync(
            existing,
            cancellationToken);
        if (removeExisting is not null)
            return removeExisting;

        string? bootstrapContainerId = null;
        var initialLoginVerified = false;
        NpmInitialAdminBootstrapResult? bootstrapFailure = null;

        try
        {
            var bootstrapSpec = BuildContainerSpec(
                target,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["DB_SQLITE_FILE"] = "/data/database.sqlite",
                    [InitialAdminEmailEnvironment] = credential.Email,
                    [InitialAdminPasswordEnvironment] = credential.Password
                },
                restartPolicy: new DockerRestartPolicy(DockerRestartPolicyName.No),
                disableLogging: true);

            bootstrapContainerId = await dockerHost.CreateContainerAsync(
                bootstrapSpec,
                cancellationToken);
            await dockerHost.StartContainerAsync(
                bootstrapContainerId,
                cancellationToken);

            var ready = await WaitForReadinessAsync(target, cancellationToken);
            if (!ready.Succeeded)
            {
                bootstrapFailure = ready;
            }
            else
            {
                var initializedState = await WaitForSetupStateAsync(
                    baseUrl,
                    expectedState: true,
                    unavailableMessage: "MEM could not confirm that NPM created its initial administrator.",
                    unavailableErrorCode: "NpmInitialAdminBootstrapStateUnavailable",
                    unexpectedStateMessage: "NPM became reachable but did not report an initialized administrator after the bounded bootstrap start.",
                    unexpectedStateErrorCode: "NpmInitialAdminBootstrapDidNotInitialize",
                    cancellationToken);

                if (initializedState.Error is not null)
                {
                    bootstrapFailure = initializedState.Error;
                }

                if (bootstrapFailure is null)
                {
                    await ProgressPhaseAsync(
                        installationId,
                        "npm.bootstrap.verify-initial-login",
                        "Verifying the newly created Nginx Proxy Manager administrator.",
                        cancellationToken);

                    var initialLogin = await TryLoginAsync(
                        baseUrl,
                        credential,
                        cancellationToken);
                    if (!initialLogin.Succeeded)
                    {
                        bootstrapFailure = initialLogin.Result;
                    }
                    else
                    {
                        initialLoginVerified = true;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            bootstrapFailure = Failed(
                "NPM first-admin initialization container could not be created or started.",
                "NpmInitialAdminBootstrapStartFailed",
                ex);
        }

        var bootstrapInspection = await dockerHost.InspectByNameAsync(
            target.ContainerName,
            cancellationToken);
        if (bootstrapInspection is not null)
        {
            var removeBootstrap = await RemoveContainerPreservingVolumesAsync(
                bootstrapInspection,
                cancellationToken);
            if (removeBootstrap is not null && bootstrapFailure is null)
                bootstrapFailure = removeBootstrap;
        }
        else if (!string.IsNullOrWhiteSpace(bootstrapContainerId))
        {
            try
            {
                await dockerHost.RemoveContainerAsync(
                    bootstrapContainerId,
                    force: true,
                    removeVolumes: false,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (bootstrapFailure is null)
                {
                    bootstrapFailure = Failed(
                        "The temporary NPM bootstrap container could not be removed safely.",
                        "NpmBootstrapContainerCleanupFailed",
                        ex);
                }
            }
        }

        await ProgressPhaseAsync(
            installationId,
            "npm.bootstrap.recreate-clean",
            "Recreating Nginx Proxy Manager without bootstrap-only credentials.",
            cancellationToken);

        var cleanRecreation = await CreateAndStartCleanAsync(target, cancellationToken);
        if (!cleanRecreation.Succeeded)
        {
            return cleanRecreation with
            {
                InitialLoginVerified = initialLoginVerified,
                Recreated = false,
                BootstrapEnvironmentRemoved = true
            };
        }

        var finalInspection = await dockerHost.InspectByNameAsync(
            target.ContainerName,
            cancellationToken);
        if (finalInspection is null)
        {
            return Result(
                succeeded: false,
                status: "Failed",
                message: "NPM clean recreation completed without an inspectable long-running container.",
                errorCode: "NpmBootstrapContainerMissing",
                initialLoginVerified: initialLoginVerified,
                recreated: true,
                bootstrapEnvironmentRemoved: true);
        }

        var environmentRemoved = !HasBootstrapEnvironment(finalInspection);
        if (!environmentRemoved)
        {
            return Result(
                succeeded: false,
                status: "Failed",
                message: "The clean NPM container still exposes bootstrap-only environment variables.",
                errorCode: "NpmBootstrapEnvironmentStillPresent",
                initialLoginVerified: initialLoginVerified,
                recreated: true,
                bootstrapEnvironmentRemoved: false);
        }

        if (bootstrapFailure is not null)
        {
            return bootstrapFailure with
            {
                InitialLoginVerified = initialLoginVerified,
                Recreated = true,
                BootstrapEnvironmentRemoved = true
            };
        }

        var setupAfterRecreation = await ReadSetupStateAfterCleanRecreationAsync(
            baseUrl,
            cancellationToken);
        if (setupAfterRecreation.Error is not null)
        {
            return setupAfterRecreation.Error with
            {
                InitialLoginVerified = true,
                Recreated = true,
                BootstrapEnvironmentRemoved = true
            };
        }
        if (!setupAfterRecreation.IsSetup)
        {
            return Result(
                succeeded: false,
                status: "Failed",
                message: "The NPM administrator did not survive recreation against the persistent data volume.",
                errorCode: "NpmBootstrapStateNotPersistent",
                initialLoginVerified: true,
                recreated: true,
                bootstrapEnvironmentRemoved: true);
        }

        await ProgressPhaseAsync(
            installationId,
            "npm.bootstrap.verify-final-login",
            "Verifying the Nginx Proxy Manager administrator after clean recreation.",
            cancellationToken);

        var finalLogin = await TryLoginAsync(baseUrl, credential, cancellationToken);
        if (!finalLogin.Succeeded)
        {
            return finalLogin.Result! with
            {
                InitialLoginVerified = true,
                Recreated = true,
                BootstrapEnvironmentRemoved = true
            };
        }

        await credentialService.MarkVerifiedAsync(
            installationId,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        logger.LogInformation(
            "NPM first-admin initialization succeeded for installation {InstallationId}; clean recreation and final login are verified",
            installationId);

        return Result(
            succeeded: true,
            status: "Succeeded",
            message: "NPM first administrator was initialized, the bootstrap-only container was removed, and the account still authenticates after clean recreation against the same persistent data.",
            errorCode: null,
            initialLoginVerified: true,
            recreated: true,
            finalLoginVerified: true,
            bootstrapEnvironmentRemoved: true);
    }

    private async Task<NpmInitialAdminBootstrapResult> CreateAndStartCleanAsync(
        NpmInitialAdminBootstrapTarget target,
        CancellationToken cancellationToken)
    {
        try
        {
            var cleanSpec = BuildContainerSpec(
                target,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["DB_SQLITE_FILE"] = "/data/database.sqlite"
                },
                restartPolicy: new DockerRestartPolicy(DockerRestartPolicyName.UnlessStopped),
                disableLogging: false);

            var containerId = await dockerHost.CreateContainerAsync(
                cleanSpec,
                cancellationToken);
            await dockerHost.StartContainerAsync(containerId, cancellationToken);

            var readiness = await WaitForReadinessAsync(target, cancellationToken);
            return readiness.Succeeded
                ? Result(
                    succeeded: true,
                    status: "ContainerReady",
                    message: "The clean NPM container is running and reachable.",
                    errorCode: null,
                    recreated: true,
                    bootstrapEnvironmentRemoved: true)
                : readiness;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(
                "The clean NPM container could not be created or started.",
                "NpmCleanContainerStartFailed",
                ex);
        }
    }

    private async Task<NpmInitialAdminBootstrapResult> RecreateCleanAsync(
        NpmInitialAdminBootstrapTarget target,
        DockerContainerInspection existing,
        CancellationToken cancellationToken)
    {
        var remove = await RemoveContainerPreservingVolumesAsync(
            existing,
            cancellationToken);
        if (remove is not null)
            return remove;

        return await CreateAndStartCleanAsync(target, cancellationToken);
    }

    private async Task<NpmInitialAdminBootstrapResult?> RemoveContainerPreservingVolumesAsync(
        DockerContainerInspection inspection,
        CancellationToken cancellationToken)
    {
        try
        {
            if (inspection.Running)
            {
                await dockerHost.StopContainerAsync(
                    inspection.Id,
                    cancellationToken);
            }

            await dockerHost.RemoveContainerAsync(
                inspection.Id,
                force: true,
                removeVolumes: false,
                cancellationToken);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(
                "The NPM container could not be removed while preserving its persistent volumes.",
                "NpmBootstrapContainerCleanupFailed",
                ex);
        }
    }

    private async Task<NpmInitialAdminBootstrapResult> WaitForReadinessAsync(
        NpmInitialAdminBootstrapTarget target,
        CancellationToken cancellationToken)
    {
        string? lastError = null;
        string? lastRoute = null;

        for (var attempt = 1; attempt <= ReadinessAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inspection = await dockerHost.InspectByNameAsync(
                target.ContainerName,
                cancellationToken);
            if (inspection is null)
            {
                return Result(
                    succeeded: false,
                    status: "Failed",
                    message: "NPM disappeared during readiness checking.",
                    errorCode: "NpmBootstrapContainerMissing");
            }

            if (inspection.Running)
            {
                var probe = await npmAdminProbe.ProbeAsync(
                    target.ContainerName,
                    target.AdminPort,
                    cancellationToken);
                if (probe.Reachable)
                {
                    return Result(
                        succeeded: true,
                        status: "Ready",
                        message: $"NPM administration endpoint is reachable through {probe.RouteKind} authority.",
                        errorCode: null);
                }

                lastRoute = probe.RouteKind;
                lastError = probe.Error;
            }

            if (attempt < ReadinessAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        var detail = string.IsNullOrWhiteSpace(lastError)
            ? "No additional probe detail was recorded."
            : lastError;
        var route = string.IsNullOrWhiteSpace(lastRoute)
            ? "unknown"
            : lastRoute;

        return Result(
            succeeded: false,
            status: "Failed",
            message: $"NPM did not become ready for administrator operations. Last route={route}. {detail}",
            errorCode: "NpmApiUnavailable");
    }

    private string ResolveApiBaseUrl(NpmInitialAdminBootstrapTarget target)
    {
        var authority = authorityResolver.Resolve(
            MemManagedServicePurposes.Administration,
            new MemManagedServiceAuthoritySource(
                ServiceName: "npm",
                PublishedHostPort: target.AdminPort,
                DockerNetworkAlias: target.NetworkAlias,
                HealthPort: 81,
                AdministrationPort: 81,
                IngestionPort: null));

        return new Uri(authority.Authority, "/api")
            .ToString()
            .TrimEnd('/');
    }

    private async Task<(bool Succeeded, NpmInitialAdminBootstrapResult? Result)> TryLoginAsync(
        string baseUrl,
        NpmAdminCredential credential,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await npmApiClient.LoginAsync(
                baseUrl,
                credential.Email,
                credential.Password,
                cancellationToken);
            return (true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (IsCredentialRejection(ex.StatusCode))
        {
            return (
                false,
                Result(
                    succeeded: false,
                    status: "CredentialsRejected",
                    message: "NPM did not accept the protected administrator credential.",
                    errorCode: "NpmCredentialsRejected"));
        }
        catch (Exception ex)
        {
            return (
                false,
                Failed(
                    "NPM administrator authentication could not be verified.",
                    "NpmAuthenticationVerificationFailed",
                    ex));
        }
    }

    private Task<(bool IsSetup, NpmInitialAdminBootstrapResult? Error)> ReadSetupStateAfterCleanRecreationAsync(
        string baseUrl,
        CancellationToken cancellationToken) =>
        WaitForSetupStateAsync(
            baseUrl,
            expectedState: true,
            unavailableMessage: "MEM could not confirm NPM initialization state after clean recreation.",
            unavailableErrorCode: "NpmSetupStateUnavailable",
            unexpectedStateMessage: "NPM no longer reports an initialized administrator after clean recreation.",
            unexpectedStateErrorCode: "NpmBootstrapStateNotPersistent",
            cancellationToken);

    private async Task<(bool IsSetup, NpmInitialAdminBootstrapResult? Error)> WaitForSetupStateAsync(
        string baseUrl,
        bool? expectedState,
        string unavailableMessage,
        string unavailableErrorCode,
        string? unexpectedStateMessage,
        string? unexpectedStateErrorCode,
        CancellationToken cancellationToken)
    {
        Exception? lastTransientError = null;
        bool? lastObservedState = null;

        for (var attempt = 1; attempt <= ReadinessAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var isSetup = await npmApiClient.IsSetupAsync(
                    baseUrl,
                    cancellationToken);
                lastObservedState = isSetup;
                lastTransientError = null;

                if (!expectedState.HasValue || isSetup == expectedState.Value)
                {
                    return (isSetup, null);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex) when (IsTransientApiStartupFailure(ex))
            {
                lastTransientError = ex;

                logger.LogDebug(
                    "NPM setup-state API is not ready yet for {BaseUrl}; attempt {Attempt}/{Attempts}, HTTP {StatusCode}",
                    baseUrl,
                    attempt,
                    ReadinessAttempts,
                    ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : null);
            }
            catch (Exception ex)
            {
                return (
                    false,
                    Failed(
                        unavailableMessage,
                        unavailableErrorCode,
                        ex));
            }

            if (attempt < ReadinessAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        if (lastObservedState.HasValue &&
            expectedState.HasValue &&
            unexpectedStateMessage is not null &&
            unexpectedStateErrorCode is not null)
        {
            return (
                lastObservedState.Value,
                Result(
                    succeeded: false,
                    status: "Failed",
                    message: unexpectedStateMessage,
                    errorCode: unexpectedStateErrorCode));
        }

        return (
            false,
            Failed(
                unavailableMessage,
                unavailableErrorCode,
                lastTransientError ?? new TimeoutException(
                    "NPM setup-state API did not become available within the bounded readiness window.")));
    }

    private static bool IsTransientApiStartupFailure(HttpRequestException exception) =>
        exception.StatusCode is null or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;

    private DockerContainerSpec BuildContainerSpec(
        NpmInitialAdminBootstrapTarget target,
        IReadOnlyDictionary<string, string> environment,
        DockerRestartPolicy restartPolicy,
        bool disableLogging) =>
        new(
            Name: target.ContainerName,
            Image: target.Image,
            Environment: environment,
            Labels: ManagedContainerLabels.ForService(ManagedServiceNames.Npm, runtimeContext),
            PortBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["80/tcp"] = target.HttpPort.ToString(),
                ["443/tcp"] = target.HttpsPort.ToString(),
                ["81/tcp"] = target.AdminPort.ToString()
            },
            VolumeMounts:
            [
                new DockerVolumeMount(target.DataVolumeName, "/data"),
                new DockerVolumeMount(target.LetsEncryptVolumeName, "/etc/letsencrypt")
            ],
            RestartPolicy: restartPolicy,
            NetworkName: target.NetworkName,
            NetworkAliases: [target.NetworkAlias],
            DisableLogging: disableLogging);

    private static bool HasBootstrapEnvironment(DockerContainerInspection inspection) =>
        inspection.EnvironmentVariableNames.Any(name =>
            string.Equals(name, InitialAdminEmailEnvironment, StringComparison.Ordinal) ||
            string.Equals(name, InitialAdminPasswordEnvironment, StringComparison.Ordinal));

    private static bool UsesExpectedImage(
        DockerContainerInspection inspection,
        string expectedImage) =>
        string.Equals(
            inspection.Image.Trim(),
            expectedImage.Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsCredentialRejection(HttpStatusCode? statusCode) =>
        statusCode is HttpStatusCode.BadRequest or
            HttpStatusCode.Unauthorized or
            HttpStatusCode.Forbidden or
            HttpStatusCode.UnprocessableEntity;

    private static void ValidateTarget(NpmInitialAdminBootstrapTarget target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target.ContainerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.Image);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.DataVolumeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.LetsEncryptVolumeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.NetworkName);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.NetworkAlias);
        ValidatePort(target.HttpPort, nameof(target.HttpPort));
        ValidatePort(target.HttpsPort, nameof(target.HttpsPort));
        ValidatePort(target.AdminPort, nameof(target.AdminPort));
    }

    private static void ValidatePort(int port, string parameterName)
    {
        if (port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(parameterName, "Port must be between 1 and 65535.");
    }

    private static NpmInitialAdminBootstrapResult Failed(
        string message,
        string errorCode,
        Exception exception) =>
        Result(
            succeeded: false,
            status: "Failed",
            message: $"{message} {SafeExceptionMessage(exception)}",
            errorCode: errorCode);

    private static string SafeExceptionMessage(Exception exception) =>
        exception switch
        {
            HttpRequestException http when http.StatusCode.HasValue =>
                $"HTTP {(int)http.StatusCode.Value}.",
            _ => exception.GetType().Name + "."
        };

    private Task ProgressPhaseAsync(
        Guid installationId,
        string phaseCode,
        string safeSummary,
        CancellationToken cancellationToken) =>
        progressReporter?.BeginCurrentPhaseAsync(
            installationId,
            phaseCode,
            safeSummary,
            cancellationToken) ?? Task.CompletedTask;

    private static NpmInitialAdminBootstrapResult Result(
        bool succeeded,
        string status,
        string message,
        string? errorCode,
        bool initialLoginVerified = false,
        bool recreated = false,
        bool finalLoginVerified = false,
        bool bootstrapEnvironmentRemoved = false) =>
        new(
            Succeeded: succeeded,
            Status: status,
            Message: message,
            ErrorCode: errorCode,
            InitialLoginVerified: initialLoginVerified,
            Recreated: recreated,
            FinalLoginVerified: finalLoginVerified,
            BootstrapEnvironmentRemoved: bootstrapEnvironmentRemoved);
}
