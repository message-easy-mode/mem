using System;

namespace Infrastructure.Data.Entities;

public sealed class RuntimeRouteEntity
{
    public Guid Id { get; set; }

    public Guid RuntimeStackId { get; set; }
    public RuntimeStackEntity RuntimeStack { get; set; } = default!;

    public Guid? RuntimeServiceInstanceId { get; set; }
    public RuntimeServiceInstanceEntity? RuntimeServiceInstance { get; set; }

    public string ServiceKey { get; set; } = default!;

    public string Provider { get; set; } = default!;
    public string RouteKind { get; set; } = default!;

    public bool IsPublic { get; set; }

    public string PublicHost { get; set; } = default!;
    public string? PublicBaseUrl { get; set; }

    public string ForwardScheme { get; set; } = default!;
    public string ForwardHost { get; set; } = default!;
    public int ForwardPort { get; set; }

    public string? ProviderRouteId { get; set; }

    public Guid? CertificateId { get; set; }
    public int? NpmCertificateId { get; set; }

    public bool SslExpected { get; set; }
    public bool SslConfigured { get; set; }
    public bool ForceSsl { get; set; }
    public bool Http2 { get; set; }

    public string? AdvancedConfigHash { get; set; }
    public bool AdvancedConfigApplied { get; set; }

    public string Status { get; set; } = default!;

    public DateTime? LastVerifiedAtUtc { get; set; }
    public string? LastError { get; set; }

    public string? MetadataJson { get; set; }
}