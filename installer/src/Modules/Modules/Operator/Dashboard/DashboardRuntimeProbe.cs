using Core.RuntimeDefinition;
using Infrastructure.Docker;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;
using Modules.Setup.HostChecks.Runtime;
using Modules.Setup.Platform.Coturn;
using Modules.Shared.Storage;
using Microsoft.Extensions.Configuration;
using Shared.ControlPlane.Runtime;

namespace Modules.Operator.Dashboard;

/// <summary>
/// Narrow, safe boundary for the small number of live observations Home needs.
/// This is not a general-purpose telemetry or monitoring abstraction.
/// </summary>
public interface IDashboardRuntimeProbe
{
    Task<DashboardDockerObservation> ObserveDockerAsync(CancellationToken ct);

    Task<DashboardHostObservation> ObserveHostAsync(CancellationToken ct);

    Task<IReadOnlyList<DashboardPlatformServiceObservation>> ObservePlatformServicesAsync(
        CancellationToken ct);

    Task<DashboardIngressObservation> ObserveIngressAsync(CancellationToken ct);
}

public sealed record DashboardDockerObservation(
    string State,
    DateTimeOffset? ObservedAtUtc);

public sealed record DashboardHostObservation(
    string State,
    DateTimeOffset ObservedAtUtc,
    string? UnavailableReasonCode,
    string? OperatingSystem,
    string? Architecture,
    long? CpuCount,
    long? MemoryTotalBytes,
    string? DockerServerVersion,
    long? ContainerCount,
    long? ImageCount,
    DashboardHostDiskObservation? Disk,
    string? StorageUnavailableReasonCode);

public sealed record DashboardHostDiskObservation(
    long UsedBytes,
    long TotalBytes,
    string Scope);

internal sealed record DashboardFilesystemDriveObservation(
    string RootPath,
    long TotalBytes,
    long AvailableBytes);

public sealed record DashboardPlatformServiceObservation(
    string Key,
    string Requirement,
    string State,
    DateTimeOffset? ObservedAtUtc);

public sealed record DashboardIngressObservation(
    string State,
    DateTimeOffset? ObservedAtUtc);

/// <summary>
/// Production live-observation adapter. It classifies only the required MEM
/// platform services and deliberately discards raw Docker/NPM failure details.
/// </summary>
public sealed class DashboardRuntimeProbe : IDashboardRuntimeProbe
{
    private readonly IDockerHost _dockerHost;
    private readonly NpmReadinessService _npmReadinessService;
    private readonly IPlatformCoturnSetupService _platformCoturnSetupService;
    private readonly ISetupDockerRuntimeProbe _dockerRuntimeProbe;
    private readonly IConfiguration _configuration;
    private readonly MemControlPlaneRuntimeContext _runtimeContext;
    private readonly TimeProvider _timeProvider;

    public DashboardRuntimeProbe(
        IDockerHost dockerHost,
        NpmReadinessService npmReadinessService,
        IPlatformCoturnSetupService platformCoturnSetupService,
        ISetupDockerRuntimeProbe dockerRuntimeProbe,
        IConfiguration configuration,
        MemControlPlaneRuntimeContext runtimeContext,
        TimeProvider timeProvider)
    {
        _dockerHost = dockerHost;
        _npmReadinessService = npmReadinessService;
        _platformCoturnSetupService = platformCoturnSetupService;
        _dockerRuntimeProbe = dockerRuntimeProbe;
        _configuration = configuration;
        _runtimeContext = runtimeContext;
        _timeProvider = timeProvider;
    }

    public async Task<DashboardDockerObservation> ObserveDockerAsync(
        CancellationToken ct)
    {
        try
        {
            var reachable = await _dockerHost.PingAsync(ct);
            return new DashboardDockerObservation(
                reachable ? DashboardStates.Responsive : DashboardStates.Unavailable,
                _timeProvider.GetUtcNow());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new DashboardDockerObservation(
                DashboardStates.Unavailable,
                _timeProvider.GetUtcNow());
        }
    }

