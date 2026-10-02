using System;

namespace HostAgent.Options;

public enum ServiceConnectionMode
{
    HostLoopback = 1,
    DockerNetwork = 2
}

public sealed class RuntimeConnectivityContext
{
    public ServiceConnectionMode MatrixAdminConnectionMode { get; init; }
}
