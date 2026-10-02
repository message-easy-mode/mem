using Mem.Migrate.Core.Assessment;

namespace Mem.Migrate.Core.Capture;

public sealed record CaptureOptions
{
    public AssessmentOptions Assessment { get; init; } = new();
    public string OutputPath { get; init; } = ".";
    public string? ExpectedSourceFingerprint { get; init; }
    public string? ExpectedSourceIdentity { get; init; }
    public Guid? SourceStackId { get; init; }
    public string? AgeRecipient { get; init; }
    public string AgeCommand { get; init; } = "age";
    public string? CaptureId { get; init; }
    public bool Resume { get; init; }
    public long MaximumEntryBytes { get; init; } = 10L * 1024 * 1024 * 1024;
    public long MaximumExpandedBytes { get; init; } = 25L * 1024 * 1024 * 1024;
    public int MaximumEntries { get; init; } = 250_000;
    public double RequiredFreeSpaceMultiplier { get; init; } = 2.25;
    public bool JsonConsoleOutput { get; init; }
    public string ProducerVersion { get; init; } = "unknown";
    public bool FinalFrozenCapture { get; init; }
    public string? FreezeReportPath { get; init; }

    public CaptureOptions Normalize()
    {
        var assessment = Assessment.Normalize();
        var outputPath = Path.GetFullPath(OutputPath);

        if (MaximumEntryBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumEntryBytes),
                "Maximum entry bytes must be positive.");
        }

        if (MaximumExpandedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumExpandedBytes),
                "Maximum expanded bytes must be positive.");
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

        if (RequiredFreeSpaceMultiplier < 1.25 ||
            RequiredFreeSpaceMultiplier > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RequiredFreeSpaceMultiplier),
                "Required free-space multiplier must be between 1.25 and 10.");
        }

        if (SourceStackId == Guid.Empty)
        {
            throw new ArgumentException("Source stack ID cannot be empty.", nameof(SourceStackId));
        }

        if (!string.IsNullOrWhiteSpace(ExpectedSourceFingerprint) &&
            !IsSha256(ExpectedSourceFingerprint))
        {
            throw new ArgumentException(
                "Expected source fingerprint must be a 64-character SHA-256 value.");
        }

        if (!string.IsNullOrWhiteSpace(ExpectedSourceIdentity) &&
            !IsSha256(ExpectedSourceIdentity))
        {
            throw new ArgumentException(
                "Expected source identity must be a 64-character SHA-256 value.");
        }

        if (!string.IsNullOrWhiteSpace(CaptureId) &&
            !CaptureIdentifier.IsValid(CaptureId))
        {
            throw new ArgumentException(
                "Capture ID contains unsupported characters.");
        }

        if (Resume && string.IsNullOrWhiteSpace(CaptureId))
        {
            throw new ArgumentException(
                "--resume requires an explicit --capture-id.");
        }

        if (!string.IsNullOrWhiteSpace(AgeRecipient) &&
            !IsNativeAgeRecipient(AgeRecipient.Trim()))
        {
            throw new ArgumentException(
                "Age recipient must be one native X25519 recipient beginning with age1.");
        }

        if (string.IsNullOrWhiteSpace(AgeCommand))
        {
            throw new ArgumentException("The age command cannot be empty.");
        }

        if (FinalFrozenCapture && string.IsNullOrWhiteSpace(FreezeReportPath))
        {
            throw new ArgumentException("Final frozen capture requires --freeze-report.");
        }

        if (!FinalFrozenCapture && !string.IsNullOrWhiteSpace(FreezeReportPath))
        {
            throw new ArgumentException("--freeze-report may be used only with --final-frozen.");
        }

        if (string.IsNullOrWhiteSpace(ProducerVersion))
        {
            throw new ArgumentException("The mem-migrate producer version is required.");
        }

        return this with
        {
            Assessment = assessment,
            OutputPath = outputPath,
            ExpectedSourceFingerprint = ExpectedSourceFingerprint?.ToLowerInvariant(),
            ExpectedSourceIdentity = ExpectedSourceIdentity?.ToLowerInvariant(),
            CaptureId = string.IsNullOrWhiteSpace(CaptureId)
                ? null
                : CaptureId.Trim(),
            AgeRecipient = string.IsNullOrWhiteSpace(AgeRecipient)
                ? null
                : AgeRecipient.Trim(),
            AgeCommand = AgeCommand.Trim(),
            ProducerVersion = ProducerVersion.Trim(),
            FreezeReportPath = string.IsNullOrWhiteSpace(FreezeReportPath)
                ? null
                : Path.GetFullPath(FreezeReportPath)
        };
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsNativeAgeRecipient(string value) =>
        value.Length == 62 &&
        value.StartsWith("age1", StringComparison.Ordinal) &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) &&
            !char.IsAsciiLetterUpper(character));
}

public sealed record ArchiveReadOptions
{
    public required string ArchivePath { get; init; }
    public string WorkspacePath { get; init; } = ".workspace";
    public string? AgeIdentityPath { get; init; }
    public string AgeCommand { get; init; } = "age";
    public long MaximumEntryBytes { get; init; } = 10L * 1024 * 1024 * 1024;
    public long MaximumExpandedBytes { get; init; } = 25L * 1024 * 1024 * 1024;
    public int MaximumEntries { get; init; } = 250_000;
    public double MaximumCompressionRatio { get; init; } = 100;
    public bool JsonConsoleOutput { get; init; }

    public ArchiveReadOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(ArchivePath))
        {
            throw new ArgumentException("Archive path is required.");
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
            ArchivePath = Path.GetFullPath(ArchivePath),
            WorkspacePath = Path.GetFullPath(WorkspacePath),
            AgeIdentityPath = string.IsNullOrWhiteSpace(AgeIdentityPath)
                ? null
                : Path.GetFullPath(AgeIdentityPath),
            AgeCommand = string.IsNullOrWhiteSpace(AgeCommand)
                ? throw new ArgumentException("The age command cannot be empty.")
                : AgeCommand.Trim()
        };
    }
}

public static class CaptureIdentifier
{
    public static string Create(DateTimeOffset timestamp) =>
        $"{timestamp:yyyyMMdd-HHmmssZ}-{Guid.NewGuid():N}";

    public static bool IsValid(string value) =>
        value.Length is >= 8 and <= 96 &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
