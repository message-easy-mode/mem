namespace Infrastructure.Docker.Models;

public sealed record DockerBindMount(
    string HostPath,
    string ContainerPath,
    bool ReadOnly = false
);