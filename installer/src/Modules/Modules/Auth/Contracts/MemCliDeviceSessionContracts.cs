namespace Modules.Auth.Contracts;

/// <summary>
/// Returned only to the initiating CLI process. The raw user code is never
/// persisted or included in audit data.
/// </summary>
public sealed record MemCliDeviceAuthorizationStarted(
    Guid AuthorizationId,
    string UserCode,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Request from the initiating CLI. The API deliberately derives the current
/// installation identity server-side; clients cannot submit one here.
/// </summary>
public sealed record StartMemCliDeviceAuthorizationRequest(
    string? VerifierChallenge,
    string? DeviceLabel);

/// <summary>
/// Returned only to the initiating CLI. The user code is not persisted and is
/// never included in audit data, logs, browser responses, or support output.
/// BrowserApprovalUrl is derived solely from server-owned public Web
/// configuration; clients cannot submit or choose it. The result contains no
/// verifier, credential, installation identifier, browser cookie, or operator
/// identity.
/// </summary>
public sealed record StartMemCliDeviceAuthorizationResponse(
    string Status,
    Guid AuthorizationId,
    string UserCode,
    DateTimeOffset ExpiresAtUtc,
    string? BrowserApprovalUrl = null);

/// <summary>
/// Polling proof from the original CLI process. The displayed user code is not
/// accepted in place of this high-entropy verifier.
/// </summary>
public sealed record PollMemCliDeviceAuthorizationRequest(
    string? Verifier);

/// <summary>
/// Browser decision request. It contains only the short, user-entered code;
/// it never carries a verifier, device credential, browser cookie, password,
/// TOTP value, recovery code, or operator identifier.
/// </summary>
public sealed record DecideMemCliDeviceAuthorizationRequest(
    string? UserCode);

/// <summary>
/// Browser review request. The short user code remains in browser memory only;
/// it is never placed in a route, query string, browser storage, or audit event.
/// </summary>
public sealed record ReviewMemCliDeviceAuthorizationRequest(
    string? UserCode);

/// <summary>
/// Safe browser review result. It deliberately contains only the device label
/// and expiry for a currently pending request. It never exposes a verifier,
/// credential, authorization id, installation id, or operator identity.
/// </summary>
public sealed record MemCliDeviceAuthorizationReview(
    string Status,
    string? DeviceLabel = null,
    DateTimeOffset? ExpiresAtUtc = null);

/// <summary>
/// Safe result for an authenticated browser approval or denial. It contains no
/// verifier, user code, browser cookie, or device-session credential.
/// </summary>
public sealed record MemCliDeviceAuthorizationDecision(
    string Status,
    DateTimeOffset? ExpiresAtUtc = null);

/// <summary>
/// Safe polling result. DeviceCredential is populated only once, only after
/// successful browser approval, and only after the polling CLI proves its
/// original verifier.
/// </summary>
public sealed record MemCliDeviceAuthorizationPollResult(
    string Status,
    string? DeviceCredential = null,
    DateTimeOffset? IdleExpiresAtUtc = null,
    DateTimeOffset? AbsoluteExpiresAtUtc = null);

/// <summary>
/// Result for a future CLI authentication handler. It exposes only safe
/// identifiers and expiry information; never the credential material.
/// </summary>
public sealed record MemCliDeviceSessionValidationResult(
    string Status,
    Guid? OperatorId = null,
    Guid? SessionId = null,
    DateTimeOffset? IdleExpiresAtUtc = null,
    DateTimeOffset? AbsoluteExpiresAtUtc = null);

/// <summary>
/// Safe current-session projection for a CLI device credential. It contains
/// neither the opaque credential nor an installation identifier, raw claim,
/// browser cookie, recovery state, or server exception detail.
/// </summary>
public sealed record MemCliDeviceSessionStatusResponse(
    string Status,
    string? DisplayName,
    IReadOnlyList<string> Roles,
    DateTimeOffset? IdleExpiresAtUtc,
    DateTimeOffset? AbsoluteExpiresAtUtc);
/// <summary>
/// Safe result for revoking the current CLI device session. It contains no
/// credential material, browser cookie, installation identifier, or operator
/// profile detail.
/// </summary>
public sealed record MemCliDeviceSessionRevocationResponse(
    string Status,
    DateTimeOffset? RevokedAtUtc = null);

/// <summary>
/// Internal service result for revoking a persisted CLI device session. It is
/// deliberately narrow so endpoint responses cannot accidentally expose the
/// opaque credential or server-side session key material.
/// </summary>
public sealed record MemCliDeviceSessionRevocationResult(
    string Status,
    DateTimeOffset? RevokedAtUtc = null);
