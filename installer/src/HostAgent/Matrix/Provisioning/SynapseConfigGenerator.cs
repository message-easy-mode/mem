using System.Diagnostics;
using HostAgent.Docker;
using HostAgent.Docker.Models;
using HostAgent.Matrix.Runtime;
using HostAgent.Options;
using HostAgent.Runtime.Coturn;
using HostAgent.Runtime.Databases;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Matrix.Provisioning;

public sealed class SynapseConfigGenerator
{
    private readonly IDockerHost _docker;
    private readonly MatrixBootstrapOptions _bootstrapOptions;
    private readonly ILogger<SynapseConfigGenerator> _logger;
    private readonly MemControlPlaneRuntimeContext? _runtimeContext;

    public SynapseConfigGenerator(
        IDockerHost docker,
        IOptions<MatrixBootstrapOptions> bootstrapOptions,
        ILogger<SynapseConfigGenerator> logger,
        MemControlPlaneRuntimeContext? runtimeContext = null)
    {
        _docker = docker;
        _bootstrapOptions = bootstrapOptions.Value;
        _logger = logger;
        _runtimeContext = runtimeContext;
    }

    public async Task<string> GenerateAsync(
        Guid instanceId,
        string serverName,
        string dataPath,
        bool reportStats,
        CancellationToken cancellationToken,
        string? registrationSharedSecret = null,
        RuntimeStackPostgresSettings? postgres = null,
        CoturnSynapseConfig? coturn = null,
        string? image = null)
    {
        if (string.IsNullOrWhiteSpace(serverName))
        {
            throw new InvalidOperationException("Matrix server name is required.");
        }

        if (string.IsNullOrWhiteSpace(dataPath))
        {
            throw new InvalidOperationException("Matrix data path is required.");
        }

        if (string.IsNullOrWhiteSpace(image))
        {
            throw new InvalidOperationException(
                "An approved immutable Synapse image identity is required for configuration generation.");
        }

        Directory.CreateDirectory(dataPath);

        var homeserverPath = Path.Combine(dataPath, "homeserver.yaml");

        var effectiveSharedSecret = GetRequiredSharedSecret(
            registrationSharedSecret);

        if (File.Exists(homeserverPath))
        {
            _logger.LogInformation(
                "Synapse homeserver.yaml already exists. Patching existing config. InstanceId={InstanceId} HomeserverPath={HomeserverPath}",
                instanceId,
                homeserverPath);

            PatchHomeserverYaml(
                homeserverPath,
                effectiveSharedSecret,
                postgres,
                coturn);

            return homeserverPath;
        }

        var rawContainerName = $"mem-synapsegen-{instanceId:N}";
        var containerName = rawContainerName[..Math.Min(48, rawContainerName.Length)];

        var runtimeIdentity = SynapseRuntimeIdentityPolicy.Resolve(_runtimeContext);
        var user = runtimeIdentity.ContainerUser;

        _logger.LogInformation(
            "Synapse generate runtime identity resolved. RuntimeMode={RuntimeMode} ContainerUser={ContainerUser} IdentitySource={IdentitySource} Detail={Detail}",
            runtimeIdentity.RuntimeMode,
            user ?? "image-default",
            runtimeIdentity.IdentitySource,
            runtimeIdentity.Detail);

        var spec = new DockerContainerSpec(
            Name: containerName,
            Image: image.Trim(),
            Env: new Dictionary<string, string>
            {
                ["SYNAPSE_SERVER_NAME"] = serverName,
                ["SYNAPSE_REPORT_STATS"] = reportStats ? "yes" : "no"
            },
            Labels: new Dictionary<string, string>
            {
                ["mem.component"] = "host-agent",
                ["mem.operation"] = "synapse-config-generate",
                ["mem.matrix.instanceId"] = instanceId.ToString()
            },
            Cmd: ["generate"],
            PortBindings: null,
            AutoRemove: true,
            BindMounts:
            [
                new BindMount(
                    HostPath: dataPath,
                    ContainerPath: "/data",
                    ReadOnly: false)
            ],
            User: user
        );

        _logger.LogInformation(
            "Creating Synapse generate container. InstanceId={InstanceId} ContainerName={ContainerName} ServerName={ServerName} DataPath={DataPath}",
            instanceId,
            containerName,
            serverName,
            dataPath);

        var startedAt = Stopwatch.GetTimestamp();
        string? containerId = null;
        var stage = "create-container";

        try
        {
            containerId = await _docker.CreateContainerAsync(
                spec,
                cancellationToken);

            stage = "start-container";
            await _docker.StartContainerAsync(
                containerId,
                cancellationToken);

            stage = "wait-for-exit";
            var exitCode = await _docker.WaitForExitAsync(
                containerId,
                cancellationToken);

            if (exitCode != 0)
            {
                var logs = await TryGetLogsAsync(containerId);

                _logger.LogError(
                    "Synapse generate failed. InstanceId={InstanceId} ContainerId={ContainerId} ExitCode={ExitCode} Logs={Logs}",
                    instanceId,
                    containerId,
                    exitCode,
                    logs);

                throw new InvalidOperationException(
                    $"Synapse generate failed with exit code {exitCode}.");
            }

            if (!File.Exists(homeserverPath))
            {
                var logs = await TryGetLogsAsync(containerId);

                _logger.LogError(
                    "Synapse generate completed but homeserver.yaml was missing. InstanceId={InstanceId} ExpectedPath={HomeserverPath} Logs={Logs}",
                    instanceId,
                    homeserverPath,
                    logs);

                throw new FileNotFoundException(
                    "Synapse homeserver.yaml was not found after generate.",
                    homeserverPath);
            }

            PatchHomeserverYaml(
                homeserverPath,
                effectiveSharedSecret,
                postgres,
                coturn);

            _logger.LogInformation(
                "Synapse config generated. InstanceId={InstanceId} HomeserverPath={HomeserverPath}",
                instanceId,
                homeserverPath);

            return homeserverPath;
        }
        catch (OperationCanceledException ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            var callerCancellationRequested = cancellationToken.IsCancellationRequested;
            var exceptionCancellationRequested = ex.CancellationToken.IsCancellationRequested;
            var logs = containerId is null
                ? null
                : await TryGetLogsAsync(containerId);

            _logger.LogError(
                ex,
                "Synapse generate was cancelled. InstanceId={InstanceId} Stage={Stage} ContainerId={ContainerId} CallerCancellationRequested={CallerCancellationRequested} ExceptionCancellationRequested={ExceptionCancellationRequested} ElapsedMs={ElapsedMs} Logs={Logs}",
                instanceId,
                stage,
                containerId,
                callerCancellationRequested,
                exceptionCancellationRequested,
                elapsed.TotalMilliseconds,
                logs);

            throw new SynapseConfigGenerationCanceledException(
                instanceId,
                stage,
                containerId,
                callerCancellationRequested,
                exceptionCancellationRequested,
                elapsed,
                ex);
        }
        catch (Exception ex)
        {
            var logs = containerId is null
                ? null
                : await TryGetLogsAsync(containerId);

            _logger.LogError(
                ex,
                "Synapse generate exception. InstanceId={InstanceId} Stage={Stage} ContainerId={ContainerId} Logs={Logs}",
                instanceId,
                stage,
                containerId,
                logs);

            throw;
        }
    }

