using Infrastructure.Docker.Models;

namespace Modules.Integrations.Npm.Contracts;

public sealed record NpmPlanRequest(
    int PreferredHttpPort,
    int PreferredAdminPort,
    int PreferredHttpsPort,
    string? HostDataPath,
    string? HostLetsEncryptPath
);

public sealed record NpmDeployRequest(
    int PreferredHttpPort,
    int PreferredAdminPort,
    int PreferredHttpsPort,
    string? HostDataPath,
    string? HostLetsEncryptPath,
    bool ForcePreferredPorts = false
);

public sealed record NpmPortPlanResponse(
    int ContainerPort,
    int PreferredHostPort,
    int SelectedHostPort,
    bool IsPreferredPortAvailable,
    string Protocol,
    IReadOnlyList<string> Warnings
);

public sealed record NpmPlanResponse(
    string ServiceName,
    string ContainerName,
    string Image,
    IReadOnlyList<NpmPortPlanResponse> Ports,
    string? HostDataPath,
    string? HostLetsEncryptPath,
    IReadOnlyList<string> Warnings
);

public sealed record NpmStatusResponse(
    string ServiceName,
    string ContainerName,
    string? HostDataPath,
    string? HostLetsEncryptPath,
    int? HttpHostPort,
    int? AdminHostPort,
    int? HttpsHostPort,
    bool Exists,
    bool Running,
    string? State,
    DockerContainerInspection? Container,
    IReadOnlyList<string> Warnings
);