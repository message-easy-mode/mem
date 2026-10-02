using System.Text.Json.Serialization;
using Mem.Migrate.Core.Capture;

namespace Mem.Migrate.Core.Conversion;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConversionLifecycleStatus
{
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed record ConversionOptions
{
    public string ArchivePath { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = ".workspace";
    public string OutputPath { get; init; } = ".";
    public string? ConversionId { get; init; }
    public Guid? StackId { get; init; }
    public string DockerCommand { get; init; } = "docker";
    public string SynapseImage { get; init; } = string.Empty;
    public string PostgresImage { get; init; } = "postgres@sha256:be01cf82fc7dbba824acf0a82e150b4b360f3ff93c6631d7844af431e841a95c";
    public int CommandTimeoutSeconds { get; init; } = 900;
    public int ReadinessTimeoutSeconds { get; init; } = 120;
    public int BatchSize { get; init; } = 1000;
    public double RequiredFreeSpaceMultiplier { get; init; } = 3.0;
    public bool Resume { get; init; }
    public bool KeepResources { get; init; }
    public bool JsonConsoleOutput { get; init; }
    public long MaximumEntryBytes { get; init; } = 10L * 1024 * 1024 * 1024;
    public long MaximumExpandedBytes { get; init; } = 25L * 1024 * 1024 * 1024;
    public int MaximumEntries { get; init; } = 250_000;
    public double MaximumCompressionRatio { get; init; } = 100;

    public ConversionOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(ArchivePath))
        {
            throw new ArgumentException("A migration archive path is required.");
        }

        if (CommandTimeoutSeconds <= 0 || ReadinessTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CommandTimeoutSeconds));
        }

        if (BatchSize <= 0 || BatchSize > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(BatchSize));
        }

        if (RequiredFreeSpaceMultiplier < 2.0 || RequiredFreeSpaceMultiplier > 10.0)
        {
            throw new ArgumentOutOfRangeException(nameof(RequiredFreeSpaceMultiplier));
        }

        var archive = Path.GetFullPath(ArchivePath);
        var workspace = Path.GetFullPath(WorkspacePath);
        var output = Path.GetFullPath(OutputPath);
        var id = string.IsNullOrWhiteSpace(ConversionId)
            ? $"mm04-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..39]
            : NormalizeIdentifier(ConversionId);

        return this with
        {
            ArchivePath = archive,
            WorkspacePath = workspace,
            OutputPath = output,
            ConversionId = id,
            DockerCommand = DockerCommand.Trim(),
            SynapseImage = SynapseImage.Trim(),
            PostgresImage = PostgresImage.Trim()
        };
    }

    public ArchiveSafetyLimits ToSafetyLimits() => new(
        MaximumEntryBytes,
        MaximumExpandedBytes,
        MaximumEntries,
        MaximumCompressionRatio);

    private static string NormalizeIdentifier(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length is < 3 or > 80 ||
            trimmed.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new ArgumentException(
                "Conversion IDs must contain 3-80 ASCII letters, digits, '-' or '_'.");
        }

        return trimmed;
    }
}

public sealed record ConversionTableCount(string Table, long SourceRows, long TargetRows);

public sealed record ConversionEvidence(
    string Schema,
    int SchemaVersion,
    string ConversionId,
    string MigrationId,
    Guid SourceStackId,
    string MatrixServerName,
    string ArchiveSha256,
    string SynapseImage,
    string PostgresImage,
    string SigningKeySha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    ConversionTableCount[] TableCounts,
    string[] Warnings);

public sealed record ConversionReport(
    string Schema,
    int SchemaVersion,
    string ConversionId,
    ConversionLifecycleStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string MigrationId,
    Guid SourceStackId,
    string MatrixServerName,
    string ArchivePath,
    string ArchiveSha256,
    string SynapseImage,
    string PostgresImage,
    string PostgreSqlDumpPath,
    string PostgreSqlDumpSha256,
    long PostgreSqlDumpBytes,
    string EvidencePath,
    bool ResourcesRetained,
    ConversionTableCount[] TableCounts,
    string[] Warnings,
    string[] NextSteps);

public sealed record StoredConversionRun(
    string ConversionId,
    ConversionLifecycleStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string ArchiveSha256,
    Guid SourceStackId,
    string? ReportJson,
    string? ErrorCode,
    string? ErrorMessage);
