namespace HostAgent.Options;

public sealed class HostAgentOptions
{
    /// <summary>
    /// Whether command endpoints should be enabled. Default false until explicitly wired.
    /// </summary>
    public bool EnableInternalCommandEndpoints { get; set; }
}
