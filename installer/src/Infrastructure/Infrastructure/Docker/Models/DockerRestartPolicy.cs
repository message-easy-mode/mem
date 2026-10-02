namespace Infrastructure.Docker.Models;

public sealed record DockerRestartPolicy(
    DockerRestartPolicyName Name,
    int? MaximumRetryCount = null
);