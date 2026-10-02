using HostAgent.Runtime.Stacks.Turn;

namespace HostAgent.Tests.Runtime.Stacks.Turn;

public sealed class RuntimeStackTurnStateClassifierTests
{
    private static readonly string[] PlatformUris =
    [
        "turn:turn.example.test:3478?transport=udp",
        "turn:turn.example.test:3478?transport=tcp"
    ];

    [Fact]
    public void Empty_live_configuration_is_not_connected()
    {
        var result = RuntimeStackTurnStateClassifier.Classify(Input(
            Live(any: false, uris: [], secret: null, marker: false),
            Metadata(recorded: false, configured: null, uris: [])));

        Assert.Equal(RuntimeStackTurnStates.NotConnected, result.State);
        Assert.Equal(RuntimeStackTurnManagementKinds.None, result.Management);
    }

    [Fact]
    public void Exact_mem_managed_live_and_metadata_are_connected()
    {
        var result = RuntimeStackTurnStateClassifier.Classify(Input(
            Live(any: true, uris: PlatformUris, secret: "platform-secret", marker: true),
            Metadata(recorded: true, configured: true, uris: PlatformUris)));

        Assert.Equal(RuntimeStackTurnStates.Connected, result.State);
        Assert.Equal(RuntimeStackTurnManagementKinds.MemManaged, result.Management);
        Assert.True(result.LiveUrisMatchMetadata);
        Assert.True(result.LiveUrisMatchPlatform);
        Assert.True(result.SharedSecretMatchesPlatform);
    }

    [Fact]
    public void Unrecorded_external_configuration_is_external()
    {
        var result = RuntimeStackTurnStateClassifier.Classify(Input(
            Live(
                any: true,
                uris: ["turn:external.example:3478?transport=udp"],
                secret: "external-secret",
                marker: false),
            Metadata(recorded: false, configured: null, uris: [])));

        Assert.Equal(RuntimeStackTurnStates.External, result.State);
        Assert.Equal(RuntimeStackTurnManagementKinds.ExternalObserved, result.Management);
    }

    [Fact]
    public void Preserved_external_metadata_remains_external_when_it_matches_live_source_turn()
    {
        var externalUris = new[] { "turn:external.example:3478?transport=udp" };
        var result = RuntimeStackTurnStateClassifier.Classify(Input(
            Live(
                any: true,
                uris: externalUris,
                secret: "external-secret",
                marker: false),
            Metadata(
                recorded: true,
                configured: true,
                uris: externalUris,
                configurationSource: "migration-source-preserved",
                management: RuntimeStackTurnManagementKinds.ExternalObserved)));

        Assert.Equal(RuntimeStackTurnStates.External, result.State);
        Assert.Equal(RuntimeStackTurnManagementKinds.ExternalObserved, result.Management);
        Assert.True(result.LiveUrisMatchMetadata);
        Assert.False(result.SharedSecretMatchesPlatform);
    }

    [Fact]
    public void Preserved_external_metadata_that_exactly_matches_current_platform_is_connected_for_adoption()
    {
        var result = RuntimeStackTurnStateClassifier.Classify(Input(
            Live(any: true, uris: PlatformUris, secret: "platform-secret", marker: false),
            Metadata(
                recorded: true,
                configured: true,
                uris: PlatformUris,
                configurationSource: "migration-source-preserved",
                management: RuntimeStackTurnManagementKinds.ExternalObserved)));

        Assert.Equal(RuntimeStackTurnStates.Connected, result.State);
        Assert.Equal(RuntimeStackTurnManagementKinds.MemManaged, result.Management);
        Assert.True(result.LiveUrisMatchPlatform);
        Assert.True(result.SharedSecretMatchesPlatform);
        Assert.Contains("adoption", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mem_looking_live_configuration_without_metadata_is_drift()
    {
        var result = RuntimeStackTurnStateClassifier.Classify(Input(
            Live(any: true, uris: PlatformUris, secret: "platform-secret", marker: true),
            Metadata(recorded: false, configured: null, uris: [])));

        Assert.Equal(RuntimeStackTurnStates.Drift, result.State);
        Assert.Equal(RuntimeStackTurnManagementKinds.MemManaged, result.Management);
    }

    [Fact]
    public void Unavailable_matrix_runtime_is_unknown()
    {
        var input = Input(
            Live(any: false, uris: [], secret: null, marker: false),
            Metadata(recorded: false, configured: null, uris: [])) with
        {
            MatrixContainerAvailable = false,
            MatrixContainerRunning = false,
            MatrixContainerIdentityMatches = false
        };

        var result = RuntimeStackTurnStateClassifier.Classify(input);

        Assert.Equal(RuntimeStackTurnStates.Unknown, result.State);
        Assert.Equal(RuntimeStackTurnManagementKinds.Unknown, result.Management);
    }

    private static RuntimeStackTurnClassificationInput Input(
        SynapseTurnConfigReadResult live,
        RuntimeStackTurnMetadataObservation metadata) =>
        new(
            Live: live,
            Metadata: metadata,
            MatrixContainerAvailable: true,
            MatrixContainerRunning: true,
            MatrixContainerIdentityMatches: true,
            PlatformTurnUris: PlatformUris,
            PlatformSharedSecret: "platform-secret",
            PlatformSecretPresent: true);

    private static SynapseTurnConfigReadResult Live(
        bool any,
        IReadOnlyList<string> uris,
        string? secret,
        bool marker) =>
        new(
            Supported: true,
            AnyTurnSettings: any,
            MemManagedMarkerPresent: marker,
            TurnUris: uris,
            CredentialMechanism: secret is null ? "none" : "inline-shared-secret",
            SharedSecretPresent: secret is not null,
            SharedSecretValue: secret,
            SharedSecretFingerprint: secret is null ? null : "sha256:safe",
            SharedSecretPath: null,
            UserLifetime: "1h",
            AllowGuests: false,
            CommentPublicHost: null,
            CommentRealm: null,
            FileSha256: "sha256:config",
            ProblemCode: null,
            Detail: null);

    private static RuntimeStackTurnMetadataObservation Metadata(
        bool recorded,
        bool? configured,
        IReadOnlyList<string> uris,
        string? configurationSource = null,
        string? management = null) =>
        new(
            Recorded: recorded,
            Configured: configured,
            TurnUris: uris,
            PublicHost: "turn.example.test",
            Realm: "example.test",
            ConfigurationSource: configured == true
                ? configurationSource ?? "platform-coturn"
                : null,
            RelayPortsPublished: true,
            SharedSecretPresent: configured,
            UserLifetime: "1h",
            AllowGuests: false,
            Management: management);
}
