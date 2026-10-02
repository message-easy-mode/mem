namespace Infrastructure.Docker.Models;

public sealed record DockerPortBinding(
    uint PrivatePort,
    uint PublicPort,
    string Type,
    string Ip
);