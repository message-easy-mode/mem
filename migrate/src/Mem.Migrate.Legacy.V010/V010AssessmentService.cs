using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Fingerprints;

namespace Mem.Migrate.Legacy.V010;

public sealed class V010AssessmentService(
    IHostObservationProvider hostProvider,
    IDockerInventoryProbe dockerProbe,
    ILegacyDatabaseProbe databaseProbe,
    ISystemConfigProbe systemConfigProbe,
    ILegacyFileSystemProbe fileSystemProbe,
    IAssessmentReportWriter reportWriter,
    IAssessmentJournal journal)
{
    public async Task<AssessmentExecutionResult> RunAsync(
        AssessmentOptions rawOptions,
        CancellationToken cancellationToken)
    {
        var options = rawOptions.Normalize();
        Directory.CreateDirectory(options.WorkspacePath);
        Directory.CreateDirectory(options.OutputPath);

        await journal.InitializeAsync(cancellationToken);

        var startedAtUtc = DateTimeOffset.UtcNow;
        var host = hostProvider.Observe(options.WorkspacePath);
        var docker = await dockerProbe.ProbeAsync(
            options,
            cancellationToken);
        var database = await databaseProbe.ProbeAsync(
            options,
            docker,
            cancellationToken);
        var systemConfig = await systemConfigProbe.ProbeAsync(
            options,
            docker,
            cancellationToken);
        var fileSystem = await fileSystemProbe.ProbeAsync(
            options,
            docker,
            database,
            cancellationToken);
        var classification = V010Classifier.Classify(
            docker,
            systemConfig,
            database,
            fileSystem);
        var fingerprint = SourceFingerprint.Compute(
            host,
            docker,
            systemConfig,
            database,
            fileSystem);
        var completedAtUtc = DateTimeOffset.UtcNow;

        var result = new AssessmentResult(
            Schema: "mem-migrate-assessment",
            SchemaVersion: 1,
            AssessmentId:
                $"{completedAtUtc:yyyyMMdd-HHmmssZ}-{Guid.NewGuid():N}",
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAtUtc,
            Classification: classification.Classification,
            Recommendation: classification.Recommendation,
            CanProceedToCapture: classification.CanProceedToCapture,
            SourceFingerprint: fingerprint,
            Host: host,
            Docker: docker,
            SystemConfig: systemConfig,
            Database: database,
            FileSystem: fileSystem,
            Signals: classification.Signals,
            Findings: classification.Findings);

        var reports = reportWriter.Render(
            result,
            options.IncludeSensitivePaths);

        await reportWriter.WriteAsync(
            reports,
            options.OutputPath,
            options.WorkspacePath,
            cancellationToken);

        await journal.SaveAsync(
            result,
            reports.PublicJson,
            reports.PublicMarkdown,
            cancellationToken);

        return new AssessmentExecutionResult(result, reports);
    }
}

public sealed record AssessmentExecutionResult(
    AssessmentResult Result,
    AssessmentRenderedReports Reports);
