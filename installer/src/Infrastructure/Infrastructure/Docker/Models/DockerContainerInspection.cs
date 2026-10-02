namespace Infrastructure.Docker.Models;

public sealed record DockerContainerInspection(
    string Id,
    string Name,
    string Image,
    string State,
    bool Running,
    IReadOnlyList<DockerPortBinding> Ports)
{
    public IReadOnlyDictionary<string, string> Labels { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    // Environment values may contain protected credential material. Runtime
    // inspection exposes names only so callers can prove bootstrap-only
    // variables were removed without materialising their values.
    public IReadOnlyList<string> EnvironmentVariableNames { get; init; } = [];
}
