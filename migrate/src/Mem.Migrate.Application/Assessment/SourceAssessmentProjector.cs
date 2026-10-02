using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Legacy.V010;

namespace Mem.Migrate.Application.Assessment;

public static class SourceAssessmentProjector
{
    public static SourceAssessmentView Project(AssessmentResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var exactCandidates = result.Database.Candidates
            .Where(candidate => candidate.ExactSupportedSchema)
            .ToArray();

        var relevantCandidates = exactCandidates.Length == 1
            ? exactCandidates
            : result.Database.Candidates
                .Where(candidate => candidate.AppSchemaPresent)
                .ToArray();

        var stackRecords = relevantCandidates
            .SelectMany(candidate => candidate.Stacks)
            .GroupBy(stack => stack.Id)
            .Select(group => group.First())
            .OrderBy(stack => stack.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(stack => stack.Slug, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var services = relevantCandidates
            .SelectMany(candidate => candidate.Services)
            .GroupBy(service => service.Id)
            .Select(group => group.First())
            .ToArray();

        var filesByStack = result.FileSystem.Stacks
            .GroupBy(stack => stack.StackId)
            .ToDictionary(group => group.Key, group => group.First());

        var stacks = stackRecords
            .Select(stack => ProjectStack(stack, services, filesByStack))
            .ToArray();

        var findings = result.Findings
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.Code, StringComparer.Ordinal)
            .Select(finding => new SourceFindingSummary(
                finding.Code,
                finding.Severity.ToString(),
                finding.Message,
                finding.Remediation))
            .ToArray();

        return new SourceAssessmentView(
            SchemaVersion: 1,
            AssessmentId: result.AssessmentId,
            CompletedAtUtc: result.CompletedAtUtc,
            Classification: result.Classification.ToString(),
            Recommendation: result.Recommendation.ToString(),
            CanProceedToCapture: result.CanProceedToCapture,
            SourceFingerprint: result.SourceFingerprint,
            Host: new SourceHostSummary(
                result.Host.OperatingSystem,
                result.Host.Architecture,
                result.Host.IsLinux),
            Runtime: new SourceRuntimeSummary(
                result.Docker.Available,
                result.Docker.ServerVersion,
                result.SystemConfig.Reachable,
                result.SystemConfig.ProductName,
                result.SystemConfig.ProductVersion,
                result.Database.ProbeAttempted),
            Counts: new SourceAssessmentCounts(
                result.Docker.Containers.Length,
                result.Database.Candidates.Length,
                exactCandidates.Length,
                result.FileSystem.Stacks.Length,
                stacks.Length,
                findings.Count(finding => string.Equals(finding.Severity, "Blocker", StringComparison.Ordinal)),
                findings.Count(finding => string.Equals(finding.Severity, "Warning", StringComparison.Ordinal))),
            Stacks: stacks,
            Findings: findings);
    }

    private static SourceStackSummary ProjectStack(
        LegacyStackRecord stack,
        IReadOnlyList<LegacyServiceRecord> services,
        IReadOnlyDictionary<Guid, LegacyStackFileObservation> filesByStack)
    {
        var matrix = services.FirstOrDefault(service =>
            service.StackId == stack.Id &&
            string.Equals(service.ServiceKey, V010Constants.MatrixServiceKey, StringComparison.OrdinalIgnoreCase));
        var element = services.FirstOrDefault(service =>
            service.StackId == stack.Id &&
            string.Equals(service.ServiceKey, V010Constants.ElementServiceKey, StringComparison.OrdinalIgnoreCase));

        filesByStack.TryGetValue(stack.Id, out var files);
        var blockers = files?.Findings.Count(finding => finding.Severity is FindingSeverity.Blocker) ?? 0;
        var warnings = files?.Findings.Count(finding => finding.Severity is FindingSeverity.Warning) ?? 0;
        var configurationReady = files is not null &&
                                 files.HomeserverConfiguration.Exists &&
                                 files.HomeserverConfiguration.IsRegularFile &&
                                 !files.HomeserverConfiguration.IsSymbolicLink &&
                                 files.ParsedConfiguration.Parsed;
        var databaseReady = files is not null &&
                            files.SqliteDatabase.Exists &&
                            files.SqliteDatabase.IsRegularFile &&
                            !files.SqliteDatabase.IsSymbolicLink;
        var signingKeyReady = files is not null &&
                              files.SigningKey.Exists &&
                              files.SigningKey.IsRegularFile &&
                              !files.SigningKey.IsSymbolicLink;
        var mediaReady = files is not null && files.MediaStore.Exists && files.MediaStore.Complete;

        return new SourceStackSummary(
            SourceStackId: stack.Id.ToString("D"),
            Slug: stack.Slug,
            Name: stack.Name,
            MatrixServerName: matrix?.ServerName ?? files?.ParsedConfiguration.ServerName,
            MatrixPublicHost: matrix?.MatrixPublicHost ?? matrix?.PublicDomain,
            ElementPublicHost: element?.ElementPublicHost ?? element?.PublicDomain,
            HomeserverConfigurationReady: configurationReady,
            MatrixDatabaseReady: databaseReady,
            SigningKeyReady: signingKeyReady,
            MediaStoreReady: mediaReady,
            MediaBytes: Math.Max(0, files?.MediaStore.TotalBytes ?? 0),
            Blockers: blockers,
            Warnings: warnings,
            SourceFilesReady: configurationReady && databaseReady && signingKeyReady && mediaReady && blockers == 0);
    }
}
