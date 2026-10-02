using System.Security.Cryptography;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Infrastructure.Docker;

/// <summary>
/// Docker-API implementation of a deliberately narrow one-shot command runner.
/// The container has no network, mounts, ports or caller-supplied environment.
/// Sensitive input is written only to the attached stdin stream and its
/// temporary UTF-8 buffer is cleared immediately afterwards.
/// </summary>
public sealed class DockerIsolatedStandardInputRunner(DockerClient client)
    : IDockerIsolatedStandardInputRunner
{
    public async Task<DockerIsolatedStandardInputResult> RunAsync(
        string image,
        string containerName,
        IReadOnlyList<string> command,
        ReadOnlyMemory<char> standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentNullException.ThrowIfNull(command);
        if (command.Count == 0)
        {
            throw new ArgumentException("At least one container command argument is required.", nameof(command));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        string? containerId = null;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var operationToken = timeoutSource.Token;

        try
        {
            var created = await client.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Name = containerName,
                    Image = image,
                    Cmd = command.ToArray(),
                    AttachStdin = true,
                    AttachStdout = true,
                    AttachStderr = true,
                    OpenStdin = true,
                    StdinOnce = true,
                    Tty = false,
                    HostConfig = new HostConfig
                    {
                        AutoRemove = true,
                        NetworkMode = "none"
                    }
                },
                operationToken);

            containerId = created.ID;

            using var stream = await client.Containers.AttachContainerAsync(
                containerId,
                false,
                new ContainerAttachParameters
                {
                    Stream = true,
                    Stdin = true,
                    Stdout = true,
                    Stderr = true
                },
                operationToken);

            var started = await client.Containers.StartContainerAsync(
                containerId,
                new ContainerStartParameters(),
                operationToken);
            if (!started)
            {
                return Failure("Docker did not start the isolated one-shot container.");
            }

            var outputTask = stream.ReadOutputToEndAsync(operationToken);
            var waitTask = client.Containers.WaitContainerAsync(
                containerId,
                operationToken);

            var inputBytes = EncodeInputLine(standardInput.Span);
            try
            {
                await stream.WriteAsync(
                    inputBytes,
                    0,
                    inputBytes.Length,
                    operationToken);
                stream.CloseWrite();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(inputBytes);
            }

            await Task.WhenAll(outputTask, waitTask);
            var (stdout, stderr) = await outputTask;
            var wait = await waitTask;

            var exitCode = wait.StatusCode <= int.MaxValue
                ? (int)wait.StatusCode
                : -1;

            return new DockerIsolatedStandardInputResult(
                exitCode,
                stdout.Trim(),
                stderr.Trim(),
                TimedOut: false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DockerIsolatedStandardInputResult(
                -1,
                string.Empty,
                "Docker one-shot command timed out.",
                TimedOut: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Failure("Docker one-shot command failed.");
        }
        finally
        {
            timeoutSource.Cancel();
            if (!string.IsNullOrWhiteSpace(containerId))
            {
                await TryRemoveAsync(containerId);
            }
        }
    }

    private async Task TryRemoveAsync(string containerId)
    {
        try
        {
            using var cleanupSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await client.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters
                {
                    Force = true,
                    RemoveVolumes = false
                },
                cleanupSource.Token);
        }
        catch
        {
            // AutoRemove handles the normal success path. This is best-effort
            // cleanup for timeout, cancellation, daemon errors or attach faults.
        }
    }

    private static byte[] EncodeInputLine(ReadOnlySpan<char> input)
    {
        var byteCount = Encoding.UTF8.GetByteCount(input);
        var buffer = new byte[byteCount + 1];
        _ = Encoding.UTF8.GetBytes(input, buffer);
        buffer[byteCount] = (byte)'\n';
        return buffer;
    }

    private static DockerIsolatedStandardInputResult Failure(string safeMessage) =>
        new(
            -1,
            string.Empty,
            safeMessage,
            TimedOut: false);
}
