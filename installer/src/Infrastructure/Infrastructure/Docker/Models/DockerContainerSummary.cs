namespace Infrastructure.Docker.Models;

public sealed record DockerContainerSummary(
    string Id,
    string Name,
    string Image,
    string State,
    string Status,
    IReadOnlyList<DockerPortBinding> Ports
);