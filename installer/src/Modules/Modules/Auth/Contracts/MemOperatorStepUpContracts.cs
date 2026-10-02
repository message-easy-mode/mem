namespace Modules.Auth.Contracts;

/// <summary>
/// Credentials for a fresh, session-bound step-up authentication. A recovery
/// code is deliberately not accepted here: high-risk step-up requires the
/// operator's password and a current authenticator-app TOTP value.
/// </summary>
public sealed record VerifyMemOperatorStepUpRequest(
    string? Password,
    string? Code);

public sealed record MemOperatorStepUpResponse(
    string Status,
    DateTimeOffset? ExpiresAtUtc = null);
