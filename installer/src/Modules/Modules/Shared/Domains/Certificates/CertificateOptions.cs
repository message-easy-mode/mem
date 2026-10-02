namespace Modules.Shared.Domains.Certificates;

public sealed class CertificateStorageOptions
{
    public string RootPath { get; init; } = "data/certificates";
}

public sealed class AcmeCertificateOptions
{
    public string StagingDirectoryUrl { get; init; } = "https://acme-staging-v02.api.letsencrypt.org/directory";
    public string ProductionDirectoryUrl { get; init; } = "https://acme-v02.api.letsencrypt.org/directory";
    public bool DefaultToStaging { get; init; } = true;
    public int DnsPropagationTimeoutSeconds { get; init; } = 180;
    public int DnsPropagationPollSeconds { get; init; } = 5;

    // Optional troubleshooting observers only. MEM certificate issuance does not depend on
    // named third-party recursive resolvers; deSEC authoritative visibility is the gate.
    public string[] PublicDnsResolvers { get; init; } = [];
    public int PublicDnsMinimumVisibleResolvers { get; init; } = 1;
    public int PublicDnsVisibilityTimeoutSeconds { get; init; } = 180;

    // deSEC is anycast/distributed. Seeing the challenge from this host on both named
    // authoritative services is necessary but not sufficient proof that every remote
    // validation perspective has converged. Keep the TXT continuously visible for a
    // conservative settling window before asking Let's Encrypt to validate it.
    public int DnsVisibilityStabilitySeconds { get; init; } = 300;
    public int DnsVisibilityStabilityTimeoutSeconds { get; init; } = 420;
    public int DnsVisibilityStabilityPollSeconds { get; init; } = 5;

    // A DNS-01 challenge becomes invalid after ACME rejects it, so the safe recovery is a
    // fresh ACME order/challenge. Bound this tightly to avoid uncontrolled issuance loops.
    public int AcmeDnsValidationMaxAttempts { get; init; } = 2;

    public int DnsProviderRequestTimeoutSeconds { get; init; } = 30;
    public int AcmeRequestTimeoutSeconds { get; init; } = 30;
    public int AcmeChallengeValidationTimeoutSeconds { get; init; } = 180;
    public int AcmeOrderFinalizationTimeoutSeconds { get; init; } = 120;
    public int AcmePollSeconds { get; init; } = 3;
}
