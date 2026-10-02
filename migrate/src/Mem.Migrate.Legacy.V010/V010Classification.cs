using Mem.Migrate.Core.Assessment;

namespace Mem.Migrate.Legacy.V010;

public sealed record V010ClassificationResult(
    AssessmentClassification Classification,
    MigrationRecommendation Recommendation,
    bool CanProceedToCapture,
    AssessmentSignal[] Signals,
    AssessmentFinding[] Findings);

public static class V010Classifier
{
    public static V010ClassificationResult Classify(
        DockerInventoryObservation docker,
        SystemConfigObservation systemConfig,
        LegacyDatabaseObservation database,
        LegacyFileSystemObservation fileSystem)
    {
        var findings = new List<AssessmentFinding>();
        var exactDatabases = database.Candidates
            .Where(x => x.ExactSupportedSchema)
            .ToArray();
        var mismatchedDatabases = database.Candidates
            .Where(x => x.AppSchemaPresent && !x.ExactSupportedSchema)
            .ToArray();

        var oldApiContainers = docker.Containers
            .Where(IsLegacyApi)
            .ToArray();
        var oldWebContainers = docker.Containers
            .Where(IsLegacyWeb)
            .ToArray();
        var matrixContainers = docker.Containers
            .Where(IsMatrix)
            .ToArray();
        var hasGateway = docker.Networks.Any(network =>
            string.Equals(
                network.Name,
                V010Constants.GatewayNetworkName,
                StringComparison.OrdinalIgnoreCase));
        var exactProduct = systemConfig.Reachable
            && string.Equals(
                systemConfig.ProductName,
                V010Constants.ProductName,
                StringComparison.Ordinal)
            && string.Equals(
                systemConfig.ProductVersion,
                V010Constants.ProductVersion,
                StringComparison.Ordinal);

        var currentTargetPresent = systemConfig.Reachable
            && string.Equals(
                systemConfig.ProductName,
                V010Constants.ProductName,
                StringComparison.Ordinal)
            && IsAtLeastV011(systemConfig.ProductVersion);

        var signals = new[]
        {
            new AssessmentSignal(
                "docker.available",
                docker.Available,
                10,
                docker.Available
                    ? $"Docker {docker.ServerVersion ?? "unknown"} is available."
                    : "Docker could not be inspected."),
            new AssessmentSignal(
                "legacy.product.exact",
                exactProduct,
                40,
                exactProduct
                    ? "The public system configuration reports MatrixEasyMode 0.1.0."
                    : "The exact v0.1.0 product response was not confirmed."),
            new AssessmentSignal(
                "legacy.database.exact",
                exactDatabases.Length == 1,
                40,
                exactDatabases.Length switch
                {
                    0 => "No exact v0.1.0 app schema was confirmed.",
                    1 => "One exact v0.1.0 app schema was confirmed.",
                    _ => "More than one exact v0.1.0 app schema was confirmed."
                }),
            new AssessmentSignal(
                "legacy.api.container",
                oldApiContainers.Length > 0,
                10,
                $"{oldApiContainers.Length} legacy API candidate(s) found."),
            new AssessmentSignal(
                "legacy.web.container",
                oldWebContainers.Length > 0,
                10,
                $"{oldWebContainers.Length} legacy Web candidate(s) found."),
            new AssessmentSignal(
                "legacy.gateway.network",
                hasGateway,
                5,
                hasGateway
                    ? "The mem-gateway network exists."
                    : "The mem-gateway network was not found."),
            new AssessmentSignal(
                "legacy.matrix.container",
                matrixContainers.Length > 0,
                10,
                $"{matrixContainers.Length} Matrix container candidate(s) found."),
            new AssessmentSignal(
                "legacy.sqlite.files",
                fileSystem.Stacks.Any(stack => stack.SqliteDatabase.Exists),
                20,
                $"{fileSystem.Stacks.Count(stack => stack.SqliteDatabase.Exists)} Synapse SQLite database(s) found.")
        };

        if (!docker.Available)
        {
            findings.Add(
                Blocker(
                    "docker_unavailable",
                    "Docker could not be inspected.",
                    "Run the assessment as a local operator with permission to use the Docker socket."));
        }

        if (currentTargetPresent)
        {
            findings.Add(
                Blocker(
                    "current_v011_present",
                    "The probed MEM endpoint reports v0.1.1 or later.",
                    "Confirm that the tool is running on the intended v0.1.0 source host."));
        }

        if (exactDatabases.Length > 1)
        {
            findings.Add(
                Blocker(
                    "multiple_legacy_databases",
                    "More than one exact v0.1.0 application database was found.",
                    "Select the intended PostgreSQL container explicitly with --postgres-container."));
        }

        foreach (var mismatch in mismatchedDatabases)
        {
            findings.Add(
                Blocker(
                    "legacy_schema_mismatch",
                    $"Database '{mismatch.DatabaseName}' in container '{mismatch.ContainerName}' does not match the supported v0.1.0 schema.",
                    "Do not capture this database until its migration history and schema differences are reviewed."));
        }

        if (database.ProbeAttempted && database.Candidates.All(x => !x.AppSchemaPresent))
        {
            findings.Add(
                Blocker(
                    "legacy_database_not_confirmed",
                    "PostgreSQL candidates were found, but none exposed the expected app schema.",
                    "Verify the legacy database container and database name."));
        }

        if (!database.ProbeAttempted)
        {
            findings.Add(
                Blocker(
                    "postgres_candidate_missing",
                    "No PostgreSQL container candidate was found.",
                    "Start the legacy PostgreSQL container or provide --postgres-container."));
        }

        if (exactDatabases.Length == 1 && exactDatabases[0].Stacks.Length == 0)
        {
            findings.Add(
                new AssessmentFinding(
                    "legacy_database_has_no_stacks",
                    FindingSeverity.Warning,
                    "The supported v0.1.0 database contains no stack records.",
                    "Confirm whether this installation contains anything that needs migration."));
        }

        if (oldApiContainers.Length == 0)
        {
            findings.Add(
                new AssessmentFinding(
                    "legacy_api_not_running",
                    FindingSeverity.Warning,
                    "No legacy API container was confirmed.",
                    "The assessment can continue from the database and stack data, but the source topology needs review."));
        }

        if (oldWebContainers.Length == 0)
        {
            findings.Add(
                new AssessmentFinding(
                    "legacy_web_not_running",
                    FindingSeverity.Warning,
                    "No legacy Next.js Web container was confirmed.",
                    "The application can still be migrated, but the old public route must be identified before cutover."));
        }

        if (!systemConfig.Reachable)
        {
            findings.Add(
                new AssessmentFinding(
                    "legacy_system_config_unreachable",
                    FindingSeverity.Warning,
                    "The legacy /api/system/config endpoint was not reachable.",
                    "Use --api-url when the API is published on a non-standard local port."));
        }
        else if (!exactProduct && !currentTargetPresent)
        {
            findings.Add(
                new AssessmentFinding(
                    "legacy_product_response_unexpected",
                    FindingSeverity.Warning,
                    $"The endpoint reported '{systemConfig.ProductName ?? "unknown"}' version '{systemConfig.ProductVersion ?? "unknown"}'.",
                    "Review the source release before proceeding."));
        }

        foreach (var stack in fileSystem.Stacks)
        {
            findings.AddRange(stack.Findings);
        }

        var exactDatabase = exactDatabases.FirstOrDefault();

        if (exactDatabase is not null)
        {
            var matrixServices = exactDatabase.Services.Count(service =>
                string.Equals(
                    service.ServiceKey,
                    V010Constants.MatrixServiceKey,
                    StringComparison.OrdinalIgnoreCase));

            if (matrixServices != fileSystem.Stacks.Length)
            {
                findings.Add(
                    Blocker(
                        "stack_file_inventory_incomplete",
                        $"The database contains {matrixServices} Matrix service(s), but {fileSystem.Stacks.Length} stack file set(s) were assessed.",
                        "Resolve missing service runtime metadata or bind mounts before capture."));
            }

            if (exactDatabase.ActiveGuestChats > 0)
            {
                findings.Add(
                    new AssessmentFinding(
                        "active_guest_chat_sessions",
                        FindingSeverity.Warning,
                        $"{exactDatabase.ActiveGuestChats} active guest-chat session(s) will not be migrated.",
                        "Notify affected users and let sessions expire before final cutover."));
            }

            if (exactDatabase.ActivePasswordResetRequests > 0)
            {
                findings.Add(
                    new AssessmentFinding(
                        "active_password_reset_requests",
                        FindingSeverity.Warning,
                        $"{exactDatabase.ActivePasswordResetRequests} active password-reset request(s) will not be migrated.",
                        "Revoke or allow the links to expire before final cutover."));
            }
        }

        var classification = DetermineClassification(
            docker,
            currentTargetPresent,
            exactProduct,
            exactDatabases,
            mismatchedDatabases,
            findings,
            oldApiContainers,
            matrixContainers);

        var canProceed = classification
            is AssessmentClassification.ConfirmedSupportedV010
            && findings.All(x => x.Severity is not FindingSeverity.Blocker);

        var recommendation = classification switch
        {
            AssessmentClassification.ConfirmedSupportedV010 =>
                MigrationRecommendation.NewServerRecommended,
            AssessmentClassification.ProbableV010 or
            AssessmentClassification.PartialRepairableV010 or
            AssessmentClassification.Blocked =>
                MigrationRecommendation.ResolveBlockers,
            AssessmentClassification.AmbiguousMultipleInstallations =>
                MigrationRecommendation.ResolveBlockers,
            _ => MigrationRecommendation.Unsupported
        };

        return new V010ClassificationResult(
            Classification: classification,
            Recommendation: recommendation,
            CanProceedToCapture: canProceed,
            Signals: signals,
            Findings: findings
                .OrderByDescending(x => x.Severity)
                .ThenBy(x => x.Code, StringComparer.Ordinal)
                .ToArray());
    }

