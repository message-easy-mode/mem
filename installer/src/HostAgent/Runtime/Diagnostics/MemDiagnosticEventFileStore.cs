using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Diagnostics;

public sealed class MemDiagnosticEventFileStore
{
    private static readonly byte[] NewLine = "\n"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly MemDiagnosticsOptions _options;
    private readonly MemDiagnosticRedactor _redactor;
    private readonly MemDiagnosticCursorCodec _cursorCodec;
    private readonly MemDiagnosticHealthState _health;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MemDiagnosticEventFileStore> _logger;
    private readonly SemaphoreSlim _appendGate = new(1, 1);
    private readonly object _activePathGate = new();
    private string? _currentActiveFilePath;

    public MemDiagnosticEventFileStore(
        MemDiagnosticsOptions options,
        IHostEnvironment environment,
        MemDiagnosticRedactor redactor,
        MemDiagnosticCursorCodec cursorCodec,
        MemDiagnosticHealthState health,
        TimeProvider timeProvider,
        ILogger<MemDiagnosticEventFileStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(environment);
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _cursorCodec = cursorCodec ?? throw new ArgumentNullException(nameof(cursorCodec));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        RootPath = Path.IsPathRooted(options.RootPath)
            ? Path.GetFullPath(options.RootPath)
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.RootPath));
        EventsRootPath = Path.Combine(RootPath, "events");
    }

    internal string RootPath { get; }

    internal string EventsRootPath { get; }

    internal string? CurrentActiveFilePath
    {
        get
        {
            lock (_activePathGate)
            {
                return _currentActiveFilePath;
            }
        }
    }

    internal async Task AppendAsync(
        MemDiagnosticEvent @event,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(@event, JsonOptions);
        if (payload.Length + NewLine.Length > _options.MaximumEventBytes)
        {
            throw new InvalidOperationException(
                "The diagnostic event exceeded the configured maximum event size.");
        }

        await _appendGate.WaitAsync(cancellationToken);
        try
        {
            var eventPath = SelectAppendPath(
                @event.TimestampUtc,
                payload.Length + NewLine.Length);
            var directory = Path.GetDirectoryName(eventPath)
                ?? throw new InvalidOperationException(
                    "The diagnostic event path has no parent directory.");
            Directory.CreateDirectory(directory);

            await using var stream = new FileStream(
                eventPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 16 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            await stream.WriteAsync(payload, cancellationToken);
            await stream.WriteAsync(NewLine, cancellationToken);

            if (MemDiagnosticSeverities.RequiresImmediateFlush(@event.Severity))
            {
                await stream.FlushAsync(cancellationToken);
            }

            lock (_activePathGate)
            {
                _currentActiveFilePath = eventPath;
            }
        }
        finally
        {
            _appendGate.Release();
        }
    }

    internal async Task<MemDiagnosticEventPage> QueryAsync(
        MemDiagnosticQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var normalized = NormalizeQuery(query);
        var cursor = string.IsNullOrWhiteSpace(query.Cursor)
            ? null
            : _cursorCodec.Decode(query.Cursor);
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var positionedEvents = new List<PositionedEvent>(normalized.PageSize + 1);
        var candidates = EnumerateCandidateFiles(
                normalized.FromUtc,
                normalized.UntilUtc)
            .Select(path => new CandidateFile(path, GetFileKey(path)))
            .ToArray();

        var startFileIndex = 0;
        if (cursor is not null)
        {
            startFileIndex = Array.FindIndex(
                candidates,
                candidate => string.Equals(
                    candidate.FileKey,
                    cursor.FileKey,
                    StringComparison.Ordinal));
            if (startFileIndex < 0)
            {
                throw new MemDiagnosticCursorException();
            }
        }

        for (var fileIndex = startFileIndex; fileIndex < candidates.Length; fileIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[fileIndex];
            IReadOnlyList<string> lines;
            try
            {
                lines = await ReadBoundedLinesAsync(
                    candidate.Path,
                    cancellationToken);
            }
            catch (IOException ex)
            {
                if (cursor is not null && fileIndex == startFileIndex)
                {
                    throw new MemDiagnosticCursorException(ex);
                }

                _logger.LogWarning(
                    ex,
                    "Could not read diagnostic event file {DiagnosticFileName}",
                    Path.GetFileName(candidate.Path));
                warnings.Add(MemDiagnosticCodes.StoreReadFailed);
                _health.RecordReadWarning(MemDiagnosticCodes.StoreReadFailed);
                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                if (cursor is not null && fileIndex == startFileIndex)
                {
                    throw new MemDiagnosticCursorException(ex);
                }

                _logger.LogWarning(
                    ex,
                    "Could not access diagnostic event file {DiagnosticFileName}",
                    Path.GetFileName(candidate.Path));
                warnings.Add(MemDiagnosticCodes.StoreReadFailed);
                _health.RecordReadWarning(MemDiagnosticCodes.StoreReadFailed);
                continue;
            }

            var firstLineIndex = lines.Count - 1;
            if (cursor is not null && fileIndex == startFileIndex)
            {
                if (cursor.LineIndex >= lines.Count ||
                    !TryParseSafeEvent(lines[cursor.LineIndex], out var cursorEvent) ||
                    !string.Equals(
                        cursorEvent.EventId,
                        cursor.EventId,
                        StringComparison.Ordinal))
                {
                    throw new MemDiagnosticCursorException();
                }

                firstLineIndex = cursor.LineIndex - 1;
            }

            for (var lineIndex = firstLineIndex; lineIndex >= 0; lineIndex--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = lines[lineIndex];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (!TryParseSafeEvent(line, out var safeEvent))
                {
                    warnings.Add(MemDiagnosticCodes.EventMalformed);
                    _health.RecordReadWarning(
                        MemDiagnosticCodes.EventMalformed,
                        malformedLineCount: 1);
                    continue;
                }

                if (safeEvent.TimestampUtc < normalized.FromUtc ||
                    safeEvent.TimestampUtc > normalized.UntilUtc ||
                    !Matches(safeEvent, normalized))
                {
                    continue;
                }

                positionedEvents.Add(new PositionedEvent(
                    safeEvent,
                    candidate.FileKey,
                    lineIndex));
                if (positionedEvents.Count > normalized.PageSize)
                {
                    break;
                }
            }

            if (positionedEvents.Count > normalized.PageSize)
            {
                break;
            }
        }

        var hasNextPage = positionedEvents.Count > normalized.PageSize;
        var returned = positionedEvents
            .Take(normalized.PageSize)
            .ToArray();
        var nextCursor = hasNextPage && returned.Length > 0
            ? _cursorCodec.Encode(new MemDiagnosticCursorPosition(
                returned[^1].FileKey,
                returned[^1].LineIndex,
                returned[^1].Event.EventId))
            : null;

        _health.RecordReadSuccess(_timeProvider.GetUtcNow());
        return new MemDiagnosticEventPage(
            FromUtc: normalized.FromUtc,
            UntilUtc: normalized.UntilUtc,
            PageSize: normalized.PageSize,
            WindowClamped: normalized.WindowClamped,
            Events: returned.Select(item => item.Event).ToArray(),
            NextCursor: nextCursor,
            Warnings: warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    internal MemDiagnosticEventPage CreateEmptyPage(
        MemDiagnosticQuery query,
        params string[] warnings)
    {
        ArgumentNullException.ThrowIfNull(query);
        var normalized = NormalizeQuery(query);
        return new MemDiagnosticEventPage(
            FromUtc: normalized.FromUtc,
            UntilUtc: normalized.UntilUtc,
            PageSize: normalized.PageSize,
            WindowClamped: normalized.WindowClamped,
            Events: Array.Empty<MemDiagnosticEvent>(),
            NextCursor: null,
            Warnings: warnings
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
    }

    internal async Task<T> ExecuteExclusiveAsync<T>(
        Func<T> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        await _appendGate.WaitAsync(cancellationToken);
        try
        {
            return action();
        }
        finally
        {
            _appendGate.Release();
        }
    }

    internal IReadOnlyList<string> EnumerateAllEventFiles() =>
        !Directory.Exists(EventsRootPath)
            ? Array.Empty<string>()
            : Directory.EnumerateFiles(
                    EventsRootPath,
                    "*.ndjson",
                    SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

    private string SelectAppendPath(DateTimeOffset timestampUtc, int appendBytes)
    {
        var utc = timestampUtc.ToUniversalTime();
        var directory = Path.Combine(
            EventsRootPath,
            utc.ToString("yyyy"),
            utc.ToString("MM"),
            utc.ToString("dd"));

        for (var sequence = 0; sequence <= 999; sequence++)
        {
            var candidate = Path.Combine(
                directory,
                $"{utc:HH}-{sequence:000}.ndjson");
            var currentLength = File.Exists(candidate)
                ? new FileInfo(candidate).Length
                : 0L;
            if (currentLength + appendBytes <= _options.EventFileMaximumBytes)
            {
                return candidate;
            }
        }

        throw new IOException(
            "The diagnostic event store exhausted the hourly rollover sequence.");
    }

    private IEnumerable<string> EnumerateCandidateFiles(
        DateTimeOffset fromUtc,
        DateTimeOffset untilUtc)
    {
        var fromDate = fromUtc.UtcDateTime.Date;
        var untilDate = untilUtc.UtcDateTime.Date;
        for (var date = untilDate; date >= fromDate; date = date.AddDays(-1))
        {
            var directory = Path.Combine(
                EventsRootPath,
                date.ToString("yyyy"),
                date.ToString("MM"),
                date.ToString("dd"));
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory
                         .EnumerateFiles(directory, "*.ndjson", SearchOption.TopDirectoryOnly)
                         .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal))
            {
                if (FileIntersectsWindow(file, date, fromUtc, untilUtc))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool FileIntersectsWindow(
        string file,
        DateTime date,
        DateTimeOffset fromUtc,
        DateTimeOffset untilUtc)
    {
        var fileName = Path.GetFileName(file);
        if (fileName.Length < 2 ||
            !int.TryParse(fileName.AsSpan(0, 2), out var hour) ||
            hour is < 0 or > 23)
        {
            return false;
        }

        var start = new DateTimeOffset(
            date.Year,
            date.Month,
            date.Day,
            hour,
            0,
            0,
            TimeSpan.Zero);
        var end = start.AddHours(1);
        return end >= fromUtc && start <= untilUtc;
    }

    private async Task<IReadOnlyList<string>> ReadBoundedLinesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(path);
        var maximumReadableBytes = _options.EventFileMaximumBytes + _options.MaximumEventBytes;
        if (fileInfo.Length > maximumReadableBytes)
        {
            throw new IOException(
                "The diagnostic event file exceeds the configured read boundary.");
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        return content.Split('\n');
    }

    private NormalizedQuery NormalizeQuery(MemDiagnosticQuery query)
    {
        var now = _timeProvider.GetUtcNow();
        var until = query.UntilUtc?.ToUniversalTime() ?? now;
        var windowClamped = false;
        if (until > now)
        {
            until = now;
            windowClamped = true;
        }

        var requestedFrom = query.FromUtc?.ToUniversalTime()
            ?? until.AddHours(-_options.DefaultQueryWindowHours);
        if (requestedFrom > until)
        {
            throw new ArgumentException(
                "Diagnostic query FromUtc cannot be later than UntilUtc.",
                nameof(query));
        }

        var earliestAllowed = until.AddHours(-_options.MaximumQueryWindowHours);
        var from = requestedFrom < earliestAllowed
            ? earliestAllowed
            : requestedFrom;
        windowClamped |= from != requestedFrom;

        var pageSize = Math.Clamp(
            query.PageSize ?? _options.DefaultPageSize,
            1,
            _options.MaximumPageSize);
        var severity = NormalizeFilter(query.Severity, 32);
        if (severity is not null && !MemDiagnosticSeverities.IsKnown(severity))
        {
            throw new ArgumentException(
                "Diagnostic query severity is not supported.",
                nameof(query));
        }

        return new NormalizedQuery(
            FromUtc: from,
            UntilUtc: until,
            PageSize: pageSize,
            WindowClamped: windowClamped,
            Severity: severity,
            Source: NormalizeFilter(query.Source, 100),
            Feature: NormalizeFilter(query.Feature, 100),
            Stage: NormalizeFilter(query.Stage, 120),
            EventCode: NormalizeFilter(query.EventCode, 200),
            IncidentId: NormalizeFilter(query.IncidentId, 80),
            TraceId: NormalizeFilter(query.TraceId, 80),
            OperationId: query.OperationId,
            StackId: NormalizeFilter(query.StackId, 100),
            EventId: NormalizeFilter(query.EventId, 80),
            ResourceKind: NormalizeFilter(query.ResourceKind, 80),
            Search: NormalizeSearch(query.Search),
            RequireIncidentId: query.RequireIncidentId);
    }

    private static bool Matches(
        MemDiagnosticEvent @event,
        NormalizedQuery query)
    {
        if ((query.RequireIncidentId && string.IsNullOrWhiteSpace(@event.IncidentId)) ||
            !EqualsOptional(@event.Severity, query.Severity) ||
            !EqualsOptional(@event.Source, query.Source) ||
            !EqualsOptional(@event.Feature, query.Feature) ||
            !EqualsOptional(@event.Stage, query.Stage) ||
            !EqualsOptional(@event.EventCode, query.EventCode) ||
            !EqualsOptional(@event.IncidentId, query.IncidentId) ||
            !EqualsOptional(@event.TraceId, query.TraceId) ||
            !EqualsOptional(@event.EventId, query.EventId) ||
            !EqualsOptional(@event.Resource?.Kind, query.ResourceKind) ||
            (query.OperationId.HasValue && @event.OperationId != query.OperationId) ||
            (query.StackId is not null && !string.Equals(
                @event.Resource?.StackId,
                query.StackId,
                StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (query.Search is null)
        {
            return true;
        }

        return Contains(@event.EventCode, query.Search) ||
               Contains(@event.Message, query.Search) ||
               Contains(@event.Source, query.Search) ||
               Contains(@event.Feature, query.Search) ||
               Contains(@event.Stage, query.Search) ||
               Contains(@event.IncidentId, query.Search) ||
               Contains(@event.TraceId, query.Search) ||
               Contains(@event.OperationId?.ToString(), query.Search) ||
               Contains(@event.Resource?.Id, query.Search) ||
               Contains(@event.Resource?.StackId, query.Search) ||
               Contains(@event.Resource?.StackSlug, query.Search) ||
               Contains(@event.Resource?.Service, query.Search);
    }

    private bool TryParseSafeEvent(
        string line,
        out MemDiagnosticEvent safeEvent)
    {
        MemDiagnosticEvent? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<MemDiagnosticEvent>(line, JsonOptions);
        }
        catch (JsonException)
        {
            parsed = null;
        }

        if (!IsValid(parsed))
        {
            safeEvent = null!;
            return false;
        }

        var redacted = _redactor.RedactStoredEvent(parsed!);
        if (!IsValid(redacted))
        {
            safeEvent = null!;
            return false;
        }

        safeEvent = redacted;
        return true;
    }

    private string GetFileKey(string path) =>
        Path.GetRelativePath(EventsRootPath, path)
            .Replace('\\', '/');

    private static bool IsValid(MemDiagnosticEvent? @event) =>
        @event is not null &&
        @event.SchemaVersion == 1 &&
        !string.IsNullOrWhiteSpace(@event.EventId) &&
        @event.EventId.Length is >= 12 and <= 80 &&
        @event.EventId.StartsWith("evt_", StringComparison.Ordinal) &&
        MemDiagnosticSeverities.IsKnown(@event.Severity) &&
        !string.IsNullOrWhiteSpace(@event.EventCode) &&
        !string.IsNullOrWhiteSpace(@event.Source) &&
        !string.IsNullOrWhiteSpace(@event.Feature) &&
        @event.Message is not null;

    private static string? NormalizeFilter(string? value, int maximumCharacters)
    {
        var normalized = MemDiagnosticRedactor.NormalizeIdentifier(
            value,
            maximumCharacters);
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized.ToLowerInvariant();
    }

    private static string? NormalizeSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var characters = value.Trim()
            .Where(character => !char.IsControl(character))
            .Take(200)
            .ToArray();
        var normalized = new string(characters);
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized;
    }

    private static bool EqualsOptional(string? actual, string? expected) =>
        expected is null || string.Equals(
            actual,
            expected,
            StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private sealed record CandidateFile(
        string Path,
        string FileKey);

    private sealed record PositionedEvent(
        MemDiagnosticEvent Event,
        string FileKey,
        int LineIndex);

    private sealed record NormalizedQuery(
        DateTimeOffset FromUtc,
        DateTimeOffset UntilUtc,
        int PageSize,
        bool WindowClamped,
        string? Severity,
        string? Source,
        string? Feature,
        string? Stage,
        string? EventCode,
        string? IncidentId,
        string? TraceId,
        Guid? OperationId,
        string? StackId,
        string? EventId,
        string? ResourceKind,
        string? Search,
        bool RequireIncidentId);
}
