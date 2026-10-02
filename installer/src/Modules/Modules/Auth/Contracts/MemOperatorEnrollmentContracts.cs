namespace Modules.Auth.Contracts;

/// <summary>
/// A one-time code returned only to the issuing Platform Owner. Its raw value
/// must be transferred through a secure out-of-band channel and is never
/// available from the operator directory after this response.
/// </summary>
public sealed record IssueMemOperatorEnrollmentGrantResponse(
    Guid OperatorId,
    string Username,
    string EnrollmentCode,
    DateTimeOffset ExpiresAtUtc);

public sealed record VerifyMemOperatorEnrollmentCodeRequest(
    string? EnrollmentCode);

public sealed record PrepareMemOperatorEnrollmentRequest(
    string? Password);

public sealed record BeginMemOperatorEnrollmentResponse(
    Guid GrantId,
    MemOperatorEnrollmentStateResponse State);

public sealed record VerifyMemOperatorEnrollmentTotpRequest(
    string? Code);

/// <summary>
/// Non-secret state used only by the temporary, scoped enrolment cookie.
/// It never includes a raw enrolment code, password, TOTP seed, recovery code,
/// or Identity security stamp.
/// </summary>
public sealed record MemOperatorEnrollmentStateResponse(
    bool Active,
    string? Username = null,
    string? Stage = null,
    DateTimeOffset? ExpiresAtUtc = null);

public sealed record PrepareMemOperatorEnrollmentResponse(
    string Username,
    string ManualEntryKey,
    string AuthenticatorUri);

public sealed record CompleteMemOperatorEnrollmentResponse(
    string Username,
    IReadOnlyList<string> RecoveryCodes);
