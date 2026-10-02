namespace HostAgent.Runtime.Coturn;

public sealed class CoturnRuntimeOptions
{
    public const string SectionName = "Coturn";

    public string StorageRootPath { get; set; } = string.Empty;

    /// <summary>
    /// Allows a repository-relative Coturn storage root only for an explicitly
    /// selected Direct Source development launch profile. Production and managed
    /// development continue to require an absolute host path.
    /// </summary>
    public bool AllowRelativeDevelopmentStorageRoot { get; set; }
}
