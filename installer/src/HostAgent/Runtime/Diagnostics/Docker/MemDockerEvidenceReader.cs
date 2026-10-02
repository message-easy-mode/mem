using Microsoft.Extensions.Logging;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Diagnostics.Docker;

internal sealed class MemDockerEvidenceReader(
    MemDockerEvidenceOptions options,
    IMemDiagnosticResourceResolver resolver,
    IMemDockerEvidenceSource source,
    MemDockerLogDecoder decoder,
    MemDockerLogSanitizer sanitizer,
    TimeProvider timeProvider,
    ILogger<MemDockerEvidenceReader> logger)
    : IMemDockerEvidenceReader
{
    public async Task<MemDockerEvidenceReadResult> ReadForIncidentAsync(
        string incidentId,
        IReadOnlyCollection<MemDiagnosticEvent> incidentEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        ArgumentNullException.ThrowIfNull(incidentEvents);

        if (!options.Enabled)
        {
            return Unavailable(MemDiagnosticCodes.DockerEvidenceDisabled);
        }

        var matchingEvents = incidentEvents
            .Where(@event => string.Equals(
                @event.IncidentId,
                incidentId,
                StringComparison.Ordinal))
            .ToArray();
        if (matchingEvents.Length == 0)
        {
            return Unavailable(MemDiagnosticCodes.DockerEvidenceResourceNotResolved);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            var resolved = await resolver.ResolveAsync(
                incidentId,
                matchingEvents,
                timeout.Token);
            if (resolved is null)
            {
                return Unavailable(MemDiagnosticCodes.DockerEvidenceResourceNotResolved);
            }

            var container = await source.InspectAsync(
                resolved.ContainerReference,
                timeout.Token);
            if (container is null)
            {
                return Unavailable(MemDiagnosticCodes.DockerEvidenceContainerNotFound);
            }

            await using var logStream = await source.OpenLogsAsync(
                container.Reference,
                options.DefaultTailLines,
                timeout.Token);
            var decoded = await decoder.DecodeAsync(
                logStream,
                container.Tty,
                options.MaximumRawBytes,
                options.MaximumFrameBytes,
                timeout.Token);
            var sanitized = sanitizer.Sanitize(
                decoded.Content,
                options.DefaultTailLines,
                options.MaximumCharacters,
                resolved.ExactSecrets,
                decoded.Truncated);
            var warnings = new List<string>
            {
                MemDiagnosticCodes.DockerEvidenceSensitiveOperationalMetadata
            };
            if (decoded.Truncated || sanitized.Truncated)
            {
                warnings.Add(MemDiagnosticCodes.DockerEvidenceTruncated);
            }

            return new MemDockerEvidenceReadResult(
                Available: true,
                Evidence: new MemDockerEvidence(
                    resolved.Resource,
                    timeProvider.GetUtcNow(),
                    new MemDockerEvidenceContainer(
                        resolved.LogicalName,
                        container.ObservedState,
                        container.ExitCode,
                        container.Health,
                        container.StartedAtUtc,
                        container.FinishedAtUtc,
                        container.RestartCount,
                        container.Image),
                    new MemDockerEvidenceLogTail(
                        options.DefaultTailLines,
                        sanitized.ReturnedLines,
                        options.MaximumCharacters,
                        sanitized.Content,
                        sanitized.Truncated,
                        sanitized.RedactionsApplied),
                    warnings),
                WarningCode: null,
                Warnings: warnings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "MEM Docker evidence collection timed out. IncidentId={IncidentId}",
                incidentId);
            return Unavailable(MemDiagnosticCodes.DockerEvidenceTimedOut);
        }
        catch (Exception exception) when (
            exception is not StackOverflowException and not OutOfMemoryException)
        {
            logger.LogWarning(
                exception,
                "MEM Docker evidence collection failed. IncidentId={IncidentId}",
                incidentId);
            return Unavailable(MemDiagnosticCodes.DockerEvidenceReadFailed);
        }
    }

    private static MemDockerEvidenceReadResult Unavailable(string warningCode) =>
        new(
            Available: false,
            Evidence: null,
            WarningCode: warningCode,
            Warnings: [warningCode]);
}
