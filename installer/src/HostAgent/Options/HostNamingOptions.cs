using System;

namespace HostAgent.Options;

public sealed class HostNamingOptions
{
    public string PublicHostSuffix { get; set; } = "";
    public string InternalHostSuffix { get; set; } = "";
}
