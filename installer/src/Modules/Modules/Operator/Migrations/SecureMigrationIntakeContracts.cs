namespace Modules.Operator.Migrations;

public sealed record SecureMigrationIntakeCreateRequest(string DisplayName);

public sealed record SecureMigrationIntakeDto(
    string IntakeId,
    string DisplayName,
    string Status,
    string AgeRecipient,
    string RecipientFingerprint,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    string? PackageFileName = null,
    long? PackageSizeBytes = null,
    string? EncryptedPackageSha256 = null,
    string? DecryptedArchiveSha256 = null,
    DateTime? PackageUploadedAtUtc = null,
    DateTime? PackageValidatedAtUtc = null,
    string? ArchiveMigrationId = null,
    string? ArchiveSourceProduct = null,
    string? ArchiveSourceVersion = null,
    int? ArchiveStackCount = null);

public sealed record SecureMigrationIntakeProblemResponse(string Code, string Message);
