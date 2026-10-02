using HostAgent.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HostAgent.Tests.Services;

public sealed class CreateChatStackRuntimeOperationLifetimeTests
{
    [Fact]
    public void STACK_CREATE_REL_01B_request_abort_after_acceptance_does_not_cancel_server_operation()
    {
        var application = new FakeHostApplicationLifetime();
        using var request = new CancellationTokenSource();
        var lifetime = new CreateChatStackRuntimeOperationLifetime(
            application,
            NullLogger<CreateChatStackRuntimeOperationLifetime>.Instance,
            TimeSpan.FromMinutes(1));

        using var scope = lifetime.Begin(
            Guid.NewGuid(),
            "rel-01b-request-abort",
            request.Token);

        request.Cancel();

        Assert.True(scope.RequestAborted);
        Assert.False(scope.CancellationToken.IsCancellationRequested);
        Assert.False(scope.OperationTimeoutRequested);
        Assert.False(scope.ApplicationStoppingRequested);
    }

    [Fact]
    public void STACK_CREATE_REL_01B_already_aborted_request_is_rejected_before_server_operation_begins()
    {
        var application = new FakeHostApplicationLifetime();
        using var request = new CancellationTokenSource();
        request.Cancel();
        var lifetime = new CreateChatStackRuntimeOperationLifetime(
            application,
            NullLogger<CreateChatStackRuntimeOperationLifetime>.Instance,
            TimeSpan.FromMinutes(1));

        Assert.Throws<OperationCanceledException>(() =>
            lifetime.Begin(
                Guid.NewGuid(),
                "rel-01b-pre-aborted",
                request.Token));
    }

    [Fact]
    public void STACK_CREATE_REL_01B_application_shutdown_cancels_server_operation()
    {
        var application = new FakeHostApplicationLifetime();
        var lifetime = new CreateChatStackRuntimeOperationLifetime(
            application,
            NullLogger<CreateChatStackRuntimeOperationLifetime>.Instance,
            TimeSpan.FromMinutes(1));

        using var scope = lifetime.Begin(
            Guid.NewGuid(),
            "rel-01b-shutdown",
            CancellationToken.None);

        application.StopApplication();

        Assert.True(scope.CancellationToken.IsCancellationRequested);
        Assert.True(scope.ApplicationStoppingRequested);
        Assert.False(scope.OperationTimeoutRequested);
    }

    [Fact]
    public async Task STACK_CREATE_REL_01B_server_operation_timeout_remains_bounded()
    {
        var application = new FakeHostApplicationLifetime();
        var lifetime = new CreateChatStackRuntimeOperationLifetime(
            application,
            NullLogger<CreateChatStackRuntimeOperationLifetime>.Instance,
            TimeSpan.FromMilliseconds(25));

        using var scope = lifetime.Begin(
            Guid.NewGuid(),
            "rel-01b-timeout",
            CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Task.Delay(TimeSpan.FromSeconds(5), scope.CancellationToken));

        Assert.True(scope.CancellationToken.IsCancellationRequested);
        Assert.True(scope.OperationTimeoutRequested);
        Assert.False(scope.ApplicationStoppingRequested);
    }

    [Fact]
    public void STACK_CREATE_REL_01B_default_server_operation_timeout_is_twenty_minutes()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(20),
            CreateChatStackRuntimeOperationLifetime.DefaultOperationTimeout);
    }

    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication()
        {
            _stopping.Cancel();
        }
    }
}