    private static AssessmentClassification DetermineClassification(
        DockerInventoryObservation docker,
        bool currentTargetPresent,
        bool exactProduct,
        LegacyDatabaseCandidateObservation[] exactDatabases,
        LegacyDatabaseCandidateObservation[] mismatchedDatabases,
        IReadOnlyCollection<AssessmentFinding> findings,
        DockerContainerObservation[] oldApiContainers,
        DockerContainerObservation[] matrixContainers)
    {
        if (currentTargetPresent)
        {
            return AssessmentClassification.CurrentV011Present;
        }

        if (exactDatabases.Length > 1)
        {
            return AssessmentClassification.AmbiguousMultipleInstallations;
        }

        if (mismatchedDatabases.Length > 0)
        {
            return AssessmentClassification.UnsupportedSource;
        }

        if (!docker.Available)
        {
            return AssessmentClassification.Blocked;
        }

        if (exactDatabases.Length == 1)
        {
            return findings.Any(x => x.Severity is FindingSeverity.Blocker)
                ? AssessmentClassification.PartialRepairableV010
                : AssessmentClassification.ConfirmedSupportedV010;
        }

        if (exactProduct &&
            (oldApiContainers.Length > 0 || matrixContainers.Length > 0))
        {
            return AssessmentClassification.ProbableV010;
        }

        if (oldApiContainers.Length > 0 && matrixContainers.Length > 0)
        {
            return AssessmentClassification.ProbableV010;
        }

        return AssessmentClassification.UnsupportedSource;
    }

