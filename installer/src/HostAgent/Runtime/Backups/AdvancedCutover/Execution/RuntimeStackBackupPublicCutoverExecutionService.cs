using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Runtime.Backups.AdvancedCutover.Candidate;
using HostAgent.Runtime.Backups.AdvancedCutover.Confirmation;
using HostAgent.Runtime.Backups.AdvancedCutover.Preflight;
using HostAgent.Runtime.Backups.AdvancedCutover.Preview;
using HostAgent.Runtime.Ingress;
using Microsoft.Extensions.Logging;
using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;
using HostAgent.Runtime.Migrations.Cutover;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Execution;

public sealed class RuntimeStackBackupPublicCutoverExecutionService
{
    private readonly RuntimeStackBackupPublicCutoverConfirmationHistoryService _confirmationHistory;
    private readonly RuntimeStackBackupPublicCutoverPreviewHistoryService _previewHistory;
    private readonly RuntimeStackBackupProductionCandidateHistoryService _candidateHistory;
    private readonly RuntimeStackBackupProductionRestorePlanService _planService;
    private readonly ValidatedImportArtifactService _validatedImportArtifacts;
    private readonly DockerClient _docker;
    private readonly IRoutePublisher _routePublisher;
    private readonly RuntimeStackBackupPublicCutoverExecutionHistoryService _executionHistory;
    private readonly MigrationCutoverContextResolver _migrationContextResolver;
    private readonly ILogger<RuntimeStackBackupPublicCutoverExecutionService> _logger;

    public RuntimeStackBackupPublicCutoverExecutionService(
        RuntimeStackBackupPublicCutoverConfirmationHistoryService confirmationHistory,
        RuntimeStackBackupPublicCutoverPreviewHistoryService previewHistory,
        RuntimeStackBackupProductionCandidateHistoryService candidateHistory,
        RuntimeStackBackupProductionRestorePlanService planService,
        ValidatedImportArtifactService validatedImportArtifacts,
        DockerClient docker,
        IRoutePublisher routePublisher,
        RuntimeStackBackupPublicCutoverExecutionHistoryService executionHistory,
        MigrationCutoverContextResolver migrationContextResolver,
        ILogger<RuntimeStackBackupPublicCutoverExecutionService> logger)
    {
        _confirmationHistory = confirmationHistory;
        _previewHistory = previewHistory;
        _candidateHistory = candidateHistory;
        _planService = planService;
        _validatedImportArtifacts = validatedImportArtifacts;
        _docker = docker;
        _routePublisher = routePublisher;
        _executionHistory = executionHistory;
        _migrationContextResolver = migrationContextResolver;
        _logger = logger;
    }

