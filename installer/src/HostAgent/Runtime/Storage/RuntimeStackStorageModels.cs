// HostAgent/Runtime/Storage/RuntimeStackStorageModels.cs

namespace HostAgent.Runtime.Storage;

public sealed record RuntimeStackStorageResponse(
    string Source,
    string Status,
    Guid RuntimeStackId,
    string Slug,
    RuntimeStackMatrixStorageResponse Matrix,
    RuntimeStackElementStorageResponse? Element,
    string? Detail);

public sealed record RuntimeStackMatrixStorageResponse(
    string DataPath,
    string? HomeserverYamlPath,
    long HomeserverYamlBytes,
    string? SigningKeyPath,
    long SigningKeyBytes,
    string MediaStorePath,
    bool MediaStoreExists,
    long TotalBytes,
    long TotalFiles,
    IReadOnlyList<RuntimeStackStorageSectionResponse> Sections);

public sealed record RuntimeStackElementStorageResponse(
    string? DataPath,
    string? ConfigPath,
    long ConfigBytes);

public sealed record RuntimeStackStorageSectionResponse(
    string Key,
    string DisplayName,
    string Path,
    bool Exists,
    long Bytes,
    long Files);