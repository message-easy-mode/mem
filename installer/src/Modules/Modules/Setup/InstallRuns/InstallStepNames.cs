namespace Modules.Setup.InstallRuns;

public static class InstallStepNames
{
    public const string ValidateInstallPlan = "Validate install plan";
    public const string InstallMemCliHostCommand = "Install MEM CLI host command";
    public const string CreateOrVerifyDockerNetwork = "Create or verify Docker network";
    public const string CreatePersistentVolumes = "Create persistent volumes";
    public const string StartPostgres = "Start Postgres";
    public const string WaitForPostgresReadiness = "Wait for Postgres readiness";
    public const string StartNpmIngress = "Start NPM / ingress";
    public const string IssueAndImportPlatformCertificate = "Issue and import platform certificate";
    public const string ResolvePlatformDomainAndCertificate = "Resolve platform domain and certificate";
    public const string InstallSharedPlatformTurn = "Install shared platform TURN";
    public const string VerifySharedPlatformTurn = "Verify shared platform TURN";

    // Retained only so installation plans persisted by an older MEM v0.1.0-era
    // workflow can be completed safely. These steps are never added to a new
    // plan and the executor treats them as successful retirement no-ops.
    public const string StartMemApi = "Start MEM API";
    public const string WaitForMemApiHealth = "Wait for MEM API health";
    public const string StartMemWeb = "Start MEM Web";
    public const string ConfigurePlatformIngressRoutes = "Configure platform ingress routes";
    public const string VerifyPlatformRoutes = "Verify platform routes";

    public const string StartSelectedSupportTools = "Start selected support tools";
    public const string RunVerificationChecks = "Run verification checks";
    public const string CompleteSetupHandoff = "Complete setup handoff";

    public static IReadOnlyList<string> RetiredLegacyApplicationSteps { get; } =
    [
        StartMemApi,
        WaitForMemApiHealth,
        StartMemWeb,
        ConfigurePlatformIngressRoutes,
        VerifyPlatformRoutes
    ];

    public static IReadOnlyList<string> InitialSteps { get; } =
    [
        ValidateInstallPlan,
        InstallMemCliHostCommand,
        CreateOrVerifyDockerNetwork,
        CreatePersistentVolumes,
        StartPostgres,
        WaitForPostgresReadiness,
        StartNpmIngress,
        IssueAndImportPlatformCertificate,
        InstallSharedPlatformTurn,
        VerifySharedPlatformTurn,
        StartSelectedSupportTools,
        RunVerificationChecks,
        CompleteSetupHandoff
    ];

    public static bool IsRetiredLegacyApplicationStep(string stepName) =>
        RetiredLegacyApplicationSteps.Contains(stepName, StringComparer.Ordinal);
}
