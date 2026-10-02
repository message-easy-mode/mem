namespace Core.RuntimeDefinition;

public static class ManagedServiceNames
{
    public const string Postgres = "postgres";
    public const string Npm = "npm";
    public const string MemApi = "mem-api";
    public const string MemWeb = "mem-web";
    public const string PgAdmin = "pgadmin";
    public const string Seq = "seq";
    public const string Portainer = "portainer";
}

public static class ManagedVolumeNames
{
    public const string PostgresData = "mem_postgres_data";
    public const string NpmData = "mem_npm_data";
    public const string NpmLetsEncrypt = "mem_npm_letsencrypt";
    public const string Logs = "mem_logs";
    public const string PgAdminData = "mem_pgadmin_data";
    public const string SeqData = "mem_seq_data";
    public const string PortainerData = "portainer_data";
}

public static class ManagedContainerLabels
{
    // Canonical labels for resources created after RUNTIME-CONTEXT-01B.
    public const string CanonicalManagedKey = Shared.ControlPlane.Runtime.MemDockerOwnershipLabels.ManagedKey;
    public const string CanonicalServiceKey = Shared.ControlPlane.Runtime.MemDockerOwnershipLabels.ServiceKey;
    public const string ControlPlaneInstanceKey = Shared.ControlPlane.Runtime.MemDockerOwnershipLabels.ControlPlaneInstanceKey;
    public const string RuntimeModeKey = Shared.ControlPlane.Runtime.MemDockerOwnershipLabels.RuntimeModeKey;
    public const string CanonicalResourceKindKey = Shared.ControlPlane.Runtime.MemDockerOwnershipLabels.ResourceKey;

    // Existing resources retain these labels and remain supported. New
    // resources carry both sets during the compatibility window.
    public const string ManagedKey = "matrixeasymode.managed";
    public const string ServiceKey = "matrixeasymode.service";
    public const string ResourceKindKey = "matrixeasymode.resource-kind";

    public static Dictionary<string, string> ForService(
        string serviceName,
        Shared.ControlPlane.Runtime.MemControlPlaneRuntimeContext? runtimeContext = null)
    {
        var labels = new Dictionary<string, string>
        {
            [ManagedKey] = "true",
            [ServiceKey] = serviceName,
            [ResourceKindKey] = "platform"
        };

        Shared.ControlPlane.Runtime.MemDockerOwnershipLabels.AddTo(
            labels,
            runtimeContext,
            resource: "platform-service",
            service: serviceName);
        return labels;
    }
}


public static class ManagedContainerNames
{
    public static string ForService(string serviceName)
        => serviceName switch
        {
            ManagedServiceNames.Postgres => "mem-postgres",
            ManagedServiceNames.Npm => "mem-npm",
            ManagedServiceNames.MemApi => "mem-api",
            ManagedServiceNames.MemWeb => "mem-web",
            ManagedServiceNames.PgAdmin => "mem-pgadmin",
            ManagedServiceNames.Seq => "mem-seq",
            ManagedServiceNames.Portainer => "portainer",
            _ => $"mem-{serviceName}"
        };

    public static string Postgres => ForService(ManagedServiceNames.Postgres);
    public static string Npm => ForService(ManagedServiceNames.Npm);
    public static string MemApi => ForService(ManagedServiceNames.MemApi);
    public static string MemWeb => ForService(ManagedServiceNames.MemWeb);
    public static string PgAdmin => ForService(ManagedServiceNames.PgAdmin);
    public static string Seq => ForService(ManagedServiceNames.Seq);
    public static string Portainer => ForService(ManagedServiceNames.Portainer);
}