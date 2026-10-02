namespace HostAgent.Runtime.Diagnostics;

public static class MemDiagnosticsOptionsValidator
{
    public static void ThrowIfInvalid(MemDiagnosticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Enabled && string.IsNullOrWhiteSpace(options.RootPath))
        {
            throw new InvalidOperationException(
                $"{MemDiagnosticsOptions.SectionName}:RootPath is required when safe events are enabled.");
        }

        if (options.Enabled)
        {
            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(options.RootPath);
            }
            catch (Exception ex) when (
                ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new InvalidOperationException(
                    $"{MemDiagnosticsOptions.SectionName}:RootPath is invalid.",
                    ex);
            }

            var volumeRoot = Path.GetPathRoot(fullRoot);
            if (!string.IsNullOrWhiteSpace(volumeRoot) &&
                string.Equals(
                    fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    volumeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{MemDiagnosticsOptions.SectionName}:RootPath cannot be a filesystem root.");
            }
        }

        RequireRange(options.RetentionDays, 1, 365, nameof(options.RetentionDays));
        RequireRange(options.MaximumTotalBytes, 1024L * 1024L, 10L * 1024L * 1024L * 1024L, nameof(options.MaximumTotalBytes));
        RequireRange(options.EventFileMaximumBytes, 64L * 1024L, 512L * 1024L * 1024L, nameof(options.EventFileMaximumBytes));
        RequireRange(options.MaximumEventBytes, 4096, 1024 * 1024, nameof(options.MaximumEventBytes));
        RequireRange(options.DefaultQueryWindowHours, 1, 24 * 30, nameof(options.DefaultQueryWindowHours));
        RequireRange(options.MaximumQueryWindowHours, options.DefaultQueryWindowHours, 24 * 90, nameof(options.MaximumQueryWindowHours));
        RequireRange(options.DefaultPageSize, 1, 1000, nameof(options.DefaultPageSize));
        RequireRange(options.MaximumPageSize, options.DefaultPageSize, 2000, nameof(options.MaximumPageSize));
        RequireRange(options.MaximumDetailCharacters, 100, 10000, nameof(options.MaximumDetailCharacters));
        RequireRange(options.MaximumStackTraceCharacters, 500, 100000, nameof(options.MaximumStackTraceCharacters));
        RequireRange(options.MaximumExceptionDepth, 1, 20, nameof(options.MaximumExceptionDepth));
        RequireRange(options.RetentionIntervalMinutes, 5, 24 * 60, nameof(options.RetentionIntervalMinutes));
        RequireRange(options.LowDiskWarningBytes, 64L * 1024L * 1024L, 1024L * 1024L * 1024L * 1024L, nameof(options.LowDiskWarningBytes));
        RequireRange(options.CriticalDiskWarningBytes, 32L * 1024L * 1024L, 1024L * 1024L * 1024L * 1024L, nameof(options.CriticalDiskWarningBytes));

        if (options.CriticalDiskWarningBytes > options.LowDiskWarningBytes)
        {
            throw new InvalidOperationException(
                $"{MemDiagnosticsOptions.SectionName}:CriticalDiskWarningBytes cannot exceed LowDiskWarningBytes.");
        }

        if (options.MaximumEventBytes > options.EventFileMaximumBytes)
        {
            throw new InvalidOperationException(
                $"{MemDiagnosticsOptions.SectionName}:MaximumEventBytes cannot exceed EventFileMaximumBytes.");
        }

        if (options.EventFileMaximumBytes > options.MaximumTotalBytes)
        {
            throw new InvalidOperationException(
                $"{MemDiagnosticsOptions.SectionName}:EventFileMaximumBytes cannot exceed MaximumTotalBytes.");
        }
    }

    private static void RequireRange(long value, long minimum, long maximum, string name)
    {
        if (value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"{MemDiagnosticsOptions.SectionName}:{name} must be between {minimum} and {maximum}.");
        }
    }
}
