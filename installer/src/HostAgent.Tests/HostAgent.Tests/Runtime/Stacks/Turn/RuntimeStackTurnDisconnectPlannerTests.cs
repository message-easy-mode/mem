using HostAgent.Runtime.Stacks.Turn;

namespace HostAgent.Tests.Runtime.Stacks.Turn;

public sealed class RuntimeStackTurnDisconnectPlannerTests
{
    [Fact]
    public void Exact_connected_mem_managed_state_is_ready_for_disconnect()
    {
        var plan = RuntimeStackTurnDisconnectPlanner.Plan(Inspection(
            RuntimeStackTurnStates.Connected,
            RuntimeStackTurnManagementKinds.MemManaged,
            recorded: true,
            configured: true,
            marker: true));

        Assert.True(plan.ConfigurationChangeRequired);
        Assert.True(plan.RestartRequired);
    }

    [Fact]
    public void Already_disconnected_state_is_no_change()
    {
        var plan = RuntimeStackTurnDisconnectPlanner.Plan(Inspection(
            RuntimeStackTurnStates.NotConnected,
            RuntimeStackTurnManagementKinds.None,
            recorded: true,
            configured: false,
            marker: false));

        Assert.False(plan.ConfigurationChangeRequired);
        Assert.False(plan.RestartRequired);
    }

    [Theory]
    [InlineData(RuntimeStackTurnStates.External, "turn_disconnect_external_configuration")]
    [InlineData(RuntimeStackTurnStates.Drift, "turn_disconnect_drift_requires_attention")]
    [InlineData(RuntimeStackTurnStates.Unknown, "turn_disconnect_state_unavailable")]
    public void Unsafe_states_are_refused(string state, string expectedCode)
    {
        var error = Assert.Throws<RuntimeStackTurnConnectionException>(() =>
            RuntimeStackTurnDisconnectPlanner.Plan(Inspection(
                state,
                state == RuntimeStackTurnStates.External
                    ? RuntimeStackTurnManagementKinds.ExternalObserved
                    : RuntimeStackTurnManagementKinds.MemManaged,
                recorded: true,
                configured: true,
                marker: true)));

        Assert.Equal(expectedCode, error.Code);
    }

    [Fact]
    public void Connected_state_without_durable_association_is_refused()
    {
        var error = Assert.Throws<RuntimeStackTurnConnectionException>(() =>
            RuntimeStackTurnDisconnectPlanner.Plan(Inspection(
                RuntimeStackTurnStates.Connected,
                RuntimeStackTurnManagementKinds.MemManaged,
                recorded: false,
                configured: null,
                marker: true)));

        Assert.Equal("turn_disconnect_state_not_supported", error.Code);
    }

    private static RuntimeStackTurnInspectionResponse Inspection(
        string state,
        string management,
        bool recorded,
        bool? configured,
        bool marker) =>
        new(
            Source: "control-plane",
            Status: "ok",
            RuntimeStackId: Guid.NewGuid(),
            Slug: "tester",
            InspectedAtUtc: DateTimeOffset.UtcNow,
            State: state,
            Management: management,
            LiveConfiguration: state == RuntimeStackTurnStates.NotConnected
                ? new RuntimeStackTurnLiveConfigurationResponse(
                    Supported: true,
                    AnyTurnSettings: false,
                    MemManagedMarkerPresent: false,
                    TurnUris: [],
                    CredentialMechanism: "none",
                    SharedSecretPresent: false,
                    SharedSecretMatchesPlatform: null,
                    UserLifetime: null,
                    AllowGuests: null,
                    PublicHost: null,
                    Realm: null,
                    FileSha256: "sha256:empty",
                    ProblemCode: null,
                    Detail: null)
                : new RuntimeStackTurnLiveConfigurationResponse(
                    Supported: true,
                    AnyTurnSettings: true,
                    MemManagedMarkerPresent: marker,
                    TurnUris: ["turn:turn.example.test:3478?transport=udp"],
                    CredentialMechanism: "inline-shared-secret",
                    SharedSecretPresent: true,
                    SharedSecretMatchesPlatform: true,
                    UserLifetime: "1h",
                    AllowGuests: true,
                    PublicHost: "turn.example.test",
                    Realm: "example.test",
                    FileSha256: "sha256:connected",
                    ProblemCode: null,
                    Detail: null),
            PersistedMetadata: new RuntimeStackTurnPersistedMetadataResponse(
                Recorded: recorded,
                Configured: configured,
                TurnUris: configured == true
                    ? ["turn:turn.example.test:3478?transport=udp"]
                    : [],
                PublicHost: configured == true ? "turn.example.test" : null,
                Realm: configured == true ? "example.test" : null,
                ConfigurationSource: configured == true ? "platform-coturn" : null,
                RelayPortsPublished: configured == true ? true : (bool?)null,
                SharedSecretPresent: configured == true ? true : (bool?)null,
                UserLifetime: configured == true ? "1h" : null,
                AllowGuests: configured == true ? true : (bool?)null,
                MatchesLiveConfiguration: configured == true ? true : (bool?)null),
            Platform: null,
            MatrixRuntime: new RuntimeStackTurnMatrixRuntimeResponse(
                Exists: true,
                Running: true,
                IdentityMatches: true,
                ProblemCode: null,
                Detail: null),
            Diagnostics: [],
            Warnings: [],
            Detail: "test");
}
