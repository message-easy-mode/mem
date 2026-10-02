using HostAgent.Runtime.Backups.Workspace.PrivateTest;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HostAgent.Tests.Runtime.Backups.Workspace.PrivateTest;

public sealed class RestoreWorkspacePrivateTestOperationLifetimeTests
{
    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_04_request_abort_after_acceptance_does_not_cancel_private_test()
    {
        var application = new FakeHostApplicationLifetime();
        using var request = new CancellationTokenSource();
        var lifetime = new RestoreWorkspacePrivateTestOperationLifetime(
            application,
            NullLogger<RestoreWorkspacePrivateTestOperationLifetime>.Instance,
            TimeSpan.FromMinutes(1));

        using var scope = lifetime.BeginAfterAcceptance(
            "restore-private-test-request-abort",
            Guid.NewGuid(),
            request.Token);

        request.Cancel();

        Assert.True(scope.RequestAborted);
        Assert.False(scope.CancellationToken.IsCancellationRequested);
        Assert.False(scope.OperationTimeoutRequested);
        Assert.False(scope.ApplicationStoppingRequested);
    }

    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_04_already_aborted_request_after_acceptance_still_gets_server_lifetime()
    {
        var application = new FakeHostApplicationLifetime();
        using var request = new CancellationTokenSource();
        request.Cancel();
        var lifetime = new RestoreWorkspacePrivateTestOperationLifetime(
            application,
            NullLogger<RestoreWorkspacePrivateTestOperationLifetime>.Instance,
            TimeSpan.FromMinutes(1));

        using var scope = lifetime.BeginAfterAcceptance(
            "restore-private-test-pre-aborted",
            Guid.NewGuid(),
            request.Token);

        Assert.True(scope.RequestAborted);
        Assert.False(scope.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_04_application_shutdown_cancels_private_test()
    {
        var application = new FakeHostApplicationLifetime();
        var lifetime = new RestoreWorkspacePrivateTestOperationLifetime(
            application,
            NullLogger<RestoreWorkspacePrivateTestOperationLifetime>.Instance,
            TimeSpan.FromMinutes(1));

        using var scope = lifetime.BeginAfterAcceptance(
            "restore-private-test-shutdown",
            Guid.NewGuid(),
            CancellationToken.None);

        application.StopApplication();

        Assert.True(scope.CancellationToken.IsCancellationRequested);
        Assert.True(scope.ApplicationStoppingRequested);
        Assert.False(scope.OperationTimeoutRequested);
    }

    [Fact]
    public async Task PLATFORM_TURN_01E_CLEANROOM_CORR_04_private_test_timeout_remains_bounded()
    {
        var application = new FakeHostApplicationLifetime();
        var lifetime = new RestoreWorkspacePrivateTestOperationLifetime(
            application,
            NullLogger<RestoreWorkspacePrivateTestOperationLifetime>.Instance,
            TimeSpan.FromMilliseconds(25));

        using var scope = lifetime.BeginAfterAcceptance(
            "restore-private-test-timeout",
            Guid.NewGuid(),
            CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Task.Delay(TimeSpan.FromSeconds(5), scope.CancellationToken));

        Assert.True(scope.OperationTimeoutRequested);
        Assert.False(scope.ApplicationStoppingRequested);
    }

    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_04_default_timeout_is_twenty_minutes()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(20),
            RestoreWorkspacePrivateTestOperationLifetime.DefaultOperationTimeout);
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
