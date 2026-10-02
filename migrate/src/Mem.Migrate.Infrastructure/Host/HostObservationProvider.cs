using System.Runtime.InteropServices;
using Mem.Migrate.Core.Assessment;

namespace Mem.Migrate.Infrastructure.Host;

public sealed class HostObservationProvider : IHostObservationProvider
{
    public HostObservation Observe(string workspacePath)
    {
        var fullWorkspace = Path.GetFullPath(workspacePath);
        var root = Path.GetPathRoot(fullWorkspace)
            ?? throw new InvalidOperationException(
                $"Unable to determine the filesystem root for '{fullWorkspace}'.");

        var drive = new DriveInfo(root);

        return new HostObservation(
            OperatingSystem: RuntimeInformation.OSDescription,
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            MachineName: Environment.MachineName,
            CurrentUser: Environment.UserName,
            WorkspacePath: fullWorkspace,
            WorkspaceAvailableBytes: drive.AvailableFreeSpace,
            IsLinux: OperatingSystem.IsLinux(),
            ObservedAtUtc: DateTimeOffset.UtcNow);
    }
}
