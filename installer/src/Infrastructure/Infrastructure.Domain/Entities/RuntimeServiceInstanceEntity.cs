using System;

namespace Infrastructure.Data.Entities;

public sealed class RuntimeServiceInstanceEntity
{
    public Guid Id { get; set; }

    public Guid RuntimeStackId { get; set; }
    public RuntimeStackEntity RuntimeStack { get; set; } = default!;

    public Guid InstanceId { get; set; }

    public string ServiceKey { get; set; } = default!;
    public string? Role { get; set; }
    public string Status { get; set; } = default!;

    public string? Image { get; set; }
    public string? Version { get; set; }

    public string? ContainerId { get; set; }
    public string? ContainerName { get; set; }
    public string? NetworkName { get; set; }

    public string? InternalHost { get; set; }
    public string? InternalBaseUrl { get; set; }

    public string? PublicHost { get; set; }
    public string? PublicBaseUrl { get; set; }

    public int? HostPort { get; set; }
    public int? ContainerPort { get; set; }

    public string? DataPath { get; set; }
    public string? ConfigPath { get; set; }

    public bool BackupInclude { get; set; }

    public string? ServerName { get; set; }

    public string? RuntimeMetadataJson { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? LastObservedAtUtc { get; set; }

    public string? LastError { get; set; }
}