using System.Text.Json.Serialization;
using Mem.Migrate.Core.Capture;

namespace Mem.Migrate.Core.Conversion;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CandidateLifecycleStatus { Running, Completed, Failed, Cancelled }

public sealed record CandidateOptions
{
    public string ArchivePath { get; init; } = string.Empty;
    public string ConversionReportPath { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = ".workspace";
    public string OutputPath { get; init; } = ".";
    public string? CandidateId { get; init; }
    public string DockerCommand { get; init; } = "docker";
    public string SynapseImage { get; init; } = string.Empty;
    public string PostgresImage { get; init; } = "postgres@sha256:be01cf82fc7dbba824acf0a82e150b4b360f3ff93c6631d7844af431e841a95c";
    public int CommandTimeoutSeconds { get; init; } = 900;
    public int ReadinessTimeoutSeconds { get; init; } = 180;
    public bool KeepResources { get; init; }
    public bool JsonConsoleOutput { get; init; }
    public long MaximumEntryBytes { get; init; } = 10L * 1024 * 1024 * 1024;
    public long MaximumExpandedBytes { get; init; } = 25L * 1024 * 1024 * 1024;
    public int MaximumEntries { get; init; } = 250_000;
    public double MaximumCompressionRatio { get; init; } = 100;

    public CandidateOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(ArchivePath) || string.IsNullOrWhiteSpace(ConversionReportPath))
            throw new ArgumentException("A verified archive and MM-04A conversion report are required.");
        if (CommandTimeoutSeconds <= 0 || ReadinessTimeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(CommandTimeoutSeconds));
        var id = string.IsNullOrWhiteSpace(CandidateId)
            ? $"mm04b-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..40]
            : NormalizeIdentifier(CandidateId);
        return this with {
            ArchivePath = Path.GetFullPath(ArchivePath),
            ConversionReportPath = Path.GetFullPath(ConversionReportPath),
            WorkspacePath = Path.GetFullPath(WorkspacePath),
            OutputPath = Path.GetFullPath(OutputPath),
            CandidateId = id,
            DockerCommand = DockerCommand.Trim(),
            SynapseImage = SynapseImage.Trim(),
            PostgresImage = PostgresImage.Trim()
        };
    }

    public ArchiveSafetyLimits ToSafetyLimits() => new(MaximumEntryBytes, MaximumExpandedBytes, MaximumEntries, MaximumCompressionRatio);

    private static string NormalizeIdentifier(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length is < 3 or > 80 || trimmed.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("Candidate IDs must contain 3-80 ASCII letters, digits, '-' or '_'.");
        return trimmed;
    }
}

public sealed record CandidateIdentityEvidence(
    string MatrixServerName,
    string SigningKeySha256,
    string SigningKeyId,
    string ReportedServerName,
    string[] ReportedVerifyKeyIds);

public sealed record CandidateEvidence(
    string Schema,
    int SchemaVersion,
    string CandidateId,
    string ConversionId,
    string MigrationId,
    Guid SourceStackId,
    string ArchiveSha256,
    string PostgreSqlDumpSha256,
    string SynapseImage,
    string PostgresImage,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    CandidateIdentityEvidence Identity,
    ConversionTableCount[] TableCounts,
    bool ClientVersionsHealthy,
    bool ServerKeyHealthy,
    bool PublishedPortsAbsent,
    bool PublicIngressCreated,
    string[] Warnings);

public sealed record CandidateReport(
    string Schema,
    int SchemaVersion,
    string CandidateId,
    CandidateLifecycleStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string ConversionId,
    string MigrationId,
    Guid SourceStackId,
    string MatrixServerName,
    string ArchivePath,
    string ArchiveSha256,
    string PostgreSqlDumpPath,
    string PostgreSqlDumpSha256,
    string SynapseImage,
    string PostgresImage,
    string EvidencePath,
    string CandidateLogPath,
    bool ResourcesRetained,
    bool Go,
    CandidateIdentityEvidence Identity,
    ConversionTableCount[] TableCounts,
    string[] Warnings,
    string[] NextSteps);