    private async Task<string?> TryGetLogsAsync(string containerId)
    {
        try
        {
            using var diagnosticsCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            return await _docker.GetLogsAsync(
                containerId,
                tail: 300,
                diagnosticsCancellation.Token);
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            _logger.LogDebug(
                ex,
                "Synapse generate logs were unavailable during failure capture. ContainerId={ContainerId}",
                containerId);
            return null;
        }
    }

    private string GetRequiredSharedSecret(
        string? requestedSharedSecret = null)
    {
        var sharedSecret = (requestedSharedSecret ?? _bootstrapOptions.SharedSecret ?? string.Empty)
            .Trim();

        if (sharedSecret.Length == 0)
        {
            throw new InvalidOperationException(
                "Matrix registration shared secret is missing. It is required for Synapse shared-secret registration.");
        }

        return sharedSecret;
    }

    private static void PatchHomeserverYaml(
        string homeserverPath,
        string sharedSecret,
        RuntimeStackPostgresSettings? postgres,
        CoturnSynapseConfig? coturn)
    {
        var lines = File.ReadAllLines(homeserverPath).ToList();

        var secretLine = $"registration_shared_secret: \"{EscapeYaml(sharedSecret)}\"";

        var replaced = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith("registration_shared_secret:", StringComparison.Ordinal))
            {
                var indent = lines[i][..(lines[i].Length - trimmed.Length)];
                lines[i] = indent + secretLine;
                replaced = true;
                break;
            }
        }

        if (!replaced)
        {
            var insertAt = 0;

            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith("server_name:", StringComparison.Ordinal))
                {
                    insertAt = i + 1;
                    break;
                }
            }

            lines.Insert(insertAt, secretLine);
        }

        EnsureOrReplaceBool(
            lines,
            "enable_registration",
            false);

        EnsureOrReplaceBool(
            lines,
            "enable_registration_without_verification",
            false);

        EnsureUserDirectoryBlock(
            lines,
            enabled: true,
            searchAllUsers: true,
            preferLocalUsers: true);

        if (postgres is not null)
        {
            EnsurePostgresDatabaseBlock(
                lines,
                postgres);
        }

        if (coturn is not null)
        {
            EnsureCoturnTurnBlock(
                lines,
                coturn);
        }

        File.WriteAllLines(
            homeserverPath,
            lines);
    }

    private static void EnsureOrReplaceBool(
        List<string> lines,
        string key,
        bool value)
    {
        var target = $"{key}: {(value ? "true" : "false")}";

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith(key + ":", StringComparison.Ordinal))
            {
                var indent = lines[i][..(lines[i].Length - trimmed.Length)];
                lines[i] = indent + target;
                return;
            }
        }

        lines.Add(target);
    }

    private static void EnsureUserDirectoryBlock(
        List<string> lines,
        bool enabled,
        bool searchAllUsers,
        bool preferLocalUsers)
    {
        var startIndex = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith("user_directory:", StringComparison.Ordinal))
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex >= 0)
        {
            var endIndex = lines.Count;

            for (var i = startIndex + 1; i < lines.Count; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    continue;
                }

                var leadingSpaces = line.TakeWhile(char.IsWhiteSpace).Count();

                if (leadingSpaces == 0)
                {
                    endIndex = i;
                    break;
                }
            }

            lines.RemoveRange(
                startIndex,
                endIndex - startIndex);
        }

        lines.Add("user_directory:");
        lines.Add($"  enabled: {(enabled ? "true" : "false")}");
        lines.Add($"  search_all_users: {(searchAllUsers ? "true" : "false")}");
        lines.Add($"  prefer_local_users: {(preferLocalUsers ? "true" : "false")}");
    }

    private static void EnsurePostgresDatabaseBlock(
        List<string> lines,
        RuntimeStackPostgresSettings postgres)
    {
        RemoveTopLevelBlock(
            lines,
            "database");

        lines.Add(string.Empty);
        lines.Add("database:");
        lines.Add("  name: psycopg2");
        lines.Add("  args:");
        lines.Add($"    user: \"{EscapeYaml(postgres.Username)}\"");
        lines.Add($"    password: \"{EscapeYaml(postgres.Password)}\"");
        lines.Add($"    database: \"{EscapeYaml(postgres.DatabaseName)}\"");
        lines.Add($"    host: \"{EscapeYaml(postgres.Host)}\"");
        lines.Add($"    port: {postgres.Port}");
        lines.Add("    cp_min: 5");
        lines.Add("    cp_max: 10");
    }

    private static void EnsureCoturnTurnBlock(
        List<string> lines,
        CoturnSynapseConfig coturn)
    {
        RemoveTopLevelBlock(
            lines,
            "turn_uris");

        RemoveTopLevelBlock(
            lines,
            "turn_shared_secret");

        RemoveTopLevelBlock(
            lines,
            "turn_user_lifetime");

        RemoveTopLevelBlock(
            lines,
            "turn_allow_guests");

        lines.Add(string.Empty);
        lines.Add("# MEM-managed TURN configuration for Matrix voice/video calls.");
        lines.Add($"# TURN public host: {EscapeYaml(coturn.PublicHost)}");
        lines.Add($"# TURN realm: {EscapeYaml(coturn.Realm)}");
        lines.Add("turn_uris:");

        foreach (var uri in coturn.TurnUris)
        {
            lines.Add($"  - \"{EscapeYaml(uri)}\"");
        }

        lines.Add($"turn_shared_secret: \"{EscapeYaml(coturn.SharedSecret)}\"");
        lines.Add($"turn_user_lifetime: \"{EscapeYaml(coturn.UserLifetime)}\"");
        lines.Add($"turn_allow_guests: {(coturn.AllowGuests ? "true" : "false")}");
    }

    private static void RemoveTopLevelBlock(
        List<string> lines,
        string key)
    {
        var startIndex = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith(key + ":", StringComparison.Ordinal))
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex < 0)
        {
            return;
        }

        var endIndex = lines.Count;

        for (var i = startIndex + 1; i < lines.Count; i++)
        {
            var line = lines[i];

            if (line.Trim().Length == 0)
            {
                continue;
            }

            var leadingSpaces = line.TakeWhile(char.IsWhiteSpace).Count();

            if (leadingSpaces == 0)
            {
                endIndex = i;
                break;
            }
        }

        lines.RemoveRange(
            startIndex,
            endIndex - startIndex);
    }

    private static string EscapeYaml(
        string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}