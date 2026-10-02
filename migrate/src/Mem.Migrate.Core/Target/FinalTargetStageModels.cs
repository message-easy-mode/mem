using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Conversion;
using Mem.Migrate.Core.Rehearsal;

namespace Mem.Migrate.Core.Target;

public sealed record FinalTargetStageOptions
{
    public string ArchivePath { get; init; } = string.Empty;
    public string FreezeReportPath { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = ".workspace";
    public string OutputPath { get; init; } = ".";
    public string? AttemptId { get; init; }
    public string DockerCommand { get; init; } = "docker";
    public string SynapseImage { get; init; } = string.Empty;
    public string PostgresImage { get; init; } = "postgres@sha256:be01cf82fc7dbba824acf0a82e150b4b360f3ff93c6631d7844af431e841a95c";
    public int CommandTimeoutSeconds { get; init; } = 900;
    public int ReadinessTimeoutSeconds { get; init; } = 180;
    public int HttpTimeoutSeconds { get; init; } = 900;
    public bool AllowInsecureTls { get; init; }
    public bool Resume { get; init; }
    public bool JsonConsoleOutput { get; init; }

    public FinalTargetStageOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(ArchivePath)) throw new ArgumentException("A final frozen migration archive is required.");
        if (string.IsNullOrWhiteSpace(FreezeReportPath)) throw new ArgumentException("A completed MM-06B freeze report is required.");
        if (!TargetProfileValidator.TryNormalizeName(ProfileName, out var profile)) throw new ArgumentException("A valid named MEM CLI profile is required.");
        if (CommandTimeoutSeconds <= 0 || ReadinessTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(CommandTimeoutSeconds));
        if (HttpTimeoutSeconds is < 30 or > 3600) throw new ArgumentOutOfRangeException(nameof(HttpTimeoutSeconds));
        var id = string.IsNullOrWhiteSpace(AttemptId) ? $"mm06d-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}" : AttemptId.Trim();
        if (id.Length is < 8 or > 80 || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) throw new ArgumentException("Attempt ID must contain 8-80 ASCII letters, digits, '-' or '_'.");
        if (Resume && string.IsNullOrWhiteSpace(AttemptId)) throw new ArgumentException("--resume requires an explicit --attempt-id.");
        return this with
        {
            ArchivePath = Path.GetFullPath(ArchivePath), FreezeReportPath = Path.GetFullPath(FreezeReportPath),
            ProfileName = profile, WorkspacePath = Path.GetFullPath(WorkspacePath), OutputPath = Path.GetFullPath(OutputPath),
            AttemptId = id, DockerCommand = string.IsNullOrWhiteSpace(DockerCommand) ? "docker" : DockerCommand.Trim(),
            SynapseImage = SynapseImage.Trim(), PostgresImage = PostgresImage.Trim()
        };
    }

    public ArchiveSafetyLimits ToSafetyLimits() => new(10L * 1024 * 1024 * 1024, 25L * 1024 * 1024 * 1024, 250_000, 100);
}

public sealed record FinalTargetStageReport(
    string Schema, int SchemaVersion, string AttemptId, string Status,
    DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc,
    string FreezeAttemptId, string SourceFingerprint, string ArchivePath, string ArchiveSha256,
    string ConversionId, string ArtifactId, string ImportAttemptId, string StageAttemptId,
    string CatalogEntryId, string RestoreSessionId, string StagingId,
    bool FinalArchive, bool SourceFrozen, bool PrivateOnly, bool PublishedRoutesAbsent,
    bool CandidateRetained, string ReportPath, string[] Warnings, string[] NextSteps);

public interface ITargetImportService
{
    Task<TargetImportReport> ImportAsync(TargetImportOptions options, CancellationToken cancellationToken);
}

public interface ITargetPrivateStageService
{
    Task<TargetPrivateStageReport> RunAsync(TargetPrivateStageOptions options, CancellationToken cancellationToken);
}
