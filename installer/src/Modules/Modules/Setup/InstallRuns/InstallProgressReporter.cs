using Microsoft.Extensions.Logging;

namespace Modules.Setup.InstallRuns;

/// <summary>
/// Writes safe, durable sub-phase progress for the currently executing
/// installation step. Reporter failures are deliberately non-fatal: durable
/// installation state in SQLite remains authoritative.
/// </summary>
public sealed class InstallProgressReporter(
    InstallProgressStore store,
    TimeProvider timeProvider,
    ILogger<InstallProgressReporter> logger)
{
    private const int SchemaVersion = 1;

    public Task BeginStepAsync(
        InstallStepContext context,
        CancellationToken cancellationToken) =>
        SafeUpdateAsync(
            context.InstallationId,
            current =>
            {
                var now = timeProvider.GetUtcNow();
                return new InstallProgressSnapshot(
                    SchemaVersion,
                    context.InstallationId,
                    context.StepId,
                    context.Sequence,
                    context.StepName,
                    context.AttemptNumber,
                    InstallProgressStatuses.Running,
                    PhaseCode: null,
                    PhaseStatus: null,
                    SafeSummary: $"Starting installation step '{context.StepName}'.",
                    StepStartedAtUtc: now,
                    LastActivityAtUtc: now,
                    Phases: []);
            },
            cancellationToken,
            "Installation progress started for {InstallationId} step {StepSequence} attempt {AttemptNumber}",
            context.InstallationId,
            context.Sequence,
            context.AttemptNumber);

    public Task BeginPhaseAsync(
        InstallStepContext context,
        string phaseCode,
        string safeSummary,
        CancellationToken cancellationToken) =>
        BeginCurrentPhaseAsync(
            context.InstallationId,
            phaseCode,
            safeSummary,
            cancellationToken);

    public Task BeginCurrentPhaseAsync(
        Guid installationId,
        string phaseCode,
        string safeSummary,
        CancellationToken cancellationToken) =>
        SafeUpdateAsync(
            installationId,
            current =>
            {
                if (current is null)
                {
                    return null;
                }

                var now = timeProvider.GetUtcNow();
                var phases = current.Phases.ToList();

                for (var index = 0; index < phases.Count; index++)
                {
                    var existing = phases[index];
                    if (string.Equals(existing.Code, phaseCode, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (string.Equals(existing.Status, InstallProgressStatuses.Running, StringComparison.Ordinal))
                    {
                        phases[index] = existing with
                        {
                            Status = InstallProgressStatuses.Succeeded,
                            CompletedAtUtc = now
                        };
                    }
                }

                var phaseIndex = phases.FindIndex(x =>
                    string.Equals(x.Code, phaseCode, StringComparison.Ordinal));

                if (phaseIndex >= 0)
                {
                    var existing = phases[phaseIndex];
                    phases[phaseIndex] = existing with
                    {
                        Status = InstallProgressStatuses.Running,
                        SafeSummary = safeSummary,
                        StartedAtUtc = existing.StartedAtUtc ?? now,
                        CompletedAtUtc = null
                    };
                }
                else
                {
                    phases.Add(new InstallProgressPhaseSnapshot(
                        phaseCode,
                        InstallProgressStatuses.Running,
                        safeSummary,
                        now,
                        CompletedAtUtc: null));
                }

                return current with
                {
                    StepStatus = InstallProgressStatuses.Running,
                    PhaseCode = phaseCode,
                    PhaseStatus = InstallProgressStatuses.Running,
                    SafeSummary = safeSummary,
                    LastActivityAtUtc = now,
                    Phases = phases
                };
            },
            cancellationToken,
            "Installation progress phase {PhaseCode} started for {InstallationId}",
            phaseCode,
            installationId);

    public Task HeartbeatCurrentAsync(
        Guid installationId,
        string phaseCode,
        string safeSummary,
        CancellationToken cancellationToken) =>
        SafeUpdateAsync(
            installationId,
            current =>
            {
                if (current is null)
                {
                    return null;
                }

                var now = timeProvider.GetUtcNow();
                var phases = current.Phases.ToList();
                var index = phases.FindIndex(x =>
                    string.Equals(x.Code, phaseCode, StringComparison.Ordinal));

                if (index >= 0)
                {
                    phases[index] = phases[index] with
                    {
                        Status = InstallProgressStatuses.Running,
                        SafeSummary = safeSummary,
                        StartedAtUtc = phases[index].StartedAtUtc ?? now,
                        CompletedAtUtc = null
                    };
                }

                return current with
                {
                    PhaseCode = phaseCode,
                    PhaseStatus = InstallProgressStatuses.Running,
                    SafeSummary = safeSummary,
                    LastActivityAtUtc = now,
                    Phases = phases
                };
            },
            cancellationToken,
            null);

    public Task HeartbeatCurrentAsync(
        Guid installationId,
        string safeSummary,
        CancellationToken cancellationToken) =>
        SafeUpdateAsync(
            installationId,
            current =>
            {
                if (current is null)
                {
                    return null;
                }

                var now = timeProvider.GetUtcNow();
                var phases = current.Phases.ToList();
                if (!string.IsNullOrWhiteSpace(current.PhaseCode))
                {
                    var index = phases.FindIndex(x =>
                        string.Equals(x.Code, current.PhaseCode, StringComparison.Ordinal));
                    if (index >= 0)
                    {
                        phases[index] = phases[index] with
                        {
                            SafeSummary = safeSummary
                        };
                    }
                }

                return current with
                {
                    SafeSummary = safeSummary,
                    LastActivityAtUtc = now,
                    Phases = phases
                };
            },
            cancellationToken,
            null);

    public Task SetPhaseRecoveryStatusAsync(
        Guid installationId,
        string phaseCode,
        bool recovered,
        string safeSummary,
        CancellationToken cancellationToken) =>
        SafeUpdateAsync(
            installationId,
            current =>
            {
                if (current is null)
                {
                    return null;
                }

                var now = timeProvider.GetUtcNow();
                var phases = current.Phases.ToList();
                var index = phases.FindIndex(x =>
                    string.Equals(x.Code, phaseCode, StringComparison.Ordinal));

                if (index < 0)
                {
                    return current;
                }

                var status = recovered
                    ? InstallProgressStatuses.Recovered
                    : InstallProgressStatuses.Recovering;

                phases[index] = phases[index] with
                {
                    Status = status,
                    SafeSummary = safeSummary,
                    StartedAtUtc = phases[index].StartedAtUtc ?? now,
                    CompletedAtUtc = recovered ? now : null
                };

                return current with
                {
                    PhaseStatus = string.Equals(current.PhaseCode, phaseCode, StringComparison.Ordinal)
                        ? status
                        : current.PhaseStatus,
                    SafeSummary = string.Equals(current.PhaseCode, phaseCode, StringComparison.Ordinal)
                        ? safeSummary
                        : current.SafeSummary,
                    LastActivityAtUtc = now,
                    Phases = phases
                };
            },
            cancellationToken,
            "Installation progress phase {PhaseCode} marked {ProgressStatus} for {InstallationId}",
            phaseCode,
            recovered ? InstallProgressStatuses.Recovered : InstallProgressStatuses.Recovering,
            installationId);

    public Task CompleteStepAsync(
        InstallStepContext context,
        string safeSummary,
        CancellationToken cancellationToken) =>
        MarkStepAsync(
            context.InstallationId,
            InstallProgressStatuses.Succeeded,
            safeSummary,
            cancellationToken);

    public Task MarkWaitingForUserAsync(
        InstallStepContext context,
        string safeSummary,
        CancellationToken cancellationToken) =>
        MarkStepAsync(
            context.InstallationId,
            InstallProgressStatuses.WaitingForUser,
            safeSummary,
            cancellationToken);

    public Task MarkFailedAsync(
        InstallStepContext context,
        string safeSummary,
        CancellationToken cancellationToken) =>
        MarkStepAsync(
            context.InstallationId,
            InstallProgressStatuses.Failed,
            safeSummary,
            cancellationToken);

    private Task MarkStepAsync(
        Guid installationId,
        string status,
        string safeSummary,
        CancellationToken cancellationToken) =>
        SafeUpdateAsync(
            installationId,
            current =>
            {
                if (current is null)
                {
                    return null;
                }

                var now = timeProvider.GetUtcNow();
                var phases = current.Phases.ToList();
                for (var index = 0; index < phases.Count; index++)
                {
                    var phase = phases[index];
                    var isRunning = string.Equals(
                        phase.Status,
                        InstallProgressStatuses.Running,
                        StringComparison.Ordinal);
                    var isRecovering = string.Equals(
                        phase.Status,
                        InstallProgressStatuses.Recovering,
                        StringComparison.Ordinal);
                    if (!isRunning && !isRecovering)
                    {
                        continue;
                    }

                    phases[index] = phase with
                    {
                        Status = status == InstallProgressStatuses.Succeeded && isRecovering
                            ? InstallProgressStatuses.Recovered
                            : status == InstallProgressStatuses.Succeeded
                                ? InstallProgressStatuses.Succeeded
                                : status,
                        CompletedAtUtc = status == InstallProgressStatuses.WaitingForUser
                            ? null
                            : now
                    };
                }

                return current with
                {
                    StepStatus = status,
                    PhaseStatus = status,
                    SafeSummary = safeSummary,
                    LastActivityAtUtc = now,
                    Phases = phases
                };
            },
            cancellationToken,
            "Installation progress reached terminal step status {ProgressStatus} for {InstallationId}",
            status,
            installationId);

    private async Task SafeUpdateAsync(
        Guid installationId,
        Func<InstallProgressSnapshot?, InstallProgressSnapshot?> update,
        CancellationToken cancellationToken,
        string? logMessage,
        params object?[] logArguments)
    {
        try
        {
            var updated = await store.UpdateAsync(
                installationId,
                current => update(current) ?? current ?? throw new InvalidOperationException(
                    "Installation progress snapshot has not been initialized."),
                cancellationToken);

            if (logMessage is not null)
            {
                logger.LogInformation(logMessage, logArguments);
            }

            _ = updated;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(
            "has not been initialized",
            StringComparison.Ordinal))
        {
            // A direct unit invocation may execute a nested service without the
            // durable runner having initialized progress. This is intentionally
            // a safe no-op.
            logger.LogDebug(
                "Installation progress update skipped because no snapshot exists for {InstallationId}",
                installationId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Installation progress could not be updated for {InstallationId}; installation execution continues with SQLite state authoritative",
                installationId);
        }
    }
}
