using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Runtime;

internal static class TestRuntimeContext
{
    public static MemControlPlaneRuntimeContext Create(
        string root,
        string mode = MemRuntimeModes.AutomatedTest,
        bool runningInContainer = false,
        string? containerName = null,
        string? uiDeliveryMode = null,
        string version = "0.2.0-test",
        string? commit = "test-commit",
        bool allowSharedDockerHost = false,
        string? hostAccessIpv4 = null)
    {
        Directory.CreateDirectory(root);
        string? webRoot = null;
        if (MemRuntimeModes.IsContainerized(mode))
        {
            webRoot = Path.Combine(root, "wwwroot");
            Directory.CreateDirectory(webRoot);
            File.WriteAllText(Path.Combine(webRoot, "index.html"), "<html></html>");
        }

        return MemControlPlaneRuntimeContextFactory.Create(
            new MemRuntimeContextOptions
            {
                Mode = mode,
                StateRoot = Path.Combine(root, "state"),
                DockerEndpoint = "unix:///var/run/docker.sock",
                ContainerName = containerName,
                HostAccessIpv4 = hostAccessIpv4,
                UiDeliveryMode = uiDeliveryMode,
                InstanceIdentityFileName = "control-plane/runtime-instance.json",
                AllowSharedDockerHost = allowSharedDockerHost
            },
            new MemRuntimeEnvironmentSnapshot(
                EnvironmentName: mode switch
                {
                    MemRuntimeModes.LocalDevelopment => "Development",
                    MemRuntimeModes.ContainerizedDevelopment => "Development",
                    MemRuntimeModes.ContainerizedProduction => "Production",
                    _ => "Test"
                },
                ApplicationName: "mem-control-plane",
                ContentRootPath: root,
                WebRootPath: webRoot,
                RunningInContainer: runningInContainer,
                DevelopmentSetupTokenConfigured: false,
                Version: version,
                Commit: commit));
    }
}
