using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Diagnostics;

namespace Modules.Setup.InstallRuns;

public sealed class InstallationRunRecoveryService
{
    private const string InterruptedStepMessage =
        "This step was interrupted by a Control Plane restart and will be retried from durable installation state.";

    private const string UnexpectedRunnerFailureMessage =
        "The server-owned installation worker stopped unexpectedly. Review diagnostics before retrying this installation.";

    private readonly MemDbContext _db;
    private readonly ILogger<InstallationRunRecoveryService> _logger;
    private readonly IMemDiagnosticEventWriter? _diagnostics;

    public InstallationRunRecoveryService(
        MemDbContext db,
        ILogger<InstallationRunRecoveryService> logger,
        IMemDiagnosticEventWriter? diagnostics = null)
    {
        _db = db;
        _logger = logger;
        _diagnostics = diagnostics;
    }

    public async Task<IReadOnlyList<Guid>> RecoverInterruptedRunsAsync(
        CancellationToken cancellationToken)
    {
        var installations = await _db.Installations
            .Where(x => x.Status == InstallationStatuses.Running)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (installations.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        if (installations.Count > 1)
        {
            _logger.LogWarning(
                "Startup found {InstallationCount} durable installations marked Running. MEM will recover them sequentially through the single installation worker.",
                installations.Count);
        }

        var recovered = new List<Guid>(installations.Count);

        foreach (var installation in installations)
        {
            var steps = await _db.InstallationStepExecutions
                .Where(x => x.InstallationId == installation.Id)
                .OrderBy(x => x.Sequence)
                .ToListAsync(cancellationToken);

            var interruptedStep = steps
                .FirstOrDefault(x => x.Status == InstallationStepStatuses.Running);

            if (interruptedStep is not null)
            {
                foreach (var step in steps.Where(x =>
                             x.Sequence >= interruptedStep.Sequence &&
                             x.Status != InstallationStepStatuses.Succeeded))
                {
                    step.Status = InstallationStepStatuses.Pending;
                    step.Message = step.Id == interruptedStep.Id
                        ? InterruptedStepMessage
                        : null;
                    step.ErrorMessage = null;
                    step.StartedAtUtc = null;
                    step.CompletedAtUtc = null;
                }

                installation.LastError = null;
                installation.CompletedAtUtc = null;
                installation.UpdatedAtUtc = DateTime.UtcNow;

                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogWarning(
                    "Recovered interrupted installation {InstallationId} at step {StepSequence} - {StepName}. Attempt history {AttemptCount} was preserved and the step was returned to Pending.",
                    installation.Id,
                    interruptedStep.Sequence,
                    interruptedStep.StepName,
                    interruptedStep.AttemptCount);

                await RecordAsync(
                    installation.Id,
                    eventCode: "installation.run.recovered_after_restart",
                    severity: MemDiagnosticSeverities.Warning,
                    stage: "recovery",
                    message: "MEM recovered an installation that was interrupted by a Control Plane restart.",
                    details: new Dictionary<string, string?>
                    {
                        ["stepName"] = interruptedStep.StepName,
                        ["sequence"] = interruptedStep.Sequence.ToString(),
                        ["attemptCount"] = interruptedStep.AttemptCount.ToString()
                    },
                    observed: new Dictionary<string, string?>
                    {
                        ["status"] = installation.Status,
                        ["recovery"] = "queued"
                    });
            }
            else
            {
                _logger.LogInformation(
                    "Installation {InstallationId} is durably Running with no interrupted Running step. It will be queued from its persisted step state.",
                    installation.Id);
            }

            recovered.Add(installation.Id);
        }

        return recovered;
    }

    public async Task MarkUnexpectedRunnerFailureAsync(
        Guid installationId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var installation = await _db.Installations
            .FirstOrDefaultAsync(x => x.Id == installationId, cancellationToken);

        if (installation is null || installation.Status != InstallationStatuses.Running)
        {
            return;
        }

        var activeStep = await _db.InstallationStepExecutions
            .Where(x => x.InstallationId == installationId)
            .Where(x => x.Status == InstallationStepStatuses.Running)
            .OrderBy(x => x.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTime.UtcNow;

        if (activeStep is not null)
        {
            activeStep.Status = InstallationStepStatuses.Failed;
            activeStep.Message = "Installation worker stopped unexpectedly.";
            activeStep.ErrorMessage = UnexpectedRunnerFailureMessage;
            activeStep.CompletedAtUtc = now;
        }

        installation.Status = InstallationStatuses.Failed;
        installation.LastError = UnexpectedRunnerFailureMessage;
        installation.CompletedAtUtc = now;
        installation.UpdatedAtUtc = now;

        await _db.SaveChangesAsync(cancellationToken);

        await RecordAsync(
            installation.Id,
            eventCode: "installation.run.worker_failed",
            severity: MemDiagnosticSeverities.Error,
            stage: activeStep?.StepName ?? "runner",
            message: "The server-owned installation worker stopped unexpectedly.",
            createIncident: true,
            exception: exception,
            details: new Dictionary<string, string?>
            {
                ["stepName"] = activeStep?.StepName,
                ["attemptCount"] = activeStep?.AttemptCount.ToString()
            },
            observed: new Dictionary<string, string?>
            {
                ["status"] = installation.Status
            });
    }

    private Task<MemDiagnosticWriteResult?> RecordAsync(
        Guid installationId,
        string eventCode,
        string severity,
        string stage,
        string message,
        bool createIncident = false,
        Exception? exception = null,
        IReadOnlyDictionary<string, string?>? details = null,
        IReadOnlyDictionary<string, string?>? observed = null) =>
        _diagnostics.TryWriteWorkflowEventAsync(new MemDiagnosticWriteRequest(
            Severity: severity,
            EventCode: eventCode,
            Source: "api.install-recovery",
            Feature: "installation",
            Stage: stage,
            Message: message,
            CreateIncident: createIncident,
            Resource: new MemDiagnosticResource(
                Kind: "installation",
                Id: installationId.ToString("D"),
                DisplayName: "MEM installation",
                WorkspacePath: "/setup"),
            Observed: observed,
            Details: details,
            Exception: exception,
            SuggestedAction: createIncident
                ? "Open Setup activity and Diagnostics before retrying the failed installation."
                : null,
            Retryable: createIncident));
}
