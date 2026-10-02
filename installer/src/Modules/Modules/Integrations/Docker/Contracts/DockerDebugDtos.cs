using Infrastructure.Docker.Models;

namespace Modules.Integrations.Docker.Contracts;

public sealed record DockerPingResponse(
    bool Ok
);

public sealed record DockerContainersResponse(
    IReadOnlyList<DockerContainerSummary> Containers
);

public sealed record DockerInspectResponse(
    DockerContainerInspection? Container
);