namespace Modules.Shared.RuntimeImages;

public sealed class PostgresRuntimeImageOptions
{
    public const string SectionName = "RuntimeImages:Postgres";

    public string ApprovedReference { get; set; } = string.Empty;
    public int RequiredMajorVersion { get; set; }
    public string ExpectedVersion { get; set; } = string.Empty;
    public bool AllowInstallPull { get; set; }
    public bool AllowOperationalPull { get; set; }
}
