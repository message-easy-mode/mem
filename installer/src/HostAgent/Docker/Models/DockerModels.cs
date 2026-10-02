using System;

namespace HostAgent.Docker.Models;

public sealed record DockerContainerSpec(
    string Name,
    string Image,
    IReadOnlyDictionary<string, string>? Env = null,
    IReadOnlyDictionary<string, string>? Labels = null,
    IReadOnlyList<string>? Cmd = null,
    IReadOnlyDictionary<string, string>? PortBindings = null,
    bool AutoRemove = false,
    IReadOnlyList<BindMount>? BindMounts = null,
    string? User = null,

    DockerRestartPolicy? RestartPolicy = null,

    string? NetworkName = null,
    IReadOnlyList<string>? NetworkAliases = null
);

public sealed record DockerContainerInspection(
    string Id,
    string Name,
    bool Running,
    string Status,
    string? HealthStatus
)
{
    public string? ImageId { get; init; }

    public string? ConfiguredImage { get; init; }

    public bool Restarting { get; init; }

    public bool Paused { get; init; }

    public bool OomKilled { get; init; }

    public bool Dead { get; init; }

    public long ExitCode { get; init; }

    public bool StateErrorPresent { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? FinishedAtUtc { get; init; }

    public long RestartCount { get; init; }

    public string RestartPolicy { get; init; } = "unknown";

    public string? NetworkMode { get; init; }

    public IReadOnlyList<DockerNetworkAttachmentInspection> Networks { get; init; } = [];

    public IReadOnlyList<DockerBindMountInspection> BindMounts { get; init; } = [];

    public IReadOnlyList<string> Command { get; init; } = [];

    public string? User { get; init; }
}

public sealed record DockerNetworkAttachmentInspection(
    string Name,
    IReadOnlyList<string> Aliases);

public sealed record DockerBindMountInspection(
    string Source,
    string Destination,
    bool ReadOnly);

public sealed record DockerContainerSummary(
    string Id,
    string Name,
    string Status
);

public sealed record BindMount(string HostPath, string ContainerPath, bool ReadOnly = false);
