namespace Modules.Operator.Npm;

public sealed record OperatorNpmSettingsProjection(
    string RuntimeStatus,
    bool RuntimeExists,
    bool Running,
    string? RuntimeState,
    string? RuntimeImage,
    string ApprovedRuntimeImage,
    string ApprovedRuntimeVersion,
    bool RuntimeImageAligned,
    string? BrowserUrl,
    string? AdministratorEmail,
    bool CredentialStored,
    string CredentialStatus,
    DateTime? LastVerifiedAtUtc,
    string? Warning);

public sealed record OperatorNpmCredentialRevealResponse(
    string AdministratorEmail,
    string Password);

public sealed record UpdateOperatorNpmCredentialRequest(
    string? Email,
    string? Password);

public enum OperatorNpmCredentialFailureKind
{
    Validation,
    NotInstalled,
    CredentialUnavailable,
    CredentialRejected,
    VerificationUnavailable
}

public sealed class OperatorNpmCredentialException(
    OperatorNpmCredentialFailureKind kind,
    string code,
    string message) : InvalidOperationException(message)
{
    public OperatorNpmCredentialFailureKind Kind { get; } = kind;
    public string Code { get; } = code;
}
