using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Modules.Shared.RuntimeImages;

public sealed class ApprovedPostgresRuntimeStartupReporter(
    IServiceScopeFactory scopeFactory,
    ILogger<ApprovedPostgresRuntimeStartupReporter> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider
            .GetRequiredService<IApprovedPostgresRuntimeProvider>();
        var policy = provider.GetPolicy();

        logger.LogInformation(
            "MEM approved PostgreSQL runtime configured: {ApprovedReference}; major {MajorVersion}; expected version {ExpectedVersion}; install pull {AllowInstallPull}; operational pull {AllowOperationalPull}",
            policy.ApprovedReference,
            policy.RequiredMajorVersion,
            policy.ExpectedVersion,
            policy.AllowInstallPull,
            policy.AllowOperationalPull);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
