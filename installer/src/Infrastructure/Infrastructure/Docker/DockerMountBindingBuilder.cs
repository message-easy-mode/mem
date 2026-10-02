using Infrastructure.Docker.Models;

namespace Infrastructure.Docker;

public static class DockerMountBindingBuilder
{
    public static IReadOnlyList<string> Build(DockerContainerSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var bindings = new List<string>();

        foreach (var mount in spec.BindMounts)
        {
            if (string.IsNullOrWhiteSpace(mount.HostPath) ||
                string.IsNullOrWhiteSpace(mount.ContainerPath))
            {
                throw new ArgumentException(
                    "Docker bind mounts require both a host path and container path.",
                    nameof(spec));
            }

            bindings.Add(
                $"{mount.HostPath}:{mount.ContainerPath}{(mount.ReadOnly ? ":ro" : string.Empty)}");
        }

        foreach (var mount in spec.VolumeMounts)
        {
            if (string.IsNullOrWhiteSpace(mount.VolumeName) ||
                string.IsNullOrWhiteSpace(mount.ContainerPath))
            {
                throw new ArgumentException(
                    "Docker volume mounts require both a volume name and container path.",
                    nameof(spec));
            }

            bindings.Add($"{mount.VolumeName}:{mount.ContainerPath}");
        }

        return bindings;
    }
}
