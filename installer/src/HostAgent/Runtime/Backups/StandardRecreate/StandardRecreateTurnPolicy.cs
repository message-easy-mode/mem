using HostAgent.Runtime.Backups.Artifacts.PortableExports;
using HostAgent.Runtime.Stacks.Turn;

namespace HostAgent.Runtime.Backups.StandardRecreate;

public static class StandardRecreateTurnModes
{
    public const string RestoreDisconnected = "restore-disconnected";
    public const string RebindPlatform = "rebind-platform";
    public const string PreserveBackup = "preserve-backup";
}

public sealed record StandardRecreateTurnPlan(
    string Mode,
    string TargetState,
    string TargetManagement,
    string ConfigurationSource,
    bool GenerateWithPlatformCoturn,
    bool ApplyBackedUpSettings,
    bool RequiresPlatformCoturn,
    bool CanProceed,
    string Detail);

/// <summary>
/// Converts backup TURN evidence into one deterministic restore policy. Older
/// backups remain supported: a payload with no TURN block restores
/// disconnected, while an unclassified legacy TURN block is preserved rather
/// than silently replaced.
/// </summary>
public static class StandardRecreateTurnPolicy
{
    public static StandardRecreateTurnPlan Resolve(
        MemStackExportCoturnManifest? manifest,
        bool backedUpSettingsPresent,
        bool platformCoturnAvailable)
    {
        if (manifest is null)
        {
            return backedUpSettingsPresent
                ? PreserveLegacy()
                : RestoreDisconnected();
        }

        if (!manifest.Configured)
        {
            return RestoreDisconnected();
        }

        var management = manifest.Management?.Trim().ToLowerInvariant();
        if (string.Equals(
                management,
                RuntimeStackTurnManagementKinds.MemManaged,
                StringComparison.Ordinal))
        {
            return new StandardRecreateTurnPlan(
                StandardRecreateTurnModes.RebindPlatform,
                RuntimeStackTurnStates.Connected,
                RuntimeStackTurnManagementKinds.MemManaged,
                "restore-platform-rebind",
                GenerateWithPlatformCoturn: true,
                ApplyBackedUpSettings: false,
                RequiresPlatformCoturn: true,
                CanProceed: platformCoturnAvailable,
                Detail: platformCoturnAvailable
                    ? "The backup records MEM-managed TURN. The recreated stack will bind to the current platform TURN service."
                    : "The backup records MEM-managed TURN, but the current platform TURN service is not ready.");
        }

        if (!backedUpSettingsPresent)
        {
            return new StandardRecreateTurnPlan(
                StandardRecreateTurnModes.PreserveBackup,
                RuntimeStackTurnStates.External,
                RuntimeStackTurnManagementKinds.ExternalObserved,
                "restore-backup-external",
                GenerateWithPlatformCoturn: false,
                ApplyBackedUpSettings: false,
                RequiresPlatformCoturn: false,
                CanProceed: false,
                Detail: "The backup declares TURN configuration but does not contain restorable Synapse TURN settings.");
        }

        return new StandardRecreateTurnPlan(
            StandardRecreateTurnModes.PreserveBackup,
            RuntimeStackTurnStates.External,
            RuntimeStackTurnManagementKinds.ExternalObserved,
            "restore-backup-external",
            GenerateWithPlatformCoturn: false,
            ApplyBackedUpSettings: true,
            RequiresPlatformCoturn: false,
            CanProceed: true,
            Detail: string.Equals(
                    management,
                    RuntimeStackTurnManagementKinds.ExternalObserved,
                    StringComparison.Ordinal)
                ? "The backup records external TURN. The exact backed-up Synapse TURN settings will be preserved."
                : "The backup contains legacy TURN settings without a durable MEM association. The exact settings will be preserved and classified as external.");
    }

    private static StandardRecreateTurnPlan RestoreDisconnected() =>
        new(
            StandardRecreateTurnModes.RestoreDisconnected,
            RuntimeStackTurnStates.NotConnected,
            RuntimeStackTurnManagementKinds.None,
            "restore-disconnected",
            GenerateWithPlatformCoturn: false,
            ApplyBackedUpSettings: false,
            RequiresPlatformCoturn: false,
            CanProceed: true,
            Detail: "The backup records no TURN association. The recreated stack will remain disconnected even when platform TURN is available.");

    private static StandardRecreateTurnPlan PreserveLegacy() =>
        new(
            StandardRecreateTurnModes.PreserveBackup,
            RuntimeStackTurnStates.External,
            RuntimeStackTurnManagementKinds.ExternalObserved,
            "restore-backup-legacy",
            GenerateWithPlatformCoturn: false,
            ApplyBackedUpSettings: true,
            RequiresPlatformCoturn: false,
            CanProceed: true,
            Detail: "The backup predates durable TURN association metadata. Its exact Synapse TURN settings will be preserved and classified as external.");
}
