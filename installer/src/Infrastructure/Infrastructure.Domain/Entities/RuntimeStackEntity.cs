using System;

namespace Infrastructure.Data.Entities;

public sealed class RuntimeStackEntity
{
    public Guid Id { get; set; }

    public string Slug { get; set; } = default!;
    public string? DisplayName { get; set; }

    public string Status { get; set; } = default!;
    public string? LastVerifiedStatus { get; set; }
    public DateTime? LastVerifiedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public string? BaseDomain { get; set; }
    public Guid? DomainId { get; set; }
    public Guid? ActiveCertificateId { get; set; }
    public int? ActiveNpmCertificateId { get; set; }

    public string? RuntimeNetworkName { get; set; }
    public string? DataRoot { get; set; }
    public string? ManifestPath { get; set; }

    public Guid MatrixInstanceId { get; set; }
    public Guid? ElementInstanceId { get; set; }

    public string? MatrixPublicBaseUrl { get; set; }
    public string? ElementPublicBaseUrl { get; set; }

    public Guid? LastOperationId { get; set; }
    public string? LastError { get; set; }

    public string? MetadataJson { get; set; }

    public ICollection<RuntimeServiceInstanceEntity> ServiceInstances { get; set; } =
        new List<RuntimeServiceInstanceEntity>();

    public ICollection<RuntimeRouteEntity> Routes { get; set; } =
        new List<RuntimeRouteEntity>();
}