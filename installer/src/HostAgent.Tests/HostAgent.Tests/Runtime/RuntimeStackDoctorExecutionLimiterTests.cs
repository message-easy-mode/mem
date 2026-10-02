using HostAgent.Runtime.Stacks.Diagnostics;

namespace HostAgent.Tests.Runtime;

public sealed class RuntimeStackDoctorExecutionLimiterTests
{
    [Fact]
    public async Task Doctor_execution_has_a_hard_deadline_even_if_the_operation_ignores_cancellation()
    {
        var limiter = new RuntimeStackDoctorExecutionLimiter(TimeSpan.FromMilliseconds(50));
        var neverCompletesUntilReleased = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var exception = await Assert.ThrowsAsync<RuntimeStackDoctorTimeoutException>(async () =>
            await limiter.ExecuteAsync(
                _ => neverCompletesUntilReleased.Task,
                CancellationToken.None));

        Assert.Equal(TimeSpan.FromMilliseconds(50), exception.Timeout);
        neverCompletesUntilReleased.TrySetResult(0);
    }

    [Fact]
    public async Task Doctor_timeout_cancels_the_token_passed_to_the_operation()
    {
        var limiter = new RuntimeStackDoctorExecutionLimiter(TimeSpan.FromMilliseconds(50));
        var cancellationObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await Assert.ThrowsAsync<RuntimeStackDoctorTimeoutException>(async () =>
            await limiter.ExecuteAsync(
                async token =>
                {
                    using var registration = token.Register(() => cancellationObserved.TrySetResult(true));
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return 0;
                },
                CancellationToken.None));

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Request_cancellation_is_not_misreported_as_a_Doctor_timeout()
    {
        var limiter = new RuntimeStackDoctorExecutionLimiter(TimeSpan.FromSeconds(5));
        using var requestCancellation = new CancellationTokenSource();
        requestCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await limiter.ExecuteAsync(
                async token =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return 0;
                },
                requestCancellation.Token));
    }

    [Fact]
    public void Default_Doctor_execution_deadline_is_two_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), RuntimeStackDoctorExecutionLimiter.DefaultTimeout);
    }
}
