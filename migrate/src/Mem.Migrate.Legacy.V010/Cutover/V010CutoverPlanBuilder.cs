using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Target;

namespace Mem.Migrate.Legacy.V010.Cutover;

public static class V010CutoverPlanBuilder
{
    public static CutoverPlanDocument Build(
        AssessmentResult assessment,
        TargetPrivateStageReport stage,
        CutoverPrepareOptions options,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(options);

        ValidateAssessment(assessment, options);
        ValidateStage(stage, options.StageAttemptId);

        var database = assessment.Database.Candidates
            .Single(candidate => candidate.ExactSupportedSchema);
        var sourceContainers = new List<CutoverSourceContainer>();
        var usedContainerIds = new HashSet<string>(StringComparer.Ordinal);

        var postgresContainer = assessment.Docker.Containers.SingleOrDefault(
            container => ContainerIdsMatch(container.Id, database.ContainerId))
            ?? throw new InvalidOperationException(
                "The exact legacy PostgreSQL container could not be matched to the fresh Docker inventory.");

        var legacyApi = ResolveLegacyControlPlaneContainer(
            assessment.Docker.Containers,
            database.PlatformRoutes.Select(route => route.ApiForwardHost),
            postgresContainer.ComposeProject,
            "legacy API",
            options.DevelopmentExternalControlPlane);
        if (legacyApi is not null)
        {
            AddSourceContainer(
                sourceContainers,
                usedContainerIds,
                CreateSourceContainer("legacy-api", legacyApi, null, null));
        }

        var legacyWeb = ResolveLegacyControlPlaneContainer(
            assessment.Docker.Containers,
            database.PlatformRoutes.Select(route => route.WebForwardHost),
            postgresContainer.ComposeProject,
            "legacy Web",
            options.DevelopmentExternalControlPlane);
        if (legacyWeb is not null)
        {
            AddSourceContainer(
                sourceContainers,
                usedContainerIds,
                CreateSourceContainer("legacy-web", legacyWeb, null, null));
        }

        var stackPlans = new List<CutoverStack>();
        foreach (var stack in database.Stacks.OrderBy(item => item.Slug, StringComparer.Ordinal))
        {
            var matrixService = RequireService(
                database.Services,
                stack.Id,
                V010Constants.MatrixServiceKey);
            var elementService = RequireService(
                database.Services,
                stack.Id,
                V010Constants.ElementServiceKey);
            var matrixContainer = RequireServiceContainer(
                assessment.Docker.Containers,
                matrixService);
            var elementContainer = RequireServiceContainer(
                assessment.Docker.Containers,
                elementService);

            AddSourceContainer(
                sourceContainers,
                usedContainerIds,
                CreateSourceContainer(
                    "matrix",
                    matrixContainer,
                    stack.Id,
                    matrixService.Id));
            AddSourceContainer(
                sourceContainers,
                usedContainerIds,
                CreateSourceContainer(
                    "element",
                    elementContainer,
                    stack.Id,
                    elementService.Id));

            var serverName = string.IsNullOrWhiteSpace(matrixService.ServerName)
                ? assessment.FileSystem.Stacks
                    .Single(item => item.StackId == stack.Id)
                    .ParsedConfiguration.ServerName
                : matrixService.ServerName;
            if (string.IsNullOrWhiteSpace(serverName))
            {
                throw new InvalidOperationException(
                    $"Stack '{stack.Slug}' has no verified Matrix server_name.");
            }

            stackPlans.Add(
                new CutoverStack(
                    stack.Id,
                    stack.Slug,
                    stack.Name,
                    serverName,
                    matrixService.Id,
                    matrixContainer.Id,
                    elementService.Id,
                    elementContainer.Id,
                    matrixService.MatrixPublicHost ?? matrixService.PublicDomain,
                    elementService.ElementPublicHost ?? elementService.PublicDomain));
        }

        if (usedContainerIds.Contains(postgresContainer.Id))
        {
            throw new InvalidOperationException(
                "The legacy PostgreSQL container unexpectedly overlaps a source freeze resource.");
        }

        var retainedContainers = new[]
        {
            new CutoverRetainedContainer(
                "legacy-postgres",
                postgresContainer.Id,
                postgresContainer.Name,
                postgresContainer.Image,
                postgresContainer.ImageId,
                postgresContainer.State,
                postgresContainer.RestartPolicy,
                "retain-unchanged-through-acceptance-and-retention")
        };

        var routeHints = BuildRouteHints(database);
        var targetEvidence = new CutoverTargetEvidence(
            stage.StageAttemptId,
            stage.ImportAttemptId,
            stage.ProfileName,
            stage.TargetBaseUrl,
            stage.CatalogEntryId,
            stage.RestoreSessionId,
            stage.StagingId,
            stage.CompletedAtUtc,
            stage.PrivateOnly,
            stage.DatabaseImportSucceeded,
            stage.SynapseHealthPassed,
            stage.PublishedRoutesAbsent,
            stage.DestroySucceeded);

        return new CutoverPlanDocument(
            "mem-cutover-plan",
            2,
            options.PlanId!,
            options.ProducerVersion,
            generatedAtUtc,
            generatedAtUtc.AddMinutes(options.ValidForMinutes),
            assessment.AssessmentId,
            assessment.SourceFingerprint,
            assessment.Classification,
            SourceMutationOccurred: false,
            PublicRoutingMutationOccurred: false,
            sourceContainers
                .OrderBy(container => FreezeOrder(container.Role))
                .ThenBy(container => container.StackId)
                .ThenBy(container => container.ContainerName, StringComparer.Ordinal)
                .ToArray(),
            retainedContainers,
            stackPlans.ToArray(),
            routeHints,
            targetEvidence,
            BuildPreconditions(options),
            BuildExecutionSteps(options.DevelopmentExternalControlPlane),
            BuildRollbackSteps(options.DevelopmentExternalControlPlane),
            BuildWarnings(options.DevelopmentExternalControlPlane),
            BuildNextSteps(options),
            options.DevelopmentExternalControlPlane);
    }

