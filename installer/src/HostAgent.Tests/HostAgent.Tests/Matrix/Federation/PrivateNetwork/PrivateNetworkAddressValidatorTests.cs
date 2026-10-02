using HostAgent.Matrix.Federation.PrivateNetwork;

namespace HostAgent.Tests.Matrix.Federation.PrivateNetwork;

public sealed class PrivateNetworkAddressValidatorTests
{
    private readonly PrivateNetworkAddressValidator _validator = new();

    [Theory]
    [InlineData("10.0.0.238", "10.0.0.238/32")]
    [InlineData("172.16.4.20", "172.16.4.20/32")]
    [InlineData("192.168.1.50", "192.168.1.50/32")]
    [InlineData("fd12:3456::10", "fd12:3456::10/128")]
    public void Accepts_only_exact_private_addresses(string input, string expectedCidr)
    {
        var result = _validator.ValidateExactPrivateAddress(input);

        Assert.Equal(expectedCidr, result.Cidr);
    }

    [Theory]
    [InlineData("127.0.0.1", "127.0.0.1/32")]
    [InlineData("8.8.8.8", "8.8.8.8/32")]
    public void Allows_removal_of_an_existing_exact_entry_without_allowing_it_to_be_added(
        string input,
        string expectedCidr)
    {
        var result = _validator.ValidateExactAddressForRemoval(input);

        Assert.Equal(expectedCidr, result.Cidr);
    }

    [Theory]
    [InlineData("10.0.0.0/8", "private_network_exact_address_required")]
    [InlineData("127.0.0.1", "private_network_address_unsafe")]
    [InlineData("169.254.169.254", "private_network_address_unsafe")]
    [InlineData("8.8.8.8", "private_network_address_not_private")]
    [InlineData("matrix.example", "private_network_address_invalid")]
    public void Rejects_broad_or_unsafe_values(string input, string expectedCode)
    {
        var exception = Assert.Throws<PrivateNetworkFederationException>(() =>
            _validator.ValidateExactPrivateAddress(input));

        Assert.Equal(expectedCode, exception.Code);
    }
}
