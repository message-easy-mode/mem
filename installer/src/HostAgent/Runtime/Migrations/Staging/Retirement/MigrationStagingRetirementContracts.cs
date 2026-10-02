namespace HostAgent.Runtime.Migrations.Staging.Retirement;

public sealed record MigrationStagingRetirementRequest(string ReviewFingerprint, bool ConfirmRetirement, bool Retry = false);
public sealed record MigrationStagingRetirementOperation(
    Guid OperationId, string Status, string CurrentStep, int AttemptCount,
    DateTime RequestedAtUtc, DateTime UpdatedAtUtc, DateTime? CompletedAtUtc, string? FailureCode);
public sealed record MigrationStagingRetirementReview(
    string MigrationId, string StagingRunId, string DisplayName, bool CanRetire,
    string? BlockerCode, string? ReviewFingerprint,
    int ContainerCount, int NetworkCount, bool WorkspacePresent,
    MigrationStagingRetirementOperation? Operation);

// Server-only, immutable accepted membership. No paths, tokens or raw history reports.
public sealed record MigrationStagingRetirementPlan(
    string StagingId, string CandidateArtifactId, string HistoryIdentitySha256,
    string? ElementContainerId, string? SynapseContainerId, string? PostgresContainerId, string? NetworkId)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<(string Role, string? Id)> Containers =>
        new (string, string?)[] { ("element", ElementContainerId), ("synapse", SynapseContainerId), ("postgres", PostgresContainerId) };
}
public sealed record MigrationStagingRetirementInspection(
    MigrationStagingRetirementPlan Plan, int ContainerCount, int NetworkCount, bool WorkspacePresent);

public interface IMigrationStagingRetirementRuntime
{
    Task<MigrationStagingRetirementInspection> InspectAsync(string stagingId, string candidateArtifactId, CancellationToken ct);
    Task RetireAsync(MigrationStagingRetirementPlan plan, Func<string, CancellationToken, Task> progress, CancellationToken ct);
}

public sealed class MigrationStagingRetirementException(string code)
    : InvalidOperationException("Migration staging retirement is unavailable. Review the current ownership, lifecycle and runtime evidence.")
{
    public string Code { get; } = code;
}