    public async Task<DashboardHostObservation> ObserveHostAsync(CancellationToken ct)
    {
        var observedAtUtc = _timeProvider.GetUtcNow();

        try
        {
            var info = await _dockerRuntimeProbe.GetSystemInfoAsync(ct);
            var (disk, storageUnavailableReasonCode) = ObserveMemDataDisk();

            return new DashboardHostObservation(
                State: DashboardStates.Available,
                ObservedAtUtc: observedAtUtc,
                UnavailableReasonCode: null,
                OperatingSystem: Normalize(info.OperatingSystem),
                Architecture: Normalize(info.Architecture),
                CpuCount: PositiveOrNull(info.CpuCount),
                MemoryTotalBytes: PositiveOrNull(info.MemoryBytes),
                DockerServerVersion: Normalize(info.ServerVersion),
                ContainerCount: NonNegativeOrNull(info.ContainerCount),
                ImageCount: NonNegativeOrNull(info.ImageCount),
                Disk: disk,
                StorageUnavailableReasonCode: storageUnavailableReasonCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new DashboardHostObservation(
                State: DashboardStates.Unavailable,
                ObservedAtUtc: observedAtUtc,
                UnavailableReasonCode: DashboardStates.HostObservationFailed,
                OperatingSystem: null,
                Architecture: null,
                CpuCount: null,
                MemoryTotalBytes: null,
                DockerServerVersion: null,
                ContainerCount: null,
                ImageCount: null,
                Disk: null,
                StorageUnavailableReasonCode: DashboardStates.HostStorageUnavailable);
        }
    }

    public async Task<IReadOnlyList<DashboardPlatformServiceObservation>> ObservePlatformServicesAsync(
        CancellationToken ct)
    {
        var observedAtUtc = _timeProvider.GetUtcNow();

        var services = new List<DashboardPlatformServiceObservation>
        {
            await ObserveContainerAsync(
                "postgres",
                ManagedContainerNames.Postgres,
                observedAtUtc,
                ct)
        };

        services.Add(await ObserveNpmAsync(observedAtUtc, ct));
        services.Add(await ObserveCoturnAsync(observedAtUtc, ct));

        return services;
    }

    public async Task<DashboardIngressObservation> ObserveIngressAsync(
        CancellationToken ct)
    {
        var observedAtUtc = _timeProvider.GetUtcNow();

        try
        {
            var readiness = await _npmReadinessService.GetReadinessAsync(ct);
            return new DashboardIngressObservation(
                ClassifyIngress(readiness),
                observedAtUtc);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new DashboardIngressObservation(
                DashboardStates.Unknown,
                observedAtUtc);
        }
    }

    private async Task<DashboardPlatformServiceObservation> ObserveContainerAsync(
        string key,
        string containerName,
        DateTimeOffset observedAtUtc,
        CancellationToken ct)
    {
        try
        {
            var container = await _dockerHost.InspectByNameAsync(containerName, ct);

            return new DashboardPlatformServiceObservation(
                key,
                DashboardStates.Required,
                container is null
                    ? DashboardStates.NotDeployed
                    : container.Running
                        ? DashboardStates.Running
                        : DashboardStates.Stopped,
                observedAtUtc);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new DashboardPlatformServiceObservation(
                key,
                DashboardStates.Required,
                DashboardStates.Unknown,
                observedAtUtc);
        }
    }

    private async Task<DashboardPlatformServiceObservation> ObserveCoturnAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken ct)
    {
        try
        {
            var coturn = await _platformCoturnSetupService.InspectAsync(ct);
            var state = !coturn.ContainerExists
                ? DashboardStates.NotDeployed
                : !coturn.Running
                    ? DashboardStates.Stopped
                    : string.Equals(
                        coturn.Readiness,
                        "verification-limited",
                        StringComparison.Ordinal)
                        ? DashboardStates.VerificationLimited
                        : coturn.Ready
                            ? DashboardStates.Running
                            : DashboardStates.Degraded;

            return new DashboardPlatformServiceObservation(
                "coturn",
                DashboardStates.Required,
                state,
                observedAtUtc);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new DashboardPlatformServiceObservation(
                "coturn",
                DashboardStates.Required,
                DashboardStates.Unknown,
                observedAtUtc);
        }
    }