    private static void ValidateAssessment(
        AssessmentResult assessment,
        CutoverPrepareOptions options)
    {
        if (assessment.Classification is not AssessmentClassification.ConfirmedSupportedV010 ||
            !assessment.CanProceedToCapture)
        {
            throw new InvalidOperationException(
                "Cutover planning requires a fresh ConfirmedSupportedV010 assessment with capture allowed.");
        }

        var blocker = assessment.Findings.FirstOrDefault(
            finding => finding.Severity is FindingSeverity.Blocker);
        if (blocker is not null)
        {
            throw new InvalidOperationException(
                $"Fresh source assessment contains blocker '{blocker.Code}'.");
        }

        if (!string.Equals(
                assessment.SourceFingerprint,
                options.ExpectedSourceFingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The fresh source fingerprint does not match the operator-reviewed rehearsal fingerprint. " +
                "Create a new rehearsal and reviewed plan rather than bypassing source drift.");
        }

        if (assessment.Database.Candidates.Count(candidate => candidate.ExactSupportedSchema) != 1)
        {
            throw new InvalidOperationException(
                "Cutover planning requires exactly one supported legacy application database.");
        }

        if (options.DevelopmentExternalControlPlane && assessment.SystemConfig.Reachable)
        {
            throw new InvalidOperationException(
                "The development-only external legacy control plane is still reachable. " +
                "Stop the VS Code/host API and prepare again without --api-url.");
        }
    }

