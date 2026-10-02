
using Infrastructure.Docker.Models;

namespace Modules.Integrations.Postgres.Contracts;

public sealed record PostgresPlanRequest(
    int PreferredHostPort,
    string? HostDataPath
);

public sealed record PostgresDeployRequest(
    int PreferredHostPort,
    string? HostDataPath
);

public sealed record PostgresPlanResponse(
    string ServiceName,
    string ContainerName,
    string Image,
    int ContainerPort,
    int PreferredHostPort,
    int SelectedHostPort,
    bool IsPreferredPortAvailable,
    string? HostDataPath,
    IReadOnlyList<string> Warnings
);

public sealed record PostgresStatusResponse(
    string ServiceName,
    string ContainerName,
    int? PreferredHostPort,
    int? SelectedHostPort,
    string? HostDataPath,
    bool Exists,
    bool Running,
    string? State,
    DockerContainerInspection? Container,
    IReadOnlyList<string> Warnings
);