using HostAgent.Runtime.Databases;
using HostAgent.Runtime.Stacks.Destroy;

namespace HostAgent.Tests.Services;

public sealed class RuntimeStackDestroyCancellationPolicyTests
{
    [Fact]
    public void STACK_DESTROY_REL_01A_force_never_swallows_operation_cancellation()
    {
        Assert.False(RuntimeStackDestroyService.CanContinueAfterStepFailure(
            force: true,
            new OperationCanceledException("server-owned destroy cancelled")));
    }

    [Fact]
    public void STACK_DESTROY_REL_01A_force_can_retain_warning_semantics_for_non_cancellation_failure()
    {
        Assert.True(RuntimeStackDestroyService.CanContinueAfterStepFailure(
            force: true,
            new InvalidOperationException("resource was already inconsistent")));
    }

    [Fact]
    public void STACK_DESTROY_REL_01A_without_force_propagates_step_failure()
    {
        Assert.False(RuntimeStackDestroyService.CanContinueAfterStepFailure(
            force: false,
            new InvalidOperationException("container removal failed")));
    }

    [Fact]
    public void STACK_DESTROY_REL_01A_destroy_idempotency_key_is_stable_when_supplied()
    {
        var stackId = Guid.NewGuid();
        var request = new RuntimeStackDestroyRequest(
            IdempotencyKey: "destroy-stack-stable-key");

        Assert.Equal(
            "destroy-stack-stable-key",
            RuntimeStackDestroyService.ResolveIdempotencyKey(request, stackId));
    }

    [Fact]
    public void STACK_DESTROY_REL_01A_database_force_never_swallows_operation_cancellation()
    {
        Assert.False(RuntimeStackDatabaseService.CanContinueAfterDatabaseDropFailure(
            force: true,
            new OperationCanceledException("server-owned database drop cancelled")));
    }

    [Fact]
    public void STACK_DESTROY_REL_01A_database_force_retains_non_cancellation_warning_semantics()
    {
        Assert.True(RuntimeStackDatabaseService.CanContinueAfterDatabaseDropFailure(
            force: true,
            new InvalidOperationException("database cleanup was already inconsistent")));
    }
}
