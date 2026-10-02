using Mem.Migrate.Application.Assessment;
using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Infrastructure.Archive;
using Mem.Migrate.Infrastructure.Encryption;
using Mem.Migrate.Infrastructure.Persistence;
using Mem.Migrate.Infrastructure.Processes;
using Mem.Migrate.Infrastructure.Sqlite;
using Mem.Migrate.Legacy.V010.Capture;

namespace Mem.Migrate.Application.Workflow;

public static class SourceMigrationRuntimeFactory
{
    public static SourceMigrationApplicationService Create(
        AssessmentOptions assessmentOptions,
        string producerVersion,
        TimeProvider? timeProvider = null)
    {
        var normalized = assessmentOptions.Normalize();
        var processRunner = new ProcessRunner();
        var assessmentService = SourceAssessmentRuntimeFactory.CreateService(
            normalized,
            processRunner);
        var captureService = new V010SourceCaptureService(
            assessmentService,
            new SourceCaptureFileSystem(),
            new SqliteSnapshotter(),
            new MigrationArchiveWriter(),
            new MigrationArchiveReader(),
            new AgeEnvelope(processRunner),
            new SqliteCaptureJournal(normalized.WorkspacePath));
        var captureDiscovery = new SourceCaptureArchiveDiscovery(
            normalized.WorkspacePath);
        var packageService = new PackageForIntakeService(
            new MigrationArchiveReader(),
            new AgeEnvelope(processRunner),
            timeProvider);
        var workflowJournal = new SqliteSourceWorkflowJournal(
            normalized.WorkspacePath);

        return new SourceMigrationApplicationService(
            normalized,
            producerVersion,
            workflowJournal,
            captureService,
            captureDiscovery,
            packageService,
            new SecureIntakeRequestValidator(timeProvider),
            timeProvider);
    }
}
