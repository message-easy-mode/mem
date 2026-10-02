using System;

namespace Infrastructure.Docker.Models;

public sealed record DockerVolumeMount(
    string VolumeName,
    string ContainerPath
);
