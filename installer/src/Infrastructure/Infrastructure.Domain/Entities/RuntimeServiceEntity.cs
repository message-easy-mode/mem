using System;

namespace Infrastructure.Data.Entities
{

    public sealed class RuntimeServiceEntity
    {
        public Guid Id { get; set; }

        public string ServiceName { get; set; } = default!;
        public string ContainerName { get; set; } = default!;
        public string Image { get; set; } = default!;

        public int ContainerPort { get; set; }
        public int PreferredHostPort { get; set; }
        public int SelectedHostPort { get; set; }

        public string? HostPath { get; set; }
        public string? SecondaryHostPath { get; set; }

        public string? ContainerId { get; set; }
        public string Status { get; set; } = default!;

        public DateTime CreatedAtUtc { get; set; }
        public DateTime? LastObservedAtUtc { get; set; }
    }
}