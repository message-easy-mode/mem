using System.Diagnostics;
using System.Text.Json;
using Shared.ControlPlane.Runtime;

namespace Api.Runtime;

/// <summary>
/// Persists a safe, process-scoped observation of the normal running MEM API so
/// trusted host commands can report the active API identity without accidentally
/// substituting their own transient process identity.
/// </summary>
public static class MemActiveApiProcessStateStore
{
    public const string RelativeStatePath = "control-plane/active-api-process.json";
    public const string UnavailableWarningCode =
        "runtime_active_api_process_unavailable_for_host_command";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static bool TryWrite(
        MemControlPlaneRuntimeContext runtimeContext,
        out string? warningCode)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);
        warningCode = null;

        try
        {
            using var process = Process.GetCurrentProcess();
            var state = new StoredActiveApiProcess(
                SchemaVersion: 1,
                ControlPlaneInstanceId: runtimeContext.ControlPlaneInstanceId,
                ApiProcessInstanceId: runtimeContext.ApiProcessInstanceId,
                RuntimeMode: runtimeContext.RuntimeMode,
                ProcessId: Environment.ProcessId,
                ProcessStartedAtUtc: new DateTimeOffset(process.StartTime.ToUniversalTime()),
                RecordedAtUtc: DateTimeOffset.UtcNow);

            var path = ResolvePath(runtimeContext);
            var directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException(
                    "The active API process state path has no parent directory.");
            Directory.CreateDirectory(directory);

            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(
                    temporaryPath,
                    JsonSerializer.Serialize(state, JsonOptions));
                TryRestrictFilePermissions(temporaryPath);
                File.Move(temporaryPath, path, overwrite: true);
                TryRestrictFilePermissions(path);
            }
            finally
            {
                TryDelete(temporaryPath);
            }

            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or JsonException or
                NotSupportedException or ArgumentException or
                InvalidOperationException)
        {
            warningCode = $"runtime_active_api_process_state_write_failed:{exception.GetType().Name}";
            return false;
        }
    }

    /// <summary>
    /// Replaces the transient host-command process identity with the currently
    /// running API process identity when it can be safely proven. If no live API
    /// can be proven, the API process identity is reported as unavailable rather
    /// than attributing the host command's newly generated identity to the API.
    /// </summary>
    public static MemControlPlaneRuntimeContext ResolveForHostCommand(
        MemControlPlaneRuntimeContext commandRuntimeContext)
    {
        ArgumentNullException.ThrowIfNull(commandRuntimeContext);

        if (TryReadLive(commandRuntimeContext, out var active))
        {
            return commandRuntimeContext with
            {
                ApiProcessInstanceId = active.ApiProcessInstanceId
            };
        }

        return commandRuntimeContext with
        {
            ApiProcessInstanceId = Guid.Empty,
            Warnings = commandRuntimeContext.Warnings
                .Append(UnavailableWarningCode)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
    }

    public static void TryClear(MemControlPlaneRuntimeContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);

        try
        {
            var path = ResolvePath(runtimeContext);
            if (!File.Exists(path))
            {
                return;
            }

            var stored = Read(path);
            if (stored is null ||
                stored.ControlPlaneInstanceId != runtimeContext.ControlPlaneInstanceId ||
                stored.ApiProcessInstanceId != runtimeContext.ApiProcessInstanceId)
            {
                return;
            }

            File.Delete(path);
        }
        catch
        {
            // Best-effort shutdown hygiene only. A stale record is independently
            // rejected by process-liveness and process-start-time validation.
        }
    }

    private static bool TryReadLive(
        MemControlPlaneRuntimeContext runtimeContext,
        out StoredActiveApiProcess active)
    {
        active = default!;

        try
        {
            var stored = Read(ResolvePath(runtimeContext));
            if (stored is null ||
                stored.SchemaVersion != 1 ||
                stored.ControlPlaneInstanceId == Guid.Empty ||
                stored.ApiProcessInstanceId == Guid.Empty ||
                stored.ProcessId <= 0 ||
                stored.ControlPlaneInstanceId != runtimeContext.ControlPlaneInstanceId ||
                !string.Equals(
                    stored.RuntimeMode,
                    runtimeContext.RuntimeMode,
                    StringComparison.Ordinal))
            {
                return false;
            }

            using var process = Process.GetProcessById(stored.ProcessId);
            if (process.HasExited)
            {
                return false;
            }

            var observedStartUtc = new DateTimeOffset(process.StartTime.ToUniversalTime());
            if (Math.Abs(
                    (observedStartUtc - stored.ProcessStartedAtUtc).TotalSeconds) > 2)
            {
                // Protect against PID reuse after an unclean API exit.
                return false;
            }

            active = stored;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or JsonException or
                NotSupportedException or ArgumentException or
                InvalidOperationException)
        {
            return false;
        }
    }

    private static StoredActiveApiProcess? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<StoredActiveApiProcess>(
            File.ReadAllText(path),
            JsonOptions);
    }

    private static string ResolvePath(MemControlPlaneRuntimeContext runtimeContext)
    {
        var stateRoot = Path.GetFullPath(runtimeContext.StateRootPath);
        var path = Path.GetFullPath(Path.Combine(stateRoot, RelativeStatePath));
        var relative = Path.GetRelativePath(stateRoot, path);
        if (relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The active API process state path escapes the runtime state root.");
        }

        return path;
    }

    private static void TryRestrictFilePermissions(string path)
    {
        try
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // The state contains only safe runtime/process identifiers. Permission
            // tightening is best effort and must not make the API unavailable.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort temporary-file cleanup.
        }
    }

    private sealed record StoredActiveApiProcess(
        int SchemaVersion,
        Guid ControlPlaneInstanceId,
        Guid ApiProcessInstanceId,
        string RuntimeMode,
        int ProcessId,
        DateTimeOffset ProcessStartedAtUtc,
        DateTimeOffset RecordedAtUtc);
}
