namespace Modules.Auth.Contracts;

/// <summary>
/// Changes only the password of the currently signed-in named MEM operator.
/// The request deliberately carries no operator id, current password, TOTP,
/// recovery code, or other authority that could turn this into an
/// administrator password-reset surface.
/// </summary>
public sealed record ChangeMemOperatorPasswordRequest(
    string? NewPassword,
    string? ConfirmPassword);

public sealed record MemOperatorPasswordChangeResponse(
    string Status);
