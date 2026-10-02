namespace Modules.Auth.Configuration;

/// <summary>
/// Persistent local key-ring configuration for authentication cookies and
/// ASP.NET Core Identity token providers.
/// </summary>
public sealed class MemDataProtectionOptions
{
    public const string SectionName = "DataProtection";

    public string ApplicationName { get; set; } = "matrix-easy-mode.control-plane";

    public string KeyRingPath { get; set; } = "/data/data-protection-keys";
}
