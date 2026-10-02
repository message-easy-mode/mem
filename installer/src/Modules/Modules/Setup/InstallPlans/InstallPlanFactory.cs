namespace Modules.Setup.InstallPlans;

public static class InstallPlanFactory
{
    public static InstallPlan CreateDefault()
    {
        return new InstallPlan(
            General: new GeneralSetupConfig(
                Mode: "Easy",
                InstallName: "Message Easy Mode",
                PublicBaseHostname: null),

            Platform: new PlatformSetupConfig(
                Postgres: new PostgresSetupConfig(
                    Enabled: true,
                    ContainerName: "mem-postgres",
                    DatabaseName: "mem",
                    Username: "postgres",
                    UseDockerVolume: true,
                    VolumeName: "mem_postgres_data"),

                Ingress: new IngressSetupConfig(
                    Provider: "Npm",
                    Enabled: true,
                    ContainerName: "mem-npm",
                    HttpPort: 80,
                    HttpsPort: 443,
                    AdminPort: 81,
                    UseExistingIfDetected: true),

                MemApi: new MemApiSetupConfig(
                    Enabled: false,
                    ContainerName: "mem-api",
                    Image: "mem-api:local",
                    InternalPort: 7000),

                MemWeb: new MemWebSetupConfig(
                    Enabled: false,
                    ContainerName: "mem-web",
                    Image: "mem-web:local",
                    InternalPort: 3000)),

            PublicAccess: new PublicAccessSetupConfig(
                Domain: "",
                Zone: "",
                AcmeEmail: "",
                DnsProvider: "desec",
                UseStaging: false,
                CertificateId: null,
                NpmCertificateId: null,
                ProxyHostDomain: null,
                ForwardHost: null,
                ForwardPort: null,
                ForwardScheme: "http",
                CertificateValidated: false,
                ImportedToNpm: false,
                ProxyHostVerified: false,
                LastVerifiedAtUtc: null,
                Preparation: null),

            SupportTools: new SupportToolsSetupConfig(
                Seq: new SeqSetupConfig(
                    Enabled: false,
                    ContainerName: "mem-seq",
                    HostPort: 5341),

                PgAdmin: new PgAdminSetupConfig(
                    Enabled: false,
                    ContainerName: "mem-pgadmin",
                    HostPort: 5050),

                Portainer: new PortainerSetupConfig(
                    Enabled: true,
                    UseExistingIfDetected: true,
                    ContainerName: "portainer",
                    HostPort: 9443)),

            Preflight: null,
            Review: null);
    }
}