using Infrastructure.Docker;
using Infrastructure.Docker.Models;

namespace Api.IntegrationTests.Portainer;

public sealed class DockerMountBindingBuilderTests
{
    [Fact]
    public void Bind_and_named_volume_mounts_are_both_forwarded_to_Docker()
    {
        var spec = new DockerContainerSpec(
            Name: "portainer",
            Image: $"sha256:{new string('a', 64)}",
            BindMounts:
            [
                new DockerBindMount(
                    "/var/run/docker.sock",
                    "/var/run/docker.sock"),
                new DockerBindMount(
                    "/srv/read-only",
                    "/read-only",
                    ReadOnly: true)
            ],
            VolumeMounts:
            [
                new DockerVolumeMount("portainer_data", "/data")
            ]);

        var bindings = DockerMountBindingBuilder.Build(spec);

        Assert.Equal(
            new[]
            {
                "/var/run/docker.sock:/var/run/docker.sock",
                "/srv/read-only:/read-only:ro",
                "portainer_data:/data"
            },
            bindings);
    }
}
