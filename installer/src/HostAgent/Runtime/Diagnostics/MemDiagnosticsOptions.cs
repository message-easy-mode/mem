namespace HostAgent.Runtime.Diagnostics;

public sealed class MemDiagnosticsOptions
{
    public const string SectionName = "Diagnostics:SafeEvents";

    public bool Enabled { get; set; } = true;

    public string RootPath { get; set; } = "/data/diagnostics";

    public int RetentionDays { get; set; } = 14;

    public long MaximumTotalBytes { get; set; } = 256L * 1024L * 1024L;

    public long EventFileMaximumBytes { get; set; } = 8L * 1024L * 1024L;

    public int MaximumEventBytes { get; set; } = 64 * 1024;

    public int DefaultQueryWindowHours { get; set; } = 24;

    public int MaximumQueryWindowHours { get; set; } = 168;

    public int DefaultPageSize { get; set; } = 100;

    public int MaximumPageSize { get; set; } = 200;

    public int MaximumDetailCharacters { get; set; } = 1500;

    public int MaximumStackTraceCharacters { get; set; } = 12000;

    public int MaximumExceptionDepth { get; set; } = 6;

    public int RetentionIntervalMinutes { get; set; } = 360;

    public long LowDiskWarningBytes { get; set; } = 1024L * 1024L * 1024L;

    public long CriticalDiskWarningBytes { get; set; } = 256L * 1024L * 1024L;
}
