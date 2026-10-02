namespace Shared.Diagnostics;

public sealed record MemStorageCapacityHealth(
    string Status,
    long? AvailableBytes,
    long? TotalBytes,
    string? WarningCode);

public interface IMemStorageCapacityProbe
{
    MemStorageCapacityHealth GetHealth(
        string path,
        long lowWarningBytes,
        long criticalWarningBytes,
        string lowWarningCode,
        string criticalWarningCode,
        string unavailableWarningCode);
}

public sealed class MemStorageCapacityProbe : IMemStorageCapacityProbe
{
    public MemStorageCapacityHealth GetHealth(
        string path,
        long lowWarningBytes,
        long criticalWarningBytes,
        string lowWarningCode,
        string criticalWarningCode,
        string unavailableWarningCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(lowWarningCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(criticalWarningCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(unavailableWarningCode);

        if (criticalWarningBytes < 0 || lowWarningBytes < criticalWarningBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(criticalWarningBytes),
                "Storage capacity thresholds are invalid.");
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var existingPath = ResolveExistingPath(fullPath);
            var root = Path.GetPathRoot(existingPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                return Unavailable(unavailableWarningCode);
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return Unavailable(unavailableWarningCode);
            }

            var available = drive.AvailableFreeSpace;
            var total = drive.TotalSize;
            if (available <= criticalWarningBytes)
            {
                return new MemStorageCapacityHealth(
                    "critical",
                    available,
                    total,
                    criticalWarningCode);
            }

            if (available <= lowWarningBytes)
            {
                return new MemStorageCapacityHealth(
                    "low",
                    available,
                    total,
                    lowWarningCode);
            }

            return new MemStorageCapacityHealth(
                "ready",
                available,
                total,
                WarningCode: null);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return Unavailable(unavailableWarningCode);
        }
    }

    private static string ResolveExistingPath(string path)
    {
        var candidate = File.Exists(path)
            ? Path.GetDirectoryName(path)
            : path;

        while (!string.IsNullOrWhiteSpace(candidate) &&
               !Directory.Exists(candidate))
        {
            candidate = Path.GetDirectoryName(candidate);
        }

        return string.IsNullOrWhiteSpace(candidate)
            ? Path.GetPathRoot(path) ?? path
            : candidate;
    }

    private static MemStorageCapacityHealth Unavailable(string warningCode) =>
        new(
            "unknown",
            AvailableBytes: null,
            TotalBytes: null,
            WarningCode: warningCode);
}
