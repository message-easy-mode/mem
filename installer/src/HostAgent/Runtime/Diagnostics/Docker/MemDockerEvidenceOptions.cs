namespace HostAgent.Runtime.Diagnostics.Docker;

public sealed class MemDockerEvidenceOptions
{
    public const string SectionName = "Diagnostics:DockerEvidence";

    public bool Enabled { get; set; } = true;

    public int DefaultTailLines { get; set; } = 100;

    public int MaximumTailLines { get; set; } = 500;

    public int MaximumCharacters { get; set; } = 30000;

    public int MaximumRawBytes { get; set; } = 1024 * 1024;

    public int MaximumFrameBytes { get; set; } = 1024 * 1024;

    public int TimeoutSeconds { get; set; } = 8;
}
