namespace Modules.Setup.InstallRuns;

public static class InstallProgressStatuses
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string WaitingForUser = "WaitingForUser";
    public const string Recovering = "Recovering";
    public const string Recovered = "Recovered";
    public const string Failed = "Failed";
}

public sealed record InstallProgressPhaseSnapshot(
    string Code,
    string Status,
    string SafeSummary,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record InstallProgressSnapshot(
    int SchemaVersion,
    Guid InstallationId,
    Guid StepId,
    int StepSequence,
    string StepName,
    int AttemptNumber,
    string StepStatus,
    string? PhaseCode,
    string? PhaseStatus,
    string SafeSummary,
    DateTimeOffset StepStartedAtUtc,
    DateTimeOffset LastActivityAtUtc,
    IReadOnlyList<InstallProgressPhaseSnapshot> Phases);

public sealed record InstallProgressHistoryDocument(
    int SchemaVersion,
    Guid InstallationId,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<InstallProgressSnapshot> Attempts);