    private static void ValidateStage(
        TargetPrivateStageReport stage,
        string expectedStageAttemptId)
    {
        if (!string.Equals(stage.StageAttemptId, expectedStageAttemptId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The MM-05D private-stage evidence does not match the requested stage attempt.");
        }

        if (!string.Equals(stage.Status, "Completed", StringComparison.Ordinal) ||
            !stage.PrivateOnly ||
            !stage.DatabaseImportSucceeded ||
            !stage.SynapseHealthPassed ||
            !stage.PublishedRoutesAbsent)
        {
            throw new InvalidOperationException(
                "MM-05D private-stage evidence is incomplete or failed a required private verification gate.");
        }

        if (!stage.DestroyRequested || !stage.DestroySucceeded)
        {
            throw new InvalidOperationException(
                "The MM-05D private staging runtime must be destroyed before a cutover plan is prepared.");
        }
    }

    private static LegacyServiceRecord RequireService(
        IEnumerable<LegacyServiceRecord> services,
        Guid stackId,
        string serviceKey)
    {
        var matches = services.Where(service =>
                service.StackId == stackId &&
                string.Equals(
                    service.ServiceKey,
                    serviceKey,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"Stack '{stackId:D}' requires exactly one '{serviceKey}' legacy service record; found {matches.Length}.");
        }

        return matches[0];
    }

    private static DockerContainerObservation RequireServiceContainer(
        IEnumerable<DockerContainerObservation> containers,
        LegacyServiceRecord service)
    {
        var matches = containers.Where(container =>
                (!string.IsNullOrWhiteSpace(service.DockerContainerId) &&
                 ContainerIdsMatch(container.Id, service.DockerContainerId!)) ||
                HasServiceIdentityLabel(container, service.Id))
            .DistinctBy(container => container.Id, StringComparer.Ordinal)
            .ToArray();

        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"Legacy service '{service.Id:D}' ({service.ServiceKey}) requires one exact Docker container; found {matches.Length}.");
        }

