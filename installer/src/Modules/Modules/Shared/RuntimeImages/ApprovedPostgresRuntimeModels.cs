namespace Modules.Shared.RuntimeImages;

public sealed record ApprovedPostgresRuntimePolicy(
    string ApprovedReference,
    int RequiredMajorVersion,
    string ExpectedVersion,
    bool AllowInstallPull,
    bool AllowOperationalPull);

public sealed record ApprovedPostgresRuntimeDescriptor(
    string ApprovedReference,
    string ResolvedImageId,
    IReadOnlyList<string> RepositoryDigests,
    int PostgresMajorVersion,
    string PostgresVersion,
    DateTime VerifiedAtUtc);

public sealed record RuntimeImageInspection(
    string ImageId,
    IReadOnlyList<string> RepositoryDigests,
    IReadOnlyList<string> EnvironmentVariables);

public interface IRuntimeImageInspector
{
    Task<RuntimeImageInspection?> InspectAsync(
        string immutableReference,
        CancellationToken cancellationToken);

    Task PullAsync(
        string immutableReference,
        CancellationToken cancellationToken);
}

public interface IApprovedPostgresRuntimeProvider
{
    ApprovedPostgresRuntimePolicy GetPolicy();

    Task<ApprovedPostgresRuntimeDescriptor> ResolveForInstallationAsync(
        CancellationToken cancellationToken);

    Task<ApprovedPostgresRuntimeDescriptor> ResolveForOperationAsync(
        CancellationToken cancellationToken);
}
