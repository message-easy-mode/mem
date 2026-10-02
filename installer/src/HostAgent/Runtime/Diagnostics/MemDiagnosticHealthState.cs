using Microsoft.Extensions.Hosting;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Diagnostics;

public sealed class MemDiagnosticHealthState : IMemDiagnosticHealthReader
{
    private const string StoreDiskLowCode = "diagnostics.safe_event_store_disk_low";
    private const string StoreDiskCriticalCode = "diagnostics.safe_event_store_disk_critical";
    private const string StoreDiskUnknownCode = "diagnostics.safe_event_store_disk_unknown";

    private readonly object _gate = new();
    private readonly MemDiagnosticsOptions _options;
    private readonly string _rootPath;
    private readonly string _eventsRootPath;
    private readonly IMemStorageCapacityProbe _storageCapacityProbe;
    private DateTimeOffset? _lastWriteAtUtc;
    private DateTimeOffset? _lastReadAtUtc;
    private long _storedEventCount;
    private long _droppedEventCount;
    private long _malformedLineCount;
    private string? _lastWriteErrorCode;
    private string? _lastReadWarningCode;
    private DateTimeOffset? _lastRetentionRunAtUtc;
    private int _lastRetentionDeletedFileCount;
    private long _lastRetentionDeletedBytes;
    private string? _lastRetentionErrorCode;

    public MemDiagnosticHealthState(MemDiagnosticsOptions options)
        : this(
            options,
            environment: null,
            storageCapacityProbe: null)
    {
    }

    public MemDiagnosticHealthState(
        MemDiagnosticsOptions options,
        IHostEnvironment? environment,
        IMemStorageCapacityProbe? storageCapacityProbe)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _storageCapacityProbe = storageCapacityProbe ?? new MemStorageCapacityProbe();
        _rootPath = Path.IsPathRooted(options.RootPath)
            ? Path.GetFullPath(options.RootPath)
            : Path.GetFullPath(Path.Combine(
                environment?.ContentRootPath ?? Directory.GetCurrentDirectory(),
                options.RootPath));
        _eventsRootPath = Path.Combine(_rootPath, "events");
    }

    internal void RecordWriteSuccess(DateTimeOffset timestampUtc)
    {
        lock (_gate)
        {
            _lastWriteAtUtc = timestampUtc;
            _storedEventCount++;
            _lastWriteErrorCode = null;
        }
    }

    internal void RecordWriteFailure(string errorCode)
    {
        lock (_gate)
        {
            _droppedEventCount++;
            _lastWriteErrorCode = errorCode;
        }
    }

    internal void RecordReadSuccess(DateTimeOffset timestampUtc)
    {
        lock (_gate)
        {
            _lastReadAtUtc = timestampUtc;
        }
    }

    internal void RecordReadWarning(string warningCode, long malformedLineCount = 0)
    {
        lock (_gate)
        {
            _lastReadWarningCode = warningCode;
            _malformedLineCount += malformedLineCount;
        }
    }

    internal void RecordRetentionSuccess(
        DateTimeOffset timestampUtc,
        int deletedFileCount,
        long deletedBytes)
    {
        lock (_gate)
        {
            _lastRetentionRunAtUtc = timestampUtc;
            _lastRetentionDeletedFileCount = deletedFileCount;
            _lastRetentionDeletedBytes = deletedBytes;
            _lastRetentionErrorCode = null;
        }
    }

    internal void RecordRetentionFailure(DateTimeOffset timestampUtc, string errorCode)
    {
        lock (_gate)
        {
            _lastRetentionRunAtUtc = timestampUtc;
            _lastRetentionErrorCode = errorCode;
        }
    }

    public MemDiagnosticStoreHealth GetHealth()
    {
        var storage = !_options.Enabled
            ? null
            : _storageCapacityProbe.GetHealth(
                _rootPath,
                _options.LowDiskWarningBytes,
                _options.CriticalDiskWarningBytes,
                StoreDiskLowCode,
                StoreDiskCriticalCode,
                StoreDiskUnknownCode);
        var activity = _options.Enabled
            ? InspectStoredActivity()
            : StoredActivity.None;

        lock (_gate)
        {
            var readWarningCode = _lastReadWarningCode ?? activity.WarningCode;
            var status = !_options.Enabled
                ? "disabled"
                : _lastWriteErrorCode is not null ||
                  readWarningCode is not null ||
                  _lastRetentionErrorCode is not null ||
                  storage?.WarningCode is not null
                    ? "degraded"
                    : "ready";
            var lastWriteAtUtc = Latest(_lastWriteAtUtc, activity.LastWriteAtUtc);

            return new MemDiagnosticStoreHealth(
                Enabled: _options.Enabled,
                Status: status,
                LastWriteAtUtc: lastWriteAtUtc,
                LastReadAtUtc: _lastReadAtUtc,
                StoredEventCount: _storedEventCount,
                DroppedEventCount: _droppedEventCount,
                MalformedLineCount: _malformedLineCount,
                LastWriteErrorCode: _lastWriteErrorCode,
                LastReadWarningCode: readWarningCode,
                LastRetentionRunAtUtc: _lastRetentionRunAtUtc,
                LastRetentionDeletedFileCount: _lastRetentionDeletedFileCount,
                LastRetentionDeletedBytes: _lastRetentionDeletedBytes,
                LastRetentionErrorCode: _lastRetentionErrorCode,
                Storage: storage,
                HasEverRecordedEvent: _storedEventCount > 0 || activity.HasRecordedEvent);
        }
    }

    private StoredActivity InspectStoredActivity()
    {
        try
        {
            if (!Directory.Exists(_eventsRootPath))
            {
                return StoredActivity.None;
            }

            DateTimeOffset? latestWriteAtUtc = null;
            var hasRecordedEvent = false;
            foreach (var path in Directory.EnumerateFiles(
                         _eventsRootPath,
                         "*.ndjson",
                         SearchOption.AllDirectories))
            {
                var file = new FileInfo(path);
                if (file.Length <= 0)
                {
                    continue;
                }

                hasRecordedEvent = true;
                var observedWriteAtUtc = new DateTimeOffset(
                    DateTime.SpecifyKind(file.LastWriteTimeUtc, DateTimeKind.Utc));
                latestWriteAtUtc = Latest(latestWriteAtUtc, observedWriteAtUtc);
            }

            return new StoredActivity(
                hasRecordedEvent,
                latestWriteAtUtc,
                WarningCode: null);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException or
                System.Security.SecurityException)
        {
            return new StoredActivity(
                HasRecordedEvent: false,
                LastWriteAtUtc: null,
                WarningCode: MemDiagnosticCodes.StoreReadFailed);
        }
    }

    private static DateTimeOffset? Latest(
        DateTimeOffset? first,
        DateTimeOffset? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return first >= second ? first : second;
    }

    private sealed record StoredActivity(
        bool HasRecordedEvent,
        DateTimeOffset? LastWriteAtUtc,
        string? WarningCode)
    {
        public static StoredActivity None { get; } = new(
            HasRecordedEvent: false,
            LastWriteAtUtc: null,
            WarningCode: null);
    }
}
