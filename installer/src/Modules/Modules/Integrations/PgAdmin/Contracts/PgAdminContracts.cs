using Infrastructure.Docker.Models;

namespace Modules.Integrations.PgAdmin.Contracts;

public sealed record PgAdminPlanRequest(
    int PreferredHostPort
);

public sealed record PgAdminDeployRequest(
    int PreferredHostPort,
    string AdminEmail,
    string AdminPassword,
    bool ForcePreferredPort = false
);

public sealed record PgAdminPlanResponse(
    string ServiceName,
    string ContainerName,
    string Image,
    int ContainerPort,
    int PreferredHostPort,
    int SelectedHostPort,
    bool IsPreferredPortAvailable,
    string VolumeName,
    IReadOnlyList<string> Warnings
);

public sealed record PgAdminStatusResponse(
    string ServiceName,
    string ContainerName,
    string VolumeName,
    int? UiHostPort,
    bool Exists,
    bool Running,
    string? State,
    DockerContainerInspection? Container,
    IReadOnlyList<string> Warnings
);