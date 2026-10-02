using HostAgent.Runtime.Coturn;

namespace HostAgent.Runtime.Stacks.Turn;

public interface IRuntimeStackTurnPlatformConfigurationProvider
{
    Task<CoturnSynapseConfig?> GetSynapseConfigAsync(CancellationToken ct);
}

public sealed class RuntimeStackTurnPlatformConfigurationProvider
    : IRuntimeStackTurnPlatformConfigurationProvider
{
    private readonly CoturnRuntimeService _coturnRuntime;

    public RuntimeStackTurnPlatformConfigurationProvider(
        CoturnRuntimeService coturnRuntime)
    {
        _coturnRuntime = coturnRuntime;
    }

    public Task<CoturnSynapseConfig?> GetSynapseConfigAsync(
        CancellationToken ct) =>
        _coturnRuntime.GetSynapseConfigAsync(ct);
}
