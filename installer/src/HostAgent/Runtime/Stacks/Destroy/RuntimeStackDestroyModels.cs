using HostAgent.Runtime.Manifests;

namespace HostAgent.Runtime.Stacks.Destroy;

public sealed record RuntimeStackDestroyRequest(
    bool RemoveContainers = true,
    bool RemoveRoutes = true,
    bool RemoveDatabase = false,
    bool RemoveFiles = false,
    bool Force = false,
    string? IdempotencyKey = null);

public sealed record RuntimeStackDestroyAcceptedResponse(
    Guid OperationId,
    Guid RuntimeStackId,
    string Slug,
    string Status,
    string PollUrl,
    bool ReusedExistingOperation);

public sealed record RuntimeStackDestroyResult(
    string Source,
    string Status,
    Guid OperationId,
    Guid RuntimeStackId,
    string Slug,
    bool ContainersRequested,
    bool RoutesRequested,
    bool DatabaseRequested,
    bool FilesRequested,
    IReadOnlyList<RuntimeStackDestroyStepResult> Steps,
    IReadOnlyList<string> Warnings,
    string? KeptDataPath,
    string? Detail,
    DateTimeOffset DestroyedAtUtc);

public sealed record RuntimeStackDestroyStepResult(
    string Code,
    string Status,
    string Message,
    IReadOnlyDictionary<string, string?> Data);

internal sealed record RuntimeStackDestroyAcceptance(
    Guid RuntimeStackId,
    string Slug,
    bool RuntimeStackRowExists,
    string OwnershipSource,
    RuntimeStackManifest OwnershipSnapshot,
    string HostMutationLevel,
    RuntimeStackDestroyRequest Request);
