using System.Buffers.Binary;
using System.Text;

namespace HostAgent.Runtime.Diagnostics.Docker;

internal sealed record MemDockerDecodedLog(
    string Content,
    bool Truncated,
    int FrameCount,
    bool Multiplexed);

internal sealed class MemDockerLogDecoder
{
    private const int HeaderLength = 8;

    public async Task<MemDockerDecodedLog> DecodeAsync(
        Stream stream,
        bool tty,
        int maximumRawBytes,
        int maximumFrameBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maximumRawBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRawBytes));
        }

        if (maximumFrameBytes < 1 || maximumFrameBytes > maximumRawBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFrameBytes));
        }

        return tty
            ? await ReadRawAsync(stream, maximumRawBytes, cancellationToken)
            : await ReadMultiplexedAsync(
                stream,
                maximumRawBytes,
                maximumFrameBytes,
                cancellationToken);
    }

    private static async Task<MemDockerDecodedLog> ReadRawAsync(
        Stream stream,
        int maximumRawBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(Math.Min(maximumRawBytes, 64 * 1024));
        var buffer = new byte[8192];
        var truncated = false;

        while (output.Length < maximumRawBytes)
        {
            var remaining = maximumRawBytes - (int)output.Length;
            var read = await stream.ReadAsync(
                buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (output.Length >= maximumRawBytes)
        {
            var probe = new byte[1];
            truncated = await stream.ReadAsync(probe, cancellationToken) > 0;
        }

        return new MemDockerDecodedLog(
            DecodeUtf8(output.ToArray()),
            truncated,
            FrameCount: 0,
            Multiplexed: false);
    }

    private static async Task<MemDockerDecodedLog> ReadMultiplexedAsync(
        Stream stream,
        int maximumRawBytes,
        int maximumFrameBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(Math.Min(maximumRawBytes, 64 * 1024));
        var header = new byte[HeaderLength];
        var frameCount = 0;
        var truncated = false;

        while (output.Length < maximumRawBytes)
        {
            var headerRead = await ReadUpToAsync(
                stream,
                header,
                HeaderLength,
                cancellationToken);
            if (headerRead == 0)
            {
                break;
            }

            if (headerRead < HeaderLength || !LooksLikeMultiplexHeader(header))
            {
                await output.WriteAsync(
                    header.AsMemory(0, Math.Min(headerRead, maximumRawBytes - (int)output.Length)),
                    cancellationToken);
                var remaining = maximumRawBytes - (int)output.Length;
                if (remaining > 0)
                {
                    truncated |= await CopyBoundedAsync(
                        stream,
                        output,
                        remaining,
                        cancellationToken);
                }

                return new MemDockerDecodedLog(
                    DecodeUtf8(output.ToArray()),
                    truncated,
                    frameCount,
                    Multiplexed: false);
            }

            var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4, 4));
            if (payloadLength == 0)
            {
                frameCount++;
                continue;
            }

            if (payloadLength > maximumFrameBytes)
            {
                truncated = true;
            }

            var bytesToRead = (int)Math.Min(
                payloadLength,
                (uint)Math.Min(maximumFrameBytes, maximumRawBytes - (int)output.Length));
            var payload = new byte[Math.Min(bytesToRead, 64 * 1024)];
            var remainingPayload = bytesToRead;

            while (remainingPayload > 0)
            {
                var read = await stream.ReadAsync(
                    payload.AsMemory(0, Math.Min(payload.Length, remainingPayload)),
                    cancellationToken);
                if (read == 0)
                {
                    truncated = true;
                    remainingPayload = 0;
                    break;
                }

                await output.WriteAsync(payload.AsMemory(0, read), cancellationToken);
                remainingPayload -= read;
            }

            frameCount++;

            if ((uint)bytesToRead < payloadLength || output.Length >= maximumRawBytes)
            {
                truncated = true;
                break;
            }
        }

        return new MemDockerDecodedLog(
            DecodeUtf8(output.ToArray()),
            truncated,
            frameCount,
            Multiplexed: true);
    }

    private static bool LooksLikeMultiplexHeader(ReadOnlySpan<byte> header) =>
        header.Length == HeaderLength &&
        header[0] is 0 or 1 or 2 or 3 &&
        header[1] == 0 &&
        header[2] == 0 &&
        header[3] == 0;

    private static async Task<int> ReadUpToAsync(
        Stream stream,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < count)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(total, count - total),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static async Task<bool> CopyBoundedAsync(
        Stream stream,
        Stream output,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var copied = 0;
        while (copied < maximumBytes)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(0, Math.Min(buffer.Length, maximumBytes - copied)),
                cancellationToken);
            if (read == 0)
            {
                return false;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;
        }

        var probe = new byte[1];
        return await stream.ReadAsync(probe, cancellationToken) > 0;
    }

    private static string DecodeUtf8(byte[] bytes) =>
        new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: false)
        .GetString(bytes);
}
