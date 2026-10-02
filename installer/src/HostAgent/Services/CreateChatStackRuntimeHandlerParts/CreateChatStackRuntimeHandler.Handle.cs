using HostAgent.Commands;
using HostAgent.Element.Runtime;
using HostAgent.Runtime.Coturn;
using HostAgent.Runtime.Databases;
using HostAgent.Runtime.Ingress;
using HostAgent.Runtime.Readiness;
using HostAgent.Runtime.Secrets;
using HostAgent.Runtime.ServiceRuntime;
using HostAgent.Runtime.Stacks.Turn;
using Modules.Shared.RuntimeImages;

namespace HostAgent.Services;

public sealed partial class CreateChatStackRuntimeHandler
{
    public async Task<CreateChatStackRuntimeResult> HandleAcceptedAsync(
        CreateChatStackRuntimeCommand command,
        Guid acceptedOperationId)
    {
        var warnings = new List<string>();
        var evidence = new List<HostAgentEvidence>();
        Guid? operationId = acceptedOperationId;
        var currentStep = "validate";
        RuntimeStackDatabaseProvisioningResult? provisionedDatabase = null;
        var databaseOwnershipPersisted = false;
        var activeRuntimeMutationStarted = false;
        CreateChatStackRuntimeOperationScope? operationLifetime = null;
        CancellationToken ct = default;

        try
        {
            Validate(command);

            operationLifetime = _operationLifetime.Begin(
                command.StackId,
                command.StackSlug!,
                CancellationToken.None);
            ct = operationLifetime.CancellationToken;


            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.create-stack.operation-lifetime.server-owned",
                Message: "Create-stack mutation is running under a bounded server-owned lifetime that is independent of the initiating HTTP request.",
                Data: new Dictionary<string, string?>
                {
                    ["requestAbortCancelsMutation"] = "false",
                    ["applicationStoppingCancelsMutation"] = "true",
                    ["operationTimeoutMinutes"] = CreateChatStackRuntimeOperationLifetime.DefaultOperationTimeout.TotalMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));

            currentStep = "resolve-domain";
            await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
            var domain = await _domainResolver.ResolveMainPlatformDomainAsync(
                command.RequestedDomainId,
                ct);

            currentStep = "plan-runtime";
            await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
            var hostnames = _hostnamePlanner.Plan(
                command.StackId,
                command.MatrixInstanceId,
                command.ElementInstanceId,
                command.StackSlug,
                domain.BaseDomain);

            var runtimePlan = _runtimePlanner.Plan(
                command.StackId,
                command.MatrixInstanceId,
                command.ElementInstanceId,
                command.StackSlug);

            currentStep = "prepare-runtime-images";
            await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
            var approvedSynapseRuntime = await _approvedOperationalRuntimeImageProvider
                .PrepareSynapseAsync(ct);
            var matrixImage = approvedSynapseRuntime.ResolvedImageId;

            ApprovedOperationalRuntimeImageDescriptor? approvedElementRuntime = null;
            if (command.ElementInstanceId.HasValue)
            {
                approvedElementRuntime = await _approvedOperationalRuntimeImageProvider
                    .PrepareElementAsync(ct);
            }

            var elementImage = approvedElementRuntime?.ResolvedImageId;

            currentStep = "require-platform-turn";
            await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
            var coturnSynapseConfig = CreateChatStackTurnPolicy.RequirePlatformConfiguration(
                await _coturnRuntimeService.GetSynapseConfigAsync(ct));

            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.coturn.required.ready",
                Message: "The required shared platform TURN service is structurally ready and will be written to Synapse before Matrix starts.",
                Data: new Dictionary<string, string?>
                {
                    ["publicHost"] = coturnSynapseConfig.PublicHost,
                    ["realm"] = coturnSynapseConfig.Realm,
                    ["turnUris"] = string.Join(", ", coturnSynapseConfig.TurnUris),
                    ["relayPortsPublished"] = coturnSynapseConfig.RelayPortsPublished ? "true" : "false",
                    ["expectedBaseDomain"] = coturnSynapseConfig.ExpectedBaseDomain
                }));

            currentStep = "resolve-secrets";
            await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
            var existingRuntimeStack = await _secretService.RuntimeStackExistsAsync(
                command.StackId,
                ct);

            var existingMatrixRegistrationSecret = await _secretService.GetMatrixRegistrationSharedSecretAsync(
                command.StackId,
                ct);

            var generatedMatrixRegistrationSecret = false;

            var matrixRegistrationSharedSecret = existingMatrixRegistrationSecret;

            if (string.IsNullOrWhiteSpace(matrixRegistrationSharedSecret) &&
                !existingRuntimeStack)
            {
                matrixRegistrationSharedSecret = RuntimeStackSecretService.CreateSecretValue();
                generatedMatrixRegistrationSecret = true;
            }

            var matrixRegistrationSecretSource = !string.IsNullOrWhiteSpace(existingMatrixRegistrationSecret)
                ? "per-stack-existing"
                : generatedMatrixRegistrationSecret
                    ? "per-stack-generated"
                    : "global-fallback";

            currentStep = "provision-database";
            await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
            var databaseProvisioning = await _databaseService.ProvisionMatrixDatabaseAsync(
                command.StackId,
                command.StackSlug,
                ct);
            provisionedDatabase = databaseProvisioning.Provisioning;

            var postgresSettings = databaseProvisioning.Provisioning.ToSynapseSettings(
                databaseProvisioning.Password);

            var directoriesToPrepare = new List<string>
            {
                runtimePlan.MatrixDataPath
            };

            if (command.ElementInstanceId.HasValue && runtimePlan.ElementDataPath is not null)
            {
                directoriesToPrepare.Add(runtimePlan.ElementDataPath);
            }

            currentStep = "prepare-filesystem";
            await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
            var preparedDirectories = await _directoryPreparer.PrepareAsync(
                directoriesToPrepare,
                ct);

            currentStep = "generate-synapse-config";
            await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
            var homeserverPath = await _synapseConfigGenerator.GenerateAsync(
                instanceId: command.MatrixInstanceId,
                serverName: hostnames.MatrixHost,
                dataPath: runtimePlan.MatrixDataPath,
                reportStats: false,
                cancellationToken: ct,
                registrationSharedSecret: matrixRegistrationSharedSecret,
                postgres: postgresSettings,
                coturn: coturnSynapseConfig,
                image: matrixImage);

            currentStep = "verify-turn-config";
            await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
            var generatedTurnConfiguration = await _turnConfigReader.ReadAsync(
                homeserverPath,
                ct);
            CreateChatStackTurnPolicy.EnsureGeneratedConfigurationMatches(
                generatedTurnConfiguration,
                coturnSynapseConfig);
            var turnInspectedAtUtc = DateTimeOffset.UtcNow;

            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.coturn.synapse-config.verified",
                Message: "The generated Synapse configuration was verified against the required shared platform TURN service before Matrix startup.",
                Data: new Dictionary<string, string?>
                {
                    ["publicHost"] = coturnSynapseConfig.PublicHost,
                    ["realm"] = coturnSynapseConfig.Realm,
                    ["turnUris"] = string.Join(", ", coturnSynapseConfig.TurnUris),
                    ["configurationSha256"] = generatedTurnConfiguration.FileSha256,
                    ["management"] = RuntimeStackTurnManagementKinds.MemManaged
                }));

            currentStep = "start-matrix";
            await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
            activeRuntimeMutationStarted = true;
            var matrixStart = await _matrixContainerStarter.EnsureStartedAsync(
                containerName: runtimePlan.MatrixContainerName,
                image: matrixImage,
                dataPath: runtimePlan.MatrixDataPath,
                networkName: runtimePlan.RuntimeNetworkName,
                internalBaseUrl: runtimePlan.MatrixInternalBaseUrl,
                cancellationToken: ct,
                allowPullIfMissing: false);

            string? elementConfigPath = null;
            ElementContainerStartResult? elementStart = null;

            if (command.ElementInstanceId.HasValue)
            {
                if (string.IsNullOrWhiteSpace(runtimePlan.ElementContainerName) ||
                    string.IsNullOrWhiteSpace(runtimePlan.ElementDataPath) ||
                    string.IsNullOrWhiteSpace(runtimePlan.ElementInternalHost) ||
                    string.IsNullOrWhiteSpace(runtimePlan.ElementInternalBaseUrl) ||
                    string.IsNullOrWhiteSpace(hostnames.ElementHost) ||
                    string.IsNullOrWhiteSpace(hostnames.ElementPublicBaseUrl))
                {
                    throw new InvalidOperationException(
                        "Element runtime was requested, but the Element runtime plan or hostname plan is incomplete.");
                }

                currentStep = "write-element-config";
                await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
                elementConfigPath = Path.Combine(
                    runtimePlan.ElementDataPath,
                    "config.json");

                await _elementConfigGenerator.WriteHostConfigAsync(
                    path: elementConfigPath,
                    homeserverBaseUrl: hostnames.MatrixPublicBaseUrl,
                    homeserverServerName: hostnames.MatrixHost,
                    ct);

                currentStep = "start-element";
                await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
                elementStart = await _elementContainerStarter.EnsureStartedAsync(
                    containerName: runtimePlan.ElementContainerName,
                    image: elementImage
                        ?? throw new InvalidOperationException("The approved Element runtime image was not prepared."),
                    dataPath: runtimePlan.ElementDataPath,
                    configPath: elementConfigPath,
                    networkName: runtimePlan.RuntimeNetworkName,
                    internalBaseUrl: runtimePlan.ElementInternalBaseUrl,
                    cancellationToken: ct,
                    allowPullIfMissing: false);
            }

            RoutePublishResult? matrixPublicRoute = null;
            RoutePublishResult? elementPublicRoute = null;

            if (domain.ActiveCertificateId is not null && domain.ActiveNpmCertificateId is null)
            {
                warnings.Add(
                    "Active installer certificate was found, but it does not have an imported NPM certificate id. Public routes may be created without TLS.");
            }

            if (domain.ActiveCertificateId is null)
            {
                warnings.Add(
                    "No active installer certificate was found for the selected platform domain. Public routes may be created without TLS.");
            }

            currentStep = "publish-matrix-route";
            await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
            matrixPublicRoute = await _routePublisher.EnsureAsync(
                new RoutePublishRequest(
                    Domain: hostnames.MatrixHost,
                    ForwardHost: runtimePlan.MatrixInternalHost,
                    ForwardPort: 8008,
                    Kind: RouteKind.Matrix,
                    IsPublic: true,
                    RequireSsl: true,
                    CertificateId: domain.ActiveNpmCertificateId,
                    ForceSsl: true,
                    Http2: true),
                ct);

            if (elementStart is not null &&
                !string.IsNullOrWhiteSpace(hostnames.ElementHost) &&
                !string.IsNullOrWhiteSpace(runtimePlan.ElementInternalHost))
            {
                currentStep = "publish-element-route";
                await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
                elementPublicRoute = await _routePublisher.EnsureAsync(
                    new RoutePublishRequest(
                        Domain: hostnames.ElementHost,
                        ForwardHost: runtimePlan.ElementInternalHost,
                        ForwardPort: 80,
                        Kind: RouteKind.ElementWeb,
                        IsPublic: true,
                        RequireSsl: true,
                        CertificateId: domain.ActiveNpmCertificateId,
                        ForceSsl: true,
                        Http2: true),
                    ct);
            }

            var routesCreated = matrixPublicRoute is not null &&
                                (elementStart is null || elementPublicRoute is not null);

            RuntimeReadinessVerificationResult? readiness = null;

            if (routesCreated &&
                elementStart is not null &&
                !string.IsNullOrWhiteSpace(hostnames.ElementHost) &&
                !string.IsNullOrWhiteSpace(hostnames.ElementPublicBaseUrl) &&
                !string.IsNullOrWhiteSpace(runtimePlan.ElementInternalHost) &&
                !string.IsNullOrWhiteSpace(runtimePlan.ElementInternalBaseUrl))
            {
                currentStep = "verify-readiness";
                await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
                readiness = await _readinessVerifier.VerifyAsync(
                    new RuntimeReadinessVerificationRequest(
                        MatrixInternalBaseUrl: runtimePlan.MatrixInternalBaseUrl,
                        ElementInternalBaseUrl: runtimePlan.ElementInternalBaseUrl,
                        MatrixPublicBaseUrl: hostnames.MatrixPublicBaseUrl,
                        ElementPublicBaseUrl: hostnames.ElementPublicBaseUrl,
                        MatrixPublicHost: hostnames.MatrixHost,
                        ElementPublicHost: hostnames.ElementHost,
                        MatrixForwardHost: runtimePlan.MatrixInternalHost,
                        MatrixForwardPort: 8008,
                        ElementForwardHost: runtimePlan.ElementInternalHost,
                        ElementForwardPort: 80,
                        ExpectedNpmCertificateId: domain.ActiveNpmCertificateId),
                    ct);
            }

            if (routesCreated)
            {
                warnings.Add("Matrix and Element containers were started, and public NPM routes were created.");
            }
            else if (elementStart is not null)
            {
                warnings.Add("Matrix and Element containers were started. Public NPM routes were not fully created.");
            }
            else
            {
                warnings.Add("Matrix container was started. Element container and public NPM routes were not fully created.");
            }

            if (readiness is not null && readiness.AllPassed)
            {
                warnings.Add("Matrix and Element internal/public readiness checks passed.");
            }
            else if (readiness is not null)
            {
                var failed = readiness.Checks
                    .Where(x => !x.Success)
                    .Select(x => x.Code)
                    .ToArray();

                warnings.Add(
                    $"One or more runtime readiness checks failed: {string.Join(", ", failed)}");
            }

            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.command.validated",
                Message: "CreateChatStackRuntimeCommand passed first-stage validation.",
                Data: new Dictionary<string, string?>
                {
                    ["stackId"] = command.StackId.ToString(),
                    ["matrixInstanceId"] = command.MatrixInstanceId.ToString(),
                    ["elementInstanceId"] = command.ElementInstanceId?.ToString(),
                    ["stackSlug"] = command.StackSlug,
                    ["requestedDomainId"] = command.RequestedDomainId,
                    ["displayName"] = command.DisplayName,
                    ["category"] = command.Category,
                    ["idempotencyKey"] = command.IdempotencyKey,
                    ["operationId"] = operationId?.ToString()
                }));

            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.domain.resolved",
                Message: "Active platform domain was resolved by HostAgent from the installer database.",
                Data: new Dictionary<string, string?>
                {
                    ["domainId"] = domain.DomainId.ToString(),
                    ["baseDomain"] = domain.BaseDomain,
                    ["displayName"] = domain.DisplayName,
                    ["activeCertificateId"] = domain.ActiveCertificateId?.ToString(),
                    ["activeNpmCertificateId"] = domain.ActiveNpmCertificateId?.ToString()
                }));

            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.hostnames.planned",
                Message: "Matrix/Element public hostnames were derived by HostAgent.",
                Data: new Dictionary<string, string?>
                {
                    ["matrixHost"] = hostnames.MatrixHost,
                    ["matrixPublicBaseUrl"] = hostnames.MatrixPublicBaseUrl,
                    ["elementHost"] = hostnames.ElementHost,
                    ["elementPublicBaseUrl"] = hostnames.ElementPublicBaseUrl
                }));

            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.runtime.plan.created",
                Message: "Docker runtime names, network, internal URLs, and filesystem paths were planned by HostAgent.",
                Data: new Dictionary<string, string?>
                {
                    ["runtimeNetworkName"] = runtimePlan.RuntimeNetworkName,
                    ["matrixContainerName"] = runtimePlan.MatrixContainerName,
                    ["matrixInternalHost"] = runtimePlan.MatrixInternalHost,
                    ["matrixInternalBaseUrl"] = runtimePlan.MatrixInternalBaseUrl,
                    ["matrixDataPath"] = runtimePlan.MatrixDataPath,
                    ["elementContainerName"] = runtimePlan.ElementContainerName,
                    ["elementInternalHost"] = runtimePlan.ElementInternalHost,
                    ["elementInternalBaseUrl"] = runtimePlan.ElementInternalBaseUrl,
                    ["elementDataPath"] = runtimePlan.ElementDataPath
                }));

            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.matrix.registration-secret.resolved",
                Message: "Matrix registration shared secret source was resolved by HostAgent.",
                Data: new Dictionary<string, string?>
                {
                    ["source"] = matrixRegistrationSecretSource,
                    ["generated"] = generatedMatrixRegistrationSecret ? "true" : "false"
                }));

            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.matrix.postgres.provisioned",
                Message: "Per-stack Matrix Postgres database and role were created or verified by HostAgent.",
                Data: new Dictionary<string, string?>
                {
                    ["databaseEngine"] = databaseProvisioning.Provisioning.DatabaseEngine,
                    ["databaseHost"] = databaseProvisioning.Provisioning.DatabaseHost,
                    ["databasePort"] = databaseProvisioning.Provisioning.DatabasePort.ToString(),
                    ["databaseName"] = databaseProvisioning.Provisioning.DatabaseName,
                    ["databaseUsername"] = databaseProvisioning.Provisioning.DatabaseUsername,
                    ["passwordSecretKind"] = databaseProvisioning.Provisioning.PasswordSecretKind,
                    ["status"] = databaseProvisioning.Provisioning.Status,
                    ["databaseCreatedOrVerified"] = databaseProvisioning.Provisioning.DatabaseCreatedOrVerified ? "true" : "false",
                    ["userCreatedOrVerified"] = databaseProvisioning.Provisioning.UserCreatedOrVerified ? "true" : "false"
                }));

            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.runtime.directories.prepared",
                Message: "Runtime filesystem directories were created or verified by HostAgent.",
                Data: preparedDirectories.ToDictionary(
                    item => item.Path,
                    item => (string?)(item.ExistsAfterPrepare ? "exists" : "missing"))));

            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.matrix.synapse-config.generated",
                Message: "Synapse homeserver.yaml was generated or verified by HostAgent.",
                Data: new Dictionary<string, string?>
                {
                    ["homeserverPath"] = homeserverPath,
                    ["exists"] = File.Exists(homeserverPath) ? "true" : "false",
                    ["serverName"] = hostnames.MatrixHost,
                    ["registrationSecretSource"] = matrixRegistrationSecretSource,
                    ["turnConfigured"] = "true",
                    ["turnPublicHost"] = coturnSynapseConfig.PublicHost,
                    ["turnRealm"] = coturnSynapseConfig.Realm,
                    ["turnUris"] = string.Join(", ", coturnSynapseConfig.TurnUris),
                    ["turnRelayPortsPublished"] = coturnSynapseConfig.RelayPortsPublished ? "true" : "false",
                    ["turnSharedSecretPresent"] = generatedTurnConfiguration.SharedSecretPresent ? "true" : "false",
                    ["turnUserLifetime"] = generatedTurnConfiguration.UserLifetime,
                    ["turnAllowGuests"] = generatedTurnConfiguration.AllowGuests.HasValue
                        ? generatedTurnConfiguration.AllowGuests.Value ? "true" : "false"
                        : null,
                    ["turnConfigurationSource"] = "platform-coturn",
                    ["turnManagement"] = RuntimeStackTurnManagementKinds.MemManaged,
                    ["turnConfigurationSha256"] = generatedTurnConfiguration.FileSha256,
                    ["turnLastOperationId"] = operationId.Value.ToString("D"),
                    ["turnLastOperationMode"] = "create",
                    ["turnLastInspectedAtUtc"] = turnInspectedAtUtc.ToString("O"),
                    ["databaseEngine"] = databaseProvisioning.Provisioning.DatabaseEngine,
                    ["databaseHost"] = databaseProvisioning.Provisioning.DatabaseHost,
                    ["databasePort"] = databaseProvisioning.Provisioning.DatabasePort.ToString(),
                    ["databaseName"] = databaseProvisioning.Provisioning.DatabaseName,
                    ["databaseUsername"] = databaseProvisioning.Provisioning.DatabaseUsername,
                    ["databasePasswordSecretKind"] = databaseProvisioning.Provisioning.PasswordSecretKind
                }));

            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.matrix.container.started",
                Message: "Matrix container was created or started by HostAgent.",
                Data: new Dictionary<string, string?>
                {
                    ["containerId"] = matrixStart.ContainerId,
                    ["containerName"] = matrixStart.ContainerName,
                    ["image"] = matrixStart.Image,
                    ["alreadyExisted"] = matrixStart.AlreadyExisted ? "true" : "false",
                    ["running"] = matrixStart.Running ? "true" : "false",
                    ["networkName"] = matrixStart.NetworkName,
                    ["dataPath"] = matrixStart.DataPath,
                    ["internalBaseUrl"] = matrixStart.InternalBaseUrl
                }));

            if (elementConfigPath is not null)
            {
                evidence.Add(new HostAgentEvidence(
                    Code: "host-agent.element.config.generated",
                    Message: "Element config.json was generated by HostAgent.",
                    Data: new Dictionary<string, string?>
                    {
                        ["configPath"] = elementConfigPath,
                        ["exists"] = File.Exists(elementConfigPath) ? "true" : "false",
                        ["homeserverBaseUrl"] = hostnames.MatrixPublicBaseUrl,
                        ["homeserverServerName"] = hostnames.MatrixHost
                    }));
            }

            if (elementStart is not null)
            {
                evidence.Add(new HostAgentEvidence(
                    Code: "host-agent.element.container.started",
                    Message: "Element container was created or started by HostAgent.",
                    Data: new Dictionary<string, string?>
                    {
                        ["containerId"] = elementStart.ContainerId,
                        ["containerName"] = elementStart.ContainerName,
                        ["image"] = elementStart.Image,
                        ["alreadyExisted"] = elementStart.AlreadyExisted ? "true" : "false",
                        ["running"] = elementStart.Running ? "true" : "false",
                        ["networkName"] = elementStart.NetworkName,
                        ["dataPath"] = elementStart.DataPath,
                        ["configPath"] = elementStart.ConfigPath,
                        ["internalBaseUrl"] = elementStart.InternalBaseUrl
                    }));
            }

            if (matrixPublicRoute is not null)
            {
                evidence.Add(new HostAgentEvidence(
                    Code: "host-agent.matrix.public-route.created",
                    Message: "Matrix public NPM route was created or updated by HostAgent.",
                    Data: new Dictionary<string, string?>
                    {
                        ["routeId"] = matrixPublicRoute.RouteId,
                        ["domain"] = matrixPublicRoute.Domain,
                        ["forwardHost"] = matrixPublicRoute.ForwardHost,
                        ["forwardPort"] = matrixPublicRoute.ForwardPort.ToString(),
                        ["kind"] = matrixPublicRoute.Kind.ToString(),
                        ["sslExpected"] = matrixPublicRoute.SslExpected ? "true" : "false",
                        ["sslConfigured"] = matrixPublicRoute.SslConfigured ? "true" : "false",
                        ["certificateId"] = matrixPublicRoute.CertificateId?.ToString(),
                        ["ready"] = matrixPublicRoute.Ready ? "true" : "false",
                        ["warning"] = matrixPublicRoute.Warning
                    }));
            }

            if (elementPublicRoute is not null)
            {
                evidence.Add(new HostAgentEvidence(
                    Code: "host-agent.element.public-route.created",
                    Message: "Element public NPM route was created or updated by HostAgent.",
                    Data: new Dictionary<string, string?>
                    {
                        ["routeId"] = elementPublicRoute.RouteId,
                        ["domain"] = elementPublicRoute.Domain,
                        ["forwardHost"] = elementPublicRoute.ForwardHost,
                        ["forwardPort"] = elementPublicRoute.ForwardPort.ToString(),
                        ["kind"] = elementPublicRoute.Kind.ToString(),
                        ["sslExpected"] = elementPublicRoute.SslExpected ? "true" : "false",
                        ["sslConfigured"] = elementPublicRoute.SslConfigured ? "true" : "false",
                        ["certificateId"] = elementPublicRoute.CertificateId?.ToString(),
                        ["ready"] = elementPublicRoute.Ready ? "true" : "false",
                        ["warning"] = elementPublicRoute.Warning
                    }));
            }

            if (readiness is not null)
            {
                foreach (var check in readiness.Checks)
                {
                    evidence.Add(new HostAgentEvidence(
                        Code: check.Code,
                        Message: check.Success
                            ? $"{check.Name} passed."
                            : $"{check.Name} failed.",
                        Data: new Dictionary<string, string?>
                        {
                            ["url"] = check.Url,
                            ["success"] = check.Success ? "true" : "false",
                            ["statusCode"] = check.StatusCode?.ToString(),
                            ["detail"] = check.Detail,
                            ["bodyPreview"] = check.BodyPreview
                        }));
                }
            }

            var matrix = new HostAgentServiceRuntimeResult(
                InstanceId: command.MatrixInstanceId,
                ServiceKey: ServiceKeys.Matrix,
                ContainerId: matrixStart.ContainerId,
                ContainerName: matrixStart.ContainerName,
                HostPort: 0,
                DataPath: runtimePlan.MatrixDataPath,
                ServerName: hostnames.MatrixHost,
                PublicHost: hostnames.MatrixHost,
                PublicBaseUrl: hostnames.MatrixPublicBaseUrl,
                InternalHost: runtimePlan.MatrixInternalHost,
                InternalBaseUrl: runtimePlan.MatrixInternalBaseUrl,
                PublicRouteId: matrixPublicRoute?.RouteId,
                InternalRouteId: null,
                NpmCertificateId: matrixPublicRoute?.CertificateId,
                RuntimeMetadata: new Dictionary<string, string?>
                {
                    ["plannedOnly"] = "false",
                    ["preparedOnly"] = "false",
                    ["configuredOnly"] = "false",
                    ["matrixStarted"] = "true",
                    ["matrixImage"] = matrixStart.Image,
                    ["approvedMatrixImageReference"] = approvedSynapseRuntime.ApprovedReference,
                    ["homeserverPath"] = homeserverPath,
                    ["domainId"] = domain.DomainId.ToString(),
                    ["baseDomain"] = domain.BaseDomain,
                    ["activeCertificateId"] = domain.ActiveCertificateId?.ToString(),
                    ["activeNpmCertificateId"] = domain.ActiveNpmCertificateId?.ToString(),
                    ["runtimeNetworkName"] = runtimePlan.RuntimeNetworkName,
                    ["registrationSecretSource"] = matrixRegistrationSecretSource,
                    ["turnConfigured"] = "true",
                    ["turnPublicHost"] = coturnSynapseConfig.PublicHost,
                    ["turnRealm"] = coturnSynapseConfig.Realm,
                    ["turnUris"] = string.Join(", ", coturnSynapseConfig.TurnUris),
                    ["turnRelayPortsPublished"] = coturnSynapseConfig.RelayPortsPublished ? "true" : "false",
                    ["turnSharedSecretPresent"] = generatedTurnConfiguration.SharedSecretPresent ? "true" : "false",
                    ["turnUserLifetime"] = generatedTurnConfiguration.UserLifetime,
                    ["turnAllowGuests"] = generatedTurnConfiguration.AllowGuests.HasValue
                        ? generatedTurnConfiguration.AllowGuests.Value ? "true" : "false"
                        : null,
                    ["turnConfigurationSource"] = "platform-coturn",
                    ["turnManagement"] = RuntimeStackTurnManagementKinds.MemManaged,
                    ["turnConfigurationSha256"] = generatedTurnConfiguration.FileSha256,
                    ["turnLastOperationId"] = operationId.Value.ToString("D"),
                    ["turnLastOperationMode"] = "create",
                    ["turnLastInspectedAtUtc"] = turnInspectedAtUtc.ToString("O"),
                    ["databaseEngine"] = databaseProvisioning.Provisioning.DatabaseEngine,
                    ["databaseHost"] = databaseProvisioning.Provisioning.DatabaseHost,
                    ["databasePort"] = databaseProvisioning.Provisioning.DatabasePort.ToString(),
                    ["databaseName"] = databaseProvisioning.Provisioning.DatabaseName,
                    ["databaseUsername"] = databaseProvisioning.Provisioning.DatabaseUsername,
                    ["databasePasswordSecretKind"] = databaseProvisioning.Provisioning.PasswordSecretKind,
                    ["databaseStatus"] = databaseProvisioning.Provisioning.Status,
                    ["publicRouteId"] = matrixPublicRoute?.RouteId,
                    ["publicRouteReady"] = matrixPublicRoute?.Ready == true ? "true" : "false",
                    ["npmCertificateId"] = matrixPublicRoute?.CertificateId?.ToString(),
                    ["publicForwardHost"] = matrixPublicRoute?.ForwardHost,
                    ["publicForwardPort"] = matrixPublicRoute?.ForwardPort.ToString(),
                    ["readinessVerified"] = readiness?.AllPassed == true ? "true" : "false",
                    ["matrixInternalReady"] = ReadinessSuccess(
                        readiness,
                        "host-agent.matrix.internal-http.reachable"),
                    ["matrixPublicReady"] = ReadinessSuccess(
                        readiness,
                        "host-agent.matrix.public-https.reachable"),
                    ["npmReady"] = ReadinessSuccess(
                        readiness,
                        "host-agent.npm.ready"),
                    ["matrixRouteConfigured"] = ReadinessSuccess(
                        readiness,
                        "host-agent.matrix.public-route.configured")
                });

            HostAgentServiceRuntimeResult? element = null;

            if (command.ElementInstanceId.HasValue && hostnames.ElementHost is not null)
            {
                element = new HostAgentServiceRuntimeResult(
                    InstanceId: command.ElementInstanceId.Value,
                    ServiceKey: ServiceKeys.ElementWeb,
                    ContainerId: elementStart?.ContainerId ?? string.Empty,
                    ContainerName: elementStart?.ContainerName
                        ?? runtimePlan.ElementContainerName
                        ?? $"mem-element-{Short(command.ElementInstanceId.Value)}",
                    HostPort: 0,
                    DataPath: runtimePlan.ElementDataPath,
                    ServerName: null,
                    PublicHost: hostnames.ElementHost,
                    PublicBaseUrl: hostnames.ElementPublicBaseUrl,
                    InternalHost: runtimePlan.ElementInternalHost,
                    InternalBaseUrl: runtimePlan.ElementInternalBaseUrl,
                    PublicRouteId: elementPublicRoute?.RouteId,
                    InternalRouteId: null,
                    NpmCertificateId: elementPublicRoute?.CertificateId,
                    RuntimeMetadata: new Dictionary<string, string?>
                    {
                        ["plannedOnly"] = "false",
                        ["preparedOnly"] = "false",
                        ["configuredOnly"] = elementConfigPath is not null ? "false" : "true",
                        ["matrixStarted"] = "true",
                        ["elementConfigured"] = elementConfigPath is not null ? "true" : "false",
                        ["elementStarted"] = elementStart?.Running == true ? "true" : "false",
                        ["elementImage"] = elementStart?.Image ?? elementImage,
                        ["approvedElementImageReference"] = approvedElementRuntime?.ApprovedReference,
                        ["elementConfigPath"] = elementConfigPath,
                        ["domainId"] = domain.DomainId.ToString(),
                        ["baseDomain"] = domain.BaseDomain,
                        ["activeCertificateId"] = domain.ActiveCertificateId?.ToString(),
                        ["activeNpmCertificateId"] = domain.ActiveNpmCertificateId?.ToString(),
                        ["runtimeNetworkName"] = runtimePlan.RuntimeNetworkName,
                        ["matrixPublicBaseUrl"] = hostnames.MatrixPublicBaseUrl,
                        ["matrixInternalBaseUrl"] = runtimePlan.MatrixInternalBaseUrl,
                        ["matrixHomeserverPath"] = homeserverPath,
                        ["registrationSecretSource"] = matrixRegistrationSecretSource,
                        ["turnConfigured"] = "true",
                        ["turnPublicHost"] = coturnSynapseConfig.PublicHost,
                        ["turnRealm"] = coturnSynapseConfig.Realm,
                        ["turnUris"] = string.Join(", ", coturnSynapseConfig.TurnUris),
                        ["turnRelayPortsPublished"] = coturnSynapseConfig.RelayPortsPublished ? "true" : "false",
                        ["turnConfigurationSource"] = "platform-coturn",
                        ["turnManagement"] = RuntimeStackTurnManagementKinds.MemManaged,
                        ["matrixDatabaseEngine"] = databaseProvisioning.Provisioning.DatabaseEngine,
                        ["matrixDatabaseHost"] = databaseProvisioning.Provisioning.DatabaseHost,
                        ["matrixDatabasePort"] = databaseProvisioning.Provisioning.DatabasePort.ToString(),
                        ["matrixDatabaseName"] = databaseProvisioning.Provisioning.DatabaseName,
                        ["matrixDatabaseUsername"] = databaseProvisioning.Provisioning.DatabaseUsername,
                        ["matrixDatabasePasswordSecretKind"] = databaseProvisioning.Provisioning.PasswordSecretKind,
                        ["publicRouteId"] = elementPublicRoute?.RouteId,
                        ["publicRouteReady"] = elementPublicRoute?.Ready == true ? "true" : "false",
                        ["npmCertificateId"] = elementPublicRoute?.CertificateId?.ToString(),
                        ["publicForwardHost"] = elementPublicRoute?.ForwardHost,
                        ["publicForwardPort"] = elementPublicRoute?.ForwardPort.ToString(),
                        ["readinessVerified"] = readiness?.AllPassed == true ? "true" : "false",
                        ["elementInternalReady"] = ReadinessSuccess(
                            readiness,
                            "host-agent.element.internal-http.reachable"),
                        ["elementPublicReady"] = ReadinessSuccess(
                            readiness,
                            "host-agent.element.public-https.reachable"),
                        ["npmReady"] = ReadinessSuccess(
                            readiness,
                            "host-agent.npm.ready"),
                        ["elementRouteConfigured"] = ReadinessSuccess(
                            readiness,
                            "host-agent.element.public-route.configured")
                    });
            }

            var readinessVerified = readiness?.AllPassed == true;

            var status = readinessVerified
                ? "public_routes_verified"
                : routesCreated
                    ? "public_routes_created"
                    : elementStart is not null
                        ? "element_started"
                        : "matrix_started";

            var message = readinessVerified
                ? "HostAgent validated the command, resolved the active platform domain, prepared runtime files, provisioned the Matrix Postgres database, started Matrix and Element, created public NPM routes, and verified internal/public readiness checks."
                : routesCreated
                    ? "HostAgent validated the command, resolved the active platform domain, prepared runtime files, provisioned the Matrix Postgres database, started Matrix and Element, and created public NPM routes. One or more readiness checks may still need attention."
                    : elementStart is not null
                        ? "HostAgent validated the command, resolved the active platform domain, prepared runtime files, provisioned the Matrix Postgres database, started the Matrix container, generated Element config, and started the Element container. NPM routes are not enabled yet."
                        : "HostAgent validated the command, resolved the active platform domain, prepared Matrix runtime files, provisioned the Matrix Postgres database, and started the Matrix container. Element was not requested and NPM routes are not enabled yet.";

            var result = new CreateChatStackRuntimeResult(
                StackId: command.StackId,
                Status: status,
                Message: message,
                Matrix: matrix,
                Element: element,
                Warnings: warnings,
                Evidence: evidence);

            currentStep = "persist-manifest";
            await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
            await _manifestStore.SaveAsync(
                command.StackSlug,
                result,
                ct,
                displayName: command.DisplayName,
                category: command.Category);

            currentStep = "persist-database-ownership";
            await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
            await _databaseService.SaveOwnershipAsync(
                databaseProvisioning.Provisioning,
                databaseProvisioning.Password,
                ct);
            databaseOwnershipPersisted = true;

            if (generatedMatrixRegistrationSecret &&
                !string.IsNullOrWhiteSpace(matrixRegistrationSharedSecret))
            {
                currentStep = "persist-stack-secret";
                await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
                await _secretService.UpsertMatrixRegistrationSharedSecretAsync(
                    result.StackId,
                    matrixRegistrationSharedSecret,
                    source: "create-stack-runtime",
                    ct);
            }

            if (operationId.HasValue)
            {
                currentStep = "attach-runtime-stack";
                await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
                await _operationStore.AttachRuntimeStackAsync(
                    operationId.Value,
                    result.StackId,
                    ct);

                currentStep = "complete";
                await _operationStore.UpdateStepAsync(operationId.Value, currentStep, ct);
                await _operationStore.CompleteAsync(
                    operationId.Value,
                    status: "succeeded",
                    currentStep: currentStep,
                    result: new
                    {
                        result.StackId,
                        result.Status,
                        result.Message
                    },
                    evidence: result.Evidence,
                    ct);
            }

            return result;
        }
        catch (Exception ex)
        {
            if (operationId.HasValue)
            {
                await _failureFinalizer.FinalizeAsync(
                    operationId.Value,
                    currentStep,
                    command,
                    provisionedDatabase,
                    databaseOwnershipPersisted,
                    activeRuntimeMutationStarted,
                    ex,
                    evidence,
                    requestCancellationRequested: false,
                    operationTimeoutRequested: operationLifetime?.OperationTimeoutRequested == true,
                    applicationStoppingRequested: operationLifetime?.ApplicationStoppingRequested == true);
            }

            throw;
        }
        finally
        {
            operationLifetime?.Dispose();
        }
    }
}