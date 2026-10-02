using System.Text.Json.Serialization;

namespace Modules.Setup.HostChecks;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HostCheckRunStatus
{
    Pending,
    Running,
    Succeeded,
    SucceededWithWarnings,
    Failed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HostCheckStatus
{
    Pass,
    Warning,
    Fail,
    Skipped,
    Unavailable,
    Unknown
}

public sealed record HostCheckRunResponse(
    string Id,
    HostCheckRunStatus Status,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    HostCheckSummaryDto Summary,
    IReadOnlyList<HostCheckGroupDto> Groups);

public sealed record HostCheckSummaryDto(
    int Passed,
    int Warnings,
    int Failed,
    int Skipped,
    int Unavailable,
    int Unknown);

public sealed record HostCheckGroupDto(
    string Key,
    string Title,
    string Description,
    IReadOnlyList<HostCheckResultDto> Checks);

public sealed record HostCheckResultDto(
    string Key,
    string Title,
    HostCheckStatus Status,
    bool Blocking,
    string Summary,
    string? Details,
    string WhyItMatters,
    string? RecommendedAction,
    IReadOnlyList<DiagnosticEvidenceDto> Evidence);

public sealed record DiagnosticEvidenceDto(
    string Kind,
    string Label,
    string Value,
    bool Sensitive = false);