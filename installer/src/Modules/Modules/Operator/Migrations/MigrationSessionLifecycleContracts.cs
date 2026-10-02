namespace Modules.Operator.Migrations;

public static class MigrationSessionEncryptedPackageRetentionPolicies
{
    public const string Remove = "remove";
    public const string RetainEncrypted = "retain-encrypted";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Remove,
            RetainEncrypted,
        };
}

public sealed record MigrationSessionLifecycleInspectionDto(
    string MigrationId,
    string LifecycleStatus,
    bool Archived,
    DateTime? ArchivedAtUtc,
    string? ArchivedBy,
    DateTime? ClosedAtUtc,
    string? ClosureKind,
    long StateVersion,
    string CurrentOperation,
    string PackageState,
    string CandidateArtifactState,
    string PrivateStagingRuntimeState,
    string ProductionRuntimeState,
    string PublicRoutesState,
    string SourceState,
    MigrationSessionLifecycleCapabilitiesDto Capabilities,
    string SourceUnaffectedNotice);

public sealed record MigrationSessionLifecycleCapabilitiesDto(
    bool CanArchive,
    bool CanUnarchive,
    bool CanCancel,
    bool CanDelete,
    string? CancelBlockedCode,
    string? CancelBlockedReason,
    string? DeleteBlockedCode,
    string? DeleteBlockedReason);

public sealed record MigrationSessionLifecycleMutationRequest(
    long ExpectedStateVersion);

public sealed record MigrationSessionCancelRequest(
    long ExpectedStateVersion,
    bool AcknowledgeSourceUnaffected,
    string EncryptedPackageRetention);

public sealed record MigrationSessionDeleteRequest(
    long ExpectedStateVersion,
    string ConfirmationMigrationId,
    bool AcknowledgeSourceUnaffected);

public sealed record MigrationSessionDeleteResponse(
    string ResultCode,
    bool Idempotent,
    string MigrationId,
    DateTimeOffset DeletedAtUtc);

public sealed record MigrationSessionLifecycleMutationResponse(
    string ResultCode,
    bool Idempotent,
    MigrationSessionLifecycleInspectionDto Lifecycle);

public sealed record MigrationSessionLifecycleProblemResponse(
    string Code,
    string Message);

public sealed class MigrationSessionLifecycleException(
    int statusCode,
    string code,
    string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

/// <summary>
/// Read-only cancellation presentation shared by the workspace and lifecycle
/// inspection. This is not authorization; CancelAsync rechecks the same policy
/// and the optimistic state version before changing any target-side material.
/// </summary>
public sealed record MigrationSessionCancellationDto(
    long StateVersion,
    string LifecycleStatus,
    bool CanCancel,
    string ConfirmationKind,
    string? BlockedCode,
    string? BlockedReason);
