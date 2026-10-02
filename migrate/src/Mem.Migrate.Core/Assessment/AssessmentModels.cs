using System.Text.Json.Serialization;

namespace Mem.Migrate.Core.Assessment;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AssessmentClassification
{
    ConfirmedSupportedV010,
    ProbableV010,
    PartialRepairableV010,
    UnsupportedSource,
    AmbiguousMultipleInstallations,
    CurrentV011Present,
    Blocked,
    ExecutionFailed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FindingSeverity
{
    Information,
    Warning,
    Blocker
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MigrationRecommendation
{
    NewServerRecommended,
    NewServerRequired,
    InPlacePotentiallyFeasible,
    Unsupported,
    ResolveBlockers
}

public sealed record AssessmentFinding(
    string Code,
    FindingSeverity Severity,
    string Message,
    string? Remediation = null);

public sealed record AssessmentSignal(
    string Code,
    bool Present,
    int Weight,
    string Summary);

public sealed record HostObservation(
    string OperatingSystem,
    string Architecture,
    string MachineName,
    string CurrentUser,
    string WorkspacePath,
    long WorkspaceAvailableBytes,
    bool IsLinux,
    DateTimeOffset ObservedAtUtc);

public sealed record DockerPortBinding(
    string ContainerPort,
    string? HostIp,
    string? HostPort);

public sealed record DockerMountObservation(
    string Type,
    string? Name,
    string Source,
    string Destination,
    bool ReadWrite);

public sealed record DockerNetworkAttachment(
    string Name,
    string? IpAddress,
    string[] Aliases);

public sealed record DockerContainerObservation(
    string Id,
    string Name,
    string Image,
    string ImageId,
    string State,
    string? Health,
    string RestartPolicy,
    DateTimeOffset? CreatedAtUtc,
    string? ComposeProject,
    string? ComposeService,
    IReadOnlyDictionary<string, string> ManagedLabels,
    string[] EnvironmentNames,
    IReadOnlyDictionary<string, string> SafeEnvironment,
    DockerPortBinding[] Ports,
    DockerMountObservation[] Mounts,
    DockerNetworkAttachment[] Networks);

public sealed record DockerNetworkObservation(
    string Id,
    string Name,
    string Driver,
    string Scope,
    bool Internal,
    IReadOnlyDictionary<string, string> ManagedLabels);

public sealed record DockerInventoryObservation(
    bool Available,
    string? ServerVersion,
    string? ErrorCode,
    string? ErrorMessage,
    DockerContainerObservation[] Containers,
    DockerNetworkObservation[] Networks,
    DateTimeOffset ObservedAtUtc);

public sealed record SystemConfigObservation(
    bool Reachable,
    Uri? Endpoint,
    string? ProductName,
    string? ProductVersion,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record LegacyStackRecord(
    Guid Id,
    Guid OwnerUserId,
    string Slug,
    string Name,
    int Status,
    string? Description,
    string? PrimaryUrl,
    Guid? MatrixInstanceId,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record LegacyServiceRecord(
    Guid Id,
    Guid StackId,
    string ServiceKey,
    int Status,
    string Image,
    string Version,
    string? DockerContainerId,
    int? HostPort,
    string? BaseUrl,
    string? ServerName,
    string? DataPath,
    bool HasAdminAccessToken,
    string? MatrixPublicHost,
    string? MatrixInternalHost,
    Guid? HomeserverInstanceId,
    string? ElementPublicHost,
    string? ElementInternalHost,
    string? PublicRouteId,
    string? PublicDomain,
    string? InternalRouteId,
    string? InternalDomain,
    string? ForwardHost,
    int? ForwardPort,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record LegacyPlatformRouteRecord(
    string Key,
    string Version,
    string? WebDomain,
    string? WebForwardHost,
    int? WebForwardPort,
    string? WebRouteId,
    string? ApiDomain,
    string? ApiForwardHost,
    int? ApiForwardPort,
    string? ApiRouteId,
    bool Completed,
    DateTimeOffset? CompletedAtUtc);

public sealed record LegacyDatabaseCandidateObservation(
    string ContainerId,
    string ContainerName,
    string DatabaseName,
    string DatabaseUser,
    string? ServerVersion,
    bool AppSchemaPresent,
    bool ExactSupportedSchema,
    string[] MigrationIds,
    string[] TableNames,
    string[] MissingTables,
    string[] UnexpectedTables,
    IReadOnlyDictionary<string, long> RowCounts,
    LegacyStackRecord[] Stacks,
    LegacyServiceRecord[] Services,
    LegacyPlatformRouteRecord[] PlatformRoutes,
    long ActiveGuestChats,
    long ActivePasswordResetRequests,
    long ProvisioningJobs,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record LegacyDatabaseObservation(
    bool ProbeAttempted,
    LegacyDatabaseCandidateObservation[] Candidates,
    DateTimeOffset ObservedAtUtc);

public sealed record FileObservation(
    string Path,
    bool Exists,
    bool IsRegularFile,
    bool IsSymbolicLink,
    long? SizeBytes,
    DateTimeOffset? LastWriteAtUtc,
    string? ErrorCode);

public sealed record DirectorySizeObservation(
    string Path,
    bool Exists,
    long TotalBytes,
    long FileCount,
    bool Complete,
    string? ErrorCode);

public sealed record HomeserverConfigurationObservation(
    string Path,
    bool Parsed,
    string? ServerName,
    string? DatabaseEngine,
    string? DatabasePath,
    string? MediaStorePath,
    string? SigningKeyPath,
    string? ErrorCode);

public sealed record LegacyStackFileObservation(
    Guid StackId,
    Guid MatrixServiceId,
    string? MatrixContainerId,
    string? DataRoot,
    FileObservation HomeserverConfiguration,
    FileObservation SqliteDatabase,
    FileObservation SigningKey,
    DirectorySizeObservation MediaStore,
    HomeserverConfigurationObservation ParsedConfiguration,
    Guid? ElementServiceId,
    string? ElementDataRoot,
    FileObservation? ElementConfiguration,
    AssessmentFinding[] Findings);

public sealed record LegacyFileSystemObservation(
    LegacyStackFileObservation[] Stacks,
    DateTimeOffset ObservedAtUtc);

public sealed record AssessmentResult(
    string Schema,
    int SchemaVersion,
    string AssessmentId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    AssessmentClassification Classification,
    MigrationRecommendation Recommendation,
    bool CanProceedToCapture,
    string SourceFingerprint,
    HostObservation Host,
    DockerInventoryObservation Docker,
    SystemConfigObservation SystemConfig,
    LegacyDatabaseObservation Database,
    LegacyFileSystemObservation FileSystem,
    AssessmentSignal[] Signals,
    AssessmentFinding[] Findings);
