using System.Security.Cryptography;
using System.Text;

namespace Mem.Migrate.Core.Capture;

public sealed record PackageForIntakeOptions
{
    public string IntakeId { get; init; } = string.Empty;
    public string PackageRevisionId { get; init; } = string.Empty;
    public string AgeRecipient { get; init; } = string.Empty;
    public string RecipientFingerprint { get; init; } = string.Empty;
    public Guid? SourceStackId { get; init; }
    public string ArchivePath { get; init; } = string.Empty;
    public string OutputDirectory { get; init; } = ".";
    public string WorkspacePath { get; init; } = string.Empty;
    public string AgeCommand { get; init; } = "age";
    public long MaximumEntryBytes { get; init; } = 10L * 1024 * 1024 * 1024;
    public long MaximumExpandedBytes { get; init; } = 25L * 1024 * 1024 * 1024;
    public int MaximumEntries { get; init; } = 250_000;
    public double MaximumCompressionRatio { get; init; } = 100;
    public bool RequireFinalFrozen { get; init; }
    public bool JsonConsoleOutput { get; init; }

    public PackageForIntakeOptions Normalize()
    {
        var intakeId = IntakeId.Trim();
        var packageRevisionId = PackageRevisionId.Trim();
        var recipient = AgeRecipient.Trim();
        var fingerprint = RecipientFingerprint.Trim().ToUpperInvariant();

        if (!CaptureIdentifier.IsValid(intakeId))
        {
            throw new ArgumentException(
                "Intake ID contains unsupported characters.",
                nameof(IntakeId));
        }

        if (RequireFinalFrozen && !CaptureIdentifier.IsValid(packageRevisionId))
        {
            throw new ArgumentException(
                "A package revision ID is required for final frozen packaging.",
                nameof(PackageRevisionId));
        }

        if (packageRevisionId.Length > 0 && !CaptureIdentifier.IsValid(packageRevisionId))
        {
            throw new ArgumentException(
                "Package revision ID contains unsupported characters.",
                nameof(PackageRevisionId));
        }

        if (SourceStackId == Guid.Empty)
        {
            throw new ArgumentException(
                "Source stack ID cannot be empty.",
                nameof(SourceStackId));
        }

        if (!IsNativeAgeRecipient(recipient))
        {
            throw new ArgumentException(
                "Age recipient must be one native X25519 recipient beginning with age1.",
                nameof(AgeRecipient));
        }

        if (!IsFingerprint(fingerprint))
        {
            throw new ArgumentException(
                "Recipient fingerprint must use XXXX-XXXX-XXXX-XXXX hexadecimal form.",
                nameof(RecipientFingerprint));
        }

        var archivePath = ArchivePath.Trim();
        if (archivePath.Length > 0 &&
            !archivePath.EndsWith(".memmigration.zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Archive path must identify a plaintext .memmigration.zip file.",
                nameof(ArchivePath));
        }

        var outputDirectory = string.IsNullOrWhiteSpace(OutputDirectory)
            ? "."
            : OutputDirectory.Trim();
        var workspacePath = WorkspacePath.Trim();

        if (string.IsNullOrWhiteSpace(AgeCommand))
        {
            throw new ArgumentException("The age command cannot be empty.", nameof(AgeCommand));
        }

        if (MaximumEntryBytes <= 0 || MaximumExpandedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumExpandedBytes),
                "Archive byte limits must be positive.");
        }

        if (MaximumEntryBytes > MaximumExpandedBytes)
        {
            throw new ArgumentException(
                "Maximum entry bytes cannot exceed maximum expanded bytes.");
        }

        if (MaximumEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumEntries),
                "Maximum archive entries must be positive.");
        }

        if (MaximumCompressionRatio < 1 || MaximumCompressionRatio > 10_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumCompressionRatio),
                "Maximum compression ratio must be between 1 and 10000.");
        }

        return this with
        {
            IntakeId = intakeId,
            PackageRevisionId = packageRevisionId,
            AgeRecipient = recipient,
            RecipientFingerprint = fingerprint,
            ArchivePath = archivePath.Length == 0
                ? string.Empty
                : Path.GetFullPath(archivePath),
            OutputDirectory = Path.GetFullPath(outputDirectory),
            WorkspacePath = workspacePath.Length == 0
                ? string.Empty
                : Path.GetFullPath(workspacePath),
            AgeCommand = AgeCommand.Trim()
        };
    }

    public ArchiveSafetyLimits ToSafetyLimits() =>
        new(
            MaximumEntryBytes,
            MaximumExpandedBytes,
            MaximumEntries,
            MaximumCompressionRatio);

    public static string CalculateRecipientFingerprint(string recipient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(recipient.Trim())));
        return $"{hash[..4]}-{hash.Substring(4, 4)}-{hash.Substring(8, 4)}-{hash.Substring(12, 4)}";
    }

    private static bool IsFingerprint(string value) =>
        value.Length == 19 &&
        value[4] == '-' &&
        value[9] == '-' &&
        value[14] == '-' &&
        value.Where(character => character != '-').All(Uri.IsHexDigit);

    private static bool IsNativeAgeRecipient(string value) =>
        value.Length == 62 &&
        value.StartsWith("age1", StringComparison.Ordinal) &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) &&
            !char.IsAsciiLetterUpper(character));
}

public sealed record PackageForIntakeReport(
    string Schema,
    int SchemaVersion,
    string IntakeId,
    string? PackageRevisionId,
    DateTimeOffset CompletedAtUtc,
    string RecipientFingerprint,
    string RequestedCaptureKind,
    string MigrationId,
    string CaptureKind,
    bool SourceFrozen,
    bool RehearsalOnly,
    Guid SourceStackId,
    string SourceStackSlug,
    string MatrixServerName,
    int StackCount,
    string SourceArchivePath,
    string SourceArchiveSha256,
    long SourceArchiveBytes,
    int VerifiedFileCount,
    long VerifiedExpandedBytes,
    string EncryptedPackagePath,
    string EncryptedPackageSha256,
    long EncryptedPackageBytes,
    string JsonReportPath,
    string MarkdownReportPath);
