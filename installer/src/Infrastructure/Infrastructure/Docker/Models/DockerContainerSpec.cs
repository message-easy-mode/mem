namespace Infrastructure.Docker.Models;

public sealed record DockerContainerSpec(
    string Name,
    string Image,
    IReadOnlyDictionary<string, string>? Environment = null,
    IReadOnlyDictionary<string, string>? Labels = null,
    IReadOnlyList<string>? Command = null,
    IReadOnlyDictionary<string, string>? PortBindings = null,
    IReadOnlyList<DockerBindMount>? BindMounts = null,
    IReadOnlyList<DockerVolumeMount>? VolumeMounts = null,
    DockerRestartPolicy? RestartPolicy = null,
    string? NetworkName = null,
    IReadOnlyList<string>? NetworkAliases = null,
    bool DisableLogging = false
)
{
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        Environment ?? new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> Labels { get; init; } =
        Labels ?? new Dictionary<string, string>();

    public IReadOnlyList<string> Command { get; init; } =
        Command ?? [];

    public IReadOnlyDictionary<string, string> PortBindings { get; init; } =
        PortBindings ?? new Dictionary<string, string>();

    public IReadOnlyList<DockerBindMount> BindMounts { get; init; } =
        BindMounts ?? [];

    public IReadOnlyList<DockerVolumeMount> VolumeMounts { get; init; } =
        VolumeMounts ?? [];

    public DockerRestartPolicy RestartPolicy { get; init; } =
        RestartPolicy ?? new DockerRestartPolicy(DockerRestartPolicyName.UnlessStopped);

    public string? NetworkName { get; init; } = NetworkName;

    public IReadOnlyList<string> NetworkAliases { get; init; } =
        NetworkAliases ?? [];

    public bool DisableLogging { get; init; } = DisableLogging;
}