    private static bool IsLegacyApi(DockerContainerObservation container) =>
        string.Equals(
            container.Name,
            "mem-api",
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            container.ComposeService,
            "api",
            StringComparison.OrdinalIgnoreCase)
        || container.Image.Contains(
            "matrix-easy-mode-api",
            StringComparison.OrdinalIgnoreCase)
        || container.Image.Contains(
            "mem-api",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsLegacyWeb(DockerContainerObservation container) =>
        string.Equals(
            container.Name,
            "mem-web",
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            container.ComposeService,
            "web",
            StringComparison.OrdinalIgnoreCase)
        || container.Image.Contains(
            "mem-web",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsMatrix(DockerContainerObservation container) =>
        container.ManagedLabels.TryGetValue(
            "mem.serviceKey",
            out var key)
        && string.Equals(
            key,
            V010Constants.MatrixServiceKey,
            StringComparison.OrdinalIgnoreCase)
        || container.Image.Contains(
            "matrixdotorg/synapse",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsAtLeastV011(string? version)
    {
        return Version.TryParse(version, out var parsed)
            && parsed >= new Version(0, 1, 1);
    }

    private static AssessmentFinding Blocker(
        string code,
        string message,
        string remediation) =>
        new(code, FindingSeverity.Blocker, message, remediation);
}
