using Core.RuntimeDefinition;
using Shared.ControlPlane.Runtime;
using Infrastructure.Docker;
using Modules.Integrations.Npm.Services;
using Modules.Shared.RuntimeImages;
using Modules.Integrations.Portainer.Services;
using Modules.Setup.Platform.Coturn;

namespace Modules.Setup.InstallRuns;

public sealed partial class InstallStepExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string NetworkName = "mem-gateway";
    private const string NpmImage = NpmRuntimeRelease.ApprovedImage;
    private const string NpmDataVolumeName = "mem_npm_data";
    private const string NpmLetsEncryptVolumeName = "mem_npm_letsencrypt";

    private readonly ICommandRunner _commandRunner;
    private readonly IDockerHost _dockerHost;
    private readonly ISetupDockerRuntimeProbe _dockerRuntimeProbe;
    private readonly InstallNpmAdminProbe _npmAdminProbe;
    private readonly CertificateStorageService _certificateStorage;
    private readonly CertificateValidationService _certificateValidation;
    private readonly NpmCertificateImportProbe _npmCertificateProbe;
    private readonly NpmReadinessService _npmReadiness;
    private readonly NpmProxyHostService _npmProxyHostService;
    private readonly IOptions<NpmApiOptions> _npmOptions;
    private readonly ILogger<InstallStepExecutor> _logger;
    private readonly MemDbContext _db;
    private readonly MemCliHostCommandInstaller _memCliHostCommandInstaller;
    private readonly IApprovedPostgresRuntimeProvider _approvedPostgresRuntimeProvider;
    private readonly PortainerRuntimeService _portainerRuntimeService;
    private readonly IInstallationSecretStore _installationSecretStore;
    private readonly InstallPlatformCertificateProvisioner _platformCertificateProvisioner;
    private readonly INpmInitialAdminBootstrapService _npmInitialAdminBootstrap;
    private readonly MemControlPlaneRuntimeContext? _runtimeContext;
    private readonly InstallProgressReporter? _progressReporter;
    private readonly IPlatformCoturnSetupService? _platformCoturnSetupService;

    public InstallStepExecutor(
        ICommandRunner commandRunner,
        IDockerHost dockerHost,
        ISetupDockerRuntimeProbe dockerRuntimeProbe,
        InstallNpmAdminProbe npmAdminProbe,
        MemDbContext db,
        CertificateStorageService certificateStorage,
        CertificateValidationService certificateValidation,
        NpmCertificateImportProbe npmCertificateProbe,
        NpmReadinessService npmReadiness,
        NpmProxyHostService npmProxyHostService,
        IOptions<NpmApiOptions> npmOptions,
        MemCliHostCommandInstaller memCliHostCommandInstaller,
        IApprovedPostgresRuntimeProvider approvedPostgresRuntimeProvider,
        PortainerRuntimeService portainerRuntimeService,
        IInstallationSecretStore installationSecretStore,
        InstallPlatformCertificateProvisioner platformCertificateProvisioner,
        INpmInitialAdminBootstrapService npmInitialAdminBootstrap,
        ILogger<InstallStepExecutor> logger,
        MemControlPlaneRuntimeContext? runtimeContext = null,
        InstallProgressReporter? progressReporter = null,
        IPlatformCoturnSetupService? platformCoturnSetupService = null)
    {
        _commandRunner = commandRunner;
        _dockerHost = dockerHost;
        _dockerRuntimeProbe = dockerRuntimeProbe;
        _npmAdminProbe = npmAdminProbe;
        _db = db;
        _certificateStorage = certificateStorage;
        _certificateValidation = certificateValidation;
        _npmCertificateProbe = npmCertificateProbe;
        _npmReadiness = npmReadiness;
        _npmProxyHostService = npmProxyHostService;
        _npmOptions = npmOptions;
        _memCliHostCommandInstaller = memCliHostCommandInstaller;
        _approvedPostgresRuntimeProvider = approvedPostgresRuntimeProvider;
        _portainerRuntimeService = portainerRuntimeService;
        _installationSecretStore = installationSecretStore;
        _platformCertificateProvisioner = platformCertificateProvisioner;
        _npmInitialAdminBootstrap = npmInitialAdminBootstrap;
        _logger = logger;
        _runtimeContext = runtimeContext;
        _progressReporter = progressReporter;
        _platformCoturnSetupService = platformCoturnSetupService;
    }

    public async Task<InstallStepResult> ExecuteAsync(
        InstallStepContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Executing installation step {StepSequence}: {StepName} for installation {InstallationId}",
            context.Sequence,
            context.StepName,
            context.InstallationId);

        if (InstallStepNames.IsRetiredLegacyApplicationStep(context.StepName))
        {
            return Succeeded(
                $"Retired legacy application step '{context.StepName}' was skipped. " +
                "MEM v0.2.0 does not install or route the retired mem-api or mem-web applications.");
        }

        return context.StepName switch
        {
            InstallStepNames.ValidateInstallPlan => await ValidateInstallPlanAsync(context, cancellationToken),
            InstallStepNames.InstallMemCliHostCommand => await _memCliHostCommandInstaller.InstallAsync(cancellationToken),
            InstallStepNames.CreateOrVerifyDockerNetwork => await CreateOrVerifyDockerNetworkAsync(context, cancellationToken),
            InstallStepNames.CreatePersistentVolumes => await CreatePersistentVolumesAsync(context, cancellationToken),
            InstallStepNames.StartPostgres => await StartPostgresAsync(context, cancellationToken),
            InstallStepNames.WaitForPostgresReadiness => await WaitForPostgresReadinessAsync(context, cancellationToken),
            InstallStepNames.StartNpmIngress => await StartNpmIngressAsync(context, cancellationToken),
            InstallStepNames.IssueAndImportPlatformCertificate => await PreparePlatformPublicAccessAsync(context, cancellationToken),
            InstallStepNames.ResolvePlatformDomainAndCertificate => await PreparePlatformPublicAccessAsync(context, cancellationToken),
            InstallStepNames.InstallSharedPlatformTurn => await InstallSharedPlatformTurnAsync(context, cancellationToken),
            InstallStepNames.VerifySharedPlatformTurn => await VerifySharedPlatformTurnAsync(context, cancellationToken),
            InstallStepNames.StartSelectedSupportTools => await StartSelectedSupportToolsAsync(
                context,
                cancellationToken),
            InstallStepNames.RunVerificationChecks => await RunFinalVerificationChecksAsync(context, cancellationToken),
            InstallStepNames.CompleteSetupHandoff => new InstallStepResult(
                Succeeded: false,
                Message: "Verification passed. Finish setup to acknowledge the handoff into the operator Control Plane.",
                ErrorMessage: null,
                StepStatus: "WaitingForUser"),

            _ => await PlaceholderAsync(
                $"Step '{context.StepName}' completed by placeholder executor.",
                cancellationToken)
        };
    }
}