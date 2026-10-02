using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HostAgent.Runtime.Filesystem;

/// <summary>
/// Fails startup for a containerized Control Plane whose configured host-data
/// paths do not resolve to the same physical host paths when reused as Docker
/// bind sources. This prevents sibling-container operations from discovering a
/// namespace mismatch only after they have already mutated the host.
/// </summary>
public sealed class HostDataPathParityStartupValidator(
    IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var validator = scope.ServiceProvider
            .GetRequiredService<HostDataPathParityValidator>();

        await validator.ValidateConfiguredRootsAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
