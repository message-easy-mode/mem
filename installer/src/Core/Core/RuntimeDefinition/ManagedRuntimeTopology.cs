namespace Core.RuntimeDefinition;

public static class ManagedNetworkNames
{
    public const string MemGateway = "mem-gateway";

    [Obsolete("Use MemGateway.")]
    public const string DeltaboxAio = MemGateway;
}

public static class ManagedNetworkAliases
{
    public const string Postgres = "postgres";
    public const string Npm = "npm";
    public const string MemApi = "mem-api";
    public const string MemWeb = "mem-web";
    public const string Seq = "seq";
    public const string PgAdmin = "pgadmin";
    public const string Portainer = "portainer";
}