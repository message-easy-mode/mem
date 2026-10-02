using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Diagnostics;

public sealed class MemDiagnosticRetentionService : BackgroundService
{
    private readonly MemDiagnosticsOptions _options;
    private readonly MemDiagnosticEventFileStore _store;
    private readonly MemDiagnosticHealthState _health;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MemDiagnosticRetentionService> _logger;

    public MemDiagnosticRetentionService(
        MemDiagnosticsOptions options,
        MemDiagnosticEventFileStore store,
        MemDiagnosticHealthState health,
        TimeProvider timeProvider,
        ILogger<MemDiagnosticRetentionService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);

            try
            {
                await Task.Delay(
                    TimeSpan.FromMinutes(_options.RetentionIntervalMinutes),
                    _timeProvider,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        try
        {
            var result = await _store.ExecuteExclusiveAsync(
                () => ApplyRetention(now, cancellationToken),
                cancellationToken);
            _health.RecordRetentionSuccess(
                now,
                result.DeletedFileCount,
                result.DeletedBytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            _health.RecordRetentionFailure(
                now,
                MemDiagnosticCodes.RetentionFailed);
            _logger.LogWarning(
                ex,
                "MEM diagnostic retention could not complete.");
        }
    }

    private RetentionResult ApplyRetention(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var activePath = _store.CurrentActiveFilePath;
        var files = _store.EnumerateAllEventFiles()
            .Select(path => new FileEntry(
                path,
                File.GetLastWriteTimeUtc(path),
                new FileInfo(path).Length))
            .OrderBy(entry => entry.LastWriteUtc)
            .ThenBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();

        var cutoff = now.UtcDateTime.AddDays(-_options.RetentionDays);
        var deletedFileCount = 0;
        long deletedBytes = 0;

        foreach (var file in files
                     .Where(file => file.LastWriteUtc < cutoff)
                     .ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsActive(file.Path, activePath))
            {
                continue;
            }

            if (TryDelete(file, ref deletedFileCount, ref deletedBytes))
            {
                files.Remove(file);
            }
        }

        var retainedBytes = files.Sum(file => file.Length);
        foreach (var file in files.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (retainedBytes <= _options.MaximumTotalBytes)
            {
                break;
            }

            if (IsActive(file.Path, activePath))
            {
                continue;
            }

            if (TryDelete(file, ref deletedFileCount, ref deletedBytes))
            {
                retainedBytes -= file.Length;
                files.Remove(file);
            }
        }

        RemoveEmptyDirectories(_store.EventsRootPath);
        return new RetentionResult(deletedFileCount, deletedBytes);
    }

    private static bool TryDelete(
        FileEntry file,
        ref int deletedFileCount,
        ref long deletedBytes)
    {
        if (!File.Exists(file.Path))
        {
            return true;
        }

        File.Delete(file.Path);
        deletedFileCount++;
        deletedBytes += file.Length;
        return true;
    }

    private static bool IsActive(string path, string? activePath) =>
        activePath is not null && string.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(activePath),
            StringComparison.Ordinal);

    private static void RemoveEmptyDirectories(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        foreach (var directory in Directory
                     .EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }

    private sealed record FileEntry(
        string Path,
        DateTime LastWriteUtc,
        long Length);

    private sealed record RetentionResult(
        int DeletedFileCount,
        long DeletedBytes);
}
