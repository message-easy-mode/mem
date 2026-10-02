namespace Modules.Auth.Identity;

/// <summary>
/// Cookie and claim names for the scoped, short-lived first-owner bootstrap
/// flow. This scheme is never a normal control-plane operator session.
/// </summary>
public static class MemBootstrapAuthentication
{
    public const string Scheme = "mem.bootstrap";

    public const string GrantIdClaimType = "mem_bootstrap_grant_id";
}
