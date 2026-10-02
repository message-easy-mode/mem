using Modules.Shared.Domains.Certificates;

namespace Modules.Shared.Domains.Dns;

public interface IDnsZoneAccessProbe
{
    Task<DnsZoneAccessProbeResult> ProbeAsync(
        DnsZoneAccessProbeRequest request,
        CancellationToken cancellationToken);
}

public sealed record DnsZoneAccessProbeRequest(
    string Zone,
    string ProviderToken);

public sealed record DnsZoneAccessProbeResult(
    bool Succeeded,
    string Message,
    string? ErrorCode,
    IReadOnlyList<CertificateOperationEvidence> Evidence,
    int? ProviderStatusCode = null);