    public async Task<RuntimeStackBackupPublicCutoverExecutionResult> ExecuteAsync(
        string confirmationId,
        RuntimeStackBackupPublicCutoverExecutionRequest request,
        CancellationToken ct)
    {
        ValidatePathSegment(confirmationId, "Public Cutover confirmation id");

        var executionId = CreateExecutionId();
        var startedAtUtc = DateTimeOffset.UtcNow;
        var checks = new List<RuntimeStackBackupPublicCutoverExecutionCheck>();
        var blockers = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();

        var confirmationResponse = await _confirmationHistory.GetConfirmationAsync(
            confirmationId,
            ct);

        var confirmation = confirmationResponse?.Confirmation;

        if (confirmation is null)
        {
            throw new FileNotFoundException($"Public Cutover confirmation '{confirmationId}' was not found.");
        }

        var previewResponse = await _previewHistory.GetPreviewAsync(
            confirmation.PreviewId,
            ct);

        var preview = previewResponse?.Preview;

        if (preview is null)
        {
            throw new FileNotFoundException($"Public Cutover preview '{confirmation.PreviewId}' was not found.");
        }

        var candidateResponse = await _candidateHistory.GetCandidateAsync(
            confirmation.CandidateId,
            ct);

        var candidate = candidateResponse?.Candidate;

        if (candidate is null)
        {
            throw new FileNotFoundException($"Production Restore candidate '{confirmation.CandidateId}' was not found.");
        }

        ValidateConfirmation(
            confirmation,
            preview,
            candidate,
            checks,
            blockers,
            warnings);

        RuntimeStackBackupProductionRestorePlanResult freshPlan;
        MigrationCutoverContext? migrationContext = null;

        if (string.Equals(preview.SourceKind, "migration-session", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(candidate.SourceKind, "migration-session", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(candidate.ValidationId, confirmation.ValidationId, StringComparison.OrdinalIgnoreCase))
            {
                blockers.Add("Migration confirmation, preview, and production candidate are not linked to one Migration Session.");
            }

            migrationContext = await _migrationContextResolver.ResolveAsync(
                confirmation.ValidationId,
                ct);

            freshPlan = await _planService.CreatePlanFromMigrationSourceAsync(
                confirmation.ValidationId,
                migrationContext.SourceSummary,
                confirmation.CandidateId,
                preview.Candidate.TargetStackSlug,
                confirmation.RestoreMode,
                preview.Routes.Matrix.Host,
                preview.Routes.Element.Host,
                ct);

            AddCheck(
                checks,
                "public-cutover-execution.migration.final-capture",
                "migration-source",
                migrationContext.Capture.FinalCutoverEligible ? "info" : "blocker",
                migrationContext.Capture.FinalCutoverEligible,
                migrationContext.Capture.FinalCutoverEligible ? "satisfied" : "blocked",
                migrationContext.Capture.FinalCutoverEligible
                    ? "Migration source package is a final frozen capture."
                    : "Migration source package is rehearsal-only or not frozen and cannot authorize public route execution.",
                migrationContext.Capture.Kind);

            if (!migrationContext.Capture.FinalCutoverEligible)
            {
                blockers.Add("A final frozen Migration capture is required before public cutover execution.");
            }
        }
        else
        {
            await _validatedImportArtifacts.EnsureRestoreCanContinueAsync(
                confirmation.ValidationId,
                ct);

            freshPlan = await _planService.CreatePlanAsync(
                confirmation.ValidationId,
                stagingId: null,
                candidateId: confirmation.CandidateId,
                targetStackSlug: preview.Candidate.TargetStackSlug,
                restoreMode: confirmation.RestoreMode,
                intendedMatrixHost: preview.Routes.Matrix.Host,
                intendedElementHost: preview.Routes.Element.Host,
                ct: ct);
        }

        warnings.AddRange(freshPlan.Warnings);
        errors.AddRange(freshPlan.Errors);

        ValidateFreshPlan(
            freshPlan,
            confirmation,
            checks,
            blockers);

        var acknowledgements = BuildAcknowledgements(request);
        var missingAcknowledgements = acknowledgements
            .Where(acknowledgement => acknowledgement.Required && !acknowledgement.Acknowledged)
            .ToArray();

        foreach (var acknowledgement in missingAcknowledgements)
        {
            blockers.Add($"Required execution acknowledgement missing: {acknowledgement.Label}");
        }

        AddCheck(
            checks,
            "public-cutover-execution.acknowledgements.required",
            "acknowledgement",
            missingAcknowledgements.Length == 0 ? "info" : "blocker",
            missingAcknowledgements.Length == 0,
            missingAcknowledgements.Length == 0 ? "satisfied" : "blocked",
            missingAcknowledgements.Length == 0
                ? "All required Public Cutover execution acknowledgements were supplied."
                : "One or more required Public Cutover execution acknowledgements are missing.",
            missingAcknowledgements.Length == 0
                ? null
                : string.Join("; ", missingAcknowledgements.Select(ack => ack.Code)));

        var candidateSummary = BuildCandidateSummary(candidate);
        var cutoverIntent = BuildCutoverIngressIntent(preview, candidate);

        RuntimeStackBackupPublicCutoverIngressSummary cutoverIngress;
        RuntimeStackBackupPublicCutoverExecutionRouteResult matrixResult;
        RuntimeStackBackupPublicCutoverExecutionRouteResult elementResult;

        if (blockers.Count == 0 && errors.Count == 0)
        {
            cutoverIngress = await EnsureCutoverIngressAsync(
                cutoverIntent,
                checks,
                warnings,
                errors,
                ct);

            if (!cutoverIngress.Ready)
            {
                blockers.Add("Cutover ingress network is not ready. Public NPM routes must not be created until NPM, candidate Synapse, and candidate Element can share the cutover ingress network without exposing Postgres.");
            }
        }
        else
        {
            cutoverIngress = BuildSkippedCutoverIngress(cutoverIntent);
        }

        if (blockers.Count == 0 && errors.Count == 0)
        {
            matrixResult = await ExecuteRouteAsync(
                component: "matrix",
                previewRoute: preview.Routes.Matrix,
                confirmationRoute: confirmation.Routes.Matrix,
                freshRoute: freshPlan.Npm.MatrixRoute,
                desiredForwardHost: cutoverIngress.MatrixAlias,
                kind: RouteKind.Matrix,
                required: true,
                checks,
                warnings,
                errors,
                ct);

            elementResult = matrixResult.Succeeded
                ? await ExecuteRouteAsync(
                    component: "element",
                    previewRoute: preview.Routes.Element,
                    confirmationRoute: confirmation.Routes.Element,
                    freshRoute: freshPlan.Npm.ElementRoute,
                    desiredForwardHost: cutoverIngress.ElementAlias,
                    kind: RouteKind.ElementWeb,
                    required: !string.IsNullOrWhiteSpace(preview.Routes.Element.Host),
                    checks,
                    warnings,
                    errors,
                    ct)
                : BuildBlockedRouteResult(
                    "element",
                    preview.Routes.Element,
                    confirmation.Routes.Element,
                    freshPlan.Npm.ElementRoute,
                    "Element route execution skipped because Matrix route execution did not succeed.");
        }
        else
        {
            matrixResult = BuildBlockedRouteResult(
                "matrix",
                preview.Routes.Matrix,
                confirmation.Routes.Matrix,
                freshPlan.Npm.MatrixRoute,
                "Execution blocked before Matrix route mutation.");

            elementResult = BuildBlockedRouteResult(
                "element",
                preview.Routes.Element,
                confirmation.Routes.Element,
                freshPlan.Npm.ElementRoute,
                "Execution blocked before Element route mutation.");
        }

        var anyRouteMutated = matrixResult.Mutated || elementResult.Mutated;
        var anyDockerNetworkMutated = cutoverIngress.DockerNetworksChanged;
        var allRequestedRoutesSucceeded =
            matrixResult.Succeeded &&
            (!elementResult.Requested || elementResult.Succeeded);

        var routes = new RuntimeStackBackupPublicCutoverExecutionRouteSummary(
            Matrix: matrixResult,
            Element: elementResult,
            AllRequestedRoutesSucceeded: allRequestedRoutesSucceeded,
            AnyRouteMutated: anyRouteMutated,
            Notes:
            [
                "Public Cutover Executor v1A creates a per-candidate cutover ingress network before ensuring NPM proxy hosts.",
                "NPM is attached to the cutover ingress network, not the private restore/database network.",
                "Only candidate Synapse and Element are attached to the cutover ingress network; candidate Postgres remains private-only.",
                "NPM routes point at stable cutover aliases instead of timestamped restore-staging container names.",
                "No DNS records are created, updated, or deleted.",
                "No certificates are requested, imported, renewed, or removed.",
                "No runtime stack is promoted or registered in this sprint.",
                "No production containers or databases are stopped, deleted, replaced, or modified."
            ]);

        if (matrixResult.Warning is not null)
        {
            warnings.Add($"Matrix route warning: {matrixResult.Warning}");
        }

        if (elementResult.Warning is not null)
        {
            warnings.Add($"Element route warning: {elementResult.Warning}");
        }

        if (matrixResult.Error is not null)
        {
            errors.Add($"Matrix route error: {matrixResult.Error}");
        }

        if (elementResult.Error is not null)
        {
            errors.Add($"Element route error: {elementResult.Error}");
        }

        var distinctBlockers = blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var distinctWarnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var distinctErrors = errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var status = DetermineStatus(
            distinctBlockers,
            distinctErrors,
            routes,
            cutoverIngress);

        var mutations = new RuntimeStackBackupProductionRestoreMutationSummary(
            RuntimeChanged: anyDockerNetworkMutated,
            ProductionContainersTouched: false,
            ProductionDatabasesTouched: false,
            DnsChanged: false,
            NpmRoutesChanged: anyRouteMutated,
            CertificatesChanged: false,
            PublicRoutesChanged: anyRouteMutated,
            FederationExposureChanged: matrixResult.Mutated,
            Notes: BuildMutationNotes(anyRouteMutated, matrixResult.Mutated, anyDockerNetworkMutated, cutoverIngress));

        var result = new RuntimeStackBackupPublicCutoverExecutionResult(
            Source: "control-plane",
            Status: status,
            ExecutionId: executionId,
            ConfirmationId: confirmation.ConfirmationId,
            PreviewId: confirmation.PreviewId,
            ValidationId: confirmation.ValidationId,
            CandidateId: confirmation.CandidateId,
            FreshPlanId: freshPlan.PlanId,
            RestoreMode: confirmation.RestoreMode,
            CreatedAtUtc: startedAtUtc,
            FinishedAtUtc: DateTimeOffset.UtcNow,
            Operator: NormalizeOptional(request.Operator),
            Note: NormalizeOptional(request.Note),
            ExecutionRequested: request.ExecuteNpmRouteMutation && request.ExecuteCutoverIngressNetworkMutation,
            ProductionExecutionLocked: false,
            NpmRouteExecutionPerformed: anyRouteMutated,
            CutoverIngressNetworkExecutionPerformed: anyDockerNetworkMutated,
            RuntimePromotionLocked: true,
            DnsMutationLocked: true,
            CertificateMutationLocked: true,
            Mutations: mutations,
            CutoverIngress: cutoverIngress,
            Candidate: candidateSummary,
            Routes: routes,
            Acknowledgements: acknowledgements,
            Checks: checks,
            Blockers: distinctBlockers,
            Warnings: distinctWarnings,
            Errors: distinctErrors,
            Detail: BuildDetail(status, anyRouteMutated, anyDockerNetworkMutated));

        await _executionHistory.SaveExecutionAsync(
            result,
            ct);

        _logger.LogWarning(
            "Executed Public Cutover executor v1A. ExecutionId={ExecutionId} ConfirmationId={ConfirmationId} CandidateId={CandidateId} Status={Status} AnyRouteMutated={AnyRouteMutated} DockerNetworksChanged={DockerNetworksChanged}",
            result.ExecutionId,
            result.ConfirmationId,
            result.CandidateId,
            result.Status,
            result.Routes.AnyRouteMutated,
            result.CutoverIngress.DockerNetworksChanged);

        return result;
    }


    private async Task<RuntimeStackBackupPublicCutoverIngressSummary> EnsureCutoverIngressAsync(
        CutoverIngressIntent intent,
        List<RuntimeStackBackupPublicCutoverExecutionCheck> checks,
        List<string> warnings,
        List<string> errors,
        CancellationToken ct)
    {
        var networkExistedBefore = await NetworkExistsAsync(intent.CutoverNetworkName, ct);
        var networkCreated = false;

        if (!networkExistedBefore)
        {
            try
            {
                await _docker.Networks.CreateNetworkAsync(
                    new NetworksCreateParameters
                    {
                        Name = intent.CutoverNetworkName,
                        Driver = "bridge",
                        Internal = false,
                        CheckDuplicate = true,
                        Labels = new Dictionary<string, string>
                        {
                            ["mem.component"] = "production-restore-cutover-ingress",
                            ["mem.production-candidate.id"] = intent.CandidateId,
                            ["mem.restore-staging.network"] = intent.PrivateRestoreNetworkName,
                            ["mem.managed-by"] = "mem-host-agent"
                        }
                    },
                    ct);

                networkCreated = true;
            }
            catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                networkExistedBefore = true;
            }
        }

        var npmAttachedToPrivateBefore = await IsContainerAttachedToNetworkAsync(
            intent.PrivateRestoreNetworkName,
            intent.NpmContainerName,
            ct);

        var npmAttachment = await EnsureNetworkAttachmentAsync(
            intent.CutoverNetworkName,
            intent.NpmContainerName,
            aliases: [intent.NpmContainerName],
            ct);

        var matrixAttachment = await EnsureNetworkAttachmentAsync(
            intent.CutoverNetworkName,
            intent.MatrixContainerName,
            aliases: [intent.MatrixAlias, "cutover-matrix", "matrix"],
            ct);

        var elementAttachment = await EnsureNetworkAttachmentAsync(
            intent.CutoverNetworkName,
            intent.ElementContainerName,
            aliases: [intent.ElementAlias, "cutover-element", "element"],
            ct);

        var postgresAttachedToCutover = !string.IsNullOrWhiteSpace(intent.PostgresContainerName) &&
            await IsContainerAttachedToNetworkAsync(
                intent.CutoverNetworkName,
                intent.PostgresContainerName!,
                ct);

        var npmAttachedToPrivateAfter = await IsContainerAttachedToNetworkAsync(
            intent.PrivateRestoreNetworkName,
            intent.NpmContainerName,
            ct);

        var dockerNetworksChanged = networkCreated ||
            npmAttachment.Changed ||
            matrixAttachment.Changed ||
            elementAttachment.Changed;

        var ready = npmAttachment.AttachedAfter &&
            matrixAttachment.AttachedAfter &&
            elementAttachment.AttachedAfter &&
            !postgresAttachedToCutover;

        AddCheck(
            checks,
            "public-cutover-execution.cutover-ingress.network-ready",
            "docker-network",
            ready ? "info" : "blocker",
            ready,
            ready ? "satisfied" : "blocked",
            ready
                ? "Per-candidate cutover ingress network is ready for public NPM upstream routing."
                : "Per-candidate cutover ingress network is not ready or has an unsafe Postgres attachment.",
            $"network={intent.CutoverNetworkName}; npm={npmAttachment.AttachedAfter}; matrix={matrixAttachment.AttachedAfter}; element={elementAttachment.AttachedAfter}; postgresOnCutover={postgresAttachedToCutover}");

        if (npmAttachedToPrivateAfter)
        {
            warnings.Add($"NPM container '{intent.NpmContainerName}' is still attached to the private restore network '{intent.PrivateRestoreNetworkName}'. v1A does not attach NPM to private restore networks; disconnect this manual/debug attachment after public checks succeed through '{intent.CutoverNetworkName}'.");
        }

        if (postgresAttachedToCutover)
        {
            errors.Add($"Candidate Postgres container '{intent.PostgresContainerName}' is attached to cutover ingress network '{intent.CutoverNetworkName}'. This violates the cutover ingress boundary.");
        }

        var notes = new List<string>
        {
            "Per-candidate cutover ingress network is a temporary public bridge until runtime promotion/cleanup.",
            "NPM, candidate Synapse, and candidate Element may join this network.",
            "Candidate Postgres must remain only on the private restore network.",
            "NPM routes should target stable aliases on this cutover ingress network, not timestamped restore-staging container names."
        };

        if (npmAttachedToPrivateAfter)
        {
            notes.Add("NPM is currently attached to the private restore network from an earlier manual/debug step; remove that attachment after the cutover ingress route path is verified.");
        }

        return new RuntimeStackBackupPublicCutoverIngressSummary(
            NetworkName: intent.CutoverNetworkName,
            NpmContainerName: intent.NpmContainerName,
            MatrixContainerName: intent.MatrixContainerName,
            ElementContainerName: intent.ElementContainerName,
            PostgresContainerName: intent.PostgresContainerName,
            MatrixAlias: intent.MatrixAlias,
            ElementAlias: intent.ElementAlias,
            PrivateRestoreNetworkName: intent.PrivateRestoreNetworkName,
            NetworkExistedBefore: networkExistedBefore,
            NetworkCreated: networkCreated,
            NpmAttachedBefore: npmAttachment.AttachedBefore,
            NpmAttachedAfter: npmAttachment.AttachedAfter,
            NpmAttachmentChanged: npmAttachment.Changed,
            MatrixAttachedBefore: matrixAttachment.AttachedBefore,
            MatrixAttachedAfter: matrixAttachment.AttachedAfter,
            MatrixAttachmentChanged: matrixAttachment.Changed,
            MatrixAliasReady: matrixAttachment.AttachedAfter,
            ElementAttachedBefore: elementAttachment.AttachedBefore,
            ElementAttachedAfter: elementAttachment.AttachedAfter,
            ElementAttachmentChanged: elementAttachment.Changed,
            ElementAliasReady: elementAttachment.AttachedAfter,
            PostgresAttachedToCutoverIngress: postgresAttachedToCutover,
            NpmAttachedToPrivateRestoreNetworkBefore: npmAttachedToPrivateBefore,
            NpmAttachedToPrivateRestoreNetworkAfter: npmAttachedToPrivateAfter,
            DockerNetworksChanged: dockerNetworksChanged,
            Ready: ready,
            Status: ready ? "ready" : "blocked",
            Notes: notes);
    }

