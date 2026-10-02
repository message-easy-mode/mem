namespace Modules.Setup.Secrets;

public sealed record NpmAdminCredentialRequest(
    string? Email,
    string? Password);

public sealed record NpmAdminCredentialProjection(
    Guid? InstallationId,
    string Status,
    string Message,
    string? ErrorCode,
    string? AdministratorEmail,
    bool CredentialStored,
    DateTime? VerifiedAtUtc);

public sealed record NpmAdminCredential(
    Guid InstallationId,
    string Email,
    string Password,
    DateTime? VerifiedAtUtc);


internal sealed record NpmAdminCredentialValidation(
    bool IsValid,
    string? ErrorCode,
    string? Message,
    string? Email,
    string? Password);
