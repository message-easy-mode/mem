using System.Runtime.InteropServices;
using HostAgent.Matrix.Runtime;

namespace HostAgent.Runtime.Backups.StandardRecreate;

/// <summary>
/// Normalizes bind-mounted Synapse runtime material after Standard Recreate has
/// copied and rewritten it. MEM's containerized Control Plane runs as root, but
/// the approved Synapse image drops to UID/GID 991 by default. Files copied by
/// the Control Plane must therefore be handed back to the Synapse runtime user
/// before the restored homeserver is started.
/// </summary>
internal static class StandardRecreateRuntimeOwnership
{
    // The approved matrixdotorg/synapse runtime image contract currently uses
    // UID/GID 991 when the container starts as root and UID/GID are not
    // overridden. Keep these values explicit and covered by restore tests so an
    // approved-image change cannot silently alter the writable-filesystem
    // contract.
    internal const uint DefaultSynapseUid = SynapseRuntimeIdentityPolicy.DefaultSynapseUid;
    internal const uint DefaultSynapseGid = SynapseRuntimeIdentityPolicy.DefaultSynapseGid;

    public static StandardRecreateRuntimeOwnershipResult Normalize(
        string matrixDataPath)
    {
        if (string.IsNullOrWhiteSpace(matrixDataPath) ||
            !Directory.Exists(matrixDataPath))
        {
            throw new InvalidDataException(
                "The restored Matrix data directory is unavailable for runtime ownership normalization.");
        }

        if (!OperatingSystem.IsLinux())
        {
            return new StandardRecreateRuntimeOwnershipResult(
                ContainerUser: null,
                TargetUid: null,
                TargetGid: null,
                OwnershipChanged: false,
                Detail: "Unix ownership normalization is not required on this platform.");
        }

        return NormalizeLinux(
            matrixDataPath,
            NativeMethods.GetEffectiveUserId(),
            NativeMethods.GetEffectiveGroupId(),
            ChangeOwner);
    }

    internal static StandardRecreateRuntimeOwnershipResult NormalizeLinux(
        string matrixDataPath,
        uint effectiveUid,
        uint effectiveGid,
        Action<string, uint, uint> changeOwner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matrixDataPath);
        ArgumentNullException.ThrowIfNull(changeOwner);

        if (!Directory.Exists(matrixDataPath))
        {
            throw new InvalidDataException(
                "The restored Matrix data directory is unavailable for runtime ownership normalization.");
        }

        // Host-process development deliberately configures a non-root Control
        // Plane uid/gid as the Synapse container user. In that mode all restored
        // files already use that development identity, so changing ownership is
        // neither necessary nor permitted. Containerized Control Plane modes do
        // not propagate the Control Plane container's uid/gid into Synapse;
        // they leave Docker's User override unset so the approved image uses
        // its default runtime identity. Restored material must therefore be
        // assigned to that approved identity before Synapse starts.
        if (effectiveUid != 0)
        {
            return new StandardRecreateRuntimeOwnershipResult(
                ContainerUser: $"{effectiveUid}:{effectiveGid}",
                TargetUid: effectiveUid,
                TargetGid: effectiveGid,
                OwnershipChanged: false,
                Detail: $"Restored Matrix material already uses the non-root runtime identity {effectiveUid}:{effectiveGid}.");
        }

        var entries = EnumerateSafeTree(matrixDataPath).ToArray();

        foreach (var entry in entries)
        {
            changeOwner(entry, DefaultSynapseUid, DefaultSynapseGid);
        }

        // Normalize the root last so traversal cannot be affected by ownership
        // changes while the tree is being inspected.
        changeOwner(matrixDataPath, DefaultSynapseUid, DefaultSynapseGid);

        return new StandardRecreateRuntimeOwnershipResult(
            ContainerUser: $"{DefaultSynapseUid}:{DefaultSynapseGid}",
            TargetUid: DefaultSynapseUid,
            TargetGid: DefaultSynapseGid,
            OwnershipChanged: true,
            Detail: $"Restored Matrix material was assigned to the approved Synapse runtime identity {DefaultSynapseUid}:{DefaultSynapseGid} before startup.");
    }

    private static IEnumerable<string> EnumerateSafeTree(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        "Restored Matrix material contains a symbolic link or reparse point. MEM will not change ownership through it.");
                }

                yield return entry;

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
            }
        }
    }

    private static void ChangeOwner(string path, uint uid, uint gid)
    {
        if (NativeMethods.LChown(path, uid, gid) == 0)
        {
            return;
        }

        var error = Marshal.GetLastPInvokeError();
        throw new IOException(
            $"Could not assign restored Matrix runtime ownership for '{path}'. errno={error}.");
    }

    private static class NativeMethods
    {
        [DllImport("libc", EntryPoint = "geteuid")]
        internal static extern uint GetEffectiveUserId();

        [DllImport("libc", EntryPoint = "getegid")]
        internal static extern uint GetEffectiveGroupId();

        [DllImport("libc", EntryPoint = "lchown", CharSet = CharSet.Ansi, SetLastError = true)]
        internal static extern int LChown(string path, uint owner, uint group);
    }
}

internal sealed record StandardRecreateRuntimeOwnershipResult(
    string? ContainerUser,
    uint? TargetUid,
    uint? TargetGid,
    bool OwnershipChanged,
    string Detail);