    private async Task<DashboardPlatformServiceObservation> ObserveNpmAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken ct)
    {
        try
        {
            var readiness = await _npmReadinessService.GetReadinessAsync(ct);

            var state = !readiness.ContainerExists
                ? DashboardStates.NotDeployed
                : !readiness.ContainerRunning
                    ? DashboardStates.Stopped
                    : IsReady(readiness)
                        ? DashboardStates.Running
                        : DashboardStates.Degraded;

            return new DashboardPlatformServiceObservation(
                "npm_ingress",
                DashboardStates.Required,
                state,
                observedAtUtc);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new DashboardPlatformServiceObservation(
                "npm_ingress",
                DashboardStates.Required,
                DashboardStates.Unknown,
                observedAtUtc);
        }
    }

    private (DashboardHostDiskObservation? Disk, string? UnavailableReasonCode) ObserveMemDataDisk()
    {
        if (!string.Equals(
                _runtimeContext.RuntimeMode,
                MemRuntimeModes.LocalDevelopment,
                StringComparison.Ordinal) &&
            !MemRuntimeModes.IsContainerized(_runtimeContext.RuntimeMode))
        {
            return (null, DashboardStates.MemDataStorageUnavailable);
        }

        try
        {
            var memDataRoot = MemDataRootResolver.Resolve(_configuration);
            if (!Directory.Exists(memDataRoot))
            {
                return (null, DashboardStates.MemDataStorageUnavailable);
            }

            var drives = DriveInfo.GetDrives()
                .Select(TryObserveDrive)
                .Where(observation => observation is not null)
                .Cast<DashboardFilesystemDriveObservation>()
                .ToArray();

            return ProjectMemDataDisk(
                _runtimeContext.RuntimeMode,
                memDataRoot,
                drives);
        }
        catch
        {
            return (null, DashboardStates.MemDataStorageUnavailable);
        }
    }

    internal static (DashboardHostDiskObservation? Disk, string? UnavailableReasonCode) ProjectMemDataDisk(
        string runtimeMode,
        string memDataRoot,
        IReadOnlyList<DashboardFilesystemDriveObservation> drives)
    {
        if (string.IsNullOrWhiteSpace(memDataRoot) ||
            (!string.Equals(
                 runtimeMode,
                 MemRuntimeModes.LocalDevelopment,
                 StringComparison.Ordinal) &&
             !MemRuntimeModes.IsContainerized(runtimeMode)))
        {
            return (null, DashboardStates.MemDataStorageUnavailable);
        }

        var normalizedDataRoot = NormalizeFilesystemPath(memDataRoot);
        var filesystemRoot = Path.GetPathRoot(normalizedDataRoot);
        var candidate = drives
            .Where(drive =>
                drive.TotalBytes > 0 &&
                drive.AvailableBytes >= 0 &&
                drive.AvailableBytes <= drive.TotalBytes)
            .Select(drive => new
            {
                Drive = drive,
                Root = NormalizeFilesystemPath(drive.RootPath)
            })
            .Where(item => IsSameOrDescendant(normalizedDataRoot, item.Root))
            .OrderByDescending(item => item.Root.Length)
            .FirstOrDefault();

        if (candidate is null)
        {
            return (null, DashboardStates.MemDataStorageUnavailable);
        }

        if (MemRuntimeModes.IsContainerized(runtimeMode) &&
            !string.IsNullOrWhiteSpace(filesystemRoot) &&
            string.Equals(
                candidate.Root,
                NormalizeFilesystemPath(filesystemRoot),
                PathComparison))
        {
            // Containerized MEM requires identity-mounted host-data roots. If the
            // only visible filesystem is the container root, fail closed rather
            // than presenting overlay capacity as host-backed MEM data capacity.
            return (null, DashboardStates.MemDataStorageUnavailable);
        }

        return (
            new DashboardHostDiskObservation(
                UsedBytes: Math.Max(0, candidate.Drive.TotalBytes - candidate.Drive.AvailableBytes),
                TotalBytes: candidate.Drive.TotalBytes,
                Scope: "mem_data"),
            null);
    }

    private static DashboardFilesystemDriveObservation? TryObserveDrive(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady || drive.TotalSize <= 0)
            {
                return null;
            }

            return new DashboardFilesystemDriveObservation(
                drive.RootDirectory.FullName,
                drive.TotalSize,
                drive.AvailableFreeSpace);
        }
        catch
        {
            return null;
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string NormalizeFilesystemPath(string path)
    {
        var fullPath = Path.GetFullPath(path.Trim());
        var root = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrWhiteSpace(root) &&
            string.Equals(fullPath, root, PathComparison))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    private static bool IsSameOrDescendant(string path, string root)
    {
        if (string.Equals(path, root, PathComparison))
        {
            return true;
        }

        var prefix = root.EndsWith(
            Path.DirectorySeparatorChar.ToString(),
            PathComparison)
            ? root
            : root + Path.DirectorySeparatorChar;

        return path.StartsWith(prefix, PathComparison);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static long? PositiveOrNull(long value) => value > 0 ? value : null;

    private static long? NonNegativeOrNull(long value) => value >= 0 ? value : null;

    private static bool IsReady(NpmReadinessResponse readiness) =>
        readiness.ContainerExists &&
        readiness.ContainerRunning &&
        readiness.AdminUiReachable &&
        readiness.Initialized &&
        readiness.ApiAuthenticated &&
        readiness.CertificateApiReachable &&
        string.Equals(
            readiness.RecommendedAction,
            "ready",
            StringComparison.OrdinalIgnoreCase);

    private static string ClassifyIngress(NpmReadinessResponse readiness)
    {
        if (!readiness.ContainerExists || !readiness.ContainerRunning)
        {
            return DashboardStates.Unavailable;
        }

        return IsReady(readiness)
            ? DashboardStates.Ready
            : DashboardStates.Degraded;
    }
}
