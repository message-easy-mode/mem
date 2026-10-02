namespace HostAgent.Identity;

public static class ControlPlaneRoles
{
    public const string PowerAdmin = "power_admin";
    public const string Admin = "admin";
    public const string Viewer = "viewer";

    public static readonly string[] All =
    [
        PowerAdmin,
        Admin,
        Viewer
    ];

    public static bool IsValid(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        var normalized = Normalize(role);

        return All.Contains(
            normalized,
            StringComparer.OrdinalIgnoreCase);
    }

    public static string Normalize(string role)
    {
        return role.Trim().ToLowerInvariant();
    }
}
