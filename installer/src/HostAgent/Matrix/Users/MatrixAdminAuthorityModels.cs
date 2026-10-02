namespace HostAgent.Matrix.Users;

public static class MatrixAdminAuthorityStates
{
    public const string Required = "required";
    public const string Available = "available";
    public const string Invalid = "invalid";
}

public enum MatrixAdminAuthorityFailureKind
{
    Required,
    InvalidRequest,
    Rejected,
    Unavailable
}

public sealed class MatrixAdminAuthorityException : Exception
{
    public MatrixAdminAuthorityException(
        string code,
        string safeDetail,
        MatrixAdminAuthorityFailureKind failureKind,
        Exception? innerException = null)
        : base(safeDetail, innerException)
    {
        Code = code;
        SafeDetail = safeDetail;
        FailureKind = failureKind;
    }

    public string Code { get; }
    public string SafeDetail { get; }
    public MatrixAdminAuthorityFailureKind FailureKind { get; }
}

public sealed record MatrixAdminAuthorityIdentity(
    string MatrixUserId,
    bool IsAdmin,
    bool IsDeactivated);

public sealed record MatrixAdminAuthorityLoginResult(
    string AccessToken,
    MatrixAdminAuthorityIdentity Identity);

public sealed record MatrixAdminAuthorityCredential(
    string AccessToken,
    string? AdminUserId,
    string? Source);

public sealed record MatrixAdminAuthorityStatus(
    string Status,
    bool CanResetPasswords,
    string? Source,
    string? AdminUserId,
    DateTime? StoredAtUtc,
    DateTime? LastValidatedAtUtc,
    string? ErrorCode)
{
    public static MatrixAdminAuthorityStatus Required() => new(
        MatrixAdminAuthorityStates.Required,
        CanResetPasswords: false,
        Source: null,
        AdminUserId: null,
        StoredAtUtc: null,
        LastValidatedAtUtc: null,
        ErrorCode: null);
}

public sealed record ImportMatrixAdminAuthorityRequest(
    string? AccessToken = null,
    string? MatrixUserId = null,
    string? Password = null);

public sealed record ResetRuntimeStackUserPasswordRequest(
    string NewPassword);

public sealed record RuntimeStackUserPasswordResetResponse(
    string Source,
    string Status,
    Guid RuntimeStackId,
    Guid UserId,
    string MatrixUserId,
    bool LogoutDevices,
    bool AdminAuthorityInvalidated,
    DateTime CompletedAtUtc);
