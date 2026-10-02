using System.Globalization;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace HostAgent.Runtime.Diagnostics.Docker;

internal sealed record MemDockerSourceContainer(
    string Reference,
    string Name,
    string ObservedState,
    long? ExitCode,
    string? Health,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    long RestartCount,
    string? Image,
    bool Tty);

internal interface IMemDockerEvidenceSource
{
    Task<MemDockerSourceContainer?> InspectAsync(
        string containerReference,
        CancellationToken cancellationToken);

    Task<Stream> OpenLogsAsync(
        string containerReference,
        int tailLines,
        CancellationToken cancellationToken);
}

internal sealed class DockerMemDockerEvidenceSource(DockerClient client)
    : IMemDockerEvidenceSource
{
    public async Task<MemDockerSourceContainer?> InspectAsync(
        string containerReference,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(containerReference))
        {
            return null;
        }

        try
        {
            var inspect = await client.Containers.InspectContainerAsync(
                containerReference,
                cancellationToken);
            var state = inspect.State;

            return new MemDockerSourceContainer(
                Reference: inspect.ID ?? containerReference,
                Name: (inspect.Name ?? string.Empty).TrimStart('/'),
                ObservedState: string.IsNullOrWhiteSpace(state?.Status)
                    ? "unknown"
                    : state.Status,
                ExitCode: state is null ? null : state.ExitCode,
                Health: string.IsNullOrWhiteSpace(state?.Health?.Status)
                    ? null
                    : state.Health.Status,
                StartedAtUtc: ParseDockerTimestamp(state?.StartedAt),
                FinishedAtUtc: ParseDockerTimestamp(state?.FinishedAt),
                RestartCount: inspect.RestartCount,
                Image: FirstNonEmpty(inspect.Image, inspect.Config?.Image),
                Tty: inspect.Config?.Tty ?? false);
        }
        catch (DockerApiException exception) when (
            exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Stream> OpenLogsAsync(
        string containerReference,
        int tailLines,
        CancellationToken cancellationToken)
    {
        var stream = await client.Containers.GetContainerLogsAsync(
            containerReference,
            new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = false,
                Timestamps = false,
                Tail = tailLines.ToString(CultureInfo.InvariantCulture)
            },
            cancellationToken);

        return stream;
    }

    private static DateTimeOffset? ParseDockerTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith("0001-01-01", StringComparison.Ordinal))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
