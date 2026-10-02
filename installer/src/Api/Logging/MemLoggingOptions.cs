namespace Api.Logging;

/// <summary>
/// Controls the mandatory MEM control-plane logging foundation. These options
/// configure console output and the local rolling black-box recorder only;
/// browser-safe diagnostic projection is delivered by later slices.
/// </summary>
public sealed class MemLoggingOptions
{
    public const string SectionName = "Diagnostics:Logging";

    public bool PersistentFileEnabled { get; set; } = true;

    /// <summary>
    /// Serilog rolling-file path. A relative path is resolved from the
    /// installer root in development and from the API content root in a
    /// published/container deployment.
    /// </summary>
    public string FilePath { get; set; } = "/data/logs/control-plane/mem-control-plane-.clef";

    public int FileSizeLimitMiB { get; set; } = 25;

    public int RetainedFileCountLimit { get; set; } = 28;

    public int FlushIntervalMilliseconds { get; set; } = 1000;

    public int LowDiskWarningMiB { get; set; } = 1024;

    public int CriticalDiskWarningMiB { get; set; } = 256;

    public string CorrelationHeaderName { get; set; } = "X-Correlation-ID";
}
