namespace HostAgent.Runtime.Stacks.Turn;

public static class RuntimeStackTurnStates
{
    public const string Connected = "connected";
    public const string NotConnected = "not-connected";
    public const string External = "external";
    public const string Drift = "drift";
    public const string Unknown = "unknown";
}

public static class RuntimeStackTurnManagementKinds
{
    public const string MemManaged = "mem-managed";
    public const string ExternalObserved = "external-observed";
    public const string None = "none";
    public const string Unknown = "unknown";
}

public static class RuntimeStackTurnDiagnosticStatuses
{
    public const string Passed = "passed";
    public const string Warning = "warning";
    public const string Failed = "failed";
    public const string Unavailable = "unavailable";
}

public sealed record SynapseTurnConfigReadResult(
    bool Supported,
    bool AnyTurnSettings,
    bool MemManagedMarkerPresent,
    IReadOnlyList<string> TurnUris,
    string CredentialMechanism,
    bool SharedSecretPresent,
    string? SharedSecretValue,
    string? SharedSecretFingerprint,
    string? SharedSecretPath,
    string? UserLifetime,
    bool? AllowGuests,
    string? CommentPublicHost,
    string? CommentRealm,
    string FileSha256,
    string? ProblemCode,
    string? Detail);

public sealed record RuntimeStackTurnMetadataObservation(
    bool Recorded,
    bool? Configured,
    IReadOnlyList<string> TurnUris,
    string? PublicHost,
    string? Realm,
    string? ConfigurationSource,
    bool? RelayPortsPublished,
    bool? SharedSecretPresent,
    string? UserLifetime,
    bool? AllowGuests,
    string? Management = null);

public sealed record RuntimeStackTurnClassificationInput(
    SynapseTurnConfigReadResult? Live,
    RuntimeStackTurnMetadataObservation Metadata,
    bool MatrixContainerAvailable,
    bool MatrixContainerRunning,
    bool MatrixContainerIdentityMatches,
    IReadOnlyList<string> PlatformTurnUris,
    string? PlatformSharedSecret,
    bool PlatformSecretPresent);

public sealed record RuntimeStackTurnClassification(
    string State,
    string Management,
    bool? LiveUrisMatchMetadata,
    bool? LiveUrisMatchPlatform,
    bool? SharedSecretMatchesPlatform,
    IReadOnlyList<RuntimeStackTurnDiagnosticResponse> Diagnostics,
    IReadOnlyList<string> Warnings,
    string Detail);

public sealed record RuntimeStackTurnDiagnosticResponse(
    string Code,
    string Status,
    string Message);

public sealed record RuntimeStackTurnLiveConfigurationResponse(
    bool Supported,
    bool AnyTurnSettings,
    bool MemManagedMarkerPresent,
    IReadOnlyList<string> TurnUris,
    string CredentialMechanism,
    bool SharedSecretPresent,
    bool? SharedSecretMatchesPlatform,
    string? UserLifetime,
    bool? AllowGuests,
    string? PublicHost,
    string? Realm,
    string FileSha256,
    string? ProblemCode,
    string? Detail);

public sealed record RuntimeStackTurnPersistedMetadataResponse(
    bool Recorded,
    bool? Configured,
    IReadOnlyList<string> TurnUris,
    string? PublicHost,
    string? Realm,
    string? ConfigurationSource,
    bool? RelayPortsPublished,
    bool? SharedSecretPresent,
    string? UserLifetime,
    bool? AllowGuests,
    bool? MatchesLiveConfiguration);

public sealed record RuntimeStackTurnPlatformResponse(
    string Status,
    string Readiness,
    bool Running,
    bool OwnershipVerified,
    bool ImageApproved,
    string PublicHost,
    IReadOnlyList<string> TurnUris,
    bool SecretPresent,
    bool RelayPortsPublished,
    bool SecurityPolicyApplied,
    string? Detail);

public sealed record RuntimeStackTurnMatrixRuntimeResponse(
    bool Exists,
    bool Running,
    bool IdentityMatches,
    string? ProblemCode,
    string? Detail);

public sealed record RuntimeStackTurnInspectionResponse(
    string Source,
    string Status,
    Guid RuntimeStackId,
    string Slug,
    DateTimeOffset InspectedAtUtc,
    string State,
    string Management,
    RuntimeStackTurnLiveConfigurationResponse? LiveConfiguration,
    RuntimeStackTurnPersistedMetadataResponse PersistedMetadata,
    RuntimeStackTurnPlatformResponse? Platform,
    RuntimeStackTurnMatrixRuntimeResponse MatrixRuntime,
    IReadOnlyList<RuntimeStackTurnDiagnosticResponse> Diagnostics,
    IReadOnlyList<string> Warnings,
    string Detail);
