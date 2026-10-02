namespace Mem.Cli.Config;

/// <summary>
/// Raised when the temporary installer-token cookie transition cannot establish
/// a CLI session. The exception deliberately contains no token or response body.
/// </summary>
public sealed class InstallerAuthenticationException : Exception
{
    public InstallerAuthenticationException(
        InstallerAuthenticationFailureKind failureKind,
        int? statusCode = null)
        : base(failureKind.ToString())
    {
        FailureKind = failureKind;
        StatusCode = statusCode;
    }

    public InstallerAuthenticationFailureKind FailureKind { get; }

    public int? StatusCode { get; }
}

public enum InstallerAuthenticationFailureKind
{
    MissingInstallerToken,
    InstallerTokenRejected
}
