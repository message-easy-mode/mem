using System.Text.Json;
using Shared.ControlPlane;

namespace Shared.ControlPlane.Runtime;

public static class MemControlPlaneRuntimeContextFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static MemControlPlaneRuntimeContext Create(
        MemRuntimeContextOptions options,
        MemRuntimeEnvironmentSnapshot environment,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        timeProvider ??= TimeProvider.System;

        var warnings = new List<string>();
        var mode = ResolveRuntimeMode(options.Mode, environment, warnings);
        ValidateDeclaredMode(mode, environment);

        var stateRoot = ResolveStateRoot(options.StateRoot, mode, environment.ContentRootPath);
        var dockerEndpoint = ResolveDockerEndpoint(options.DockerEndpoint);
        var uiDeliveryMode = ResolveUiDeliveryMode(
            options.UiDeliveryMode,
            mode,
            environment.WebRootPath,
            warnings);
        var containerName = NormalizeContainerName(options.ContainerName);
        ValidateContainerIdentity(mode, containerName);
        var hostAccessIpv4 = MemHostAccessIpv4.Normalize(options.HostAccessIpv4);

        if (options.AllowSharedDockerHost &&
            string.Equals(
                mode,
                MemRuntimeModes.ContainerizedProduction,
                StringComparison.Ordinal))
        {
            throw new MemRuntimeContextValidationException(
                "runtime_shared_docker_host_override_forbidden",
                "MEM_DEV_ALLOW_SHARED_DOCKER_HOST is forbidden in containerized production.");
        }

        if (string.Equals(
                mode,
                MemRuntimeModes.ContainerizedProduction,
                StringComparison.Ordinal) &&
            environment.DevelopmentSetupTokenConfigured)
        {
            throw new MemRuntimeContextValidationException(
                "runtime_production_development_token_forbidden",
                "Containerized production cannot start with a deterministic development setup token.");
        }

        var expectedInstanceId = ParseExpectedInstanceId(options.ExpectedInstanceId);
        var identityPath = ResolveIdentityPath(
            stateRoot,
            options.InstanceIdentityFileName);
        var instanceId = LoadOrCreateInstanceIdentity(
            identityPath,
            expectedInstanceId,
            timeProvider.GetUtcNow());

        return new MemControlPlaneRuntimeContext(
            SchemaVersion: 1,
            RuntimeMode: mode,
            ControlPlaneInstanceId: instanceId,
            ApiProcessInstanceId: Guid.NewGuid(),
            EnvironmentName: NormalizeRequired(
                environment.EnvironmentName,
                "runtime_environment_name_missing",
                "The hosting environment name is required."),
            RunningInContainer: environment.RunningInContainer,
            ContentRootPath: Path.GetFullPath(environment.ContentRootPath),
            ContentRootKind: ResolveContentRootKind(mode),
            StateRootPath: stateRoot,
            StateRootKind: ResolveStateRootKind(mode),
            StateRootProfile: ResolveStateRootProfile(
                mode,
                stateRoot,
                environment.ContentRootPath),
            UiDeliveryMode: uiDeliveryMode,
            DockerEndpoint: dockerEndpoint,
            DockerEndpointKind: ResolveDockerEndpointKind(dockerEndpoint),
            ConfiguredContainerName: containerName,
            ApplicationName: string.IsNullOrWhiteSpace(environment.ApplicationName)
                ? MemControlPlaneIdentity.CanonicalContainerName
                : environment.ApplicationName.Trim(),
            Version: string.IsNullOrWhiteSpace(environment.Version)
                ? "unknown"
                : environment.Version.Trim(),
            Commit: NormalizeOptional(environment.Commit),
            ValidationState: warnings.Count == 0
                ? MemRuntimeValidationStates.Valid
                : MemRuntimeValidationStates.Warning,
            MutationsAllowed: true,
            ShowDevelopmentBanner: MemRuntimeModes.IsNonProduction(mode),
            Warnings: warnings.Distinct(StringComparer.Ordinal).ToArray())
        {
            AllowSharedDockerHost = options.AllowSharedDockerHost,
            HostAccessIpv4 = hostAccessIpv4
        };
    }

    private static string ResolveRuntimeMode(
        string? configuredMode,
        MemRuntimeEnvironmentSnapshot environment,
        List<string> warnings)
    {
        if (MemRuntimeModes.TryNormalize(configuredMode, out var mode))
        {
            return mode;
        }

        if (!string.IsNullOrWhiteSpace(configuredMode))
        {
            throw new MemRuntimeContextValidationException(
                "runtime_mode_invalid",
                $"Unsupported MEM runtime mode '{configuredMode}'.");
        }

        if (string.Equals(environment.EnvironmentName, "Test", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("runtime_mode_defaulted_for_test");
            return MemRuntimeModes.AutomatedTest;
        }

        if (string.Equals(
                environment.EnvironmentName,
                "Development",
                StringComparison.OrdinalIgnoreCase) &&
            !environment.RunningInContainer)
        {
            warnings.Add("runtime_mode_defaulted_for_development");
            return MemRuntimeModes.LocalDevelopment;
        }

        throw new MemRuntimeContextValidationException(
            "runtime_mode_missing",
            "MEM_RUNTIME_MODE or MemRuntime:Mode must explicitly identify the active Control Plane runtime.");
    }

    private static void ValidateDeclaredMode(
        string mode,
        MemRuntimeEnvironmentSnapshot environment)
    {
        if (string.Equals(mode, MemRuntimeModes.LocalDevelopment, StringComparison.Ordinal) &&
            environment.RunningInContainer)
        {
            throw new MemRuntimeContextValidationException(
                "runtime_mode_container_contradiction",
                "local-development cannot run inside a container.");
        }

        if (MemRuntimeModes.IsContainerized(mode) && !environment.RunningInContainer)
        {
            throw new MemRuntimeContextValidationException(
                "runtime_mode_host_contradiction",
                $"{mode} requires an observed container runtime.");
        }
    }

    private static string ResolveStateRoot(
        string? configuredPath,
        string mode,
        string contentRootPath)
    {
        var contentRoot = Path.GetFullPath(NormalizeRequired(
            contentRootPath,
            "runtime_content_root_missing",
            "The Control Plane content root is required."));
        var configured = configuredPath?.Trim();

        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = mode switch
            {
                MemRuntimeModes.LocalDevelopment => "../../data",
                MemRuntimeModes.ContainerizedDevelopment => "/data",
                MemRuntimeModes.ContainerizedProduction => "/data",
                _ => Path.Combine(Path.GetTempPath(), "mem-control-plane-tests")
            };
        }

        if (MemRuntimeModes.IsContainerized(mode) && !Path.IsPathRooted(configured))
        {
            throw new MemRuntimeContextValidationException(
                "runtime_container_state_root_not_absolute",
                "Containerized runtime state must use an absolute server-owned path.");
        }

        var resolved = Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(contentRoot, configured));

        if (string.Equals(resolved, Path.GetPathRoot(resolved), StringComparison.Ordinal))
        {
            throw new MemRuntimeContextValidationException(
                "runtime_state_root_unsafe",
                "The filesystem root cannot be used as the MEM state root.");
        }

        return resolved;
    }

    private static Uri ResolveDockerEndpoint(string? value)
    {
        var configured = string.IsNullOrWhiteSpace(value)
            ? "unix:///var/run/docker.sock"
            : value.Trim();
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("unix" or "npipe" or "http" or "https"))
        {
            throw new MemRuntimeContextValidationException(
                "runtime_docker_endpoint_invalid",
                "The configured Docker endpoint is not a supported absolute authority.");
        }

        return endpoint;
    }

    private static string ResolveUiDeliveryMode(
        string? configuredMode,
        string runtimeMode,
        string? webRootPath,
        List<string> warnings)
    {
        var expected = runtimeMode switch
        {
            MemRuntimeModes.LocalDevelopment => MemUiDeliveryModes.Vite,
            MemRuntimeModes.ContainerizedDevelopment => MemUiDeliveryModes.EmbeddedSpa,
            MemRuntimeModes.ContainerizedProduction => MemUiDeliveryModes.EmbeddedSpa,
            _ => MemUiDeliveryModes.TestHost
        };

        var mode = expected;
        if (!string.IsNullOrWhiteSpace(configuredMode))
        {
            if (!MemUiDeliveryModes.TryNormalize(configuredMode, out mode))
            {
                throw new MemRuntimeContextValidationException(
                    "runtime_ui_delivery_mode_invalid",
                    $"Unsupported UI delivery mode '{configuredMode}'.");
            }
        }

        if (string.Equals(runtimeMode, MemRuntimeModes.LocalDevelopment, StringComparison.Ordinal) &&
            !string.Equals(mode, MemUiDeliveryModes.Vite, StringComparison.Ordinal))
        {
            throw new MemRuntimeContextValidationException(
                "runtime_ui_delivery_contradiction",
                "local-development must use the Vite browser delivery mode.");
        }

        if (MemRuntimeModes.IsContainerized(runtimeMode) &&
            string.Equals(mode, MemUiDeliveryModes.Vite, StringComparison.Ordinal))
        {
            throw new MemRuntimeContextValidationException(
                "runtime_ui_delivery_contradiction",
                "A containerized Control Plane cannot declare Vite as its browser delivery mode.");
        }

        if (MemRuntimeModes.IsContainerized(runtimeMode) &&
            string.Equals(mode, MemUiDeliveryModes.EmbeddedSpa, StringComparison.Ordinal))
        {
            var indexPath = string.IsNullOrWhiteSpace(webRootPath)
                ? null
                : Path.Combine(webRootPath, "index.html");
            if (indexPath is null || !File.Exists(indexPath))
            {
                warnings.Add("runtime_embedded_spa_not_observed");
                return MemUiDeliveryModes.ApiOnly;
            }
        }

        return mode;
    }

    private static string? NormalizeContainerName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateContainerIdentity(string mode, string? containerName)
    {
        if (!MemRuntimeModes.IsContainerized(mode))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new MemRuntimeContextValidationException(
                "runtime_container_identity_missing",
                "A containerized Control Plane must declare its server-owned container identity.");
        }

        if (!MemControlPlaneIdentity.IsRecognizedContainerName(containerName, mode))
        {
            throw new MemRuntimeContextValidationException(
                "runtime_container_identity_unrecognized",
                $"Container identity '{containerName}' is not approved for runtime mode '{mode}'.");
        }
    }

    private static Guid? ParseExpectedInstanceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Guid.TryParse(value.Trim(), out var parsed) || parsed == Guid.Empty)
        {
            throw new MemRuntimeContextValidationException(
                "runtime_instance_id_invalid",
                "MEM_CONTROL_PLANE_INSTANCE_ID must be a non-empty GUID when supplied.");
        }

        return parsed;
    }

    private static string ResolveIdentityPath(
        string stateRoot,
        string? configuredFileName)
    {
        var relative = string.IsNullOrWhiteSpace(configuredFileName)
            ? "control-plane/runtime-instance.json"
            : configuredFileName.Trim();
        if (Path.IsPathRooted(relative))
        {
            throw new MemRuntimeContextValidationException(
                "runtime_instance_identity_path_invalid",
                "The runtime instance identity filename must remain within the active state root.");
        }

        var path = Path.GetFullPath(Path.Combine(stateRoot, relative));
        var relativeCheck = Path.GetRelativePath(stateRoot, path);
        if (relativeCheck.Equals("..", StringComparison.Ordinal) ||
            relativeCheck.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new MemRuntimeContextValidationException(
                "runtime_instance_identity_path_invalid",
                "The runtime instance identity path escapes the active state root.");
        }

        return path;
    }

    private static Guid LoadOrCreateInstanceIdentity(
        string path,
        Guid? expectedInstanceId,
        DateTimeOffset now)
    {
        try
        {
            if (File.Exists(path))
            {
                var stored = JsonSerializer.Deserialize<StoredRuntimeIdentity>(
                    File.ReadAllText(path),
                    JsonOptions);
                if (stored is null || stored.SchemaVersion != 1 || stored.InstanceId == Guid.Empty)
                {
                    throw new MemRuntimeContextValidationException(
                        "runtime_instance_identity_invalid",
                        "The persisted Control Plane instance identity is invalid.");
                }

                if (expectedInstanceId.HasValue && stored.InstanceId != expectedInstanceId.Value)
                {
                    throw new MemRuntimeContextValidationException(
                        "runtime_instance_identity_mismatch",
                        "The configured Control Plane instance ID does not match the persisted state-root identity.");
                }

                return stored.InstanceId;
            }

            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new MemRuntimeContextValidationException(
                    "runtime_instance_identity_path_invalid",
                    "The runtime instance identity path has no parent directory.");
            }

            Directory.CreateDirectory(directory);
            var identity = new StoredRuntimeIdentity(
                SchemaVersion: 1,
                InstanceId: expectedInstanceId ?? Guid.NewGuid(),
                CreatedAtUtc: now);
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(
                    temporaryPath,
                    JsonSerializer.Serialize(identity, JsonOptions));
                TryRestrictFilePermissions(temporaryPath);
                File.Move(temporaryPath, path, overwrite: false);
                TryRestrictFilePermissions(path);
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }

            return identity.InstanceId;
        }
        catch (MemRuntimeContextValidationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or JsonException or
                NotSupportedException or ArgumentException)
        {
            throw new MemRuntimeContextValidationException(
                "runtime_instance_identity_unavailable",
                $"MEM could not load or persist the Control Plane instance identity: {exception.GetType().Name}.");
        }
    }

    private static void TryRestrictFilePermissions(string path)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Preserve the primary failure. A uniquely named temporary file
            // contains only a random instance ID and creation timestamp.
        }
    }

    private static string ResolveContentRootKind(string mode) => mode switch
    {
        MemRuntimeModes.LocalDevelopment => "source-tree",
        MemRuntimeModes.ContainerizedDevelopment => "published-container",
        MemRuntimeModes.ContainerizedProduction => "published-container",
        _ => "test-host"
    };

    private static string ResolveStateRootKind(string mode) => mode switch
    {
        MemRuntimeModes.LocalDevelopment => "repository-local",
        MemRuntimeModes.ContainerizedDevelopment => "development-volume",
        MemRuntimeModes.ContainerizedProduction => "persistent-volume",
        _ => "disposable-test"
    };

    private static string ResolveStateRootProfile(
        string mode,
        string stateRoot,
        string contentRootPath)
    {
        var standard = mode switch
        {
            MemRuntimeModes.LocalDevelopment => string.Equals(
                stateRoot,
                Path.GetFullPath(Path.Combine(
                    Path.GetFullPath(contentRootPath),
                    "../../data")),
                StringComparison.Ordinal),
            MemRuntimeModes.ContainerizedDevelopment => string.Equals(
                stateRoot,
                "/data",
                StringComparison.Ordinal),
            MemRuntimeModes.ContainerizedProduction => string.Equals(
                stateRoot,
                "/data",
                StringComparison.Ordinal),
            _ => true
        };

        return standard
            ? MemStateRootProfiles.Default
            : MemStateRootProfiles.Custom;
    }

    private static string ResolveDockerEndpointKind(Uri endpoint) => endpoint.Scheme switch
    {
        "unix" => "local-unix-socket",
        "npipe" => "windows-named-pipe",
        "http" => "remote-http",
        "https" => "remote-https",
        _ => "custom"
    };

    private static string NormalizeRequired(
        string? value,
        string code,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new MemRuntimeContextValidationException(code, message);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record StoredRuntimeIdentity(
        int SchemaVersion,
        Guid InstanceId,
        DateTimeOffset CreatedAtUtc);
}
