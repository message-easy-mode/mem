using System.Text.Json.Serialization;
using Mem.Localization;

namespace HostAgent.Commands;

public sealed record CreateChatStackRuntimeCommand(
    Guid StackId,
    Guid MatrixInstanceId,
    Guid? ElementInstanceId,
    string? StackSlug,
    string? RequestedDomainId,
    string? MatrixImage,
    string? MatrixVersion,
    string? ElementImage,
    string? ElementVersion,
    string IdempotencyKey,
    string? DisplayName = null,
    string? Category = null);


public sealed record CreateChatStackRuntimeAcceptedResponse(
    Guid OperationId,
    Guid StackId,
    string StackSlug,
    string Status,
    string PollUrl);

public sealed record CreateChatStackRuntimeOperationResponse(
    Guid OperationId,
    Guid? RuntimeStackId,
    string Status,
    string? CurrentStep,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? LastError,
    bool Terminal,
    bool Succeeded);

public sealed record CreateChatStackRuntimeResult(
    Guid StackId,
    string Status,
    string Message,
    HostAgentServiceRuntimeResult? Matrix,
    HostAgentServiceRuntimeResult? Element,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<HostAgentEvidence> Evidence);

public sealed record HostAgentServiceRuntimeResult(
    Guid InstanceId,
    string ServiceKey,
    string ContainerId,
    string ContainerName,
    int HostPort,
    string? DataPath,
    string? ServerName,
    string? PublicHost,
    string? PublicBaseUrl,
    string? InternalHost,
    string? InternalBaseUrl,
    string? PublicRouteId,
    string? InternalRouteId,
    int? NpmCertificateId,
    IReadOnlyDictionary<string, string?> RuntimeMetadata);

public sealed record HostAgentEvidence(
    string Code,
    string Message,
    string? Detail = null,
    IReadOnlyDictionary<string, string?>? Data = null);

/// <summary>
/// Preserves the established machine error code and raw diagnostic detail while
/// allowing newly touched handlers to append a localisable semantic descriptor.
/// Existing clients can continue to use the established error and detail properties.
/// </summary>
public sealed record HostAgentErrorResponse(
    string Error,
    string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    LocalizedMessage? Message = null);