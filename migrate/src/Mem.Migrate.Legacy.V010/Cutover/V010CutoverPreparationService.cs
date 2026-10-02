using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Target;

namespace Mem.Migrate.Legacy.V010.Cutover;

public sealed class V010CutoverPreparationService(
    V010AssessmentService assessmentService,
    ITargetPrivateStageJournal stageJournal,
    ICutoverPlanJournal cutoverJournal)
{
    public async Task<CutoverPreparationReport> PrepareAsync(
        CutoverPrepareOptions rawOptions,
        CancellationToken cancellationToken)
    {
        var options = rawOptions.Normalize();
        await cutoverJournal.InitializeAsync(cancellationToken);
        var inputBinding = CutoverPlanHash.ComputeInputBinding(options);
        var stored = await cutoverJournal.GetAsync(
            options.PlanId!,
            cancellationToken);

        if (stored is not null)
        {
            if (!options.Resume)
            {
                throw new InvalidOperationException(
                    "A cutover plan with this ID already exists. Use --resume or choose a new --plan-id.");
            }

            if (!string.Equals(
                    stored.InputBindingSha256,
                    inputBinding,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    stored.ExpectedSourceFingerprint,
                    options.ExpectedSourceFingerprint,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    stored.StageAttemptId,
                    options.StageAttemptId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Resumed cutover-plan inputs do not match the journaled plan identity.");
            }

            if (string.Equals(stored.Status, "Completed", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(stored.ReportJson))
            {
                return JsonSerializer.Deserialize<CutoverPreparationReport>(
                           stored.ReportJson!,
                           CaptureJson.Options)
                       ?? throw new InvalidDataException(
                           "The completed cutover preparation report could not be read.");
            }
        }
        else
        {
            await cutoverJournal.StartAsync(
                options.PlanId!,
                DateTimeOffset.UtcNow,
                inputBinding,
                options.ExpectedSourceFingerprint,
                options.StageAttemptId,
                cancellationToken);
        }

        try
        {
            var stageJournalPath = Path.Combine(
                options.TargetWorkspacePath!,
                "target-private-stage-journal.db");
            if (!File.Exists(stageJournalPath))
            {
                throw new InvalidOperationException(
                    $"MM-05D private-stage journal was not found in target workspace '{options.TargetWorkspacePath}'.");
            }

            await stageJournal.InitializeAsync(cancellationToken);
            var storedStage = await stageJournal.GetAsync(
                options.StageAttemptId,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    $"MM-05D private-stage attempt '{options.StageAttemptId}' was not found.");

            if (!string.Equals(storedStage.Status, "Completed", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(storedStage.ReportJson))
            {
                throw new InvalidOperationException(
                    "The requested MM-05D private-stage attempt is not completed with durable evidence.");
            }

            var stageReport = JsonSerializer.Deserialize<TargetPrivateStageReport>(
                                  storedStage.ReportJson!,
                                  CaptureJson.Options)
                              ?? throw new InvalidDataException(
                                  "MM-05D private-stage evidence could not be read.");
            var assessment = await assessmentService.RunAsync(
                options.Assessment,
                cancellationToken);
            var generatedAtUtc = DateTimeOffset.UtcNow;
            var plan = V010CutoverPlanBuilder.Build(
                assessment.Result,
                stageReport,
                options,
                generatedAtUtc);
            var planHash = CutoverPlanHash.Compute(plan);
            var report = await CutoverPlanRenderer.WriteAsync(
                plan,
                planHash,
                options.OutputPath,
                cancellationToken);
            var reportJson = JsonSerializer.Serialize(
                report,
                CaptureJson.Options);
            await cutoverJournal.CompleteAsync(
                report,
                reportJson,
                cancellationToken);
            return report;
        }
        catch (Exception ex)
        {
            await cutoverJournal.FailAsync(
                options.PlanId!,
                DateTimeOffset.UtcNow,
                "cutover_prepare_failed",
                ex.Message,
                cancellationToken);
            throw;
        }
    }
}
