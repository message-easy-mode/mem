using HostAgent.Runtime.Backups.Artifacts.PortableExports;
using HostAgent.Runtime.Backups.StandardRecreate;
using HostAgent.Runtime.Stacks.Turn;

namespace HostAgent.Tests.Runtime.Backups.StandardRecreate;

public sealed class StandardRecreateTurnPolicyTests
{
    [Fact]
    public void Disconnected_backup_stays_disconnected_when_platform_turn_is_available()
    {
        var plan = StandardRecreateTurnPolicy.Resolve(
            Manifest(configured: false, management: RuntimeStackTurnManagementKinds.None),
            backedUpSettingsPresent: false,
            platformCoturnAvailable: true);

        Assert.Equal(StandardRecreateTurnModes.RestoreDisconnected, plan.Mode);
        Assert.Equal(RuntimeStackTurnStates.NotConnected, plan.TargetState);
        Assert.False(plan.GenerateWithPlatformCoturn);
        Assert.False(plan.ApplyBackedUpSettings);
        Assert.True(plan.CanProceed);
    }

    [Fact]
    public void Mem_managed_backup_rebinds_to_ready_target_platform()
    {
        var plan = StandardRecreateTurnPolicy.Resolve(
            Manifest(configured: true, management: RuntimeStackTurnManagementKinds.MemManaged),
            backedUpSettingsPresent: true,
            platformCoturnAvailable: true);

        Assert.Equal(StandardRecreateTurnModes.RebindPlatform, plan.Mode);
        Assert.Equal(RuntimeStackTurnManagementKinds.MemManaged, plan.TargetManagement);
        Assert.True(plan.GenerateWithPlatformCoturn);
        Assert.False(plan.ApplyBackedUpSettings);
        Assert.True(plan.CanProceed);
    }

    [Fact]
    public void Mem_managed_backup_is_blocked_when_target_platform_turn_is_unavailable()
    {
        var plan = StandardRecreateTurnPolicy.Resolve(
            Manifest(configured: true, management: RuntimeStackTurnManagementKinds.MemManaged),
            backedUpSettingsPresent: true,
            platformCoturnAvailable: false);

        Assert.True(plan.RequiresPlatformCoturn);
        Assert.False(plan.CanProceed);
    }

    [Fact]
    public void External_backup_preserves_exact_settings_without_platform_rebind()
    {
        var plan = StandardRecreateTurnPolicy.Resolve(
            Manifest(configured: true, management: RuntimeStackTurnManagementKinds.ExternalObserved),
            backedUpSettingsPresent: true,
            platformCoturnAvailable: true);

        Assert.Equal(StandardRecreateTurnModes.PreserveBackup, plan.Mode);
        Assert.Equal(RuntimeStackTurnManagementKinds.ExternalObserved, plan.TargetManagement);
        Assert.False(plan.GenerateWithPlatformCoturn);
        Assert.True(plan.ApplyBackedUpSettings);
        Assert.True(plan.CanProceed);
    }

    [Fact]
    public void Legacy_turn_block_is_preserved_and_classified_external()
    {
        var plan = StandardRecreateTurnPolicy.Resolve(
            manifest: null,
            backedUpSettingsPresent: true,
            platformCoturnAvailable: true);

        Assert.Equal(StandardRecreateTurnModes.PreserveBackup, plan.Mode);
        Assert.Equal("restore-backup-legacy", plan.ConfigurationSource);
        Assert.True(plan.ApplyBackedUpSettings);
    }

    [Fact]
    public void Declared_external_turn_without_payload_settings_is_blocked()
    {
        var plan = StandardRecreateTurnPolicy.Resolve(
            Manifest(configured: true, management: RuntimeStackTurnManagementKinds.ExternalObserved),
            backedUpSettingsPresent: false,
            platformCoturnAvailable: true);

        Assert.False(plan.CanProceed);
    }

    private static MemStackExportCoturnManifest Manifest(
        bool configured,
        string management) =>
        new(
            Configured: configured,
            PublicHost: configured ? "turn.example.test" : null,
            Realm: configured ? "example.test" : null,
            TurnUris: configured
                ? ["turn:turn.example.test:3478?transport=udp"]
                : [],
            SharedSecretPresent: configured,
            UserLifetime: configured ? "1h" : null,
            AllowGuests: configured ? true : null,
            State: configured ? RuntimeStackTurnStates.Connected : RuntimeStackTurnStates.NotConnected,
            Management: management);
}
