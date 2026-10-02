using HostAgent.Matrix.Federation;

namespace HostAgent.Tests.Matrix.Federation;

public sealed class FederationDomainValidatorTests
{
    private readonly FederationDomainValidator _validator = new();

    [Fact]
    public void Canonicalises_case_idna_trailing_dot_and_sort_order()
    {
        var result = _validator.ValidateMany(
            ["Partner.Example.", "bücher.example"],
            requireAtLeastOne: true);

        Assert.True(result.Valid);
        Assert.Equal(
            ["partner.example", "xn--bcher-kva.example"],
            result.CanonicalDomains);
    }

    [Theory]
    [InlineData("https://matrix.example")]
    [InlineData("matrix.example:8448")]
    [InlineData("matrix.example/path")]
    [InlineData("*.example")]
    [InlineData("192.0.2.10")]
    [InlineData("2001:db8::1")]
    [InlineData("bad_label.example")]
    [InlineData(" partner.example")]
    [InlineData("partner.example ")]
    public void Rejects_values_that_are_not_exact_dns_homeserver_domains(string value)
    {
        var result = _validator.ValidateOne(value);

        Assert.False(result.Valid);
        Assert.NotNull(result.ErrorCode);
        Assert.Empty(result.CanonicalDomains);
    }

    [Fact]
    public void Rejects_duplicates_after_canonicalisation()
    {
        var result = _validator.ValidateMany(
            ["Partner.Example", "partner.example."],
            requireAtLeastOne: true);

        Assert.False(result.Valid);
        Assert.Equal("federation_domain_duplicate", result.ErrorCode);
    }

    [Fact]
    public void Restricted_requires_at_least_one_domain()
    {
        var result = _validator.ValidateMany([], requireAtLeastOne: true);

        Assert.False(result.Valid);
        Assert.Equal("federation_domain_required", result.ErrorCode);
    }
}
