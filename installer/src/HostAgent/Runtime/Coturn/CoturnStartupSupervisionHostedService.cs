using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Runtime.Coturn;

/// <summary>
/// Supervises the startup transition for shared Coturn after Docker/runtime
/// authority becomes available. Before first-time Setup has authoritatively
/// verified Coturn, the service remains idle and periodically re-evaluates the
/// durable eligibility boundary without inspecting or mutating Coturn. Once
/// Coturn becomes expected, the normal bounded startup pass runs. If that pass
/// overlaps a Coturn mutation accepted by this API process, supervision waits
/// for the bounded operator operation to become terminal and then resumes.
/// It never competes with active Coturn mutation and never performs destructive
/// Coturn repair.
/// </summary>
public sealed class CoturnStartupSupervisionHostedService : BackgroundService
{
    internal static readonly TimeSpan StartupReadinessWindow = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan OperatorMutationResumeWindow = TimeSpan.FromMinutes(21);
    internal static readonly TimeSpan OperatorMutationRetryDelay = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan EligibilityRetryDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MemControlPlaneRuntimeContext _runtimeContext;
    private readonly CoturnStartupSupervisionState _state;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CoturnStartupSupervisionHostedService> _logger;
    private readonly TimeSpan _operatorMutationResumeWindow;
    private readonly TimeSpan _operatorMutationRetryDelay;
    private readonly TimeSpan _eligibilityRetryDelay;

    public CoturnStartupSupervisionHostedService(
        IServiceScopeFactory scopeFactory,
        MemControlPlaneRuntimeContext runtimeContext,
        CoturnStartupSupervisionState state,
        TimeProvider timeProvider,
        ILogger<CoturnStartupSupervisionHostedService> logger)
        : this(
            scopeFactory,
            runtimeContext,
            state,
            timeProvider,
            logger,
            OperatorMutationResumeWindow,
            OperatorMutationRetryDelay,
            EligibilityRetryDelay)
    {
    }

