using Shared.ControlPlane;

namespace Modules.Auth.Configuration;

public sealed class InstallerAuthOptions
{
    public const string SectionName = "InstallerAuth";

    public string TokenEnvironmentVariableName { get; set; } =
        MemControlPlaneIdentity.EnvironmentVariables.SetupToken;

    public string TokenPathEnvironmentVariableName { get; set; } =
        MemControlPlaneIdentity.EnvironmentVariables.SetupTokenPath;

    public string LegacyTokenEnvironmentVariableName { get; set; } =
        MemControlPlaneIdentity.Legacy.EnvironmentVariables.SetupToken;

    public string LegacyTokenPathEnvironmentVariableName { get; set; } =
        MemControlPlaneIdentity.Legacy.EnvironmentVariables.SetupTokenPath;

    public string DefaultTokenPath { get; set; } = "/data/setup-token";

    public string CookieName { get; set; } =
        MemControlPlaneIdentity.Compatibility.InstallerAuthCookieName;

    public int SessionHours { get; set; } = 12;

    public string? DevelopmentSetupToken { get; set; }
}
