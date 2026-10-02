namespace Mem.Migrate.Core.Assessment;

public interface IDockerInventoryProbe
{
    Task<DockerInventoryObservation> ProbeAsync(
        AssessmentOptions options,
        CancellationToken cancellationToken);
}

public interface ILegacyDatabaseProbe
{
    Task<LegacyDatabaseObservation> ProbeAsync(
        AssessmentOptions options,
        DockerInventoryObservation docker,
        CancellationToken cancellationToken);
}

public interface ISystemConfigProbe
{
    Task<SystemConfigObservation> ProbeAsync(
        AssessmentOptions options,
        DockerInventoryObservation docker,
        CancellationToken cancellationToken);
}

public interface ILegacyFileSystemProbe
{
    Task<LegacyFileSystemObservation> ProbeAsync(
        AssessmentOptions options,
        DockerInventoryObservation docker,
        LegacyDatabaseObservation database,
        CancellationToken cancellationToken);
}

public interface IHostObservationProvider
{
    HostObservation Observe(string workspacePath);
}

public interface IAssessmentJournal
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task SaveAsync(
        AssessmentResult result,
        string publicJson,
        string publicMarkdown,
        CancellationToken cancellationToken);

    Task<StoredAssessmentReport?> GetLatestAsync(CancellationToken cancellationToken);
}

public sealed record StoredAssessmentReport(
    string AssessmentId,
    DateTimeOffset CompletedAtUtc,
    AssessmentClassification Classification,
    string SourceFingerprint,
    string PublicJson,
    string PublicMarkdown);

public interface IAssessmentReportWriter
{
    AssessmentRenderedReports Render(
        AssessmentResult result,
        bool includeSensitivePaths);

    Task WriteAsync(
        AssessmentRenderedReports reports,
        string outputDirectory,
        string workspaceDirectory,
        CancellationToken cancellationToken);
}

public sealed record AssessmentRenderedReports(
    string PublicJson,
    string PublicMarkdown,
    string PrivateJson);
