using HostAgent.Runtime.Backups.Catalog;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HostAgent.Tests.Runtime.Backups.Catalog;

public sealed class BackupCatalogDeleteOperationLifetimeTests
{
    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_05B_request_abort_after_acceptance_does_not_cancel_permanent_delete()
    {
        var application = new FakeHostApplicationLifetime();
        using var request = new CancellationTokenSource();
        var lifetime = new BackupCatalogDeleteOperationLifetime(
            application,
            NullLogger<BackupCatalogDeleteOperationLifetime>.Instance,
            TimeSpan.FromMinutes(1));

        using var scope = lifetime.BeginAfterAcceptance(
            "bkp-request-abort",
            request.Token);

        request.Cancel();

        Assert.True(scope.RequestAborted);
        Assert.False(scope.CancellationToken.IsCancellationRequested);
        Assert.False(scope.OperationTimeoutRequested);
        Assert.False(scope.ApplicationStoppingRequested);
    }

    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_05B_already_aborted_request_after_acceptance_still_gets_server_lifetime()
    {
        var application = new FakeHostApplicationLifetime();
        using var request = new CancellationTokenSource();
        request.Cancel();
        var lifetime = new BackupCatalogDeleteOperationLifetime(
            application,
            NullLogger<BackupCatalogDeleteOperationLifetime>.Instance,
            TimeSpan.FromMinutes(1));

        using var scope = lifetime.BeginAfterAcceptance(
            "bkp-pre-aborted",
            request.Token);

        Assert.True(scope.RequestAborted);
        Assert.False(scope.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_05B_application_shutdown_cancels_permanent_delete()
    {
        var application = new FakeHostApplicationLifetime();
        var lifetime = new BackupCatalogDeleteOperationLifetime(
            application,
            NullLogger<BackupCatalogDeleteOperationLifetime>.Instance,
            TimeSpan.FromMinutes(1));

        using var scope = lifetime.BeginAfterAcceptance(
            "bkp-shutdown",
            CancellationToken.None);

        application.StopApplication();

        Assert.True(scope.CancellationToken.IsCancellationRequested);
        Assert.True(scope.ApplicationStoppingRequested);
        Assert.False(scope.OperationTimeoutRequested);
    }

    [Fact]
    public async Task PLATFORM_TURN_01E_CLEANROOM_CORR_05B_server_owned_permanent_delete_timeout_remains_bounded()
    {
        var application = new FakeHostApplicationLifetime();
        var lifetime = new BackupCatalogDeleteOperationLifetime(
            application,
            NullLogger<BackupCatalogDeleteOperationLifetime>.Instance,
            TimeSpan.FromMilliseconds(25));

        using var scope = lifetime.BeginAfterAcceptance(
            "bkp-timeout",
            CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Task.Delay(TimeSpan.FromSeconds(5), scope.CancellationToken));

        Assert.True(scope.CancellationToken.IsCancellationRequested);
        Assert.True(scope.OperationTimeoutRequested);
        Assert.False(scope.ApplicationStoppingRequested);
    }

    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_05B_default_timeout_is_five_minutes()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(5),
            BackupCatalogDeleteOperationLifetime.DefaultOperationTimeout);
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
