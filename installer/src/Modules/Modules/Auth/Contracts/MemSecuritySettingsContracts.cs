namespace Modules.Auth.Contracts;

public sealed record MemSecuritySettingsResponse(
    MemHighRiskStepUpSettingsResponse HighRiskStepUp);

public sealed record MemHighRiskStepUpSettingsResponse(
    bool Required,
    int ReuseVerificationMinutes,
    IReadOnlyList<int> AllowedReuseVerificationMinutes,
    bool IsDefaulted,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByOperatorId);

/// <summary>
/// Nullable members distinguish omitted JSON properties from an explicit
/// request. The server validates both fields before changing persisted policy.
/// </summary>
public sealed record UpdateMemHighRiskStepUpSettingsRequest(
    bool? Required,
    int? ReuseVerificationMinutes);
