using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Modules.Setup.InstallRuns;

/// <summary>
/// Reconciles the durable first-time installation step graph when a newer MEM
/// version adds required setup steps to an already-created, incomplete plan.
/// Completed step evidence and attempt counters are preserved; only sequence
/// numbers at/after the insertion point move forward.
/// </summary>
public sealed class InstallStepGraphReconciler(MemDbContext db)
{
    public async Task ReconcileAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        var steps = await db.InstallationStepExecutions
            .Where(x => x.InstallationId == installationId)
            .OrderBy(x => x.Sequence)
            .ToListAsync(cancellationToken);

        if (steps.Count == 0)
        {
            return;
        }

        var install = steps.FirstOrDefault(x =>
            x.StepName == InstallStepNames.InstallSharedPlatformTurn);
        var verify = steps.FirstOrDefault(x =>
            x.StepName == InstallStepNames.VerifySharedPlatformTurn);

        if (install is not null && verify is not null)
        {
            return;
        }

        if (install is null && verify is null)
        {
            var anchor = steps.FirstOrDefault(x =>
                x.StepName == InstallStepNames.StartSelectedSupportTools)
                ?? steps.FirstOrDefault(x =>
                    x.StepName == InstallStepNames.RunVerificationChecks)
                ?? steps.FirstOrDefault(x =>
                    x.StepName == InstallStepNames.CompleteSetupHandoff);

            if (anchor is null)
            {
                throw new InvalidOperationException(
                    "The incomplete installation step graph cannot be reconciled safely because no post-platform insertion anchor exists.");
            }

            var insertAt = anchor.Sequence;
            ShiftAtOrAfter(steps, insertAt, 2);
            db.InstallationStepExecutions.Add(NewStep(
                installationId,
                InstallStepNames.InstallSharedPlatformTurn,
                insertAt));
            db.InstallationStepExecutions.Add(NewStep(
                installationId,
                InstallStepNames.VerifySharedPlatformTurn,
                insertAt + 1));
        }
        else if (install is not null)
        {
            var insertAt = install.Sequence + 1;
            ShiftAtOrAfter(steps, insertAt, 1);
            db.InstallationStepExecutions.Add(NewStep(
                installationId,
                InstallStepNames.VerifySharedPlatformTurn,
                insertAt));
        }
        else
        {
            var insertAt = verify!.Sequence;
            ShiftAtOrAfter(steps, insertAt, 1);
            db.InstallationStepExecutions.Add(NewStep(
                installationId,
                InstallStepNames.InstallSharedPlatformTurn,
                insertAt));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ShiftAtOrAfter(
        IEnumerable<InstallationStepExecutionEntity> steps,
        int sequence,
        int delta)
    {
        foreach (var step in steps.Where(x => x.Sequence >= sequence))
        {
            step.Sequence += delta;
        }
    }

    private static InstallationStepExecutionEntity NewStep(
        Guid installationId,
        string stepName,
        int sequence) =>
        new()
        {
            Id = Guid.NewGuid(),
            InstallationId = installationId,
            StepName = stepName,
            Sequence = sequence,
            Status = InstallationStepStatuses.Pending,
            Message = null,
            ErrorMessage = null,
            AttemptCount = 0,
            StartedAtUtc = null,
            CompletedAtUtc = null
        };
}