    private static RuntimeStackBackupPublicCutoverIngressSummary BuildSkippedCutoverIngress(
        CutoverIngressIntent intent)
    {
        return new RuntimeStackBackupPublicCutoverIngressSummary(
            NetworkName: intent.CutoverNetworkName,
            NpmContainerName: intent.NpmContainerName,
            MatrixContainerName: intent.MatrixContainerName,
            ElementContainerName: intent.ElementContainerName,
            PostgresContainerName: intent.PostgresContainerName,
            MatrixAlias: intent.MatrixAlias,
            ElementAlias: intent.ElementAlias,
            PrivateRestoreNetworkName: intent.PrivateRestoreNetworkName,
            NetworkExistedBefore: false,
            NetworkCreated: false,
            NpmAttachedBefore: false,
            NpmAttachedAfter: false,
            NpmAttachmentChanged: false,
            MatrixAttachedBefore: false,
            MatrixAttachedAfter: false,
            MatrixAttachmentChanged: false,
            MatrixAliasReady: false,
            ElementAttachedBefore: false,
            ElementAttachedAfter: false,
            ElementAttachmentChanged: false,
            ElementAliasReady: false,
            PostgresAttachedToCutoverIngress: false,
            NpmAttachedToPrivateRestoreNetworkBefore: false,
            NpmAttachedToPrivateRestoreNetworkAfter: false,
            DockerNetworksChanged: false,
            Ready: false,
            Status: "skipped",
            Notes:
            [
                "Cutover ingress network execution was skipped because earlier gates blocked execution."
            ]);
    }

