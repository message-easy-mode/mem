using System;

namespace Infrastructure.Docker.Models;

public enum DockerRestartPolicyName
{
    No = 0,
    Always = 1,
    UnlessStopped = 2,
    OnFailure = 3
}