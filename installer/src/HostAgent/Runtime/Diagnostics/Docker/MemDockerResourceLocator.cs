using Microsoft.Extensions.Logging;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Diagnostics.Docker;

internal sealed class MemDockerResourceLocator(
    MemDockerEvidenceOptions options,
    IMemDiagnosticResourceResolver resolver,
    IMemDockerEvidenceSource source,
    ILogger<MemDockerResourceLocator> logger)
    : IMemDockerResourceLocator
{
    public async Task<MemDockerResourceLocation?> LocateForIncidentAsync(
        string incidentId,
        IReadOnlyCollection<MemDiagnosticEvent> incidentEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        ArgumentNullException.ThrowIfNull(incidentEvents);

        var matchingEvents = incidentEvents
            .Where(@event => string.Equals(
                @event.IncidentId,
                incidentId,
                StringComparison.Ordinal))
            .ToArray();
        if (matchingEvents.Length == 0)
        {
            return null;
        }

        return await LocateResolvedAsync(
            token => resolver.ResolveAsync(incidentId, matchingEvents, token),
            incidentId,
            cancellationToken);
    }

    public async Task<MemDockerResourceLocation?> LocateAsync(
        MemDiagnosticResource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return await LocateResolvedAsync(
            token => resolver.ResolveResourceAsync(resource, token),
            $"{resource.Kind}:{resource.Id}",
            cancellationToken);
    }

    private async Task<MemDockerResourceLocation?> LocateResolvedAsync(
        Func<CancellationToken, Task<MemResolvedDockerResource?>> resolve,
        string resourceReference,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            var resolved = await resolve(timeout.Token);
            if (resolved is null)
            {
                return null;
            }

            var container = await source.InspectAsync(
                resolved.ContainerReference,
                timeout.Token);
            if (container is null || string.IsNullOrWhiteSpace(container.Reference))
            {
                return null;
            }

            return new MemDockerResourceLocation(
                resolved.Resource,
                container.Reference,
                resolved.LogicalName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "MEM Docker resource location timed out. Resource={Resource}",
                resourceReference);
            return null;
        }
        catch (Exception exception) when (
            exception is not StackOverflowException and not OutOfMemoryException)
        {
            logger.LogWarning(
                exception,
                "MEM Docker resource location failed. Resource={Resource}",
                resourceReference);
            return null;
        }
    }
}