    private async Task<NetworkAttachmentState> EnsureNetworkAttachmentAsync(
        string networkName,
        string containerName,
        IReadOnlyList<string> aliases,
        CancellationToken ct)
    {
        var attachedBefore = await IsContainerAttachedToNetworkAsync(
            networkName,
            containerName,
            ct);

        if (!attachedBefore)
        {
            await _docker.Networks.ConnectNetworkAsync(
                networkName,
                new NetworkConnectParameters
                {
                    Container = containerName,
                    EndpointConfig = new EndpointSettings
                    {
                        Aliases = aliases.ToList()
                    }
                },
                ct);
        }

        var attachedAfter = await IsContainerAttachedToNetworkAsync(
            networkName,
            containerName,
            ct);

        return new NetworkAttachmentState(
            AttachedBefore: attachedBefore,
            AttachedAfter: attachedAfter,
            Changed: !attachedBefore && attachedAfter);
    }

    private async Task<bool> NetworkExistsAsync(
        string networkName,
        CancellationToken ct)
    {
        try
        {
            _ = await _docker.Networks.InspectNetworkAsync(
                networkName,
                ct);

            return true;
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task<bool> IsContainerAttachedToNetworkAsync(
        string networkName,
        string containerName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(networkName) || string.IsNullOrWhiteSpace(containerName))
        {
            return false;
        }

        try
        {
            var network = await _docker.Networks.InspectNetworkAsync(
                networkName,
                ct);

            if (network.Containers is null)
            {
                return false;
            }

            return network.Containers.Values.Any(container =>
                string.Equals(container.Name, containerName, StringComparison.OrdinalIgnoreCase));
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private static CutoverIngressIntent BuildCutoverIngressIntent(
        RuntimeStackBackupPublicCutoverPreviewResult preview,
        RuntimeStackBackupProductionCandidateResult candidate)
    {
        var targetStackSlug = Slugify(preview.Candidate.TargetStackSlug);

        return new CutoverIngressIntent(
            CandidateId: candidate.CandidateId,
            CutoverNetworkName: $"mem-cutover-ingress-{candidate.CandidateId}",
            PrivateRestoreNetworkName: candidate.NetworkName,
            NpmContainerName: ResolveNpmContainerName(),
            MatrixContainerName: candidate.SynapseContainerName,
            ElementContainerName: candidate.ElementContainerName ?? throw new InvalidOperationException("Candidate Element container name is required for public cutover ingress."),
            PostgresContainerName: candidate.PostgresContainerName,
            MatrixAlias: $"mem-matrix-{targetStackSlug}",
            ElementAlias: $"mem-element-{targetStackSlug}");
    }

    private static string ResolveNpmContainerName()
    {
        return Environment.GetEnvironmentVariable("MEM_NPM_CONTAINER_NAME")?.Trim() is { Length: > 0 } configured
            ? configured
            : "mem-npm";
    }

    private sealed record CutoverIngressIntent(
        string CandidateId,
        string CutoverNetworkName,
        string PrivateRestoreNetworkName,
        string NpmContainerName,
        string MatrixContainerName,
        string ElementContainerName,
        string? PostgresContainerName,
        string MatrixAlias,
        string ElementAlias);

    private sealed record NetworkAttachmentState(
        bool AttachedBefore,
        bool AttachedAfter,
        bool Changed);

    private async Task<RuntimeStackBackupPublicCutoverExecutionRouteResult> ExecuteRouteAsync(
        string component,
        RuntimeStackBackupPublicCutoverPreviewRouteAction previewRoute,
        RuntimeStackBackupPublicCutoverConfirmationRouteGate confirmationRoute,
        RuntimeStackBackupProductionRestoreNpmRouteSummary? freshRoute,
        string desiredForwardHost,
        RouteKind kind,
        bool required,
        List<RuntimeStackBackupPublicCutoverExecutionCheck> checks,
        List<string> warnings,
        List<string> errors,
        CancellationToken ct)
    {
        var host = NormalizeOptional(previewRoute.Host);
        var forwardHost = NormalizeOptional(desiredForwardHost);
        var forwardPort = previewRoute.UpstreamPort;
        var certificateId = previewRoute.NpmCertificateId;

        if (!required && string.IsNullOrWhiteSpace(host))
        {
            AddCheck(
                checks,
                $"public-cutover-execution.{component}.not-required",
                "npm-route",
                "info",
                true,
                "skipped",
                $"{component} route is not required for this execution.",
                null);

            return new RuntimeStackBackupPublicCutoverExecutionRouteResult(
                Component: component,
                Host: host,
                DesiredForwardHost: forwardHost,
                DesiredForwardPort: forwardPort,
                ForwardScheme: previewRoute.ForwardScheme,
                NpmCertificateId: certificateId,
                PreviewAction: previewRoute.Action,
                PreviewAvailableForFutureExecution: previewRoute.AvailableForFutureExecution,
                FreshRouteCurrentlyExistsBeforeExecution: freshRoute is not null,
                FreshRouteAlreadyTargetsCandidateBeforeExecution: RouteTargetsAnyCandidateUpstream(freshRoute, previewRoute, forwardHost),
                Requested: false,
                Mutated: false,
                Succeeded: true,
                ExecutionAction: "skipped_not_required",
                Status: "skipped",
                NpmProxyHostId: null,
                SslConfigured: null,
                Ready: null,
                Warning: null,
                Error: null,
                Detail: $"{component} route was not required.");
        }

        var validationErrors = ValidateRouteInput(
            component,
            previewRoute,
            confirmationRoute,
            freshRoute,
            forwardHost,
            required);

        if (validationErrors.Count > 0)
        {
            errors.AddRange(validationErrors);

            AddCheck(
                checks,
                $"public-cutover-execution.{component}.route-input",
                "npm-route",
                "blocker",
                false,
                "blocked",
                $"{component} route execution input is not safe.",
                string.Join("; ", validationErrors));

            return new RuntimeStackBackupPublicCutoverExecutionRouteResult(
                Component: component,
                Host: host,
                DesiredForwardHost: forwardHost,
                DesiredForwardPort: forwardPort,
                ForwardScheme: previewRoute.ForwardScheme,
                NpmCertificateId: certificateId,
                PreviewAction: previewRoute.Action,
                PreviewAvailableForFutureExecution: previewRoute.AvailableForFutureExecution,
                FreshRouteCurrentlyExistsBeforeExecution: freshRoute is not null,
                FreshRouteAlreadyTargetsCandidateBeforeExecution: RouteTargetsAnyCandidateUpstream(freshRoute, previewRoute, forwardHost),
                Requested: required,
                Mutated: false,
                Succeeded: false,
                ExecutionAction: "blocked_invalid_route_input",
                Status: "blocked",
                NpmProxyHostId: freshRoute?.ProviderRouteId,
                SslConfigured: freshRoute?.SslConfigured,
                Ready: IsRouteEnabled(freshRoute),
                Warning: null,
                Error: string.Join("; ", validationErrors),
                Detail: $"{component} route execution was blocked before calling NPM.");
        }

        if (RouteTargetsDesiredUpstream(freshRoute, previewRoute, forwardHost))
        {
            AddCheck(
                checks,
                $"public-cutover-execution.{component}.already-targets-candidate",
                "npm-route",
                "info",
                true,
                "satisfied",
                $"{component} route already targets the stable cutover ingress alias.",
                $"{freshRoute!.ForwardHost}:{freshRoute.ForwardPort}");

            return new RuntimeStackBackupPublicCutoverExecutionRouteResult(
                Component: component,
                Host: host,
                DesiredForwardHost: forwardHost,
                DesiredForwardPort: forwardPort,
                ForwardScheme: previewRoute.ForwardScheme,
                NpmCertificateId: certificateId,
                PreviewAction: previewRoute.Action,
                PreviewAvailableForFutureExecution: previewRoute.AvailableForFutureExecution,
                FreshRouteCurrentlyExistsBeforeExecution: true,
                FreshRouteAlreadyTargetsCandidateBeforeExecution: true,
                Requested: required,
                Mutated: false,
                Succeeded: true,
                ExecutionAction: "already_present",
                Status: "satisfied",
                NpmProxyHostId: freshRoute.ProviderRouteId,
                SslConfigured: freshRoute.SslConfigured,
                Ready: IsRouteEnabled(freshRoute),
                Warning: null,
                Error: null,
                Detail: $"{component} route already exists and points at the stable cutover ingress alias.");
        }

        try
        {
            var publishResult = await _routePublisher.EnsureAsync(
                new RoutePublishRequest(
                    Domain: host!,
                    ForwardHost: forwardHost!,
                    ForwardPort: forwardPort!.Value,
                    Kind: kind,
                    ForwardScheme: string.IsNullOrWhiteSpace(previewRoute.ForwardScheme)
                        ? "http"
                        : previewRoute.ForwardScheme,
                    IsPublic: true,
                    RequireSsl: true,
                    CertificateId: certificateId,
                    ForceSsl: true,
                    Http2: true,
                    HstsEnabled: true,
                    HstsSubdomains: true,
                    AllowWebsocketUpgrade: true,
                    BlockExploits: true,
                    CachingEnabled: false,
                    Enabled: true,
                    TrustForwardedProto: false),
                ct);

            AddCheck(
                checks,
                $"public-cutover-execution.{component}.npm-route-ensured",
                "npm-route",
                publishResult.Ready ? "info" : "warning",
                publishResult.Ready,
                publishResult.Ready ? "satisfied" : "warning",
                $"{component} NPM proxy host was ensured.",
                $"routeId={publishResult.RouteId}; {publishResult.Domain} -> {publishResult.ForwardHost}:{publishResult.ForwardPort}; sslConfigured={publishResult.SslConfigured}");

            return new RuntimeStackBackupPublicCutoverExecutionRouteResult(
                Component: component,
                Host: host,
                DesiredForwardHost: forwardHost,
                DesiredForwardPort: forwardPort,
                ForwardScheme: previewRoute.ForwardScheme,
                NpmCertificateId: certificateId,
                PreviewAction: previewRoute.Action,
                PreviewAvailableForFutureExecution: previewRoute.AvailableForFutureExecution,
                FreshRouteCurrentlyExistsBeforeExecution: freshRoute is not null,
                FreshRouteAlreadyTargetsCandidateBeforeExecution: RouteTargetsAnyCandidateUpstream(freshRoute, previewRoute, forwardHost),
                Requested: required,
                Mutated: true,
                Succeeded: publishResult.Ready,
                ExecutionAction: freshRoute is null ? "created" : "updated",
                Status: publishResult.Ready ? "satisfied" : "warning",
                NpmProxyHostId: publishResult.RouteId,
                SslConfigured: publishResult.SslConfigured,
                Ready: publishResult.Ready,
                Warning: publishResult.Warning,
                Error: null,
                Detail: $"{component} NPM proxy host was {(freshRoute is null ? "created" : "updated")} for the stable cutover ingress alias.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AddCheck(
                checks,
                $"public-cutover-execution.{component}.npm-route-failed",
                "npm-route",
                "error",
                false,
                "failed",
                $"{component} NPM proxy host execution failed.",
                ex.Message);

            return new RuntimeStackBackupPublicCutoverExecutionRouteResult(
                Component: component,
                Host: host,
                DesiredForwardHost: forwardHost,
                DesiredForwardPort: forwardPort,
                ForwardScheme: previewRoute.ForwardScheme,
                NpmCertificateId: certificateId,
                PreviewAction: previewRoute.Action,
                PreviewAvailableForFutureExecution: previewRoute.AvailableForFutureExecution,
                FreshRouteCurrentlyExistsBeforeExecution: freshRoute is not null,
                FreshRouteAlreadyTargetsCandidateBeforeExecution: RouteTargetsAnyCandidateUpstream(freshRoute, previewRoute, forwardHost),
                Requested: required,
                Mutated: false,
                Succeeded: false,
                ExecutionAction: "failed",
                Status: "failed",
                NpmProxyHostId: freshRoute?.ProviderRouteId,
                SslConfigured: freshRoute?.SslConfigured,
                Ready: IsRouteEnabled(freshRoute),
                Warning: null,
                Error: ex.Message,
                Detail: $"{component} NPM proxy host was not ensured.");
        }
    }

    private static List<string> ValidateRouteInput(
        string component,
        RuntimeStackBackupPublicCutoverPreviewRouteAction previewRoute,
        RuntimeStackBackupPublicCutoverConfirmationRouteGate confirmationRoute,
        RuntimeStackBackupProductionRestoreNpmRouteSummary? freshRoute,
        string? desiredForwardHost,
        bool required)
    {
        var errors = new List<string>();

        if (!required)
        {
            return errors;
        }

        if (string.IsNullOrWhiteSpace(previewRoute.Host))
        {
            errors.Add($"{component} route host is missing.");
        }

        if (string.IsNullOrWhiteSpace(previewRoute.UpstreamContainerName))
        {
            errors.Add($"{component} route upstream container is missing.");
        }

        if (string.IsNullOrWhiteSpace(desiredForwardHost))
        {
            errors.Add($"{component} route stable cutover ingress alias is missing.");
        }

        if (previewRoute.UpstreamPort is null or <= 0 or > 65535)
        {
            errors.Add($"{component} route upstream port is invalid.");
        }

        if (previewRoute.NpmCertificateId is null or <= 0)
        {
            errors.Add($"{component} route has no imported NPM certificate id.");
        }

        if (!previewRoute.CertificateAvailable ||
            !previewRoute.CertificateImportedToNpm ||
            previewRoute.CertificateIsStaging)
        {
            errors.Add($"{component} certificate is not ready for production public routing.");
        }

        if (!previewRoute.AvailableForFutureExecution)
        {
            errors.Add($"{component} route preview was not available for future execution.");
        }

        if (!confirmationRoute.Passed ||
            !confirmationRoute.PreviewAvailableForFutureExecution ||
            !confirmationRoute.CertificateReady)
        {
            errors.Add($"{component} route did not pass confirmation gates.");
        }

        if (!string.Equals(previewRoute.Host, confirmationRoute.Host, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{component} host differs between preview and confirmation.");
        }

        if (!string.Equals(previewRoute.UpstreamContainerName, confirmationRoute.UpstreamContainerName, StringComparison.OrdinalIgnoreCase) ||
            previewRoute.UpstreamPort != confirmationRoute.UpstreamPort)
        {
            errors.Add($"{component} upstream differs between preview and confirmation.");
        }

        if (freshRoute is not null &&
            !RouteTargetsAnyCandidateUpstream(freshRoute, previewRoute, desiredForwardHost))
        {
            errors.Add($"{component} live NPM route now exists but does not target this private candidate or its stable cutover ingress alias. Resolve route ownership before executing cutover.");
        }

        return errors;
    }

    private static RuntimeStackBackupPublicCutoverExecutionRouteResult BuildBlockedRouteResult(
        string component,
        RuntimeStackBackupPublicCutoverPreviewRouteAction previewRoute,
        RuntimeStackBackupPublicCutoverConfirmationRouteGate confirmationRoute,
        RuntimeStackBackupProductionRestoreNpmRouteSummary? freshRoute,
        string detail)
    {
        return new RuntimeStackBackupPublicCutoverExecutionRouteResult(
            Component: component,
            Host: previewRoute.Host,
            DesiredForwardHost: previewRoute.UpstreamContainerName,
            DesiredForwardPort: previewRoute.UpstreamPort,
            ForwardScheme: previewRoute.ForwardScheme,
            NpmCertificateId: previewRoute.NpmCertificateId,
            PreviewAction: previewRoute.Action,
            PreviewAvailableForFutureExecution: previewRoute.AvailableForFutureExecution,
            FreshRouteCurrentlyExistsBeforeExecution: freshRoute is not null,
            FreshRouteAlreadyTargetsCandidateBeforeExecution: RouteTargetsAnyCandidateUpstream(freshRoute, previewRoute, previewRoute.UpstreamContainerName),
            Requested: confirmationRoute.Passed,
            Mutated: false,
            Succeeded: false,
            ExecutionAction: "blocked",
            Status: "blocked",
            NpmProxyHostId: freshRoute?.ProviderRouteId,
            SslConfigured: freshRoute?.SslConfigured,
            Ready: IsRouteEnabled(freshRoute),
            Warning: null,
            Error: detail,
            Detail: detail);
    }

    private static void ValidateConfirmation(
        RuntimeStackBackupPublicCutoverConfirmationResult confirmation,
        RuntimeStackBackupPublicCutoverPreviewResult preview,
        RuntimeStackBackupProductionCandidateResult candidate,
        List<RuntimeStackBackupPublicCutoverExecutionCheck> checks,
        List<string> blockers,
        List<string> warnings)
    {
        var confirmationPassed =
            string.Equals(confirmation.Status, "confirmed_ready_for_future_executor", StringComparison.OrdinalIgnoreCase) &&
            confirmation.Blockers.Count == 0 &&
            confirmation.Errors.Count == 0 &&
            confirmation.Candidate.CandidateFound &&
            confirmation.Candidate.PrivateOnly &&
            !confirmation.Candidate.Destroyed &&
            confirmation.Candidate.DatabaseImportSucceeded &&
            confirmation.Candidate.SynapseHealthPassed &&
            confirmation.Candidate.ElementHealthPassed &&
            confirmation.Routes.Matrix.Passed &&
            confirmation.Routes.Element.Passed &&
            confirmation.Routes.AllRequiredRoutesEligible &&
            confirmation.Routes.LiveNpmStateMatchesPreview &&
            confirmation.Acknowledgements.Where(ack => ack.Required).All(ack => ack.Acknowledged);

        if (!confirmationPassed)
        {
            blockers.Add("Public Cutover confirmation has not passed all gates for future executor use.");
        }

        AddCheck(
            checks,
            "public-cutover-execution.confirmation.passed",
            "confirmation",
            confirmationPassed ? "info" : "blocker",
            confirmationPassed,
            confirmationPassed ? "satisfied" : "blocked",
            confirmationPassed
                ? "Public Cutover confirmation passed all gates."
                : "Public Cutover confirmation is not safe to execute from.",
            confirmation.ConfirmationId);

        var previewReady =
            string.Equals(preview.Status, "ready_for_review", StringComparison.OrdinalIgnoreCase) &&
            preview.Blockers.Count == 0 &&
            preview.Errors.Count == 0 &&
            preview.Candidate.ReadyForMatrixCutoverPreview &&
            preview.Candidate.ReadyForElementCutoverPreview &&
            preview.Routes.Matrix.AvailableForFutureExecution &&
            preview.Routes.Element.AvailableForFutureExecution;

        if (!previewReady)
        {
            blockers.Add("Public Cutover preview is no longer ready for execution.");
        }

        AddCheck(
            checks,
            "public-cutover-execution.preview.ready",
            "preview",
            previewReady ? "info" : "blocker",
            previewReady,
            previewReady ? "satisfied" : "blocked",
            previewReady
                ? "Source preview is still ready for public route execution."
                : "Source preview is not ready for public route execution.",
            preview.PreviewId);

        var candidateReady =
            string.Equals(candidate.Status, "private_candidate_ready", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.PrivateRuntimeStatus, "ready", StringComparison.OrdinalIgnoreCase) &&
            candidate.Safety.PrivateOnly &&
            candidate.Destroy?.PrivateRuntimeDestroyed != true &&
            candidate.Database.ImportSucceeded &&
            candidate.Runtime.SynapseHealthPassed &&
            candidate.Runtime.ElementHealthPassed &&
            !string.IsNullOrWhiteSpace(candidate.SynapseContainerName) &&
            !string.IsNullOrWhiteSpace(candidate.ElementContainerName);

        if (!candidateReady)
        {
            blockers.Add("Private Production Restore candidate is no longer ready for public route execution.");
        }

        AddCheck(
            checks,
            "public-cutover-execution.candidate.ready",
            "production-candidate",
            candidateReady ? "info" : "blocker",
            candidateReady,
            candidateReady ? "satisfied" : "blocked",
            candidateReady
                ? "Private Production Restore candidate is still ready and private-only."
                : "Private Production Restore candidate is not ready or is no longer private-only.",
            candidate.CandidateId);

        warnings.AddRange(candidate.Warnings.Select(warning => $"Candidate warning: {warning}"));
    }

    private static void ValidateFreshPlan(
        RuntimeStackBackupProductionRestorePlanResult freshPlan,
        RuntimeStackBackupPublicCutoverConfirmationResult confirmation,
        List<RuntimeStackBackupPublicCutoverExecutionCheck> checks,
        List<string> blockers)
    {
        var freshPlanReady =
            string.Equals(freshPlan.Status, "ready_for_review", StringComparison.OrdinalIgnoreCase) &&
            freshPlan.Blockers.Count == 0 &&
            freshPlan.Errors.Count == 0;

        if (!freshPlanReady)
        {
            blockers.Add("Fresh pre-execution Production Restore plan is not ready for review.");
        }

        AddCheck(
            checks,
            "public-cutover-execution.fresh-plan.ready",
            "production-plan",
            freshPlanReady ? "info" : "blocker",
            freshPlanReady,
            freshPlanReady ? "satisfied" : "blocked",
            freshPlanReady
                ? "Fresh pre-execution Production Restore plan is ready for review."
                : "Fresh pre-execution Production Restore plan has blockers or errors.",
            freshPlan.PlanId);

        if (!string.Equals(freshPlan.ValidationId, confirmation.ValidationId, StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("Fresh plan validation id does not match confirmation validation id.");
        }

        if (!string.Equals(freshPlan.RestoreMode, confirmation.RestoreMode, StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("Fresh plan restore mode does not match confirmation restore mode.");
        }
    }

    private static RuntimeStackBackupPublicCutoverExecutionCandidateSummary BuildCandidateSummary(
        RuntimeStackBackupProductionCandidateResult candidate)
    {
        return new RuntimeStackBackupPublicCutoverExecutionCandidateSummary(
            CandidateFound: true,
            CandidateId: candidate.CandidateId,
            Status: candidate.Status,
            PrivateRuntimeStatus: candidate.PrivateRuntimeStatus,
            PrivateOnly: candidate.Safety.PrivateOnly,
            Destroyed: candidate.Destroy?.PrivateRuntimeDestroyed == true,
            DatabaseImportSucceeded: candidate.Database.ImportSucceeded,
            SynapseHealthPassed: candidate.Runtime.SynapseHealthPassed,
            ElementHealthPassed: candidate.Runtime.ElementHealthPassed,
            MatrixServerName: candidate.MatrixServerName,
            SynapseContainerName: candidate.SynapseContainerName,
            ElementContainerName: candidate.ElementContainerName,
            Detail: candidate.Detail);
    }

    private static IReadOnlyList<RuntimeStackBackupPublicCutoverExecutionAcknowledgement> BuildAcknowledgements(
        RuntimeStackBackupPublicCutoverExecutionRequest request)
    {
        return
        [
            new RuntimeStackBackupPublicCutoverExecutionAcknowledgement(
                Code: "execute-npm-route-mutation",
                Label: "Execute NPM route mutation",
                Description: "Operator explicitly requests Public Cutover Executor v1A to create or ensure public NPM proxy hosts.",
                Severity: "danger",
                Required: true,
                Acknowledged: request.ExecuteNpmRouteMutation,
                Status: request.ExecuteNpmRouteMutation ? "acknowledged" : "missing"),

            new RuntimeStackBackupPublicCutoverExecutionAcknowledgement(
                Code: "execute-cutover-ingress-network-mutation",
                Label: "Execute cutover ingress network mutation",
                Description: "Operator explicitly requests creation/attachment of the per-candidate public cutover ingress Docker network.",
                Severity: "danger",
                Required: true,
                Acknowledged: request.ExecuteCutoverIngressNetworkMutation,
                Status: request.ExecuteCutoverIngressNetworkMutation ? "acknowledged" : "missing"),

            new RuntimeStackBackupPublicCutoverExecutionAcknowledgement(
                Code: "docker-network-mutation",
                Label: "Docker network mutation",
                Description: "Operator understands this executor may create a cutover ingress Docker network and attach NPM, candidate Synapse, and candidate Element to it.",
                Severity: "danger",
                Required: true,
                Acknowledged: request.AcknowledgeDockerNetworkMutation,
                Status: request.AcknowledgeDockerNetworkMutation ? "acknowledged" : "missing"),

            new RuntimeStackBackupPublicCutoverExecutionAcknowledgement(
                Code: "npm-must-not-join-private-restore-network",
                Label: "NPM must not join private restore network",
                Description: "Operator understands NPM should reach the candidate through the cutover ingress network, not through the private restore/database network.",
                Severity: "danger",
                Required: true,
                Acknowledged: request.AcknowledgeNpmMustNotJoinPrivateRestoreNetwork,
                Status: request.AcknowledgeNpmMustNotJoinPrivateRestoreNetwork ? "acknowledged" : "missing"),

            new RuntimeStackBackupPublicCutoverExecutionAcknowledgement(
                Code: "confirmation-reviewed",
                Label: "Confirmation reviewed",
                Description: "Operator reviewed the passed Public Cutover confirmation before executing.",
                Severity: "danger",
                Required: true,
                Acknowledged: request.AcknowledgeConfirmationReviewed,
                Status: request.AcknowledgeConfirmationReviewed ? "acknowledged" : "missing"),

            new RuntimeStackBackupPublicCutoverExecutionAcknowledgement(
                Code: "creates-public-routes",
                Label: "Creates public routes",
                Description: "Operator understands this endpoint can create public Matrix and Element routes.",
                Severity: "danger",
                Required: true,
                Acknowledged: request.AcknowledgeCreatesPublicRoutes,
                Status: request.AcknowledgeCreatesPublicRoutes ? "acknowledged" : "missing"),

            new RuntimeStackBackupPublicCutoverExecutionAcknowledgement(
                Code: "npm-routes-will-change",
                Label: "NPM routes will change",
                Description: "Operator understands Nginx Proxy Manager may be mutated by this executor.",
                Severity: "danger",
                Required: true,
                Acknowledged: request.AcknowledgeNpmRoutesWillChange,
                Status: request.AcknowledgeNpmRoutesWillChange ? "acknowledged" : "missing"),

            new RuntimeStackBackupPublicCutoverExecutionAcknowledgement(
                Code: "matrix-federation-exposure-may-change",
                Label: "Matrix exposure may change",
                Description: "Operator understands creating the Matrix route may expose the restored homeserver publicly and may affect federation visibility.",
                Severity: "danger",
                Required: true,
                Acknowledged: request.AcknowledgeMatrixFederationExposureMayChange,
                Status: request.AcknowledgeMatrixFederationExposureMayChange ? "acknowledged" : "missing"),

            new RuntimeStackBackupPublicCutoverExecutionAcknowledgement(
                Code: "no-dns-mutation",
                Label: "No DNS mutation",
                Description: "Operator understands this executor will not create, update, or delete DNS records.",
                Severity: "warning",
                Required: true,
                Acknowledged: request.AcknowledgeNoDnsMutation,
                Status: request.AcknowledgeNoDnsMutation ? "acknowledged" : "missing"),

            new RuntimeStackBackupPublicCutoverExecutionAcknowledgement(
                Code: "no-certificate-mutation",
                Label: "No certificate mutation",
                Description: "Operator understands this executor will not request, import, renew, attach outside route creation, or remove certificates.",
                Severity: "warning",
                Required: true,
                Acknowledged: request.AcknowledgeNoCertificateMutation,
                Status: request.AcknowledgeNoCertificateMutation ? "acknowledged" : "missing"),

            new RuntimeStackBackupPublicCutoverExecutionAcknowledgement(
                Code: "no-runtime-promotion",
                Label: "No runtime promotion",
                Description: "Operator understands this executor does not register or promote the candidate as a production MEM runtime stack.",
                Severity: "warning",
                Required: true,
                Acknowledged: request.AcknowledgeNoRuntimePromotion,
                Status: request.AcknowledgeNoRuntimePromotion ? "acknowledged" : "missing"),

            new RuntimeStackBackupPublicCutoverExecutionAcknowledgement(
                Code: "no-automatic-rollback",
                Label: "No automatic rollback",
                Description: "Operator understands this executor does not provide automatic rollback if a route is created and later proves unhealthy.",
                Severity: "danger",
                Required: true,
                Acknowledged: request.AcknowledgeNoAutomaticRollback,
                Status: request.AcknowledgeNoAutomaticRollback ? "acknowledged" : "missing"),

            new RuntimeStackBackupPublicCutoverExecutionAcknowledgement(
                Code: "post-cutover-verification-required",
                Label: "Post-cutover verification required",
                Description: "Operator understands public Matrix and Element health must be verified immediately after route creation.",
                Severity: "danger",
                Required: true,
                Acknowledged: request.AcknowledgePostCutoverVerificationRequired,
                Status: request.AcknowledgePostCutoverVerificationRequired ? "acknowledged" : "missing")
        ];
    }

    private static bool RouteTargetsDesiredUpstream(
        RuntimeStackBackupProductionRestoreNpmRouteSummary? freshRoute,
        RuntimeStackBackupPublicCutoverPreviewRouteAction previewRoute,
        string? desiredForwardHost)
    {
        return RouteTargetsHost(
            freshRoute,
            desiredForwardHost,
            previewRoute.UpstreamPort,
            previewRoute.NpmCertificateId);
    }

    private static bool RouteTargetsAnyCandidateUpstream(
        RuntimeStackBackupProductionRestoreNpmRouteSummary? freshRoute,
        RuntimeStackBackupPublicCutoverPreviewRouteAction previewRoute,
        string? desiredForwardHost)
    {
        return RouteTargetsHost(
                freshRoute,
                previewRoute.UpstreamContainerName,
                previewRoute.UpstreamPort,
                previewRoute.NpmCertificateId) ||
            RouteTargetsHost(
                freshRoute,
                desiredForwardHost,
                previewRoute.UpstreamPort,
                previewRoute.NpmCertificateId);
    }

    private static bool RouteTargetsHost(
        RuntimeStackBackupProductionRestoreNpmRouteSummary? freshRoute,
        string? forwardHost,
        int? forwardPort,
        int? npmCertificateId)
    {
        return freshRoute is not null &&
            !string.IsNullOrWhiteSpace(forwardHost) &&
            string.Equals(NormalizeHost(freshRoute.ForwardHost), NormalizeHost(forwardHost), StringComparison.OrdinalIgnoreCase) &&
            freshRoute.ForwardPort == forwardPort &&
            (!npmCertificateId.HasValue || freshRoute.NpmCertificateId == npmCertificateId);
    }

    private static bool? IsRouteEnabled(
        RuntimeStackBackupProductionRestoreNpmRouteSummary? freshRoute)
    {
        if (freshRoute is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(freshRoute.Status))
        {
            return null;
        }

        if (bool.TryParse(freshRoute.Status, out var parsed))
        {
            return parsed;
        }

        return freshRoute.Status.StartsWith("enabled", StringComparison.OrdinalIgnoreCase) ||
               freshRoute.Status.StartsWith("active", StringComparison.OrdinalIgnoreCase);
    }

    private static string DetermineStatus(
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> errors,
        RuntimeStackBackupPublicCutoverExecutionRouteSummary routes,
        RuntimeStackBackupPublicCutoverIngressSummary cutoverIngress)
    {
        if (blockers.Count > 0)
        {
            return "blocked";
        }

        if (errors.Count > 0 && (routes.AnyRouteMutated || cutoverIngress.DockerNetworksChanged))
        {
            return "partial_failure";
        }

        if (errors.Count > 0)
        {
            return "failed";
        }

        if (!cutoverIngress.Ready)
        {
            return "failed";
        }

        if (!routes.AllRequestedRoutesSucceeded)
        {
            return routes.AnyRouteMutated || cutoverIngress.DockerNetworksChanged ? "partial_failure" : "failed";
        }

        if (routes.AnyRouteMutated || cutoverIngress.DockerNetworksChanged)
        {
            return "executed_cutover_ingress_and_npm_routes";
        }

        return "no_op_already_applied";
    }

    private static IReadOnlyList<string> BuildMutationNotes(
        bool anyRouteMutated,
        bool matrixRouteMutated,
        bool anyDockerNetworkMutated,
        RuntimeStackBackupPublicCutoverIngressSummary cutoverIngress)
    {
        var notes = new List<string>
        {
            "Public Cutover Executor v1A creates/ensures a per-candidate cutover ingress network before NPM route execution.",
            "No production containers were stopped, deleted, replaced, or modified.",
            "No production databases were created, dropped, overwritten, or modified.",
            "No DNS records were created, updated, or deleted.",
            "No certificates were requested, imported, renewed, or removed.",
            "No MEM runtime stack promotion or registration was performed."
        };

        if (anyDockerNetworkMutated)
        {
            notes.Add($"Docker network state changed for cutover ingress '{cutoverIngress.NetworkName}'.");
            notes.Add("NPM, candidate Synapse, and candidate Element may have been attached to the cutover ingress network.");
            notes.Add("Candidate Postgres was not intentionally attached to the cutover ingress network.");
        }
        else
        {
            notes.Add("No Docker network mutation was required because cutover ingress attachments were already present or execution was blocked.");
        }

        if (cutoverIngress.NpmAttachedToPrivateRestoreNetworkAfter)
        {
            notes.Add($"NPM is still attached to private restore network '{cutoverIngress.PrivateRestoreNetworkName}' from an earlier manual/debug step. Disconnect it after the cutover ingress route path is verified.");
        }

        if (anyRouteMutated)
        {
            notes.Add("One or more NPM proxy hosts were created or updated for public routing.");
            notes.Add("Public route exposure changed; immediate public health verification is required.");
        }
        else
        {
            notes.Add("No NPM proxy host mutation was required because routes already matched the stable cutover aliases or execution was blocked.");
        }

        if (matrixRouteMutated)
        {
            notes.Add("Matrix public exposure changed; treat federation visibility as changed until verified.");
        }

        return notes;
    }

    private static string BuildDetail(
        string status,
        bool anyRouteMutated,
        bool anyDockerNetworkMutated)
    {
        return status switch
        {
            "executed_cutover_ingress_and_npm_routes" => anyRouteMutated || anyDockerNetworkMutated
                ? "Public Cutover Executor v1A ensured the per-candidate cutover ingress network and NPM public routes. DNS, certificates, runtime promotion, production containers, and databases were not otherwise mutated. Run post-cutover public verification immediately."
                : "Public Cutover Executor v1A completed without requiring network or route mutation.",
            "no_op_already_applied" => "Public Cutover ingress and routes were already present and targeting stable aliases. No NPM or Docker network mutation was performed.",
            "partial_failure" => "Public Cutover Executor v1A partially failed. Some Docker network or public route mutation may have occurred. Inspect cutover ingress and route results immediately.",
            "failed" => "Public Cutover Executor v1A failed before completing cutover ingress and public route execution.",
            "blocked" => "Public Cutover Executor v1A was blocked before Docker network or public route mutation.",
            _ => "Public Cutover Executor v1A completed. Inspect cutover ingress and route results."
        };
    }

    private static void AddCheck(
        List<RuntimeStackBackupPublicCutoverExecutionCheck> checks,
        string code,
        string category,
        string severity,
        bool passed,
        string status,
        string message,
        string? detail)
    {
        checks.Add(new RuntimeStackBackupPublicCutoverExecutionCheck(
            Code: code,
            Category: category,
            Severity: severity,
            Status: status,
            Passed: passed,
            Message: message,
            Detail: detail));
    }

    private static string CreateExecutionId()
    {
        return $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}Z-{Guid.NewGuid():N}"[..27];
    }

    private static string? NormalizeOptional(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string? NormalizeHost(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().TrimEnd('.').ToLowerInvariant();
    }

    private static string Slugify(
        string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        var slug = new string(chars);

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }

    private static void ValidatePathSegment(
        string value,
        string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }

        if (value.Contains('/', StringComparison.Ordinal) ||
            value.Contains('\\', StringComparison.Ordinal) ||
            value.Contains(':', StringComparison.Ordinal) ||
            !value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'))
        {
            throw new InvalidOperationException($"{label} contains unsafe characters.");
        }
    }
}
