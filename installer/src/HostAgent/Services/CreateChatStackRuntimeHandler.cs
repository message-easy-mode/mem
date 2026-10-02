using HostAgent.Commands;
using HostAgent.Element.Provisioning;
using HostAgent.Element.Runtime;
using HostAgent.Matrix.Provisioning;
using HostAgent.Matrix.Runtime;
using HostAgent.Planning;
using HostAgent.Platform;
using HostAgent.Runtime.Coturn;
using HostAgent.Runtime.Databases;
using HostAgent.Runtime.Filesystem;
using HostAgent.Runtime.Ingress;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Operations;
using HostAgent.Runtime.Readiness;
using HostAgent.Runtime.Secrets;
using HostAgent.Runtime.ServiceRuntime;
using HostAgent.Runtime.Stacks.Turn;
using Modules.Shared.RuntimeImages;

namespace HostAgent.Services;

/// <summary>
/// HostAgent Matrix + Element + public route + readiness verification stage:
/// validate command input, resolve active installer domain,
/// derive public/runtime details, prepare directories,
/// provision per-stack Postgres database,
/// generate Synapse config, start the Matrix container,
/// generate Element config, start the Element container,
/// create public NPM routes for Matrix and Element,
/// verify internal/public readiness,
/// persist the resulting runtime stack manifest,
/// persist database/secret ownership,
/// and journal the operation in the control-plane database.
/// </summary>
public sealed partial class CreateChatStackRuntimeHandler : ICreateChatStackRuntimeHandler
{
    private readonly PlatformDomainResolver _domainResolver;
    private readonly ChatStackHostnamePlanner _hostnamePlanner;
    private readonly ChatStackRuntimePlanner _runtimePlanner;
    private readonly RuntimeDirectoryPreparer _directoryPreparer;
    private readonly SynapseConfigGenerator _synapseConfigGenerator;
    private readonly MatrixContainerStarter _matrixContainerStarter;
    private readonly ElementConfigGenerator _elementConfigGenerator;
    private readonly ElementContainerStarter _elementContainerStarter;
    private readonly IRoutePublisher _routePublisher;
    private readonly RuntimeReadinessVerifier _readinessVerifier;
    private readonly RuntimeStackManifestStore _manifestStore;
    private readonly RuntimeOperationStore _operationStore;
    private readonly RuntimeStackSecretService _secretService;
    private readonly IRuntimeStackDatabaseService _databaseService;
    private readonly CoturnRuntimeService _coturnRuntimeService;
    private readonly SynapseTurnConfigReader _turnConfigReader;
    private readonly CreateChatStackRuntimeFailureFinalizer _failureFinalizer;
    private readonly CreateChatStackRuntimeOperationLifetime _operationLifetime;
    private readonly IApprovedOperationalRuntimeImageProvider _approvedOperationalRuntimeImageProvider;

    public CreateChatStackRuntimeHandler(
        PlatformDomainResolver domainResolver,
        ChatStackHostnamePlanner hostnamePlanner,
        ChatStackRuntimePlanner runtimePlanner,
        RuntimeDirectoryPreparer directoryPreparer,
        SynapseConfigGenerator synapseConfigGenerator,
        MatrixContainerStarter matrixContainerStarter,
        ElementConfigGenerator elementConfigGenerator,
        ElementContainerStarter elementContainerStarter,
        IRoutePublisher routePublisher,
        RuntimeReadinessVerifier readinessVerifier,
        RuntimeStackManifestStore manifestStore,
        RuntimeOperationStore operationStore,
        RuntimeStackSecretService secretService,
        IRuntimeStackDatabaseService databaseService,
        CoturnRuntimeService coturnRuntimeService,
        SynapseTurnConfigReader turnConfigReader,
        CreateChatStackRuntimeFailureFinalizer failureFinalizer,
        CreateChatStackRuntimeOperationLifetime operationLifetime,
        IApprovedOperationalRuntimeImageProvider approvedOperationalRuntimeImageProvider)
    {
        _domainResolver = domainResolver;
        _hostnamePlanner = hostnamePlanner;
        _runtimePlanner = runtimePlanner;
        _directoryPreparer = directoryPreparer;
        _synapseConfigGenerator = synapseConfigGenerator;
        _matrixContainerStarter = matrixContainerStarter;
        _elementConfigGenerator = elementConfigGenerator;
        _elementContainerStarter = elementContainerStarter;
        _routePublisher = routePublisher;
        _readinessVerifier = readinessVerifier;
        _manifestStore = manifestStore;
        _operationStore = operationStore;
        _secretService = secretService;
        _databaseService = databaseService;
        _coturnRuntimeService = coturnRuntimeService;
        _turnConfigReader = turnConfigReader;
        _failureFinalizer = failureFinalizer;
        _operationLifetime = operationLifetime;
        _approvedOperationalRuntimeImageProvider = approvedOperationalRuntimeImageProvider;
    }
}