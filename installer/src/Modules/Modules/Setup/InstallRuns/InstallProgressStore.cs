using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shared.ControlPlane.Runtime;

namespace Modules.Setup.InstallRuns;

/// <summary>
/// Persists bounded, support-safe progress beneath the active Control Plane
/// state root. The current snapshot remains a small compatibility projection,
/// while a sidecar history document retains the latest durable state for each
/// step attempt so completed operation detail can be reviewed later.
///
/// Progress is intentionally separate from the reviewed installation plan so
/// heartbeat/sub-phase updates never rewrite the frozen Review fingerprint or
/// require a database migration.
/// </summary>
public sealed class InstallProgressStore
{
    private const int HistorySchemaVersion = 1;
    private const int MaxHistoryAttempts = 64;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _directory;
    private readonly ILogger<InstallProgressStore>? _logger;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    public InstallProgressStore(
        MemControlPlaneRuntimeContext runtimeContext,
        ILogger<InstallProgressStore>? logger = null)
        : this(
            Path.Combine(
                runtimeContext.StateRootPath,
                "setup",
                "install-progress"),
            logger)
    {
    }

    public InstallProgressStore(
        string directory,
        ILogger<InstallProgressStore>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Installation progress directory is required.", nameof(directory));
        }

        _directory = Path.GetFullPath(directory);
        _logger = logger;
    }

    public async Task<InstallProgressSnapshot?> GetAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        var gate = GateFor(installationId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadCurrentUnlockedAsync(installationId, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<InstallProgressSnapshot>> GetHistoryAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        var gate = GateFor(installationId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var history = await ReadHistoryUnlockedAsync(
                installationId,
                cancellationToken);
            var current = await ReadCurrentUnlockedAsync(
                installationId,
                cancellationToken);

            var attempts = history?.Attempts.ToList() ?? [];
            if (current is not null)
            {
                UpsertAttempt(attempts, current);
            }

            return attempts
                .OrderBy(x => x.StepSequence)
                .ThenBy(x => x.AttemptNumber)
                .ThenBy(x => x.StepStartedAtUtc)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<InstallProgressSnapshot> UpdateAsync(
        Guid installationId,
        Func<InstallProgressSnapshot?, InstallProgressSnapshot> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        var gate = GateFor(installationId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await ReadCurrentUnlockedAsync(installationId, cancellationToken);
            var next = update(current);
            await WriteCurrentUnlockedAsync(next, cancellationToken);

            // History is supplemental evidence. Failure to retain the sidecar
            // must never turn an otherwise valid installation operation into
            // a failed installation.
            try
            {
                await UpsertHistoryUnlockedAsync(next, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Installation progress history could not be updated for {InstallationId}; current progress remains available and installation execution continues",
                    installationId);
            }

            return next;
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim GateFor(Guid installationId) =>
        _gates.GetOrAdd(installationId, static _ => new SemaphoreSlim(1, 1));

    private string CurrentPathFor(Guid installationId) =>
        Path.Combine(_directory, $"{installationId:D}.json");

    private string HistoryPathFor(Guid installationId) =>
        Path.Combine(
            _directory,
            "history",
            $"{installationId:D}.json");

    private async Task<InstallProgressSnapshot?> ReadCurrentUnlockedAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        var path = CurrentPathFor(installationId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.Open(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            return await JsonSerializer.DeserializeAsync<InstallProgressSnapshot>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Installation progress snapshot could not be read for {InstallationId}; progress projection will degrade without blocking installation",
                installationId);
            return null;
        }
    }

    private async Task<InstallProgressHistoryDocument?> ReadHistoryUnlockedAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        var path = HistoryPathFor(installationId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.Open(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            var history = await JsonSerializer.DeserializeAsync<InstallProgressHistoryDocument>(
                stream,
                JsonOptions,
                cancellationToken);

            if (history is null || history.InstallationId != installationId)
            {
                return null;
            }

            return history;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Installation progress history could not be read for {InstallationId}; current progress remains available",
                installationId);
            return null;
        }
    }

    private async Task UpsertHistoryUnlockedAsync(
        InstallProgressSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var existing = await ReadHistoryUnlockedAsync(
            snapshot.InstallationId,
            cancellationToken);

        var attempts = existing?.Attempts.ToList() ?? [];
        UpsertAttempt(attempts, snapshot);

        if (attempts.Count > MaxHistoryAttempts)
        {
            attempts = attempts
                .OrderByDescending(x => x.LastActivityAtUtc)
                .Take(MaxHistoryAttempts)
                .OrderBy(x => x.StepSequence)
                .ThenBy(x => x.AttemptNumber)
                .ToList();
        }

        var history = new InstallProgressHistoryDocument(
            HistorySchemaVersion,
            snapshot.InstallationId,
            snapshot.LastActivityAtUtc,
            attempts
                .OrderBy(x => x.StepSequence)
                .ThenBy(x => x.AttemptNumber)
                .ThenBy(x => x.StepStartedAtUtc)
                .ToArray());

        await WriteAtomicAsync(
            HistoryPathFor(snapshot.InstallationId),
            history,
            cancellationToken);
    }

    private static void UpsertAttempt(
        List<InstallProgressSnapshot> attempts,
        InstallProgressSnapshot snapshot)
    {
        var index = attempts.FindIndex(x =>
            x.StepId == snapshot.StepId &&
            x.AttemptNumber == snapshot.AttemptNumber);

        if (index >= 0)
        {
            attempts[index] = snapshot;
        }
        else
        {
            attempts.Add(snapshot);
        }
    }

    private Task WriteCurrentUnlockedAsync(
        InstallProgressSnapshot snapshot,
        CancellationToken cancellationToken) =>
        WriteAtomicAsync(
            CurrentPathFor(snapshot.InstallationId),
            snapshot,
            cancellationToken);

    private async Task WriteAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var parentDirectory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Installation progress path has no parent directory.");
        Directory.CreateDirectory(parentDirectory);

        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = File.Open(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    value,
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Best-effort cleanup. Progress evidence must never become
                    // part of the installation control path.
                }
            }
        }
    }
}
