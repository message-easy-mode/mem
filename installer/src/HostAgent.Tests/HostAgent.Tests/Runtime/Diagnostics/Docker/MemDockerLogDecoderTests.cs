using System.Buffers.Binary;
using System.Text;
using HostAgent.Runtime.Diagnostics.Docker;

namespace HostAgent.Tests.Runtime.Diagnostics.Docker;

public sealed class MemDockerLogDecoderTests
{
    [Fact]
    public async Task Decodes_multiplexed_stdout_and_stderr_in_frame_order()
    {
        var bytes = Frame(1, "stdout line\n")
            .Concat(Frame(2, "stderr line\n"))
            .ToArray();
        var decoder = new MemDockerLogDecoder();

        var result = await decoder.DecodeAsync(
            new MemoryStream(bytes),
            tty: false,
            maximumRawBytes: 4096,
            maximumFrameBytes: 2048,
            CancellationToken.None);

        Assert.True(result.Multiplexed);
        Assert.Equal(2, result.FrameCount);
        Assert.False(result.Truncated);
        Assert.Equal("stdout line\nstderr line\n", result.Content);
    }

    [Fact]
    public async Task Reads_tty_output_without_treating_text_as_a_frame_header()
    {
        var decoder = new MemDockerLogDecoder();
        var source = Encoding.UTF8.GetBytes("tty output\nsecond line\n");

        var result = await decoder.DecodeAsync(
            new MemoryStream(source),
            tty: true,
            maximumRawBytes: 4096,
            maximumFrameBytes: 2048,
            CancellationToken.None);

        Assert.False(result.Multiplexed);
        Assert.Equal(0, result.FrameCount);
        Assert.False(result.Truncated);
        Assert.Equal("tty output\nsecond line\n", result.Content);
    }

    [Fact]
    public async Task Falls_back_to_raw_output_when_the_stream_is_not_multiplexed()
    {
        var decoder = new MemDockerLogDecoder();
        var source = Encoding.UTF8.GetBytes("ordinary non-tty output\n");

        var result = await decoder.DecodeAsync(
            new MemoryStream(source),
            tty: false,
            maximumRawBytes: 4096,
            maximumFrameBytes: 2048,
            CancellationToken.None);

        Assert.False(result.Multiplexed);
        Assert.Equal("ordinary non-tty output\n", result.Content);
    }

    [Fact]
    public async Task Treats_a_short_non_multiplexed_stream_as_complete_raw_output()
    {
        var decoder = new MemDockerLogDecoder();
        var source = Encoding.UTF8.GetBytes("ok\n");

        var result = await decoder.DecodeAsync(
            new MemoryStream(source),
            tty: false,
            maximumRawBytes: 4096,
            maximumFrameBytes: 2048,
            CancellationToken.None);

        Assert.False(result.Multiplexed);
        Assert.False(result.Truncated);
        Assert.Equal("ok\n", result.Content);
    }

    [Fact]
    public async Task Marks_output_truncated_when_the_raw_byte_limit_is_reached()
    {
        var decoder = new MemDockerLogDecoder();
        var source = Frame(1, new string('x', 100));

        var result = await decoder.DecodeAsync(
            new MemoryStream(source),
            tty: false,
            maximumRawBytes: 32,
            maximumFrameBytes: 32,
            CancellationToken.None);

        Assert.True(result.Truncated);
        Assert.Equal(32, result.Content.Length);
    }

    private static byte[] Frame(byte streamType, string content)
    {
        var payload = Encoding.UTF8.GetBytes(content);
        var result = new byte[8 + payload.Length];
        result[0] = streamType;
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4, 4), (uint)payload.Length);
        payload.AsSpan().CopyTo(result.AsSpan(8));
        return result;
    }
}
