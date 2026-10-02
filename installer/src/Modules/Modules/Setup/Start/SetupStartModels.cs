namespace Modules.Setup.Start;

public sealed record SetupStartResponse(
    string SetupMode,
    string InstallationState,
    string RecommendedAction,
    string StartupTarget,
    DockerStatusDto Docker,
    DetectedMemInstallationDto? DetectedInstallation,
    IReadOnlyList<InstallerServiceStatusDto> RequiredServices,
    IReadOnlyList<InstallerServiceStatusDto> SupportToolsServices,
    IReadOnlyList<InstallerWarningDto> Warnings,
    Guid? ActiveInstallationId = null,
    string? ActiveInstallationStage = null);

public sealed record DetectedMemInstallationDto(
    bool Detected,
    bool ApiContainerFound,
    bool ApiContainerRunning,
    bool ApiReachable,
    string? ProductName,
    string? Version,
    string? TargetVersion,
    bool UpgradeAvailable,
    bool CurrentVersion);

public sealed record DockerStatusDto(
    bool Reachable,
    string? Message);

public sealed record InstallerServiceStatusDto(
    string Key,
    string DisplayName,
    string Description,
    bool Required,
    bool Installed,
    bool Running,
    bool? Healthy,
    string State,
    string? ContainerName,
    string? Image,
    IReadOnlyList<ServiceUrlDto> Urls,
    IReadOnlyList<InstallerWarningDto> Warnings);

public sealed record ServiceUrlDto(
    string Label,
    string Href);

public sealed record InstallerWarningDto(
    string Code,
    string Title,
    string Message,
    bool Blocking);

public static class SetupStartModes
{
    public const string FreshInstall = "fresh-install";
    public const string MigrationRequired = "migration-required";
    public const string Upgrade = "upgrade";
    public const string AlreadyInstalled = "already-installed";
    public const string Repair = "repair";
    public const string Unknown = "unknown";
}

public static class SetupStartInstallationStates
{
    public const string NotInstalled = "not-installed";
    public const string PartiallyInstalled = "partially-installed";
    public const string Installed = "installed";
    public const string RepairRequired = "repair-required";
    public const string Unknown = "unknown";
}

public static class SetupStartTargets
{
    public const string Dashboard = "dashboard";
    public const string SetupStart = "setup-start";
    public const string ResumeInstallation = "resume-installation";
}

public static class SetupActiveInstallationStages
{
    public const string Activity = "activity";
    public const string FailureReview = "failure-review";
    public const string Verification = "verification";
    public const string Handoff = "handoff";
}

public static class SetupStartRecommendedActions
{
    public const string RunSetupCheck = "run-setup-check";
    public const string UseMemMigrate = "use-mem-migrate";
    public const string RunUpgradeCheck = "run-upgrade-check";
    public const string RunRepairCheck = "run-repair-check";
    public const string OpenDashboard = "open-dashboard";
    public const string ReviewDiagnostics = "review-diagnostics";
    public const string ContinueSetup = "continue-setup";
    public const string ResumeInstall = "resume-install";
    public const string ReviewVerification = "review-verification";
    public const string CompleteHandoff = "complete-handoff";
}
