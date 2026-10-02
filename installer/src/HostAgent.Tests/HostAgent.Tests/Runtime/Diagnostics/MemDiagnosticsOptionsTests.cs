using HostAgent.Runtime.Diagnostics;

namespace HostAgent.Tests.Runtime.Diagnostics;

public sealed class MemDiagnosticsOptionsTests
{
    [Fact]
    public void Default_options_are_bounded_and_valid()
    {
        var options = new MemDiagnosticsOptions();

        MemDiagnosticsOptionsValidator.ThrowIfInvalid(options);

        Assert.True(options.Enabled);
        Assert.Equal(14, options.RetentionDays);
        Assert.Equal(256L * 1024L * 1024L, options.MaximumTotalBytes);
        Assert.Equal(8L * 1024L * 1024L, options.EventFileMaximumBytes);
        Assert.Equal(64 * 1024, options.MaximumEventBytes);
        Assert.Equal(168, options.MaximumQueryWindowHours);
        Assert.Equal(200, options.MaximumPageSize);
        Assert.Equal(1024L * 1024L * 1024L, options.LowDiskWarningBytes);
        Assert.Equal(256L * 1024L * 1024L, options.CriticalDiskWarningBytes);
    }

    [Fact]
    public void Event_size_cannot_exceed_file_size()
    {
        var options = new MemDiagnosticsOptions
        {
            MaximumEventBytes = 128 * 1024,
            EventFileMaximumBytes = 64 * 1024
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MemDiagnosticsOptionsValidator.ThrowIfInvalid(options));

        Assert.Contains("MaximumEventBytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Enabled_store_requires_a_root_path()
    {
        var options = new MemDiagnosticsOptions
        {
            RootPath = " "
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MemDiagnosticsOptionsValidator.ThrowIfInvalid(options));

        Assert.Contains("RootPath", exception.Message, StringComparison.Ordinal);
    }
    [Fact]
    public void Invalid_root_path_is_rejected_with_a_stable_configuration_error()
    {
        var options = new MemDiagnosticsOptions
        {
            RootPath = "invalid\0path"
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MemDiagnosticsOptionsValidator.ThrowIfInvalid(options));

        Assert.Contains("RootPath is invalid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Event_file_size_cannot_exceed_total_store_limit()
    {
        var options = new MemDiagnosticsOptions
        {
            EventFileMaximumBytes = 2 * 1024 * 1024,
            MaximumTotalBytes = 1024 * 1024
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MemDiagnosticsOptionsValidator.ThrowIfInvalid(options));

        Assert.Contains("EventFileMaximumBytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Filesystem_root_and_inverted_disk_thresholds_are_rejected()
    {
        var rootOptions = new MemDiagnosticsOptions
        {
            RootPath = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))!
        };
        var thresholdOptions = new MemDiagnosticsOptions
        {
            LowDiskWarningBytes = 128L * 1024L * 1024L,
            CriticalDiskWarningBytes = 256L * 1024L * 1024L
        };

        var rootException = Assert.Throws<InvalidOperationException>(() =>
            MemDiagnosticsOptionsValidator.ThrowIfInvalid(rootOptions));
        var thresholdException = Assert.Throws<InvalidOperationException>(() =>
            MemDiagnosticsOptionsValidator.ThrowIfInvalid(thresholdOptions));

        Assert.Contains("filesystem root", rootException.Message, StringComparison.Ordinal);
        Assert.Contains("CriticalDiskWarningBytes", thresholdException.Message, StringComparison.Ordinal);
    }

}
