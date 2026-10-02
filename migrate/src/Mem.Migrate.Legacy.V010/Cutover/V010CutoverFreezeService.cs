using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Cutover;

namespace Mem.Migrate.Legacy.V010.Cutover;

public sealed class V010CutoverFreezeService(
    V010AssessmentService assessmentService,
    ICutoverSourceFreezer sourceFreezer,
    ICutoverFreezeJournal journal)
{
    public async Task<CutoverFreezeReport> FreezeAsync(
        CutoverFreezeOptions rawOptions,
        CancellationToken cancellationToken)
    {
        var options = rawOptions.Normalize();
        await journal.InitializeAsync(cancellationToken);
        var planReport = await ReadAndValidatePlanAsync(options, cancellationToken);
        var plan = planReport.Plan;
        var stored = await journal.GetAsync(options.FreezeAttemptId!, cancellationToken);
        if (stored is not null)
        {
            if (!options.Resume)
            {
                throw new InvalidOperationException(
                    "A source-freeze attempt with this ID already exists. Use --resume or choose a new ID.");
            }
            if (!string.Equals(stored.PlanId, plan.PlanId, StringComparison.Ordinal) ||
                !string.Equals(stored.PlanHash, options.ExpectedPlanHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Resumed source-freeze inputs do not match the journaled plan identity.");
            }
            if (string.Equals(stored.Status, "Completed", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(stored.ReportJson))
            {
                return JsonSerializer.Deserialize<CutoverFreezeReport>(
                           stored.ReportJson!,
                           CaptureJson.Options)
                       ?? throw new InvalidDataException(
                           "The completed source-freeze report could not be read.");
            }
        }
        else
        {
            await journal.StartAsync(
                options.FreezeAttemptId!,
                DateTimeOffset.UtcNow,
                plan.PlanId,
                options.ExpectedPlanHash,
                cancellationToken);
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            var assessment = await assessmentService.RunAsync(
                options.Assessment,
                cancellationToken);
            ValidateFreshAssessment(plan, assessment.Result, stored is not null && options.Resume);
            var results = await sourceFreezer.FreezeAsync(
                plan,
                options.Assessment.DockerCommand,
                options.Assessment.CommandTimeoutSeconds,
                options.StopTimeoutSeconds,
                cancellationToken);
            var checkpoint = new CutoverRollbackCheckpoint(
                plan.PlanId,
                options.ExpectedPlanHash,
                assessment.Result.SourceFingerprint,
                DateTimeOffset.UtcNow,
                plan.SourceContainersToFreeze,
                plan.RetainedSourceContainers,
                plan.RouteHints,
                BuildRestoreOrder(plan.SourceContainersToFreeze),
                plan.DevelopmentExternalControlPlane
                    ? "Rollback must restore captured Docker restart policies and start only Docker containers that were running before freeze. The development-only host-managed legacy API/Web must be restarted manually. Public routes have not changed in MM-06B."
                    : "Rollback must restore captured restart policies and start only containers that were running before freeze. Public routes have not changed in MM-06B.");
            var report = new CutoverFreezeReport(
                "mem-cutover-source-freeze-report",
                1,
                "Frozen",
                options.FreezeAttemptId!,
                plan.PlanId,
                options.ExpectedPlanHash,
                assessment.Result.SourceFingerprint,
                startedAtUtc,
                DateTimeOffset.UtcNow,
                true,
                false,
                results,
                checkpoint,
                string.Empty,
                string.Empty,
                [
                    "The old source is intentionally stopped and restart-disabled.",
                    "Do not start source writers or alter public routing while final capture is pending."
                ],
                [
                    "Run the final frozen source capture using this rollback checkpoint.",
                    "Do not activate target public routes until final conversion and private verification pass.",
                    "Use the later rollback command before acceptance if cutover cannot proceed."
                ]);
            report = await CutoverFreezeRenderer.WriteAsync(
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
                options.FreezeAttemptId!,
                DateTimeOffset.UtcNow,
                "cutover_freeze_failed",
                ex.Message,
                cancellationToken);
            throw;
        }
    }

    private static async Task<CutoverPreparationReport> ReadAndValidatePlanAsync(
        CutoverFreezeOptions options,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(options.PlanPath))
        {
            throw new FileNotFoundException("Cutover plan report was not found.", options.PlanPath);
        }
        var json = await File.ReadAllTextAsync(options.PlanPath, cancellationToken);
        var report = JsonSerializer.Deserialize<CutoverPreparationReport>(
                         json,
                         CaptureJson.Options)
                     ?? throw new InvalidDataException(
                         "Cutover plan report could not be read.");
        var actualHash = CutoverPlanHash.Compute(report.Plan);
        if (!string.Equals(actualHash, report.PlanHash, StringComparison.Ordinal) ||
            !string.Equals(actualHash, options.ExpectedPlanHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Cutover plan hash does not match the reviewed expected hash.");
        }
        if (DateTimeOffset.UtcNow > report.Plan.ValidUntilUtc)
        {
            throw new InvalidOperationException(
                "The reviewed cutover plan has expired. Prepare and review a fresh plan.");
        }
        if (!string.Equals(report.Plan.Schema, "mem-cutover-plan", StringComparison.Ordinal) ||
            report.Plan.SchemaVersion != 2)
        {
            throw new InvalidDataException(
                "The supplied cutover plan uses a retired schema. Prepare and review a fresh MM-06A plan.");
        }
        if (report.Plan.SourceMutationOccurred || report.Plan.PublicRoutingMutationOccurred)
        {
            throw new InvalidDataException(
                "The supplied preparation report is not a non-mutating MM-06A plan.");
        }
        return report;
    }


    private static string[] BuildRestoreOrder(
        IEnumerable<CutoverSourceContainer> containers)
    {
        var roles = containers
            .Select(container => container.Role)
            .ToHashSet(StringComparer.Ordinal);
        return new[] { "matrix", "element", "legacy-api", "legacy-web" }
            .Where(roles.Contains)
            .ToArray();
    }

    private static void ValidateFreshAssessment(
        CutoverPlanDocument plan,
        Mem.Migrate.Core.Assessment.AssessmentResult current,
        bool resume)
    {
        if (current.Classification != Mem.Migrate.Core.Assessment.AssessmentClassification.ConfirmedSupportedV010 ||
            !current.CanProceedToCapture)
        {
            throw new InvalidOperationException(
                "Fresh source assessment is no longer a confirmed supported v0.1.0 source.");
        }
        if (!string.Equals(current.SourceFingerprint, plan.SourceFingerprint, StringComparison.Ordinal))
        {
            var alreadyFrozen = resume && plan.SourceContainersToFreeze.All(expected =>
            {
                var observed = current.Docker.Containers.SingleOrDefault(container =>
                    string.Equals(container.Id, expected.ContainerId, StringComparison.Ordinal));
                return observed is not null &&
                       !string.Equals(observed.State, "running", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(observed.State, "restarting", StringComparison.OrdinalIgnoreCase) &&
                       (string.IsNullOrEmpty(observed.RestartPolicy) ||
                        string.Equals(observed.RestartPolicy, "no", StringComparison.OrdinalIgnoreCase));
            });
            if (!alreadyFrozen)
            {
                throw new InvalidOperationException(
                    "Source fingerprint changed after plan review. Prepare and review a fresh cutover plan.");
            }
        }
        foreach (var expected in plan.SourceContainersToFreeze)
        {
            var observed = current.Docker.Containers.SingleOrDefault(container =>
                string.Equals(container.Id, expected.ContainerId, StringComparison.Ordinal));
            var expectedRestartPolicy = resume && observed is not null &&
                                        !string.Equals(observed.State, "running", StringComparison.OrdinalIgnoreCase)
                ? observed.RestartPolicy
                : expected.RestartPolicy;
            if (observed is null ||
                !string.Equals(observed.ImageId, expected.ImageId, StringComparison.Ordinal) ||
                !string.Equals(observed.RestartPolicy, expectedRestartPolicy, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Source container '{expected.ContainerName}' changed after plan review.");
            }
        }
    }
}
