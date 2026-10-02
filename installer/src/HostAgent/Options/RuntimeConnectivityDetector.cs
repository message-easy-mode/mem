using System;

namespace HostAgent.Options;

public static class RuntimeConnectivityDetector
{
    public static RuntimeConnectivityContext Detect()
    {
        return new RuntimeConnectivityContext
        {
            MatrixAdminConnectionMode = DetectServiceConnectionMode()
        };
    }

    private static ServiceConnectionMode DetectServiceConnectionMode()
    {
        var overrideValue = Environment.GetEnvironmentVariable("SERVICE_CONNECTION_MODE");
        if (!string.IsNullOrWhiteSpace(overrideValue) &&
            Enum.TryParse<ServiceConnectionMode>(overrideValue, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        var dotnetInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        if (string.Equals(dotnetInContainer, "true", StringComparison.OrdinalIgnoreCase))
            return ServiceConnectionMode.DockerNetwork;

        if (File.Exists("/.dockerenv"))
            return ServiceConnectionMode.DockerNetwork;

        return ServiceConnectionMode.HostLoopback;
    }
}
