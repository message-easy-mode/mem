namespace Modules.Auth.Identity;

/// <summary>
/// The intentionally small v0.1.1 role model for named MEM control-plane operators.
/// </summary>
public static class MemOperatorRoles
{
    public const string PlatformOwner = "platform_owner";
    public const string Operator = "operator";
    public const string Auditor = "auditor";

    public static readonly IReadOnlyList<string> All =
    [
        PlatformOwner,
        Operator,
        Auditor
    ];
}
