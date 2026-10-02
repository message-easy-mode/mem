using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Reporting;
using Mem.Migrate.Infrastructure.Docker;
using Mem.Migrate.Infrastructure.FileSystem;
using Mem.Migrate.Infrastructure.Host;
using Mem.Migrate.Infrastructure.Http;
using Mem.Migrate.Infrastructure.Persistence;
using Mem.Migrate.Infrastructure.Postgres;
using Mem.Migrate.Infrastructure.Processes;
using Mem.Migrate.Legacy.V010;

namespace Mem.Migrate.Application.Assessment;

public static class SourceAssessmentRuntimeFactory
{
    public static V010AssessmentService CreateService(
        AssessmentOptions options,
        ProcessRunner? processRunner = null)
    {
        var normalized = options.Normalize();
        processRunner ??= new ProcessRunner();

        return new V010AssessmentService(
            new HostObservationProvider(),
            new DockerInventoryProbe(processRunner),
            new LegacyPostgresProbe(processRunner),
            new SystemConfigProbe(),
            new LegacyFileSystemProbe(),
            new AssessmentReportWriter(),
            new SqliteAssessmentJournal(normalized.WorkspacePath));
    }

    public static ISourceAssessmentApplicationService CreateApplication(
        AssessmentOptions options,
        ProcessRunner? processRunner = null)
    {
        var normalized = options.Normalize();
        var journal = new SqliteAssessmentJournal(normalized.WorkspacePath);
        processRunner ??= new ProcessRunner();
        var service = new V010AssessmentService(
            new HostObservationProvider(),
            new DockerInventoryProbe(processRunner),
            new LegacyPostgresProbe(processRunner),
            new SystemConfigProbe(),
            new LegacyFileSystemProbe(),
            new AssessmentReportWriter(),
            journal);

        return new SourceAssessmentApplicationService(normalized, service, journal);
    }
}
