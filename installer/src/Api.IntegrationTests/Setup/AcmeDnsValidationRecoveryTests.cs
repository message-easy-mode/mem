using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Certificates.Acme;

namespace Api.IntegrationTests.Setup;

public sealed class AcmeDnsValidationRecoveryTests
{
    [Theory]
    [InlineData("During secondary validation: No TXT record found at _acme-challenge.deltabox.dev")]
    [InlineData("During secondary validation: Incorrect TXT record found at _acme-challenge.deltabox.dev")]
    [InlineData("During secondary validation: DNS problem: NXDOMAIN looking up TXT for _acme-challenge.deltabox.dev")]
    public void CORR_04_secondary_DNS_visibility_misses_are_classified_as_recoverable(string detail)
    {
        Assert.True(AcmeCertificateIssuer.IsRecoverableSecondaryDnsValidationMiss(detail));
    }

    [Theory]
    [InlineData("No TXT record found at _acme-challenge.deltabox.dev")]
    [InlineData("During primary validation: No TXT record found at _acme-challenge.deltabox.dev")]
    [InlineData("During secondary validation: CAA forbids issuance")]
    [InlineData("")]
    public void CORR_04_unrelated_ACME_failures_do_not_enter_the_secondary_DNS_recovery_path(string detail)
    {
        Assert.False(AcmeCertificateIssuer.IsRecoverableSecondaryDnsValidationMiss(detail));
    }

    [Fact]
    public void CORR_04_defaults_use_long_authoritative_settling_and_one_bounded_fresh_order_recovery()
    {
        var options = new AcmeCertificateOptions();

        Assert.Equal(300, options.DnsVisibilityStabilitySeconds);
        Assert.Equal(420, options.DnsVisibilityStabilityTimeoutSeconds);
        Assert.Equal(5, options.DnsVisibilityStabilityPollSeconds);
        Assert.Equal(2, options.AcmeDnsValidationMaxAttempts);
        Assert.Empty(options.PublicDnsResolvers);
    }
}
