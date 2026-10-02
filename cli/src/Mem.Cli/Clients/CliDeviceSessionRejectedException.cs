namespace Mem.Cli.Clients;

/// <summary>
/// Signals that the Control Plane rejected a locally stored MEM CLI device
/// session. This is an authentication result, not a HostAgent/component-health
/// result, so callers must not synthesize infrastructure status from it.
/// </summary>
public sealed class CliDeviceSessionRejectedException : Exception
{
    public CliDeviceSessionRejectedException()
        : base("The control plane rejected the MEM CLI device session.")
    {
    }
}
