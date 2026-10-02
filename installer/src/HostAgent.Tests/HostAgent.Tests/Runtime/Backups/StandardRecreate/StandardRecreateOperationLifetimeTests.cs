using HostAgent.Runtime.Backups.StandardRecreate;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HostAgent.Tests.Runtime.Backups.StandardRecreate;

public sealed class StandardRecreateOperationLifetimeTests
{
    [Fact]
    public void PLATFORM_TURN_01D_CORR_02_request_abort_after_acceptance_does_not_cancel_restore()
    {
        var application = new FakeHostApplicationLifetime();
        using var request = new CancellationTokenSource();
        var lifetime = new StandardRecreateOperationLifetime(
            application,
            NullLogger<StandardRecreateOperationLifetime>.Instance,
            TimeSpan.FromMinutes(1));

        using var scope = lifetime.BeginAfterAcceptance(
            "restore-request-abort",
            "recreate-request-abort",
            Guid.NewGuid(),
            request.Token);

        request.Cancel();

        Assert.True(scope.RequestAborted);
        Assert.False(scope.CancellationToken.IsCancellationRequested);
        Assert.False(scope.OperationTimeoutRequested);
        Assert.False(scope.ApplicationStoppingRequested);
    }

    [Fact]
    public void PLATFORM_TURN_01D_CORR_02_already_aborted_request_after_acceptance_still_gets_server_lifetime()
    {
        var application = new FakeHostApplicationLifetime();
        using var request = new CancellationTokenSource();
        request.Cancel();
        var lifetime = new StandardRecreateOperationLifetime(
            application,
            NullLogger<StandardRecreateOperationLifetime>.Instance,
            TimeSpan.FromMinutes(1));

        using var scope = lifetime.BeginAfterAcceptance(
            "restore-pre-aborted",
            "recreate-pre-aborted",
            Guid.NewGuid(),
            request.Token);

        Assert.True(scope.RequestAborted);
        Assert.False(scope.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void PLATFORM_TURN_01D_CORR_02_application_shutdown_cancels_server_owned_restore()
    {
        var application = new FakeHostApplicationLifetime();
        var lifetime = new StandardRecreateOperationLifetime(
            application,
            NullLogger<StandardRecreateOperationLifetime>.Instance,
            TimeSpan.FromMinutes(1));

        using var scope = lifetime.BeginAfterAcceptance(
            "restore-shutdown",
            "recreate-shutdown",
            Guid.NewGuid(),
            CancellationToken.None);

        application.StopApplication();

        Assert.True(scope.CancellationToken.IsCancellationRequested);
        Assert.True(scope.ApplicationStoppingRequested);
        Assert.False(scope.OperationTimeoutRequested);
    }

    [Fact]
    public async Task PLATFORM_TURN_01D_CORR_02_server_owned_restore_timeout_remains_bounded()
    {
        var application = new FakeHostApplicationLifetime();
        var lifetime = new StandardRecreateOperationLifetime(
            application,
            NullLogger<StandardRecreateOperationLifetime>.Instance,
            TimeSpan.FromMilliseconds(25));

        using var scope = lifetime.BeginAfterAcceptance(
            "restore-timeout",
            "recreate-timeout",
            Guid.NewGuid(),
            CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Task.Delay(TimeSpan.FromSeconds(5), scope.CancellationToken));

        Assert.True(scope.CancellationToken.IsCancellationRequested);
        Assert.True(scope.OperationTimeoutRequested);
        Assert.False(scope.ApplicationStoppingRequested);
    }

    [Fact]
    public void PLATFORM_TURN_01D_CORR_02_default_timeout_is_twenty_minutes()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(20),
            StandardRecreateOperationLifetime.DefaultOperationTimeout);
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
