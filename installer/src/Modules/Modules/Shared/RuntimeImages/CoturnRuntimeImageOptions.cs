namespace Modules.Shared.RuntimeImages;

public sealed class CoturnRuntimeImageOptions
{
    public const string SectionName = "RuntimeImages:Coturn";

    public string ApprovedReference { get; set; } = string.Empty;
    public string ExpectedVersion { get; set; } = string.Empty;
    public bool AllowInstallPull { get; set; }
    public bool AllowOperationalPull { get; set; }
}
