using HostAgent.Runtime.Stacks.Destroy;
using Microsoft.Extensions.Hosting;

namespace HostAgent.Tests.Services;

public sealed class RuntimeStackDestroyOperationLifetimeTests
{
    [Fact]
    public void STACK_DESTROY_REL_01A_application_shutdown_cancels_server_owned_destroy()
    {
        var application = new FakeHostApplicationLifetime();
        var lifetime = new RuntimeStackDestroyOperationLifetime(
            application,
            TimeSpan.FromMinutes(1));

        using var scope = lifetime.Begin();
        application.StopApplication();

        Assert.True(scope.CancellationToken.IsCancellationRequested);
        Assert.True(scope.ApplicationStoppingRequested);
        Assert.False(scope.OperationTimeoutRequested);
    }

    [Fact]
    public async Task STACK_DESTROY_REL_01A_server_owned_destroy_remains_bounded()
    {
        var application = new FakeHostApplicationLifetime();
        var lifetime = new RuntimeStackDestroyOperationLifetime(
            application,
            TimeSpan.FromMilliseconds(25));

        using var scope = lifetime.Begin();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Task.Delay(TimeSpan.FromSeconds(5), scope.CancellationToken));

        Assert.True(scope.CancellationToken.IsCancellationRequested);
        Assert.True(scope.OperationTimeoutRequested);
        Assert.False(scope.ApplicationStoppingRequested);
    }

    [Fact]
    public void STACK_DESTROY_REL_01A_default_destroy_timeout_is_fifteen_minutes()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(15),
            RuntimeStackDestroyOperationLifetime.DefaultOperationTimeout);
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
