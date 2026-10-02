using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HostAgent.Tests.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;

public sealed class PrivateStagingDestroyOperationLifetimeTests
{
    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_04_CORR_03_request_abort_after_acceptance_does_not_cancel_retirement()
    {
        var application = new FakeHostApplicationLifetime();
        using var request = new CancellationTokenSource();
        var lifetime = new PrivateStagingDestroyOperationLifetime(
            application,
            NullLogger<PrivateStagingDestroyOperationLifetime>.Instance,
            TimeSpan.FromMinutes(1));

        using var scope = lifetime.BeginAfterAcceptance(
            "private-staging-request-abort",
            request.Token);

        request.Cancel();

        Assert.True(scope.RequestAborted);
        Assert.False(scope.CancellationToken.IsCancellationRequested);
        Assert.False(scope.OperationTimeoutRequested);
        Assert.False(scope.ApplicationStoppingRequested);
    }

    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_04_CORR_03_already_aborted_request_still_gets_server_owned_retirement_lifetime()
    {
        var application = new FakeHostApplicationLifetime();
        using var request = new CancellationTokenSource();
        request.Cancel();

        var lifetime = new PrivateStagingDestroyOperationLifetime(
            application,
            NullLogger<PrivateStagingDestroyOperationLifetime>.Instance,
            TimeSpan.FromMinutes(1));

        using var scope = lifetime.BeginAfterAcceptance(
            "private-staging-pre-aborted",
            request.Token);

        Assert.True(scope.RequestAborted);
        Assert.False(scope.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_04_CORR_03_application_shutdown_cancels_retirement()
    {
        var application = new FakeHostApplicationLifetime();
        var lifetime = new PrivateStagingDestroyOperationLifetime(
            application,
            NullLogger<PrivateStagingDestroyOperationLifetime>.Instance,
            TimeSpan.FromMinutes(1));

        using var scope = lifetime.BeginAfterAcceptance(
            "private-staging-shutdown",
            CancellationToken.None);

        application.StopApplication();

        Assert.True(scope.CancellationToken.IsCancellationRequested);
        Assert.True(scope.ApplicationStoppingRequested);
        Assert.False(scope.OperationTimeoutRequested);
    }

    [Fact]
    public async Task PLATFORM_TURN_01E_CLEANROOM_CORR_04_CORR_03_retirement_timeout_remains_bounded()
    {
        var application = new FakeHostApplicationLifetime();
        var lifetime = new PrivateStagingDestroyOperationLifetime(
            application,
            NullLogger<PrivateStagingDestroyOperationLifetime>.Instance,
            TimeSpan.FromMilliseconds(25));

        using var scope = lifetime.BeginAfterAcceptance(
            "private-staging-timeout",
            CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Task.Delay(TimeSpan.FromSeconds(5), scope.CancellationToken));

        Assert.True(scope.OperationTimeoutRequested);
        Assert.False(scope.ApplicationStoppingRequested);
    }

    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_04_CORR_03_default_retirement_timeout_is_five_minutes()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(5),
            PrivateStagingDestroyOperationLifetime.DefaultOperationTimeout);
    }

    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_04_CORR_03_partial_destroy_summary_is_not_terminal()
    {
        var partial = new PrivateStagingDestroySummary(
            DestroyedAtUtc: DateTimeOffset.UtcNow,
            SynapseContainerRemoved: true,
            PostgresContainerRemoved: true,
            NetworkRemoved: false,
            WorkspaceRemoved: true,
            Warnings: ["network cleanup interrupted"],
            ElementContainerRemoved: true);

        Assert.False(PrivateStagingService.IsDestroyComplete(partial));
    }

    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_04_CORR_03_complete_destroy_summary_is_terminal()
    {
        var complete = new PrivateStagingDestroySummary(
            DestroyedAtUtc: DateTimeOffset.UtcNow,
            SynapseContainerRemoved: true,
            PostgresContainerRemoved: true,
            NetworkRemoved: true,
            WorkspaceRemoved: true,
            Warnings: [],
            ElementContainerRemoved: true);

        Assert.True(PrivateStagingService.IsDestroyComplete(complete));
    }

    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication() => _stopping.Cancel();
    }
}
