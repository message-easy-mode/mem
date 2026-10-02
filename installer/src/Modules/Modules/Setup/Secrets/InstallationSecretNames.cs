namespace Modules.Setup.Secrets;

public static class InstallationSecretNames
{
    public const string DnsCategory = "setup-dns";
    public const string DesecProviderToken = "dns.desec.provider-token";

    public const string PlatformCategory = "platform";
    public const string PostgresPassword = "platform.postgres.password";

    public const string NpmAdminEmail = "platform.npm.admin.email";
    public const string NpmAdminPassword = "platform.npm.admin.password";
    public const string NpmAdminVerifiedAtUtc = "platform.npm.admin.verified-at-utc";
}
