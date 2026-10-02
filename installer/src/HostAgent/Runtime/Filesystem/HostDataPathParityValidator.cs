using HostAgent.Docker;
using HostAgent.Docker.Models;
using HostAgent.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modules.Shared.Storage;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Runtime.Filesystem;

public sealed class HostDataPathParityException : InvalidOperationException
{
    public HostDataPathParityException(
        string code,
        string logicalRoot,
        string message)
        : base(message)
    {
        Code = code;
        LogicalRoot = logicalRoot;
    }

    public string Code { get; }

    public string LogicalRoot { get; }
}

/// <summary>
/// Validates the filesystem contract required when the Control Plane gives a
/// pathname to the host Docker daemon and also expects to access that same
/// resource through its own filesystem namespace.
/// </summary>
public sealed class HostDataPathParityValidator
{
    private readonly IDockerHost _dockerHost;
    private readonly MemControlPlaneRuntimeContext _runtimeContext;
    private readonly IConfiguration _configuration;
    private readonly InstanceStorageOptions _instanceStorageOptions;
    private readonly ILogger<HostDataPathParityValidator> _logger;

    public HostDataPathParityValidator(
        IDockerHost dockerHost,
        MemControlPlaneRuntimeContext runtimeContext,
        IConfiguration configuration,
        IOptions<InstanceStorageOptions> instanceStorageOptions,
        ILogger<HostDataPathParityValidator> logger)
    {
        _dockerHost = dockerHost;
        _runtimeContext = runtimeContext;
        _configuration = configuration;
        _instanceStorageOptions = instanceStorageOptions.Value;
        _logger = logger;
    }

    public async Task ValidateConfiguredRootsAsync(
        CancellationToken cancellationToken)
    {
        if (!MemRuntimeModes.IsContainerized(_runtimeContext.RuntimeMode))
        {
            return;
        }

        if (!_runtimeContext.RunningInContainer)
        {
            throw new HostDataPathParityException(
                "host_data_path_parity.runtime_contradiction",
                "runtime",
                "A containerized MEM runtime must be running inside a container before host-data path parity can be validated.");
        }

        var containerName = _runtimeContext.ConfiguredContainerName?.Trim();
        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new HostDataPathParityException(
                "host_data_path_parity.container_identity_missing",
                "runtime",
                "The containerized MEM runtime does not declare its Control Plane container name, so host-data path parity cannot be validated.");
        }

        var inspection = await _dockerHost.InspectAsync(
            containerName,
            cancellationToken);

        var roots = ResolveRequiredRoots();
        foreach (var root in roots)
        {
            ValidatePathAgainstMounts(
                root.LogicalName,
                root.Path,
                inspection.BindMounts,
                requireWritable: true);
        }

        _logger.LogInformation(
            "Validated Docker-host / Control-Plane path parity for {RootCount} configured host-data roots in runtime {RuntimeMode}.",
            roots.Count,
            _runtimeContext.RuntimeMode);
    }

    internal static void ValidatePathAgainstMounts(
        string logicalRoot,
        string path,
        IReadOnlyList<DockerBindMountInspection> mounts,
        bool requireWritable)
    {
        if (string.IsNullOrWhiteSpace(logicalRoot))
        {
            throw new ArgumentException(
                "A logical root name is required.",
                nameof(logicalRoot));
        }

        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path.Trim()))
        {
            throw new HostDataPathParityException(
                "host_data_path_parity.path_not_absolute",
                logicalRoot,
                $"Configured host-data root '{logicalRoot}' must be an absolute path before it can be used as a Docker host bind source.");
        }

        var normalizedPath = NormalizePath(path);
        var coveringMount = mounts
            .Where(mount =>
                !string.IsNullOrWhiteSpace(mount.Source) &&
                !string.IsNullOrWhiteSpace(mount.Destination))
            .Select(mount => new
            {
                Mount = mount,
                Source = NormalizePath(mount.Source),
                Destination = NormalizePath(mount.Destination)
            })
            .Where(item => IsSameOrDescendant(normalizedPath, item.Destination))
            .OrderByDescending(item => item.Destination.Length)
            .FirstOrDefault();

        if (coveringMount is null)
        {
            throw new HostDataPathParityException(
                "host_data_path_parity.mount_missing",
                logicalRoot,
                $"Configured host-data root '{logicalRoot}' is not covered by a Control Plane mount. " +
                "A containerized runtime cannot safely pass that pathname to the host Docker daemon.");
        }

        if (requireWritable && coveringMount.Mount.ReadOnly)
        {
            throw new HostDataPathParityException(
                "host_data_path_parity.mount_read_only",
                logicalRoot,
                $"Configured host-data root '{logicalRoot}' is covered only by a read-only Control Plane mount.");
        }

        var relative = Path.GetRelativePath(
            coveringMount.Destination,
            normalizedPath);
        var hostEquivalent = string.Equals(relative, ".", StringComparison.Ordinal)
            ? coveringMount.Source
            : NormalizePath(Path.Combine(coveringMount.Source, relative));

        if (!string.Equals(
                hostEquivalent,
                normalizedPath,
                StringComparison.Ordinal))
        {
            throw new HostDataPathParityException(
                "host_data_path_parity.mapping_mismatch",
                logicalRoot,
                $"Configured host-data root '{logicalRoot}' is mounted at a different host pathname than the pathname visible inside the Control Plane. " +
                "MEM requires an identity-mounted host-data boundary for sibling-container bind sources.");
        }
    }

    private IReadOnlyList<ConfiguredHostDataRoot> ResolveRequiredRoots()
    {
        return
        [
            new ConfiguredHostDataRoot(
                "mem-data",
                ResolveRequiredAbsolutePath(
                    "MEM_DATA_ROOT",
                    MemDataRootResolver.Resolve(_configuration))),
            new ConfiguredHostDataRoot(
                "instances",
                ResolveRequiredAbsolutePath(
                    "Provisioning:InstanceDataRoot",
                    _instanceStorageOptions.ResolveRequiredRoot())),
            new ConfiguredHostDataRoot(
                "coturn",
                ResolveRequiredAbsolutePath(
                    "Coturn:StorageRootPath",
                    _configuration["Coturn:StorageRootPath"])),
            new ConfiguredHostDataRoot(
                "seq",
                ResolveRequiredAbsolutePath(
                    "Diagnostics:Seq:HostDataPath",
                    _configuration["Diagnostics:Seq:HostDataPath"]))
        ];
    }

    private static string ResolveRequiredAbsolutePath(
        string configurationKey,
        string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || !Path.IsPathRooted(trimmed))
        {
            throw new HostDataPathParityException(
                "host_data_path_parity.path_not_absolute",
                configurationKey,
                $"{configurationKey} must be configured as an absolute host-data path for a containerized MEM runtime.");
        }

        return NormalizePath(trimmed);
    }

    private static bool IsSameOrDescendant(
        string path,
        string root)
    {
        if (string.Equals(path, root, StringComparison.Ordinal))
        {
            return true;
        }

        var prefix = root.EndsWith(
            Path.DirectorySeparatorChar.ToString(),
            StringComparison.Ordinal)
            ? root
            : root + Path.DirectorySeparatorChar;

        return path.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path.Trim());
        if (string.Equals(
                fullPath,
                Path.GetPathRoot(fullPath),
                StringComparison.Ordinal))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    private sealed record ConfiguredHostDataRoot(
        string LogicalName,
        string Path);
}
