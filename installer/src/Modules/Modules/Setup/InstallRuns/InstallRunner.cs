using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Diagnostics;

namespace Modules.Setup.InstallRuns;

public sealed class InstallRunner : IInstallRunner
{
    private readonly MemDbContext _db;
    private readonly InstallStepExecutor _stepExecutor;
    private readonly ILogger<InstallRunner> _logger;
    private readonly IMemDiagnosticEventWriter? _diagnostics;
    private readonly InstallProgressReporter? _progressReporter;
    private readonly InstallStepGraphReconciler? _stepGraphReconciler;

    public InstallRunner(
        MemDbContext db,
        InstallStepExecutor stepExecutor,
        ILogger<InstallRunner> logger,
        IMemDiagnosticEventWriter? diagnostics = null,
        InstallProgressReporter? progressReporter = null,
        InstallStepGraphReconciler? stepGraphReconciler = null)
    {
        _db = db;
        _stepExecutor = stepExecutor;
        _logger = logger;
        _diagnostics = diagnostics;
        _progressReporter = progressReporter;
        _stepGraphReconciler = stepGraphReconciler;
    }

    public async Task RunAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Server-owned installation runner started for installation {InstallationId}",
            installationId);
        await RecordInstallationAsync(
            installationId,
            eventCode: "installation.run.started",
            severity: MemDiagnosticSeverities.Information,
            stage: "starting",
            message: "The MEM installation workflow started.");

        if (_stepGraphReconciler is not null)
        {
            await _stepGraphReconciler.ReconcileAsync(
                installationId,
                cancellationToken);
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var installation = await _db.Installations
                .FirstOrDefaultAsync(x => x.Id == installationId, cancellationToken);

            if (installation is null)
            {
                _logger.LogWarning(
                    "Installation {InstallationId} was not found while running workflow",
                    installationId);
                await RecordInstallationAsync(
                    installationId,
                    eventCode: "installation.run.not_found",
                    severity: MemDiagnosticSeverities.Warning,
                    stage: "starting",
                    message: "The installation workflow could not resolve its durable installation record.");

                return;
            }

            if (installation.Status == InstallationStatuses.WaitingForUser)
            {
                _logger.LogInformation(
                    "Installation {InstallationId} is waiting for user action. Rechecking waiting steps.",
                    installationId);

                var waitingSteps = await _db.InstallationStepExecutions
                    .Where(x => x.InstallationId == installation.Id)
                    .Where(x => x.Status == InstallationStepStatuses.WaitingForUser)
                    .ToListAsync(cancellationToken);

                foreach (var waitingStep in waitingSteps)
                {
                    waitingStep.Status = InstallationStepStatuses.Pending;
                    waitingStep.Message = "Rechecking after operator action...";
                    waitingStep.ErrorMessage = null;
                    waitingStep.CompletedAtUtc = null;
                }

                installation.Status = InstallationStatuses.Running;
                installation.LastError = null;
                installation.CompletedAtUtc = null;
                installation.UpdatedAtUtc = DateTime.UtcNow;

                await _db.SaveChangesAsync(cancellationToken);
            }

            if (installation.Status is not InstallationStatuses.Running)
            {
                _logger.LogInformation(
                    "Installation {InstallationId} is no longer running. Current status: {InstallationStatus}",
                    installationId,
                    installation.Status);

                return;
            }

            var nextStep = await _db.InstallationStepExecutions
                .Where(x => x.InstallationId == installationId)
                .Where(x =>
                    x.Status == InstallationStepStatuses.Pending ||
                    x.Status == InstallationStepStatuses.Failed)
                .OrderBy(x => x.Sequence)
                .FirstOrDefaultAsync(cancellationToken);

            if (nextStep is null)
            {
                var waitingStep = await _db.InstallationStepExecutions
                    .Where(x => x.InstallationId == installationId)
                    .Where(x => x.Status == InstallationStepStatuses.WaitingForUser)
                    .OrderBy(x => x.Sequence)
                    .FirstOrDefaultAsync(cancellationToken);

                if (waitingStep is not null)
                {
                    installation.Status = InstallationStatuses.WaitingForUser;
                    installation.CompletedAtUtc = null;
                    installation.UpdatedAtUtc = DateTime.UtcNow;
                    installation.LastError = waitingStep.ErrorMessage ?? waitingStep.Message;

                    await _db.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation(
                        "Installation {InstallationId} still has a waiting step {StepSequence} - {StepName}; not marking installation as succeeded",
                        installationId,
                        waitingStep.Sequence,
                        waitingStep.StepName);

                    return;
                }

                var runningStep = await _db.InstallationStepExecutions
                    .Where(x => x.InstallationId == installationId)
                    .Where(x => x.Status == InstallationStepStatuses.Running)
                    .OrderBy(x => x.Sequence)
                    .FirstOrDefaultAsync(cancellationToken);

                if (runningStep is not null)
                {
                    _logger.LogWarning(
                        "Installation {InstallationId} still has Running step {StepSequence} - {StepName}. The runner will not mark the installation as succeeded while another or interrupted execution is unresolved.",
                        installationId,
                        runningStep.Sequence,
                        runningStep.StepName);

                    return;
                }

                installation.Status = InstallationStatuses.Succeeded;
                installation.CompletedAtUtc = DateTime.UtcNow;
                installation.UpdatedAtUtc = DateTime.UtcNow;
                installation.LastError = null;

                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Installation {InstallationId} completed successfully",
                    installationId);
                await RecordInstallationAsync(
                    installationId,
                    eventCode: "installation.run.completed",
                    severity: MemDiagnosticSeverities.Information,
                    stage: "completed",
                    message: "The MEM installation workflow completed successfully.",
                    observed: new Dictionary<string, string?>
                    {
                        ["status"] = installation.Status
                    });

                return;
            }

            _logger.LogInformation(
                "Installation {InstallationId}: running step {StepSequence} - {StepName}",
                installationId,
                nextStep.Sequence,
                nextStep.StepName);

            var now = DateTime.UtcNow;

            nextStep.Status = InstallationStepStatuses.Running;
            nextStep.StartedAtUtc ??= now;
            nextStep.CompletedAtUtc = null;
            nextStep.ErrorMessage = null;
            nextStep.Message = "Running...";
            nextStep.AttemptCount += 1;

            installation.UpdatedAtUtc = now;

            await _db.SaveChangesAsync(cancellationToken);
            await RecordInstallationAsync(
                installationId,
                eventCode: "installation.step.started",
                severity: MemDiagnosticSeverities.Information,
                stage: nextStep.StepName,
                message: "An installation step started.",
                details: new Dictionary<string, string?>
                {
                    ["stepName"] = nextStep.StepName,
                    ["sequence"] = nextStep.Sequence.ToString()
                });

            var context = new InstallStepContext(
                InstallationId: installation.Id,
                StepId: nextStep.Id,
                StepName: nextStep.StepName,
                Sequence: nextStep.Sequence,
                ConfigJson: installation.FrozenConfigJson ?? installation.ConfigJson,
                AttemptNumber: nextStep.AttemptCount);

            if (_progressReporter is not null)
            {
                await _progressReporter.BeginStepAsync(context, cancellationToken);
            }

            InstallStepResult result;
            Exception? executionException = null;

            try
            {
                result = await _stepExecutor.ExecuteAsync(context, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                executionException = ex;
                _logger.LogError(
                    ex,
                    "Installation step {StepName} failed for installation {InstallationId}",
                    nextStep.StepName,
                    installationId);

                result = new InstallStepResult(
                    Succeeded: false,
                    Message: "Step failed.",
                    ErrorMessage: ex.Message);
            }

            var freshStep = await _db.InstallationStepExecutions
                .FirstAsync(x => x.Id == nextStep.Id, cancellationToken);

            var freshInstallation = await _db.Installations
                .FirstAsync(x => x.Id == installationId, cancellationToken);

            var stepStatus = result.StepStatus
                ?? (result.Succeeded
                    ? InstallationStepStatuses.Succeeded
                    : InstallationStepStatuses.Failed);

            freshStep.Status = stepStatus;
            freshStep.Message = result.Message;
            freshStep.ErrorMessage = result.ErrorMessage;
            freshStep.CompletedAtUtc = stepStatus == InstallationStepStatuses.WaitingForUser
                ? null
                : DateTime.UtcNow;

            freshInstallation.UpdatedAtUtc = DateTime.UtcNow;

            if (stepStatus == InstallationStepStatuses.WaitingForUser)
            {
                if (string.Equals(
                    freshStep.StepName,
                    InstallStepNames.CompleteSetupHandoff,
                    StringComparison.Ordinal))
                {
                    freshInstallation.Status = InstallationStatuses.Succeeded;
                    freshInstallation.LastError = null;
                    freshInstallation.CompletedAtUtc = DateTime.UtcNow;
                    freshInstallation.UpdatedAtUtc = DateTime.UtcNow;

                    await _db.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation(
                        "Installation {InstallationId} completed platform changes and is waiting for setup handoff acknowledgement.",
                        installationId);
                    await RecordInstallationAsync(
                        installationId,
                        eventCode: "installation.handoff.required",
                        severity: MemDiagnosticSeverities.Information,
                        stage: "handoff",
                        message: "The MEM platform installation completed and is waiting for the operator to finish setup handoff.",
                        details: new Dictionary<string, string?>
                        {
                            ["stepName"] = freshStep.StepName,
                            ["summary"] = result.Message
                        },
                        observed: new Dictionary<string, string?>
                        {
                            ["status"] = freshInstallation.Status,
                            ["handoff"] = "waiting"
                        });
                    await RecordInstallationAsync(
                        installationId,
                        eventCode: "installation.run.completed",
                        severity: MemDiagnosticSeverities.Information,
                        stage: "completed",
                        message: "The MEM installation workflow completed successfully and is ready for handoff.",
                        observed: new Dictionary<string, string?>
                        {
                            ["status"] = freshInstallation.Status,
                            ["handoff"] = "waiting"
                        });

                    if (_progressReporter is not null)
                    {
                        await _progressReporter.MarkWaitingForUserAsync(
                            context,
                            result.Message,
                            cancellationToken);
                    }

                    return;
                }

                freshInstallation.Status = InstallationStatuses.WaitingForUser;
                freshInstallation.LastError = result.ErrorMessage ?? result.Message;
                freshInstallation.CompletedAtUtc = null;

                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Installation {InstallationId} paused for operator action on step {StepName}: {Message}",
                    installationId,
                    freshStep.StepName,
                    result.Message);
                await RecordInstallationAsync(
                    installationId,
                    eventCode: "installation.step.waiting_for_user",
                    severity: MemDiagnosticSeverities.Warning,
                    stage: freshStep.StepName,
                    message: "The installation workflow is waiting for operator action.",
                    details: new Dictionary<string, string?>
                    {
                        ["stepName"] = freshStep.StepName,
                        ["summary"] = result.Message
                    },
                    observed: new Dictionary<string, string?>
                    {
                        ["status"] = freshInstallation.Status
                    });

                if (_progressReporter is not null)
                {
                    await _progressReporter.MarkWaitingForUserAsync(
                        context,
                        result.Message,
                        cancellationToken);
                }

                return;
            }

            if (!result.Succeeded)
            {
                freshInstallation.Status = InstallationStatuses.Failed;
                freshInstallation.LastError = result.ErrorMessage ?? result.Message;
                freshInstallation.CompletedAtUtc = DateTime.UtcNow;

                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogWarning(
                    "Installation {InstallationId} failed on step {StepName}: {ErrorMessage}",
                    installationId,
                    freshStep.StepName,
                    freshInstallation.LastError);
                await RecordInstallationAsync(
                    installationId,
                    eventCode: "installation.step.failed",
                    severity: MemDiagnosticSeverities.Error,
                    stage: freshStep.StepName,
                    message: "An installation step failed.",
                    createIncident: true,
                    exception: executionException,
                    details: new Dictionary<string, string?>
                    {
                        ["stepName"] = freshStep.StepName,
                        ["failureSummary"] = freshInstallation.LastError,
                        ["attemptCount"] = freshStep.AttemptCount.ToString()
                    },
                    observed: new Dictionary<string, string?>
                    {
                        ["status"] = freshInstallation.Status
                    });

                if (_progressReporter is not null)
                {
                    await _progressReporter.MarkFailedAsync(
                        context,
                        freshInstallation.LastError ?? result.Message,
                        cancellationToken);
                }

                return;
            }

            await _db.SaveChangesAsync(cancellationToken);

            if (_progressReporter is not null)
            {
                await _progressReporter.CompleteStepAsync(
                    context,
                    result.Message,
                    cancellationToken);
            }

            await RecordInstallationAsync(
                installationId,
                eventCode: "installation.step.completed",
                severity: MemDiagnosticSeverities.Information,
                stage: freshStep.StepName,
                message: "An installation step completed successfully.",
                details: new Dictionary<string, string?>
                {
                    ["stepName"] = freshStep.StepName,
                    ["attemptCount"] = freshStep.AttemptCount.ToString()
                },
                observed: new Dictionary<string, string?>
                {
                    ["status"] = freshStep.Status
                });
        }
    }

    private Task<MemDiagnosticWriteResult?> RecordInstallationAsync(
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
            Source: "api.install-runner",
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
                ? "Open Setup activity and Diagnostics before retrying the failed installation step."
                : null,
            Retryable: createIncident));
}