        return matches[0];
    }

    private static bool HasServiceIdentityLabel(
        DockerContainerObservation container,
        Guid serviceId)
    {
        var expected = serviceId.ToString("D");
        return LabelEquals(container, "mem.instanceId", expected) ||
               LabelEquals(container, "io.matrixeasymode.service-id", expected) ||
               LabelEquals(container, "io.matrixeasymode.instance-id", expected);
    }

    private static bool LabelEquals(
        DockerContainerObservation container,
        string key,
        string expected) =>
        container.ManagedLabels.TryGetValue(key, out var value) &&
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static DockerContainerObservation? ResolveLegacyControlPlaneContainer(
        IEnumerable<DockerContainerObservation> containers,
        IEnumerable<string?> forwardHosts,
        string? sourceComposeProject,
        string role,
        bool allowDevelopmentExternalControlPlane)
    {
        if (allowDevelopmentExternalControlPlane)
        {
            return null;
        }

        var expectedHosts = forwardHosts
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Select(host => host!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (expectedHosts.Length == 0)
        {
            throw new InvalidOperationException(
                $"Cutover planning has no legacy route forward-host evidence for {role}.");
        }

        var matches = containers
            .Where(container =>
                MatchesExpectedForwardHost(container, expectedHosts) &&
                (string.IsNullOrWhiteSpace(sourceComposeProject) ||
                 string.Equals(
                     container.ComposeProject,
                     sourceComposeProject,
                     StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(container => container.Id, StringComparer.Ordinal)
            .ToArray();

        if (matches.Length == 1)
        {
            return matches[0];
        }

        throw new InvalidOperationException(
            $"Cutover planning requires exactly one source-owned {role} Docker container matched by route forward host and legacy Compose project; found {matches.Length}. " +
            "Image-name similarity is not accepted as source ownership evidence.");
    }

    private static bool MatchesExpectedForwardHost(
        DockerContainerObservation container,
        IReadOnlyCollection<string> expectedHosts) =>
        expectedHosts.Contains(container.Name, StringComparer.OrdinalIgnoreCase) ||
        container.Networks.Any(network =>
            network.Aliases.Any(alias =>
                expectedHosts.Contains(alias, StringComparer.OrdinalIgnoreCase)));

    private static CutoverSourceContainer CreateSourceContainer(
        string role,
        DockerContainerObservation container,
        Guid? stackId,
        Guid? serviceId) =>
        new(
            role,
            container.Id,
            container.Name,
            container.Image,
            container.ImageId,
            container.State,
            container.Health,
            container.RestartPolicy,
            string.Equals(container.State, "running", StringComparison.OrdinalIgnoreCase),
            stackId,
            serviceId);

    private static void AddSourceContainer(
        ICollection<CutoverSourceContainer> resources,
        ISet<string> usedContainerIds,
        CutoverSourceContainer resource)
    {
        if (!usedContainerIds.Add(resource.ContainerId))
        {
            throw new InvalidOperationException(
                $"Docker container '{resource.ContainerName}' was assigned to more than one cutover role.");
        }

        resources.Add(resource);
    }

    private static CutoverRouteHint[] BuildRouteHints(
        LegacyDatabaseCandidateObservation database)
    {
        var hints = new List<CutoverRouteHint>();
        foreach (var platform in database.PlatformRoutes.OrderBy(route => route.Key, StringComparer.Ordinal))
        {
            hints.Add(
                new CutoverRouteHint(
                    "legacy-web",
                    null,
                    platform.WebRouteId,
                    platform.WebDomain,
                    platform.WebForwardHost,
                    platform.WebForwardPort,
                    "legacy-database-hint-requires-live-compare-and-swap-snapshot"));
            hints.Add(
                new CutoverRouteHint(
                    "legacy-api",
                    null,
                    platform.ApiRouteId,
                    platform.ApiDomain,
                    platform.ApiForwardHost,
                    platform.ApiForwardPort,
                    "legacy-database-hint-requires-live-compare-and-swap-snapshot"));
        }

        foreach (var service in database.Services
                     .Where(service =>
                         string.Equals(service.ServiceKey, V010Constants.MatrixServiceKey, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(service.ServiceKey, V010Constants.ElementServiceKey, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(service => service.StackId)
                     .ThenBy(service => service.ServiceKey, StringComparer.Ordinal))
        {
            hints.Add(
                new CutoverRouteHint(
                    string.Equals(service.ServiceKey, V010Constants.MatrixServiceKey, StringComparison.OrdinalIgnoreCase)
                        ? "matrix"
                        : "element",
                    service.StackId,
                    service.PublicRouteId,
                    service.MatrixPublicHost ?? service.ElementPublicHost ?? service.PublicDomain,
                    service.ForwardHost,
                    service.ForwardPort,
                    "legacy-database-hint-requires-live-compare-and-swap-snapshot"));
        }

        return hints
            .Where(hint =>
                !string.IsNullOrWhiteSpace(hint.RouteId) ||
                !string.IsNullOrWhiteSpace(hint.PublicHost))
            .ToArray();
    }

    private static string[] BuildPreconditions(CutoverPrepareOptions options)
    {
        var items = new List<string>
        {
            "The generated plan hash must be reviewed and supplied unchanged to the future freeze command.",
            $"The source fingerprint must remain exactly {options.ExpectedSourceFingerprint}.",
            "The plan must be used before its validity window expires.",
            "A new live route snapshot and compare-and-swap precondition must be captured before public routing changes.",
            "The target must pass a fresh private verification after the final capture and conversion.",
            "No legacy resource may be deleted during MM-06."
        };
        if (options.DevelopmentExternalControlPlane)
        {
            items.Add(
                "Development-only coexistence mode: the host-managed legacy API/Web must already be stopped and must remain stopped until rollback or completion.");
        }

        return items.ToArray();
    }

    private static CutoverPlanStep[] BuildExecutionSteps(
        bool developmentExternalControlPlane) =>
    [
        new(1, "revalidate-plan", "Validate the reviewed plan hash, validity window, source fingerprint, target identity, and exact resource inventory.", false, false),
        new(2, "snapshot-source-state", "Capture live source route state and all source restart policies before mutation.", false, false),
        developmentExternalControlPlane
            ? new(3, "confirm-development-control-plane-stopped", "Confirm the development-only host-managed legacy API/Web remain stopped outside Docker; they are not controlled by mem-migrate.", false, false)
            : new(3, "freeze-legacy-control-plane", "Disable restart policies and stop the legacy API and Web containers.", true, false),
        new(4, "freeze-legacy-stacks", "Disable restart policies and stop legacy Element and Synapse containers for every stack, then prove no source writer remains.", true, false),
        new(5, "final-capture-and-convert", "Create the final immutable source capture, perform final SQLite-to-PostgreSQL conversion, emit the standard stack artifact, and import it to the bound target.", true, false),
        new(6, "private-final-verification", "Stage the final target privately and require database import, Synapse health, identity, and no-public-route evidence.", true, false),
        new(7, "activate-target-routing", "Apply a live route compare-and-swap so only the verified target becomes public.", true, true),
        new(8, "verify-public-target", "Verify TLS, Matrix client endpoints, Element, login, rooms, history, media where present, and federation where configured.", true, true),
        new(9, "accept-or-rollback", "Require explicit operator acceptance or execute the complete pre-acceptance rollback.", true, true),
        new(10, "retain-legacy", "Keep all old containers, volumes, PostgreSQL data, raw SQLite data, archives, and evidence through the recorded retention period.", true, false)
    ];

    private static CutoverPlanStep[] BuildRollbackSteps(
        bool developmentExternalControlPlane) =>
    [
        new(1, "disable-target-routes", "Remove or disable target public routes using the captured compare-and-swap state.", false, true),
        new(2, "stop-target-runtime", "Stop target Synapse and Element without deleting target data or evidence.", false, true),
        new(3, "restore-source-routes", "Restore the exact captured source routes.", true, true),
        new(4, "restore-source-policies", "Restore every captured source restart policy.", true, false),
        new(5, "restart-source-stacks", "Restart legacy Synapse and Element in dependency order and verify readiness.", true, false),
        developmentExternalControlPlane
            ? new(6, "restart-development-control-plane-manually", "Restart the development-only host-managed legacy API/Web manually outside mem-migrate where they were previously running.", false, false)
            : new(6, "restart-source-control-plane", "Restart the legacy API and Web where they were running before freeze.", true, false),
        new(7, "verify-source-service", "Verify the restored old public service and mark the cutover attempt RolledBack.", true, true)
    ];

    private static string[] BuildWarnings(bool developmentExternalControlPlane)
    {
        var warnings = new List<string>
        {
            "This slice prepares a plan only. It does not stop containers, change restart policies, capture final data, or change routes.",
            "Route IDs and forward targets in this plan are legacy metadata hints, not a substitute for a fresh live NPM or DNS snapshot.",
            "After target public writes begin, rollback may lose target-side messages or account changes created after activation.",
            "Acceptance ends the guaranteed lossless quick-rollback period; the old environment remains retained recovery evidence."
        };
        if (developmentExternalControlPlane)
        {
            warnings.Add(
                "Development-only coexistence override is active. Host-managed legacy API/Web processes are outside mem-migrate Docker freeze and rollback authority. Never use this mode on a production source.");
        }

        return warnings.ToArray();
    }

    private static string[] BuildNextSteps(CutoverPrepareOptions options)
    {
        var steps = new List<string>
        {
            $"Review cutover-plan.json and cutover-plan.md for plan '{options.PlanId}'.",
            "Confirm every container, restart policy, stack identity, route hint, and target evidence item is correct.",
            "Do not proceed if the source fingerprint changes; repeat rehearsal and prepare a new plan.",
            "The next MM-06 slice will consume the reviewed hash to perform an explicit source freeze with rollback-state capture."
        };
        if (options.DevelopmentExternalControlPlane)
        {
            steps.Insert(
                1,
                "Confirm the VS Code/host-managed legacy API and Web are stopped before freeze and record that manual prerequisite in the live evidence.");
        }

        return steps.ToArray();
    }

    private static int FreezeOrder(string role) => role switch
    {
        "legacy-api" => 10,
        "legacy-web" => 20,
        "element" => 30,
        "matrix" => 40,
        _ => 100
    };

    private static bool ContainerIdsMatch(string observed, string expected)
    {
        if (string.IsNullOrWhiteSpace(observed) || string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        var left = observed.Trim();
        var right = expected.Trim();
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return left.Length >= 12 && right.Length >= 12 &&
               (left.StartsWith(right, StringComparison.OrdinalIgnoreCase) ||
                right.StartsWith(left, StringComparison.OrdinalIgnoreCase));
    }
}
