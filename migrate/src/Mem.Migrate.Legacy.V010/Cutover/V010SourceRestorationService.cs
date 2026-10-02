using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Cutover;

namespace Mem.Migrate.Legacy.V010.Cutover;

public sealed class V010SourceRestorationService(
    ICutoverSourceRestorer sourceRestorer,
    ISourceRestorationJournal journal)
{
    public const string HandoffSchemaVersion =
        "mem.migration.source-restoration-handoff.v1";

    private static readonly JsonSerializerOptions HandoffJsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<SourceRestorationReport> RestoreAsync(
        SourceRestorationOptions rawOptions,
        CancellationToken cancellationToken)
    {
        var options = rawOptions.Normalize();
        var freeze = await ReadFreezeReportAsync(
            options.FreezeReportPath,
            cancellationToken);
        var handoff = await ReadHandoffAsync(
            options.SourceHandoffPath,
            options.ExpectedHandoffSha256,
            cancellationToken);
        ValidateAuthority(freeze, handoff, options);

        await journal.InitializeAsync(cancellationToken);
        var stored = await journal.GetAsync(
            options.RestorationAttemptId!,
            cancellationToken);
        if (stored is not null)
        {
            if (!options.Resume)
            {
                throw new InvalidOperationException(
                    "A source restoration attempt with this ID already exists. Use --resume or choose a new ID.");
            }

            if (!string.Equals(
                    stored.SourceHandoffId,
                    handoff.Payload.HandoffId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    stored.SourceHandoffSha256,
                    handoff.PayloadSha256,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    stored.FreezeAttemptId,
                    freeze.FreezeAttemptId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Resumed source restoration inputs do not match the journaled authority.");
            }

            if (string.Equals(stored.Status, "Completed", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(stored.ReportJson))
            {
                return JsonSerializer.Deserialize<SourceRestorationReport>(
                           stored.ReportJson!,
                           CaptureJson.Options)
                       ?? throw new InvalidDataException(
                           "The completed source restoration report could not be read.");
            }
        }
        else
        {
            await journal.StartAsync(
                options.RestorationAttemptId!,
                DateTimeOffset.UtcNow,
                handoff.Payload.HandoffId,
                handoff.PayloadSha256,
                freeze.FreezeAttemptId,
                cancellationToken);
        }

        var startedAtUtc = stored?.StartedAtUtc ?? DateTimeOffset.UtcNow;
        try
        {
            var containers = await sourceRestorer.RestoreAsync(
                freeze.RollbackCheckpoint,
                freeze.Containers,
                options.Assessment.DockerCommand,
                options.Assessment.CommandTimeoutSeconds,
                options.ReadinessTimeoutSeconds,
                cancellationToken);

            var matrixVerified = RequiredRoleVerified(containers, "matrix");
            var elementVerified = RequiredRoleVerified(containers, "element");
            var restartPoliciesRestored = containers.All(item =>
                item.RestartPolicyRestored);
            var runningStatesRestored = containers.All(item =>
                item.RunningStateRestored);
            if (!matrixVerified ||
                !elementVerified ||
                !restartPoliciesRestored ||
                !runningStatesRestored)
            {
                throw new InvalidOperationException(
                    "The legacy source did not return to its complete pre-freeze runtime state.");
            }

            var developmentExternal = IsDevelopmentExternalControlPlane(
                freeze.RollbackCheckpoint);
            var warnings = developmentExternal
                ? new[]
                {
                    "The development-only host-managed legacy API and Web are outside Docker restoration authority and must be restarted and verified manually. This topology is not supported in production."
                }
                : Array.Empty<string>();
            var report = new SourceRestorationReport(
                Schema: "mem-cutover-source-restoration-report",
                SchemaVersion: 1,
                Status: "Completed",
                RestorationAttemptId: options.RestorationAttemptId!,
                SourceHandoffId: handoff.Payload.HandoffId,
                SourceHandoffSha256: handoff.PayloadSha256,
                MigrationId: handoff.Payload.MigrationId,
                SourceMigrationId: handoff.Payload.SourceMigrationId,
                TargetRollbackExecutionId: handoff.Payload.TargetRollbackExecutionId,
                FreezeAttemptId: freeze.FreezeAttemptId,
                FreezePlanId: freeze.PlanId,
                FreezePlanHash: freeze.PlanHash,
                SourceFingerprint: freeze.SourceFingerprint,
                SourceStackSlug: handoff.Payload.SourceStackSlug,
                MatrixServerName: handoff.Payload.MatrixServerName,
                StartedAtUtc: startedAtUtc,
                CompletedAtUtc: DateTimeOffset.UtcNow,
                SourceRestored: true,
                RestartPoliciesRestored: true,
                OriginalRunningStatesRestored: true,
                MatrixVerified: matrixVerified,
                ElementVerified: elementVerified,
                TargetRollbackAuthorityVerified: true,
                DevelopmentExternalControlPlane: developmentExternal,
                Containers: containers,
                TargetRouteEvidence: handoff.Payload.Routes.ToArray(),
                CompletionEvidencePath: string.Empty,
                CompletionEvidenceSha256: string.Empty,
                JsonPath: string.Empty,
                MarkdownPath: string.Empty,
                Warnings: warnings,
                NextSteps:
                [
                    "Verify end-user Matrix and Element access through the restored public routes.",
                    "Transfer this source restoration report to the MEM 0.2.0 target operator for coordinated rollback completion.",
                    "Retain the target migration data and evidence until the rollback outcome is reviewed and closed."
                ]);
            report = await SourceRestorationRenderer.WriteAsync(
                report,
                options.OutputPath,
                cancellationToken);
            var reportJson = JsonSerializer.Serialize(report, CaptureJson.Options);
            await journal.CompleteAsync(report, reportJson, cancellationToken);
            return report;
        }
        catch (Exception ex)
        {
            await journal.FailAsync(
                options.RestorationAttemptId!,
                DateTimeOffset.UtcNow,
                "source_restoration_failed",
                ex.Message,
                cancellationToken);
            throw;
        }
    }

    public static string ComputePayloadSha256(
        SourceRestorationHandoffPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, HandoffJsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }

    private static async Task<CutoverFreezeReport> ReadFreezeReportAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Source-freeze report was not found.",
                path);
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<CutoverFreezeReport>(
                   json,
                   CaptureJson.Options)
               ?? throw new InvalidDataException(
                   "Source-freeze report could not be read.");
    }

    private static async Task<SourceRestorationHandoffEnvelope> ReadHandoffAsync(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Source-restoration handoff was not found.",
                path);
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var envelope = JsonSerializer.Deserialize<SourceRestorationHandoffEnvelope>(
                           json,
                           HandoffJsonOptions)
                       ?? throw new InvalidDataException(
                           "Source-restoration handoff could not be read.");
        var actualSha256 = ComputePayloadSha256(envelope.Payload);
        if (!string.Equals(
                envelope.SchemaVersion,
                HandoffSchemaVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                actualSha256,
                envelope.PayloadSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                actualSha256,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Source-restoration handoff schema or payload hash does not match the reviewed target authority.");
        }

        return envelope with { PayloadSha256 = actualSha256 };
    }

    private static void ValidateAuthority(
        CutoverFreezeReport freeze,
        SourceRestorationHandoffEnvelope handoff,
        SourceRestorationOptions options)
    {
        if (!string.Equals(
                freeze.Schema,
                "mem-cutover-source-freeze-report",
                StringComparison.Ordinal) ||
            freeze.SchemaVersion != 1 ||
            !string.Equals(freeze.Status, "Frozen", StringComparison.Ordinal) ||
            !freeze.SourceFrozen ||
            freeze.PublicRoutingMutationOccurred)
        {
            throw new InvalidDataException(
                "The supplied MM-06B report does not prove an unchanged frozen source.");
        }

        if (freeze.Containers.Length == 0 ||
            freeze.Containers.Any(item => !item.Stopped || !item.RestartPolicyDisabled) ||
            freeze.RollbackCheckpoint.SourceContainers.Length != freeze.Containers.Length ||
            !string.Equals(
                freeze.PlanId,
                freeze.RollbackCheckpoint.PlanId,
                StringComparison.Ordinal) ||
            !string.Equals(
                freeze.PlanHash,
                freeze.RollbackCheckpoint.PlanHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                freeze.SourceFingerprint,
                freeze.RollbackCheckpoint.SourceFingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The source-freeze report and rollback checkpoint are internally inconsistent.");
        }

        var payload = handoff.Payload;
        if (!payload.SourceFrozen ||
            payload.MigrationAccepted ||
            !payload.TargetRoutesRestored ||
            !payload.TargetRuntimeRoutesRemoved ||
            !payload.TargetContainersStopped ||
            !IsSha256(payload.PlanSha256) ||
            !IsSha256(payload.PackageRevisionSha256) ||
            !IsSha256(payload.SourceFingerprint) ||
            !string.Equals(
                payload.SourceFingerprint,
                freeze.SourceFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The target handoff does not authorize source restoration for this exact frozen source.");
        }

        RequireIdentifier(payload.HandoffId, "handoff ID");
        RequireIdentifier(payload.MigrationId, "migration ID");
        RequireIdentifier(payload.AdoptionPlanId, "adoption plan ID");
        RequireIdentifier(payload.CutoverExecutionId, "cutover execution ID");
        RequireIdentifier(payload.TargetRollbackExecutionId, "target rollback execution ID");
        RequireIdentifier(payload.SourceMigrationId, "source migration ID");
        RequireIdentifier(payload.SourceId, "source ID");
        if (string.IsNullOrWhiteSpace(payload.SourceStackSlug) ||
            string.IsNullOrWhiteSpace(payload.MatrixServerName))
        {
            throw new InvalidDataException(
                "The target handoff does not identify the source stack and Matrix server.");
        }

        ValidateTargetRoutes(freeze.RollbackCheckpoint, payload.Routes);
        ValidateTargetContainers(payload.TargetContainers);
        ValidateRequiredSourceServices(freeze.RollbackCheckpoint);

        var developmentExternal = IsDevelopmentExternalControlPlane(
            freeze.RollbackCheckpoint);
        if (developmentExternal && !options.DevelopmentExternalControlPlaneReady)
        {
            throw new InvalidOperationException(
                "The freeze checkpoint uses the development-only external legacy control plane. Restart and verify the host-managed legacy API/Web, then pass --development-external-control-plane-ready. Never use this override in production.");
        }
    }

    private static void ValidateTargetRoutes(
        CutoverRollbackCheckpoint checkpoint,
        IReadOnlyList<SourceRestorationRouteEvidence> routes)
    {
        if (routes.Count != 2 ||
            routes.Select(item => item.ServiceKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != 2)
        {
            throw new InvalidDataException(
                "The target handoff must contain exactly one Matrix and one Element route restoration result.");
        }

        foreach (var route in routes)
        {
            var sourceRole = route.ServiceKey switch
            {
                "matrix" => "matrix",
                "element-web" => "element",
                _ => throw new InvalidDataException(
                    $"The target handoff contains unsupported service key '{route.ServiceKey}'.")
            };
            if (string.IsNullOrWhiteSpace(route.PublicHost) ||
                !IsSha256(route.SnapshotSha256) ||
                (!string.Equals(route.RestoredState, "restored", StringComparison.Ordinal) &&
                 !string.Equals(route.RestoredState, "absent", StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    $"Target route evidence for '{route.ServiceKey}' is incomplete.");
            }

            if (string.Equals(route.RestoredState, "absent", StringComparison.Ordinal))
            {
                if (route.RouteId is not null ||
                    route.ForwardScheme is not null ||
                    route.ForwardHost is not null ||
                    route.ForwardPort is not null ||
                    route.Enabled is not null ||
                    !string.Equals(
                        route.SnapshotSha256,
                        Sha256("absent"),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Absent target route evidence for '{route.PublicHost}' contains a live route target.");
                }
            }
            else if (route.RouteId is null ||
                     string.IsNullOrWhiteSpace(route.ForwardScheme) ||
                     string.IsNullOrWhiteSpace(route.ForwardHost) ||
                     route.ForwardPort is null or <= 0 or > 65535 ||
                     route.Enabled is null)
            {
                throw new InvalidDataException(
                    $"Restored target route evidence for '{route.PublicHost}' is incomplete.");
            }

            var matchingHints = checkpoint.RouteHints.Where(hint =>
                    string.Equals(hint.Role, sourceRole, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(hint.PublicHost, route.PublicHost, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matchingHints.Length != 1)
            {
                throw new InvalidDataException(
                    $"Target route '{route.PublicHost}' does not match exactly one source-freeze route hint.");
            }
        }
    }

    private static void ValidateTargetContainers(
        IReadOnlyList<SourceRestorationTargetContainerEvidence> containers)
    {
        if (containers.Count != 2 ||
            containers.Any(item => !item.Stopped) ||
            containers.Count(item => string.Equals(
                item.ServiceKey,
                "matrix",
                StringComparison.OrdinalIgnoreCase)) != 1 ||
            containers.Count(item => string.Equals(
                item.ServiceKey,
                "element-web",
                StringComparison.OrdinalIgnoreCase)) != 1)
        {
            throw new InvalidDataException(
                "The target handoff does not prove both migrated target containers are stopped.");
        }
    }

    private static void ValidateRequiredSourceServices(
        CutoverRollbackCheckpoint checkpoint)
    {
        foreach (var role in new[] { "matrix", "element" })
        {
            var matches = checkpoint.SourceContainers.Where(item =>
                    string.Equals(item.Role, role, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length == 0 || matches.Any(item => !item.WasRunning))
            {
                throw new InvalidDataException(
                    $"The source-freeze checkpoint does not prove a running pre-freeze '{role}' service.");
            }
        }

        var hasApi = checkpoint.SourceContainers.Any(item =>
            string.Equals(item.Role, "legacy-api", StringComparison.Ordinal));
        var hasWeb = checkpoint.SourceContainers.Any(item =>
            string.Equals(item.Role, "legacy-web", StringComparison.Ordinal));
        if (hasApi != hasWeb)
        {
            throw new InvalidDataException(
                "The source-freeze checkpoint contains only part of the legacy control-plane container pair.");
        }
    }

    private static bool RequiredRoleVerified(
        IEnumerable<SourceRestorationContainerResult> containers,
        string role)
    {
        var matches = containers.Where(item =>
                string.Equals(item.Role, role, StringComparison.Ordinal))
            .ToArray();
        return matches.Length > 0 && matches.All(item => item.ServiceVerified);
    }

    private static bool IsDevelopmentExternalControlPlane(
        CutoverRollbackCheckpoint checkpoint) =>
        !checkpoint.SourceContainers.Any(item =>
            string.Equals(item.Role, "legacy-api", StringComparison.Ordinal)) &&
        !checkpoint.SourceContainers.Any(item =>
            string.Equals(item.Role, "legacy-web", StringComparison.Ordinal));

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool IsSha256(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length == 64 &&
        value.All(Uri.IsHexDigit);

    private static void RequireIdentifier(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 160)
        {
            throw new InvalidDataException(
                $"The source-restoration handoff {name} is invalid.");
        }
    }
}
