namespace Modules.Auth;

/// <summary>
/// Shared claim contract for the temporary installer-unlock session.
/// SEC-AUTH-01A uses this only as a server-side bridge while ASP.NET Core
/// Identity replaces the coarse bootstrap session in SEC-AUTH-02/03.
/// </summary>
public static class InstallerAuthClaims
{
    public const string TransitionalInstallerUnlocked = "mem_installer_unlocked";

    public const string True = "true";
}
