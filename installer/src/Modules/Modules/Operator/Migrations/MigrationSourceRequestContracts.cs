namespace Modules.Operator.Migrations;

/// <summary>
/// Public target-to-source handoff contract. The private age identity and target
/// filesystem paths are deliberately absent.
/// </summary>
public sealed record MigrationSourceRequestDto(
    string Schema,
    int SchemaVersion,
    string IntakeId,
    string? PackageRevisionId,
    string RequestKind,
    string AgeRecipient,
    string RecipientFingerprint,
    DateTimeOffset ExpiresAtUtc,
    string TargetControlPlaneVersion,
    string? SourceStackId);

public sealed record MigrationSourceRequestFile(
    string FileName,
    byte[] Contents,
    MigrationSourceRequestDto Request);

public sealed class MigrationSourceRequestException : Exception
{
    public MigrationSourceRequestException(
        string code,
        string message,
        int statusCode)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}
