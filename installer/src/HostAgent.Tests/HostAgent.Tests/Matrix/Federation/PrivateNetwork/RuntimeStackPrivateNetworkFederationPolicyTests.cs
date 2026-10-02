using HostAgent.Matrix.Federation;
using HostAgent.Matrix.Federation.PrivateNetwork;

namespace HostAgent.Tests.Matrix.Federation.PrivateNetwork;

public sealed class RuntimeStackPrivateNetworkFederationPolicyTests
{
    [Theory]
    [InlineData(FederationModes.Public, PrivateNetworkExceptionActions.Add, true)]
    [InlineData(FederationModes.Public, PrivateNetworkExceptionActions.Remove, true)]
    [InlineData(FederationModes.Restricted, PrivateNetworkExceptionActions.Add, true)]
    [InlineData(FederationModes.Restricted, PrivateNetworkExceptionActions.Remove, true)]
    [InlineData(FederationModes.LocalOnly, PrivateNetworkExceptionActions.Add, false)]
    [InlineData(FederationModes.LocalOnly, PrivateNetworkExceptionActions.Remove, true)]
    [InlineData(FederationModes.Unknown, PrivateNetworkExceptionActions.Add, false)]
    [InlineData(FederationModes.Unknown, PrivateNetworkExceptionActions.Remove, false)]
    public void Healthy_federation_state_allows_only_safe_private_network_actions(
        string mode,
        string action,
        bool expected)
    {
        Assert.Equal(
            expected,
            RuntimeStackPrivateNetworkFederationService.IsFederationStateEligibleForAction(
                FederationConfigurationStates.Healthy,
                mode,
                action));
    }

    [Theory]
    [InlineData(FederationConfigurationStates.Incomplete)]
    [InlineData(FederationConfigurationStates.CustomUnsupported)]
    [InlineData(FederationConfigurationStates.Unavailable)]
    public void Unhealthy_federation_state_rejects_removal_even_in_local_only(
        string configurationState)
    {
        Assert.False(
            RuntimeStackPrivateNetworkFederationService.IsFederationStateEligibleForAction(
                configurationState,
                FederationModes.LocalOnly,
                PrivateNetworkExceptionActions.Remove));
    }
}
