using Shared.Diagnostics;

namespace Api.Logging;

public sealed class MemLocalLogHealthState : IMemLocalLogHealthReader
{
    private const string RecorderUnavailableCode = "diagnostics.local_recorder_unavailable";
    private const string RecorderReadFailedCode = "diagnostics.local_recorder_health_failed";
    private const string RecorderDiskLowCode = "diagnostics.local_recorder_disk_low";
    private const string RecorderDiskCriticalCode = "diagnostics.local_recorder_disk_critical";
    private const string RecorderDiskUnknownCode = "diagnostics.local_recorder_disk_unknown";

    private readonly MemLoggingOptions _options;
    private readonly string? _persistentFilePath;
    private readonly string? _persistentRecorderWarning;
    private readonly MemSerilogSelfLogState _selfLogState;
    private readonly IMemStorageCapacityProbe _storageCapacityProbe;

    public MemLocalLogHealthState(
        MemLoggingOptions options,
        string? persistentFilePath,
        string? persistentRecorderWarning,
        MemSerilogSelfLogState selfLogState,
        IMemStorageCapacityProbe? storageCapacityProbe = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _persistentFilePath = persistentFilePath;
        _persistentRecorderWarning = persistentRecorderWarning;
        _selfLogState = selfLogState ?? throw new ArgumentNullException(nameof(selfLogState));
        _storageCapacityProbe = storageCapacityProbe ?? new MemStorageCapacityProbe();
    }

    public MemLocalLogHealth GetHealth()
    {
        var selfLogCount = _selfLogState.Snapshot().Count;
        if (!_options.PersistentFileEnabled)
        {
            return new MemLocalLogHealth(
                Enabled: true,
                Status: selfLogCount > 0 ? "degraded" : "console-only",
                PersistentRecorderConfigured: false,
                PersistentRecorderActive: false,
                PersistentFilePath: null,
                LastFileWriteAtUtc: null,
                RetainedFileCount: 0,
                RetainedBytes: 0,
                SerilogSelfLogMessageCount: selfLogCount,
                WarningCode: selfLogCount > 0 ? RecorderReadFailedCode : null,
                Storage: null);
        }

        if (string.IsNullOrWhiteSpace(_persistentFilePath) ||
            !string.IsNullOrWhiteSpace(_persistentRecorderWarning))
        {
            return new MemLocalLogHealth(
                Enabled: true,
                Status: "degraded",
                PersistentRecorderConfigured: true,
                PersistentRecorderActive: false,
                PersistentFilePath: _persistentFilePath,
                LastFileWriteAtUtc: null,
                RetainedFileCount: 0,
                RetainedBytes: 0,
                SerilogSelfLogMessageCount: selfLogCount,
                WarningCode: RecorderUnavailableCode,
                Storage: null);
        }

        var storage = _storageCapacityProbe.GetHealth(
            _persistentFilePath,
            _options.LowDiskWarningMiB * 1024L * 1024L,
            _options.CriticalDiskWarningMiB * 1024L * 1024L,
            RecorderDiskLowCode,
            RecorderDiskCriticalCode,
            RecorderDiskUnknownCode);

        try
        {
            var directory = Path.GetDirectoryName(_persistentFilePath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return new MemLocalLogHealth(
                    Enabled: true,
                    Status: "degraded",
                    PersistentRecorderConfigured: true,
                    PersistentRecorderActive: false,
                    PersistentFilePath: _persistentFilePath,
                    LastFileWriteAtUtc: null,
                    RetainedFileCount: 0,
                    RetainedBytes: 0,
                    SerilogSelfLogMessageCount: selfLogCount,
                    WarningCode: RecorderUnavailableCode,
                    Storage: storage);
            }

            var files = Directory
                .EnumerateFiles(directory, "*.clef", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(Math.Max(1, _options.RetainedFileCountLimit + 2))
                .ToArray();

            DateTimeOffset? lastWrite = files.Length == 0
                ? null
                : new DateTimeOffset(
                    DateTime.SpecifyKind(files[0].LastWriteTimeUtc, DateTimeKind.Utc));
            var warning = selfLogCount > 0
                ? RecorderReadFailedCode
                : storage.WarningCode;

            return new MemLocalLogHealth(
                Enabled: true,
                Status: warning is null ? "ready" : "degraded",
                PersistentRecorderConfigured: true,
                PersistentRecorderActive: true,
                PersistentFilePath: _persistentFilePath,
                LastFileWriteAtUtc: lastWrite,
                RetainedFileCount: files.Length,
                RetainedBytes: files.Sum(file => file.Length),
                SerilogSelfLogMessageCount: selfLogCount,
                WarningCode: warning,
                Storage: storage);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new MemLocalLogHealth(
                Enabled: true,
                Status: "degraded",
                PersistentRecorderConfigured: true,
                PersistentRecorderActive: false,
                PersistentFilePath: _persistentFilePath,
                LastFileWriteAtUtc: null,
                RetainedFileCount: 0,
                RetainedBytes: 0,
                SerilogSelfLogMessageCount: selfLogCount,
                WarningCode: RecorderReadFailedCode,
                Storage: storage);
        }
    }
}
