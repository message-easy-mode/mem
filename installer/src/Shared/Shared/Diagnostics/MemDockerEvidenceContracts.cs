namespace Shared.Diagnostics;

public sealed record MemDockerEvidenceContainer(
    string LogicalName,
    string ObservedState,
    long? ExitCode,
    string? Health,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    long RestartCount,
    string? Image);

public sealed record MemDockerEvidenceLogTail(
    int RequestedLines,
    int ReturnedLines,
    int MaximumCharacters,
    string Content,
    bool Truncated,
    bool RedactionsApplied);

public sealed record MemDockerEvidence(
    MemDiagnosticResource Resource,
    DateTimeOffset ObservedAtUtc,
    MemDockerEvidenceContainer Container,
    MemDockerEvidenceLogTail LogTail,
    IReadOnlyList<string> Warnings);

public sealed record MemDockerEvidenceReadResult(
    bool Available,
    MemDockerEvidence? Evidence,
    string? WarningCode,
    IReadOnlyList<string> Warnings);
