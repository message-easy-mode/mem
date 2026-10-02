namespace Modules.Auth.Contracts;

public sealed record BootstrapStateResponse(
    bool RequiresFirstOwnerBootstrap,
    bool HasCompletedPlatformOwner);

public sealed record VerifyBootstrapCodeRequest(string? Token);

public sealed record BootstrapGrantResponse(
    DateTimeOffset ExpiresAtUtc);

public sealed record CreateFirstMemOperatorRequest(
    string? Username,
    string? Email,
    string? Password);

public sealed record PrepareFirstMemOperatorResponse(
    string Username,
    string ManualEntryKey,
    string AuthenticatorUri);

public sealed record VerifyBootstrapTotpRequest(string? Code);

public sealed record CompleteBootstrapResponse(
    string Username,
    IReadOnlyList<string> RecoveryCodes);

public sealed record MemOperatorSessionResponse(
    bool Authenticated,
    string? AuthenticationKind,
    string? DisplayName,
    IReadOnlyList<string> Roles,
    bool RequiresFirstOwnerBootstrap,
    bool HasCompletedPlatformOwner);

public sealed record MemOperatorLoginRequest(
    string? Username,
    string? Password);

public sealed record MemOperatorLoginResponse(
    string Status,
    MemOperatorSessionResponse? Session = null);

public sealed record VerifyMemOperatorTotpRequest(string? Code);

/// <summary>
/// A one-time recovery-code MFA completion attempt. This route is valid only
/// while the server-managed, short-lived two-factor pending cookie exists
/// after a successful password stage. It is not standalone authentication.
/// </summary>
public sealed record VerifyMemOperatorRecoveryCodeRequest(string? Code);
