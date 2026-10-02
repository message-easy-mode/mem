using Core.RuntimeDefinition;

namespace HostAgent.Runtime.ServiceRuntime;

public sealed class RuntimeNetworkOptions
{
    public string GatewayNetworkName { get; set; } = ManagedNetworkNames.MemGateway;
}