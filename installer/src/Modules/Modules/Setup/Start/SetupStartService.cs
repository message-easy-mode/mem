using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Modules.Setup.HostChecks.Runtime;
using Modules.Setup.InstallRuns;
using Modules.Setup.Platform.Coturn;
using Shared.ControlPlane.Runtime;

namespace Modules.Setup.Start;

public sealed class SetupStartService
{
    private readonly ISetupDockerRuntimeProbe _dockerProbe;
    private readonly MemDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SetupStartService> _logger;
    private readonly MemControlPlaneRuntimeContext? _runtimeContext;
    private readonly IPlatformCoturnSetupService? _platformCoturnSetupService;

    public SetupStartService(
        ISetupDockerRuntimeProbe dockerProbe,
        MemDbContext db,
        IConfiguration configuration,
        ILogger<SetupStartService> logger,
        MemControlPlaneRuntimeContext? runtimeContext = null,
        IPlatformCoturnSetupService? platformCoturnSetupService = null)
    {
        _dockerProbe = dockerProbe;
        _db = db;
        _configuration = configuration;
        _logger = logger;
        _runtimeContext = runtimeContext;
        _platformCoturnSetupService = platformCoturnSetupService;
    }

    public async Task<SetupStartResponse> GetAsync(CancellationToken cancellationToken)
    {
        var targetVersion = ResolveTargetVersion();

        IReadOnlyList<SetupDockerContainer> containers;
        try
        {
            containers = await _dockerProbe.ListContainersAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Setup start could not inspect the Docker daemon through the Control Plane Docker API client.");

            return new SetupStartResponse(
                SetupMode: SetupStartModes.Unknown,
                InstallationState: SetupStartInstallationStates.Unknown,
                RecommendedAction: SetupStartRecommendedActions.ReviewDiagnostics,
                StartupTarget: SetupStartTargets.SetupStart,
                Docker: new DockerStatusDto(
                    Reachable: false,
                    Message: "Docker is not reachable from the private MEM control plane."),
                DetectedInstallation: null,
                RequiredServices: BuildRequiredServices([]),
                SupportToolsServices: BuildSupportToolsServices([]),
                Warnings:
                [
                    new InstallerWarningDto(
                        "docker-unreachable",
                        "Docker is not responding",
                        "MEM cannot inspect platform dependencies until Docker is reachable.",
                        Blocking: true)
                ]);
        }

        var requiredServices = await ApplyCoturnReadinessAsync(
            BuildRequiredServices(containers),
            cancellationToken);
        var supportToolsServices = BuildSupportToolsServices(containers);
        var legacy = ObserveLegacyApplications(containers);

        var latestInstallation = await _db.Installations
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var completedInstallation =
            latestInstallation is not null &&
            string.Equals(
                latestInstallation.Status,
                InstallationStatuses.Succeeded,
                StringComparison.OrdinalIgnoreCase)
                ? latestInstallation
                : await _db.Installations
                    .AsNoTracking()
                    .Where(x => x.Status == InstallationStatuses.Succeeded)
                    .OrderByDescending(x => x.UpdatedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

        if (completedInstallation is not null &&
            latestInstallation is not null &&
            completedInstallation.Id != latestInstallation.Id)
        {
            _logger.LogInformation(
                "Durable completed installation {CompletedInstallationId} remains authoritative over newer non-completed installation {LatestInstallationId} with status {LatestInstallationStatus}.",
                completedInstallation.Id,
                latestInstallation.Id,
                latestInstallation.Status);
        }

        var installation = completedInstallation ?? latestInstallation;

        if (installation is null)
        {
            if (legacy.Detected)
            {
                _logger.LogInformation(
                    "Legacy MEM v0.1.0 API/Web containers were detected on a MEM v0.2.0 host without a current installation record. ApiFound={ApiFound} WebFound={WebFound}",
                    legacy.ApiContainer is not null,
                    legacy.WebContainer is not null);

                return new SetupStartResponse(
                    SetupMode: SetupStartModes.MigrationRequired,
                    InstallationState: SetupStartInstallationStates.PartiallyInstalled,
                    RecommendedAction: SetupStartRecommendedActions.UseMemMigrate,
                    StartupTarget: SetupStartTargets.SetupStart,
                    Docker: ReachableDocker(),
                    DetectedInstallation: BuildLegacyDetection(legacy, targetVersion),
                    RequiredServices: requiredServices,
                    SupportToolsServices: supportToolsServices,
                    Warnings:
                    [
                        new InstallerWarningDto(
                            "legacy-v010-applications-detected",
                            "Legacy MEM installation detected",
                            "The mem-api or mem-web containers belong to MEM v0.1.0. MEM v0.2.0 must be installed on a separate server; use MEM Migrate to move from the v0.1.0 source server to the v0.2.0 target server.",
                            Blocking: true)
                    ]);
            }

            var establishedState = await EstablishedOperatorStateProbe.ObserveAsync(
                _db,
                cancellationToken);
            if (establishedState.Established)
            {
                _logger.LogWarning(
                    "Startup found established MEM v0.2.0 operator state without a historical installation record. Evidence={Evidence}",
                    string.Join(",", establishedState.Evidence));

                return new SetupStartResponse(
                    SetupMode: SetupStartModes.Repair,
                    InstallationState: SetupStartInstallationStates.PartiallyInstalled,
                    RecommendedAction: SetupStartRecommendedActions.ReviewDiagnostics,
                    StartupTarget: SetupStartTargets.Dashboard,
                    Docker: ReachableDocker(),
                    DetectedInstallation: new DetectedMemInstallationDto(
                        Detected: true,
                        ApiContainerFound: false,
                        ApiContainerRunning: false,
                        ApiReachable: false,
                        ProductName: "MEM Control Plane",
                        Version: targetVersion,
                        TargetVersion: targetVersion,
                        UpgradeAvailable: false,
                        CurrentVersion: true),
                    RequiredServices: requiredServices,
                    SupportToolsServices: supportToolsServices,
                    Warnings:
                    [
                        new InstallerWarningDto(
                            "installation-history-missing",
                            "Historical installation record is unavailable",
                            "This MEM v0.2.0 Control Plane already contains durable operator-managed state, so normal startup will continue to the dashboard. First-time setup is not assumed from missing history; review Diagnostics before any repair or setup action.",
                            Blocking: false)
                    ]);
            }

            if (HasExistingManagedResources(requiredServices, containers))
            {
                return new SetupStartResponse(
                    SetupMode: SetupStartModes.Repair,
                    InstallationState: SetupStartInstallationStates.PartiallyInstalled,
                    RecommendedAction: SetupStartRecommendedActions.RunRepairCheck,
                    StartupTarget: SetupStartTargets.SetupStart,
                    Docker: ReachableDocker(),
                    DetectedInstallation: new DetectedMemInstallationDto(
                        Detected: true,
                        ApiContainerFound: false,
                        ApiContainerRunning: false,
                        ApiReachable: false,
                        ProductName: "MEM resources",
                        Version: null,
                        TargetVersion: targetVersion,
                        UpgradeAvailable: false,
                        CurrentVersion: false),
                    RequiredServices: requiredServices,
                    SupportToolsServices: supportToolsServices,
                    Warnings:
                    [
                        new InstallerWarningDto(
                            "existing-managed-resources",
                            "Existing MEM resources detected",
                            "MEM-related Docker resources exist, but this Control Plane has no authoritative installation history for them. Treat the resources as orphaned or unproven and review the host before continuing.",
                            Blocking: false)
                    ]);
            }

            return new SetupStartResponse(
                SetupMode: SetupStartModes.FreshInstall,
                InstallationState: SetupStartInstallationStates.NotInstalled,
                RecommendedAction: SetupStartRecommendedActions.RunSetupCheck,
                StartupTarget: SetupStartTargets.SetupStart,
                Docker: ReachableDocker(),
                DetectedInstallation: new DetectedMemInstallationDto(
                    Detected: false,
                    ApiContainerFound: false,
                    ApiContainerRunning: false,
                    ApiReachable: false,
                    ProductName: null,
                    Version: null,
                    TargetVersion: targetVersion,
                    UpgradeAvailable: false,
                    CurrentVersion: false),
                RequiredServices: requiredServices,
                SupportToolsServices: supportToolsServices,
                Warnings: []);
        }

        if (InstallationStatuses.IsPlanning(installation.Status))
        {
            _logger.LogInformation(
                "Startup found active first-time setup authority {InstallationId} with status {InstallationStatus}.",
                installation.Id,
                installation.Status);

            return new SetupStartResponse(
                SetupMode: SetupStartModes.FreshInstall,
                InstallationState: SetupStartInstallationStates.PartiallyInstalled,
                RecommendedAction: SetupStartRecommendedActions.ContinueSetup,
                StartupTarget: SetupStartTargets.SetupStart,
                Docker: ReachableDocker(),
                DetectedInstallation: new DetectedMemInstallationDto(
                    Detected: false,
                    ApiContainerFound: false,
                    ApiContainerRunning: false,
                    ApiReachable: false,
                    ProductName: "MEM Control Plane",
                    Version: targetVersion,
                    TargetVersion: targetVersion,
                    UpgradeAvailable: false,
                    CurrentVersion: true),
                RequiredServices: requiredServices,
                SupportToolsServices: supportToolsServices,
                Warnings:
                [
                    new InstallerWarningDto(
                        "setup-planning-in-progress",
                        "Platform setup is in progress",
                        "MEM found the durable first-time setup plan for this Control Plane. Continue that setup instead of starting a second installation plan.",
                        Blocking: false)
                ],
                ActiveInstallationId: installation.Id);
        }

        var warnings = BuildCurrentInstallationWarnings(legacy);
        var detectedCurrent = new DetectedMemInstallationDto(
            Detected: true,
            ApiContainerFound: legacy.ApiContainer is not null,
            ApiContainerRunning: IsRunning(legacy.ApiContainer),
            ApiReachable: false,
            ProductName: "MEM Control Plane",
            Version: targetVersion,
            TargetVersion: targetVersion,
            UpgradeAvailable: false,
            CurrentVersion: true);

        var requiredReady = requiredServices.All(service =>
            service.Installed &&
            service.Running &&
            service.Healthy is not false);

        if (InstallationStatuses.IsResumableRun(installation.Status))
        {
            var resumeStage = await ResolveResumeStageAsync(
                installation.Id,
                installation.Status,
                cancellationToken);

            _logger.LogInformation(
                "Startup selected resumable installation {InstallationId}. Status={InstallationStatus} Stage={InstallationStage}",
                installation.Id,
                installation.Status,
                resumeStage);

            return new SetupStartResponse(
                SetupMode: SetupStartModes.Repair,
                InstallationState: string.Equals(
                    installation.Status,
                    InstallationStatuses.Failed,
                    StringComparison.OrdinalIgnoreCase)
                        ? SetupStartInstallationStates.RepairRequired
                        : SetupStartInstallationStates.PartiallyInstalled,
                RecommendedAction: resumeStage == SetupActiveInstallationStages.Verification
                    ? SetupStartRecommendedActions.ReviewVerification
                    : SetupStartRecommendedActions.ResumeInstall,
                StartupTarget: SetupStartTargets.ResumeInstallation,
                Docker: ReachableDocker(),
                DetectedInstallation: detectedCurrent,
                RequiredServices: requiredServices,
                SupportToolsServices: supportToolsServices,
                Warnings:
                [
                    .. warnings,
                    BuildResumeWarning(
                        installation.Id,
                        installation.Status,
                        resumeStage)
                ],
                ActiveInstallationId: installation.Id,
                ActiveInstallationStage: resumeStage);
        }

        if (string.Equals(installation.Status, InstallationStatuses.Succeeded, StringComparison.OrdinalIgnoreCase))
        {
            var handoffPending = await _db.InstallationStepExecutions
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.InstallationId == installation.Id &&
                        x.StepName == InstallStepNames.CompleteSetupHandoff &&
                        x.Status == InstallationStepStatuses.WaitingForUser,
                    cancellationToken);

            if (handoffPending && requiredReady)
            {
                _logger.LogInformation(
                    "Startup selected pending setup handoff for completed installation {InstallationId}.",
                    installation.Id);

                return new SetupStartResponse(
                    SetupMode: SetupStartModes.AlreadyInstalled,
                    InstallationState: SetupStartInstallationStates.Installed,
                    RecommendedAction: SetupStartRecommendedActions.CompleteHandoff,
                    StartupTarget: SetupStartTargets.ResumeInstallation,
                    Docker: ReachableDocker(),
                    DetectedInstallation: detectedCurrent,
                    RequiredServices: requiredServices,
                    SupportToolsServices: supportToolsServices,
                    Warnings:
                    [
                        .. warnings,
                        new InstallerWarningDto(
                            "installation-handoff-required",
                            "Setup is ready to finish",
                            "Installation and verification completed successfully. Finish the durable setup handoff before normal root startup continues to the dashboard.",
                            Blocking: false)
                    ],
                    ActiveInstallationId: installation.Id,
                    ActiveInstallationStage: SetupActiveInstallationStages.Handoff);
            }

            if (requiredReady)
            {
                return new SetupStartResponse(
                    SetupMode: SetupStartModes.AlreadyInstalled,
                    InstallationState: SetupStartInstallationStates.Installed,
                    RecommendedAction: SetupStartRecommendedActions.OpenDashboard,
                    StartupTarget: SetupStartTargets.Dashboard,
                    Docker: ReachableDocker(),
                    DetectedInstallation: detectedCurrent,
                    RequiredServices: requiredServices,
                    SupportToolsServices: supportToolsServices,
                    Warnings: warnings);
            }

            return new SetupStartResponse(
                SetupMode: SetupStartModes.Repair,
                InstallationState: SetupStartInstallationStates.RepairRequired,
                RecommendedAction: SetupStartRecommendedActions.RunRepairCheck,
                StartupTarget: SetupStartTargets.Dashboard,
                Docker: ReachableDocker(),
                DetectedInstallation: detectedCurrent,
                RequiredServices: requiredServices,
                SupportToolsServices: supportToolsServices,
                Warnings:
                [
                    .. warnings,
                    new InstallerWarningDto(
                        "required-dependency-not-ready",
                        "A required platform dependency needs attention",
                        "The MEM v0.2.0 installation completed, but Postgres, Nginx Proxy Manager, or the shared Coturn TURN service is not currently ready.",
                        Blocking: false)
                ]);
        }

        return new SetupStartResponse(
            SetupMode: SetupStartModes.Repair,
            InstallationState: string.Equals(
                installation.Status,
                InstallationStatuses.Failed,
                StringComparison.OrdinalIgnoreCase)
                    ? SetupStartInstallationStates.RepairRequired
                    : SetupStartInstallationStates.PartiallyInstalled,
            RecommendedAction: SetupStartRecommendedActions.RunRepairCheck,
            StartupTarget: SetupStartTargets.SetupStart,
            Docker: ReachableDocker(),
            DetectedInstallation: detectedCurrent,
            RequiredServices: requiredServices,
            SupportToolsServices: supportToolsServices,
            Warnings:
            [
                .. warnings,
                new InstallerWarningDto(
                    "installation-not-complete",
                    "Control-plane setup is not complete",
                    $"The current MEM v0.2.0 installation record has status '{installation.Status}'. Review setup activity before making further changes.",
                    Blocking: false)
            ]);
    }

