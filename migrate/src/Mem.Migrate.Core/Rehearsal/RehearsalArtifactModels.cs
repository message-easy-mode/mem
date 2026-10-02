using Mem.Migrate.Core.Capture;

namespace Mem.Migrate.Core.Rehearsal;

public sealed record RehearsalArtifactOptions
{
    public string ArchivePath { get; init; } = string.Empty;
    public string ConversionReportPath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = ".";
    public string? ArtifactId { get; init; }
    public string DockerCommand { get; init; } = "docker";
    public int CommandTimeoutSeconds { get; init; } = 300;
    public bool JsonConsoleOutput { get; init; }
    public long MaximumEntryBytes { get; init; } = 10L * 1024 * 1024 * 1024;
    public long MaximumExpandedBytes { get; init; } = 25L * 1024 * 1024 * 1024;
    public int MaximumEntries { get; init; } = 250_000;
    public double MaximumCompressionRatio { get; init; } = 100;

    public RehearsalArtifactOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(ArchivePath)) throw new ArgumentException("A migration archive path is required.");
        if (string.IsNullOrWhiteSpace(ConversionReportPath)) throw new ArgumentException("A conversion report path is required.");
        if (CommandTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(CommandTimeoutSeconds));
        var id = string.IsNullOrWhiteSpace(ArtifactId)
            ? $"mm05a-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..40]
            : NormalizeIdentifier(ArtifactId);
        return this with
        {
            ArchivePath = Path.GetFullPath(ArchivePath),
            ConversionReportPath = Path.GetFullPath(ConversionReportPath),
            OutputPath = Path.GetFullPath(OutputPath),
            ArtifactId = id,
            DockerCommand = string.IsNullOrWhiteSpace(DockerCommand) ? "docker" : DockerCommand.Trim()
        };
    }

    public ArchiveSafetyLimits ToSafetyLimits() => new(MaximumEntryBytes, MaximumExpandedBytes, MaximumEntries, MaximumCompressionRatio);

    private static string NormalizeIdentifier(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length is < 3 or > 80 || trimmed.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("Artifact IDs must contain 3-80 ASCII letters, digits, '-' or '_'.");
        return trimmed;
    }
}

public sealed record RehearsalArtifactReport(
    string Schema,
    int SchemaVersion,
    string ArtifactId,
    string MigrationId,
    string ConversionId,
    Guid SourceStackId,
    string MatrixServerName,
    string StackExportPath,
    string StackExportSha256,
    long StackExportBytes,
    string NeutralManifestPath,
    string NeutralManifestSha256,
    int IncludedFileCount,
    long MediaFiles,
    long MediaBytes,
    string[] Warnings,
    string[] NextSteps);
