using HostAgent.Runtime.Stacks.Turn;

namespace HostAgent.Tests.Runtime.Stacks.Turn;

public sealed class RuntimeStackTurnConnectPlannerTests
{
    [Fact]
    public void Not_connected_stack_requires_configuration_and_restart()
    {
        var plan = RuntimeStackTurnConnectPlanner.Plan(Inspection(
            state: RuntimeStackTurnStates.NotConnected,
            management: RuntimeStackTurnManagementKinds.None,
            metadataRecorded: false,
            liveUris: [],
            secretMatches: null));

        Assert.Equal(RuntimeStackTurnConnectModes.Configure, plan.Mode);
        Assert.True(plan.ConfigurationChangeRequired);
        Assert.True(plan.RestartRequired);
    }

    [Fact]
    public void Exact_live_platform_match_without_metadata_is_adopted_without_restart()
    {
        var plan = RuntimeStackTurnConnectPlanner.Plan(Inspection(
            state: RuntimeStackTurnStates.Drift,
            management: RuntimeStackTurnManagementKinds.MemManaged,
            metadataRecorded: false,
            liveUris: PlatformUris,
            secretMatches: true));

        Assert.Equal(RuntimeStackTurnConnectModes.AdoptExisting, plan.Mode);
        Assert.False(plan.ConfigurationChangeRequired);
        Assert.False(plan.RestartRequired);
    }

    [Fact]
    public void Migration_preserved_platform_match_is_adopted_without_restart()
    {
        var plan = RuntimeStackTurnConnectPlanner.Plan(Inspection(
            state: RuntimeStackTurnStates.Connected,
            management: RuntimeStackTurnManagementKinds.MemManaged,
            metadataRecorded: true,
            liveUris: PlatformUris,
            secretMatches: true,
            configurationSource: "migration-source-preserved"));

        Assert.Equal(RuntimeStackTurnConnectModes.AdoptExisting, plan.Mode);
        Assert.False(plan.ConfigurationChangeRequired);
        Assert.False(plan.RestartRequired);
    }

    [Fact]
    public void Connected_stack_returns_no_change()
    {
        var plan = RuntimeStackTurnConnectPlanner.Plan(Inspection(
            state: RuntimeStackTurnStates.Connected,
            management: RuntimeStackTurnManagementKinds.MemManaged,
            metadataRecorded: true,
            liveUris: PlatformUris,
            secretMatches: true));

        Assert.Equal(RuntimeStackTurnConnectModes.NoChange, plan.Mode);
    }

    [Fact]
    public void External_stack_requires_reviewed_replacement_and_restart()
    {
        var plan = RuntimeStackTurnConnectPlanner.Plan(Inspection(
            state: RuntimeStackTurnStates.External,
            management: RuntimeStackTurnManagementKinds.ExternalObserved,
            metadataRecorded: true,
            liveUris: ["turn:external.example:3478?transport=udp"],
            secretMatches: false,
            configurationSource: "migration-source-preserved"));

        Assert.Equal(RuntimeStackTurnConnectModes.ReplaceExternal, plan.Mode);
        Assert.True(plan.ConfigurationChangeRequired);
        Assert.True(plan.RestartRequired);
    }

    [Theory]
    [InlineData(RuntimeStackTurnStates.Unknown, RuntimeStackTurnManagementKinds.Unknown, "turn_connect_state_unavailable")]
    [InlineData(RuntimeStackTurnStates.Drift, RuntimeStackTurnManagementKinds.MemManaged, "turn_connect_drift_requires_attention")]
    public void Unsafe_states_are_rejected(
        string state,
        string management,
        string expectedCode)
    {
        var inspection = Inspection(
            state,
            management,
            metadataRecorded: true,
            liveUris: ["turn:external.example:3478?transport=udp"],
            secretMatches: false);

        var error = Assert.Throws<RuntimeStackTurnConnectionException>(
            () => RuntimeStackTurnConnectPlanner.Plan(inspection));

        Assert.Equal(expectedCode, error.Code);
    }

    [Fact]
    public void Platform_must_be_ready()
    {
        var inspection = Inspection(
            RuntimeStackTurnStates.NotConnected,
            RuntimeStackTurnManagementKinds.None,
            metadataRecorded: false,
            liveUris: [],
            secretMatches: null) with
        {
            Platform = Platform() with { Readiness = "setup-incomplete" }
        };

        var error = Assert.Throws<RuntimeStackTurnConnectionException>(
            () => RuntimeStackTurnConnectPlanner.Plan(inspection));

        Assert.Equal("turn_connect_platform_not_ready", error.Code);
    }

    private static readonly string[] PlatformUris =
    [
        "turn:turn.example.test:3478?transport=udp",
        "turn:turn.example.test:3478?transport=tcp"
    ];

    private static RuntimeStackTurnInspectionResponse Inspection(
        string state,
        string management,
        bool metadataRecorded,
        IReadOnlyList<string> liveUris,
        bool? secretMatches,
        string? configurationSource = null) =>
        new(
            Source: "control-plane",
            Status: "ok",
            RuntimeStackId: Guid.Parse("7085b97d-3d30-434e-976a-62df0178be16"),
            Slug: "demo-stack",
            InspectedAtUtc: DateTimeOffset.Parse("2026-07-26T05:00:00Z"),
            State: state,
            Management: management,
            LiveConfiguration: new RuntimeStackTurnLiveConfigurationResponse(
                Supported: true,
                AnyTurnSettings: liveUris.Count > 0,
                MemManagedMarkerPresent: management == RuntimeStackTurnManagementKinds.MemManaged,
                TurnUris: liveUris,
                CredentialMechanism: liveUris.Count > 0 ? "inline-shared-secret" : "none",
                SharedSecretPresent: liveUris.Count > 0,
                SharedSecretMatchesPlatform: secretMatches,
                UserLifetime: liveUris.Count > 0 ? "1h" : null,
                AllowGuests: liveUris.Count > 0,
                PublicHost: liveUris.Count > 0 ? "turn.example.test" : null,
                Realm: liveUris.Count > 0 ? "example.test" : null,
                FileSha256: "sha256:config",
                ProblemCode: null,
                Detail: null),
            PersistedMetadata: new RuntimeStackTurnPersistedMetadataResponse(
                Recorded: metadataRecorded,
                Configured: metadataRecorded ? true : (bool?)null,
                TurnUris: metadataRecorded ? liveUris : [],
                PublicHost: null,
                Realm: null,
                ConfigurationSource: metadataRecorded
                    ? configurationSource ?? "platform-coturn"
                    : null,
                RelayPortsPublished: metadataRecorded ? true : (bool?)null,
                SharedSecretPresent: metadataRecorded ? true : (bool?)null,
                UserLifetime: metadataRecorded ? "1h" : null,
                AllowGuests: metadataRecorded ? true : (bool?)null,
                MatchesLiveConfiguration: metadataRecorded ? true : (bool?)null),
            Platform: Platform(),
            MatrixRuntime: new RuntimeStackTurnMatrixRuntimeResponse(
                Exists: true,
                Running: true,
                IdentityMatches: true,
                ProblemCode: null,
                Detail: null),
            Diagnostics: [],
            Warnings: [],
            Detail: "test");

    private static RuntimeStackTurnPlatformResponse Platform() =>
        new(
            Status: "ok",
            Readiness: "ready",
            Running: true,
            OwnershipVerified: true,
            ImageApproved: true,
            PublicHost: "turn.example.test",
            TurnUris: PlatformUris,
            SecretPresent: true,
            RelayPortsPublished: true,
            SecurityPolicyApplied: true,
            Detail: null);
}