    private async Task<string> ResolveResumeStageAsync(
        Guid installationId,
        string installationStatus,
        CancellationToken cancellationToken)
    {
        if (!installationStatus.Equals(InstallationStatuses.Failed, StringComparison.OrdinalIgnoreCase))
        {
            return SetupActiveInstallationStages.Activity;
        }

        var failedStep = await _db.InstallationStepExecutions
            .AsNoTracking()
            .Where(x =>
                x.InstallationId == installationId &&
                x.Status == InstallationStepStatuses.Failed)
            .OrderByDescending(x => x.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

        if (failedStep is not null &&
            string.Equals(
                failedStep.StepName,
                InstallStepNames.RunVerificationChecks,
                StringComparison.Ordinal))
        {
            return SetupActiveInstallationStages.Verification;
        }

        return SetupActiveInstallationStages.FailureReview;
    }

    private static InstallerWarningDto BuildResumeWarning(
        Guid installationId,
        string installationStatus,
        string resumeStage)
    {
        if (resumeStage == SetupActiveInstallationStages.Verification)
        {
            return new InstallerWarningDto(
                "installation-verification-required",
                "Installation verification needs review",
                $"Installation '{installationId:D}' failed during final verification. MEM will reopen the verification report so the operator can review evidence before retrying or continuing.",
                Blocking: false);
        }

        return new InstallerWarningDto(
            "installation-resumable",
            "An incomplete installation can be resumed",
            $"Installation '{installationId:D}' has status '{installationStatus}'. MEM will reopen the authoritative installation activity instead of restarting first-time setup.",
            Blocking: false);
    }

    private string ResolveTargetVersion()
    {
        var runtimeVersion = _runtimeContext?.Version?.Trim();
        if (!string.IsNullOrWhiteSpace(runtimeVersion) &&
            !string.Equals(runtimeVersion, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return runtimeVersion;
        }

        return _configuration["Product:Version"] ??
            _configuration["Mem:TargetVersion"] ??
            "0.2.0";
    }

    private static DockerStatusDto ReachableDocker() =>
        new(
            Reachable: true,
            Message: "Docker is reachable from the private MEM control plane.");

    private static IReadOnlyList<InstallerWarningDto> BuildCurrentInstallationWarnings(
        LegacyApplicationObservation legacy)
    {
        if (!legacy.Detected)
        {
            return [];
        }

        return
        [
            new InstallerWarningDto(
                "legacy-applications-observed",
                "Legacy MEM API/Web containers are present",
                "MEM v0.2.0 does not adopt, manage, start, stop, or route these MEM v0.1.0 application containers. MEM v0.1.0 and v0.2.0 belong on separate servers; retain the v0.1.0 server only as the migration source until migration is accepted.",
                Blocking: false)
        ];
    }

    private static DetectedMemInstallationDto BuildLegacyDetection(
        LegacyApplicationObservation legacy,
        string targetVersion) =>
        new(
            Detected: true,
            ApiContainerFound: legacy.ApiContainer is not null,
            ApiContainerRunning: IsRunning(legacy.ApiContainer),
            ApiReachable: false,
            ProductName: "Message Easy Mode v0.1.0 migration source",
            Version: "0.1.0",
            TargetVersion: targetVersion,
            UpgradeAvailable: false,
            CurrentVersion: false);

    private static bool HasExistingManagedResources(
        IReadOnlyList<InstallerServiceStatusDto> requiredServices,
        IReadOnlyList<SetupDockerContainer> containers) =>
        requiredServices.Any(service => service.Installed) ||
        containers.Any(container =>
        {
            var name = NormalizeContainerName(container.Name);
            return name.StartsWith("mem-matrix-", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("mem-element", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("mem-coturn", StringComparison.OrdinalIgnoreCase);
        });

    private static LegacyApplicationObservation ObserveLegacyApplications(
        IReadOnlyList<SetupDockerContainer> containers) =>
        new(
            ApiContainer: FindContainer(containers, "mem-api"),
            WebContainer: FindContainer(containers, "mem-web"));

    private static SetupDockerContainer? FindContainer(
        IReadOnlyList<SetupDockerContainer> containers,
        string name) =>
        containers.FirstOrDefault(container =>
            NormalizeContainerName(container.Name)
                .Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool IsRunning(SetupDockerContainer? container) =>
        container is not null &&
        (container.State.Equals("running", StringComparison.OrdinalIgnoreCase) ||
         container.Status.Contains("Up", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<InstallerServiceStatusDto> BuildRequiredServices(
        IReadOnlyList<SetupDockerContainer> containers)
    {
        return
        [
            BuildService(
                containers,
                key: "postgres",
                displayName: "Postgres",
                description: "Managed PostgreSQL service used by MEM v0.2.0 stack runtimes and recovery workflows.",
                required: true,
                containerNames: ["mem-postgres", "postgres"]),

            BuildService(
                containers,
                key: "npm",
                displayName: "Nginx Proxy Manager",
                description: "Ingress, TLS certificate, and public Matrix/Element routing provider.",
                required: true,
                containerNames: ["mem-npm", "npm"]),

            BuildService(
                containers,
                key: "coturn",
                displayName: "Coturn (TURN)",
                description: "Shared voice/video relay service used by MEM-managed Matrix stacks.",
                required: true,
                containerNames: [PlatformCoturnSetupDefaults.ContainerName])
        ];
    }

    private async Task<IReadOnlyList<InstallerServiceStatusDto>> ApplyCoturnReadinessAsync(
        IReadOnlyList<InstallerServiceStatusDto> services,
        CancellationToken cancellationToken)
    {
        var coturnIndex = services
            .Select((service, index) => (service, index))
            .FirstOrDefault(item => item.service.Key == "coturn");

        if (coturnIndex.service is null ||
            !coturnIndex.service.Installed ||
            _platformCoturnSetupService is null)
        {
            return services;
        }

        var mutable = services.ToArray();
        try
        {
            var inspection = await _platformCoturnSetupService.InspectAsync(cancellationToken);
            mutable[coturnIndex.index] = coturnIndex.service with
            {
                Running = inspection.Running,
                Healthy = inspection.Ready,
                State = inspection.Ready ? "ready" : inspection.Readiness,
                Warnings = inspection.Warnings
                    .Select(warning => new InstallerWarningDto(
                        "coturn-readiness-warning",
                        "Shared platform TURN needs attention",
                        warning,
                        Blocking: false))
                    .ToArray()
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            mutable[coturnIndex.index] = coturnIndex.service with
            {
                Healthy = false,
                State = "inspection-failed",
                Warnings =
                [
                    new InstallerWarningDto(
                        "coturn-inspection-failed",
                        "Shared platform TURN could not be verified",
                        "MEM could not safely inspect the shared platform TURN runtime. Open the Coturn workspace or Diagnostics for bounded evidence.",
                        Blocking: false)
                ]
            };
        }

        return mutable;
    }

    private static IReadOnlyList<InstallerServiceStatusDto> BuildSupportToolsServices(
        IReadOnlyList<SetupDockerContainer> containers)
    {
        return
        [
            BuildService(
                containers,
                key: "seq",
                displayName: "Seq",
                description: "Structured logs and troubleshooting visibility for control-plane and runtime diagnostics.",
                required: false,
                containerNames: ["mem-seq", "seq"]),

            BuildService(
                containers,
                key: "pgadmin",
                displayName: "PgAdmin",
                description: "Database inspection tool for development, support, and emergency troubleshooting.",
                required: false,
                containerNames: ["mem-pgadmin", "pgadmin"]),

            BuildService(
                containers,
                key: "portainer",
                displayName: "Portainer",
                description: "General Docker visibility and emergency operator access outside the MEM UI.",
                required: false,
                containerNames: ["portainer"])
        ];
    }

    private static InstallerServiceStatusDto BuildService(
        IReadOnlyList<SetupDockerContainer> containers,
        string key,
        string displayName,
        string description,
        bool required,
        IReadOnlyCollection<string> containerNames)
    {
        var container = containers.FirstOrDefault(container =>
            containerNames.Any(name =>
                NormalizeContainerName(container.Name)
                    .Equals(name, StringComparison.OrdinalIgnoreCase)));

        if (container is null)
        {
            return new InstallerServiceStatusDto(
                Key: key,
                DisplayName: displayName,
                Description: description,
                Required: required,
                Installed: false,
                Running: false,
                Healthy: null,
                State: "not-installed",
                ContainerName: null,
                Image: null,
                Urls: [],
                Warnings: []);
        }

        var running = IsRunning(container);

        bool? healthy =
            container.Status.Contains("unhealthy", StringComparison.OrdinalIgnoreCase)
                ? false
                : container.Status.Contains("healthy", StringComparison.OrdinalIgnoreCase)
                    ? true
                    : null;

        return new InstallerServiceStatusDto(
            Key: key,
            DisplayName: displayName,
            Description: description,
            Required: required,
            Installed: true,
            Running: running,
            Healthy: healthy,
            State: running ? "running" : container.State,
            ContainerName: NormalizeContainerName(container.Name),
            Image: container.Image,
            Urls: [],
            Warnings: []);
    }

    private static string NormalizeContainerName(string value) =>
        value.Trim().TrimStart('/');

    private sealed record LegacyApplicationObservation(
        SetupDockerContainer? ApiContainer,
        SetupDockerContainer? WebContainer)
    {
        public bool Detected => ApiContainer is not null || WebContainer is not null;
    }

}
