namespace Mem.Migrate.Core.Target;

public sealed record ActivationReadinessOptions
{
    public string FinalTargetStageReportPath { get; init; } = string.Empty;
    public string FreezeReportPath { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string CandidateId { get; init; } = string.Empty;
    public string PreviewId { get; init; } = string.Empty;
    public string ConfirmationId { get; init; } = string.Empty;
    public string OutputPath { get; init; } = ".";
    public string? AttemptId { get; init; }
    public int HttpTimeoutSeconds { get; init; } = 120;
    public bool AllowInsecureTls { get; init; }
    public bool JsonConsoleOutput { get; init; }

    public ActivationReadinessOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(FinalTargetStageReportPath)) throw new ArgumentException("A completed MM-06D final target stage report is required.");
        if (string.IsNullOrWhiteSpace(FreezeReportPath)) throw new ArgumentException("A completed MM-06B freeze report is required.");
        if (!TargetProfileValidator.TryNormalizeName(ProfileName, out var profile)) throw new ArgumentException("A valid named MEM CLI profile is required.");
        ValidateId(CandidateId, "candidate");
        ValidateId(PreviewId, "preview");
        ValidateId(ConfirmationId, "confirmation");
        if (HttpTimeoutSeconds is < 30 or > 900) throw new ArgumentOutOfRangeException(nameof(HttpTimeoutSeconds));
        var attempt = string.IsNullOrWhiteSpace(AttemptId) ? $"mm06e-readiness-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}" : AttemptId.Trim();
        ValidateId(attempt, "attempt");
        return this with
        {
            FinalTargetStageReportPath = Path.GetFullPath(FinalTargetStageReportPath),
            FreezeReportPath = Path.GetFullPath(FreezeReportPath),
            ProfileName = profile,
            CandidateId = CandidateId.Trim(), PreviewId = PreviewId.Trim(), ConfirmationId = ConfirmationId.Trim(),
            OutputPath = Path.GetFullPath(OutputPath), AttemptId = attempt
        };
    }

    private static void ValidateId(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 160 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
            throw new ArgumentException($"A valid {label} ID is required.");
    }
}

public sealed record ActivationReadinessReport(
    string Schema, int SchemaVersion, string AttemptId, string Status,
    DateTimeOffset CheckedAtUtc, string ProfileName, string TargetBaseUrl,
    string CatalogEntryId, string RestoreSessionId, string StagingId,
    string CandidateId, string PreviewId, string ConfirmationId,
    bool SourceFrozen, bool FinalCandidateRetained, bool ExecutionReady,
    string TargetStatus, string[] Blockers, string[] Warnings,
    string TargetResponsePath, string ReportPath, string[] NextSteps);
