using System;

namespace HostAgent.Docker.Models;

public enum DockerRestartPolicyName
{
    No = 0,
    Always = 1,
    UnlessStopped = 2,
    OnFailure = 3
}

public sealed record DockerRestartPolicy(
    DockerRestartPolicyName Name,
    int? MaximumRetryCount = null
);
