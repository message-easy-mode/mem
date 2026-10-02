namespace Mem.Cli.Models;

public sealed record InstallerUnlockRequest(
    string Token);

public sealed record HostAgentStatusResponse(
    string Source,
    string HostAgent,
    bool DockerReachable,
    string? RuntimeNetworkName,
    bool NpmReady,
    string Status,
    string? Detail);

public sealed record RuntimeStackListResponse(
    string Source,
    string Status,
    IReadOnlyList<RuntimeStackSummaryResponse> Stacks,
    string? Detail);

public sealed record RuntimeStackSummaryResponse(
    Guid StackId,
    string Slug,
    string LastVerifiedStatus,
    DateTimeOffset LastVerifiedAtUtc,
    string? MatrixPublicBaseUrl,
    string? ElementPublicBaseUrl);

public sealed record RuntimeStackInspectResponse(
    string Source,
    string Status,
    Guid? StackId,
    string Slug,
    RuntimeStackServiceInspectResponse? Matrix,
    RuntimeStackServiceInspectResponse? Element,
    DateTimeOffset? LastVerifiedAtUtc,
    string? Detail);

public sealed record RuntimeStackServiceInspectResponse(
    string ServiceKey,
    Guid InstanceId,
    string? ContainerId,
    string? ContainerName,
    string? InternalHost,
    string? InternalBaseUrl,
    string? PublicHost,
    string? PublicBaseUrl,
    string? DataPath,
    string? ConfigPath,
    string? PublicRouteId,
    int? NpmCertificateId,
    IReadOnlyDictionary<string, string?> RuntimeMetadata);

public sealed record RuntimeStackDoctorResponse(
    string Source,
    string Status,
    Guid? StackId,
    string Slug,
    string? LastVerifiedStatus,
    DateTimeOffset? LastVerifiedAtUtc,
    DateTimeOffset CheckedAtUtc,
    bool AllPassed,
    IReadOnlyList<RuntimeStackDoctorCheckResponse> Checks,
    string? Detail,
    Guid? OperationId,
    Guid? ReportId);

public sealed record RuntimeStackDoctorCheckResponse(
    string Code,
    string Name,
    string Url,
    bool Success,
    int? StatusCode,
    string? Detail,
    string? BodyPreview);

public sealed record RuntimeStackOperationsResponse(
    string Source,
    string Status,
    Guid? StackId,
    string Slug,
    IReadOnlyList<RuntimeStackOperationResponse> Operations,
    string? Detail);

public sealed record RuntimeStackOperationResponse(
    Guid Id,
    Guid? RuntimeStackId,
    string Operation,
    string Status,
    string? IdempotencyKey,
    string? RequestedBy,
    string? HostMutationLevel,
    string? CurrentStep,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? LastError);

/// <summary>
/// CLI-safe stack inspection projection. It deliberately excludes container
/// identifiers/names, internal hosts and URLs, data/config paths, NPM IDs, and
/// arbitrary runtime metadata from both human and JSON CLI output.
/// </summary>
public sealed record RuntimeStackInspectCliResult(
    string Source,
    string Status,
    Guid? StackId,
    string Slug,
    RuntimeStackServicePublicCliProjection? Matrix,
    RuntimeStackServicePublicCliProjection? Element,
    DateTimeOffset? LastVerifiedAtUtc,
    string? Detail);

public sealed record RuntimeStackServicePublicCliProjection(
    string ServiceKey,
    string? PublicHost,
    string? PublicBaseUrl);