    internal CoturnStartupSupervisionHostedService(
        IServiceScopeFactory scopeFactory,
        MemControlPlaneRuntimeContext runtimeContext,
        CoturnStartupSupervisionState state,
        TimeProvider timeProvider,
        ILogger<CoturnStartupSupervisionHostedService> logger,
        TimeSpan operatorMutationResumeWindow,
        TimeSpan operatorMutationRetryDelay,
        TimeSpan? eligibilityRetryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(runtimeContext);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        if (operatorMutationResumeWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operatorMutationResumeWindow),
                operatorMutationResumeWindow,
                "The Coturn operator-mutation resume window must be greater than zero.");
        }

        if (operatorMutationRetryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operatorMutationRetryDelay),
                operatorMutationRetryDelay,
                "The Coturn operator-mutation retry delay must be greater than zero.");
        }

        var resolvedEligibilityRetryDelay =
            eligibilityRetryDelay ?? EligibilityRetryDelay;
        if (resolvedEligibilityRetryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(eligibilityRetryDelay),
                resolvedEligibilityRetryDelay,
                "The Coturn eligibility retry delay must be greater than zero.");
        }

        _scopeFactory = scopeFactory;
        _runtimeContext = runtimeContext;
        _state = state;
        _timeProvider = timeProvider;
        _logger = logger;
        _operatorMutationResumeWindow = operatorMutationResumeWindow;
        _operatorMutationRetryDelay = operatorMutationRetryDelay;
        _eligibilityRetryDelay = resolvedEligibilityRetryDelay;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        RunAsync(stoppingToken);

    internal async Task RunAsync(CancellationToken stoppingToken)
    {
        if (!CoturnStartupSupervisionPolicy.IsEnabled(_runtimeContext))
        {
            return;
        }

        if (!await WaitForStartupRuntimeReadinessAsync(stoppingToken))
        {
            return;
        }

        DateTimeOffset? operatorMutationResumeDeadline = null;
        var waitingForEligibility = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            CoturnStartupSupervisionExecutionOutcome outcome;
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider
                    .GetRequiredService<ICoturnStartupSupervisionProcessor>();
                outcome = await processor.ExecuteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal Control Plane shutdown.
                return;
            }
            catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
            {
                _logger.LogError(
                    ex,
                    "Coturn startup supervision hosted service terminated unexpectedly");
                _state.Set(_state.Read() with
                {
                    Status = CoturnStartupSupervisionStatuses.Failed,
                    Decision = CoturnStartupSupervisionDecisions.Unavailable,
                    ObservedAtUtc = _timeProvider.GetUtcNow(),
                    Detail = "Coturn startup supervision terminated unexpectedly. Review Diagnostics before retrying."
                });
                return;
            }

            if (outcome == CoturnStartupSupervisionExecutionOutcome.Finished)
            {
                if (waitingForEligibility)
                {
                    _logger.LogInformation(
                        "Coturn startup supervision resumed in the current API process after first-time installation made shared Coturn an expected managed service");
                }

                return;
            }

            if (outcome == CoturnStartupSupervisionExecutionOutcome.DeferredNotExpected)
            {
                if (!waitingForEligibility)
                {
                    _logger.LogInformation(
                        "Coturn startup supervision is idle until first-time installation authoritatively verifies shared Coturn");
                }

                waitingForEligibility = true;
                operatorMutationResumeDeadline = null;

                try
                {
                    await Task.Delay(_eligibilityRetryDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Normal Control Plane shutdown while first-time Setup has
                    // not yet made Coturn an expected service.
                    return;
                }

                continue;
            }

            if (waitingForEligibility)
            {
                _logger.LogInformation(
                    "Shared Coturn became expected in the current API process; startup supervision is resuming");
                waitingForEligibility = false;
            }

            operatorMutationResumeDeadline ??=
                _timeProvider.GetUtcNow().Add(_operatorMutationResumeWindow);

            if (_timeProvider.GetUtcNow() >= operatorMutationResumeDeadline.Value)
            {
                _logger.LogWarning(
                    "Coturn startup supervision remained deferred because a current-process operator mutation did not reach terminal state within the bounded resume window");
                _state.Set(_state.Read() with
                {
                    ObservedAtUtc = _timeProvider.GetUtcNow(),
                    Detail = "The overlapping operator-requested Coturn mutation did not reach a terminal state within the bounded startup-supervision resume window. MEM did not compete with it; review the active operation and Diagnostics."
                });
                return;
            }

            try
            {
                await Task.Delay(_operatorMutationRetryDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal Control Plane shutdown while waiting for the operator
                // operation to become terminal.
                return;
            }
        }
    }

    private async Task<bool> WaitForStartupRuntimeReadinessAsync(
        CancellationToken stoppingToken)
    {
        var deadline = _timeProvider.GetUtcNow().Add(StartupReadinessWindow);
        Exception? lastTransientException = null;

        while (!stoppingToken.IsCancellationRequested &&
               _timeProvider.GetUtcNow() < deadline)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var runtime = scope.ServiceProvider
                    .GetRequiredService<ICoturnStartupSupervisionRuntime>();
                var current = await runtime.InspectAsync(stoppingToken);

                if (!CoturnStartupSupervisionPolicy.CouldBeTransientDuringStartup(current))
                {
                    break;
                }

                _state.Set(_state.Read() with
                {
                    Status = CoturnStartupSupervisionStatuses.Waiting,
                    Decision = CoturnStartupSupervisionPolicy.Evaluate(current),
                    ObservedAtUtc = _timeProvider.GetUtcNow(),
                    Detail = "Coturn startup supervision is waiting for Docker inspection and the MEM gateway-network attachment to settle."
                });
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
            {
                lastTransientException = ex;
                _state.Set(_state.Read() with
                {
                    Status = CoturnStartupSupervisionStatuses.Waiting,
                    Decision = CoturnStartupSupervisionDecisions.Unavailable,
                    ObservedAtUtc = _timeProvider.GetUtcNow(),
                    Detail = "Coturn startup supervision is waiting for Docker/runtime authority to become available."
                });
            }

            try
            {
                await Task.Delay(RetryDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return false;
            }
        }

        if (lastTransientException is not null)
        {
            _logger.LogDebug(
                lastTransientException,
                "Coturn startup supervision observed transient startup authority failures before the bounded readiness window ended");
        }

        return !stoppingToken.IsCancellationRequested;
    }
}
