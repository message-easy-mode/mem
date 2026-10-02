namespace Infrastructure.Data.Entities.Migrations;

public sealed class MigrationSourceEntity
{
    public Guid Id { get; set; }
    public Guid MigrationIntakeEntityId { get; set; }
    public MigrationIntakeEntity MigrationIntake { get; set; } = default!;
    public string SourceId { get; set; } = default!;
    public string SourceKind { get; set; } = default!;
    public string Product { get; set; } = default!;
    public string? ProductVersion { get; set; }
    public string SourceFingerprint { get; set; } = default!;
    public DateTime? CapturedAtUtc { get; set; }
}
