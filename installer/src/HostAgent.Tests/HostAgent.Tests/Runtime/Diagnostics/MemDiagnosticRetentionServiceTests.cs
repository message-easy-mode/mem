using HostAgent.Runtime.Diagnostics;
using Shared.Diagnostics;

namespace HostAgent.Tests.Runtime.Diagnostics;

public sealed class MemDiagnosticRetentionServiceTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        8,
        2,
        8,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task Retention_removes_old_non_active_files_and_preserves_current_file()
    {
        var options = new MemDiagnosticsOptions
        {
            RetentionDays = 14
        };
        var clock = new MemDiagnosticTestFixture.MutableTimeProvider(Now.AddDays(-20));
        using var fixture = new MemDiagnosticTestFixture(options, clock);

        await WriteAsync(fixture, "diagnostics.test.old");
        var oldPath = fixture.Store.CurrentActiveFilePath!;
        File.SetLastWriteTimeUtc(oldPath, Now.AddDays(-20).UtcDateTime);

        clock.SetUtcNow(Now);
        await WriteAsync(fixture, "diagnostics.test.current");
        var currentPath = fixture.Store.CurrentActiveFilePath!;
        File.SetLastWriteTimeUtc(currentPath, Now.UtcDateTime);

        await fixture.Retention.RunOnceAsync(CancellationToken.None);

        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(currentPath));
        var health = fixture.Health.GetHealth();
        Assert.Equal(1, health.LastRetentionDeletedFileCount);
        Assert.Null(health.LastRetentionErrorCode);
    }

    [Fact]
    public async Task Retention_enforces_total_bytes_oldest_first()
    {
        var options = new MemDiagnosticsOptions
        {
            RetentionDays = 365,
            MaximumTotalBytes = 2500,
            EventFileMaximumBytes = 64 * 1024,
            MaximumEventBytes = 4096
        };
        var clock = new MemDiagnosticTestFixture.MutableTimeProvider(Now.AddHours(-2));
        using var fixture = new MemDiagnosticTestFixture(options, clock);

        await WriteLargeAsync(fixture, "diagnostics.test.first");
        var firstPath = fixture.Store.CurrentActiveFilePath!;
        File.SetLastWriteTimeUtc(firstPath, Now.AddHours(-2).UtcDateTime);

        clock.SetUtcNow(Now.AddHours(-1));
        await WriteLargeAsync(fixture, "diagnostics.test.second");
        var secondPath = fixture.Store.CurrentActiveFilePath!;
        File.SetLastWriteTimeUtc(secondPath, Now.AddHours(-1).UtcDateTime);

        clock.SetUtcNow(Now);
        await WriteLargeAsync(fixture, "diagnostics.test.active");
        var activePath = fixture.Store.CurrentActiveFilePath!;
        File.SetLastWriteTimeUtc(activePath, Now.UtcDateTime);

        await fixture.Retention.RunOnceAsync(CancellationToken.None);

        Assert.False(File.Exists(firstPath));
        Assert.True(File.Exists(activePath));
        Assert.True(
            fixture.Store.EnumerateAllEventFiles().Sum(path => new FileInfo(path).Length) <=
            options.MaximumTotalBytes ||
            fixture.Store.EnumerateAllEventFiles().SequenceEqual([activePath]));
    }

    [Fact]
    public async Task Active_file_is_never_deleted_even_when_old_and_over_the_byte_limit()
    {
        var options = new MemDiagnosticsOptions
        {
            RetentionDays = 1,
            MaximumTotalBytes = 100,
            EventFileMaximumBytes = 64 * 1024,
            MaximumEventBytes = 4096
        };
        var clock = new MemDiagnosticTestFixture.MutableTimeProvider(Now.AddDays(-10));
        using var fixture = new MemDiagnosticTestFixture(options, clock);

        await WriteLargeAsync(fixture, "diagnostics.test.active_old");
        var activePath = fixture.Store.CurrentActiveFilePath!;
        File.SetLastWriteTimeUtc(activePath, Now.AddDays(-10).UtcDateTime);
        clock.SetUtcNow(Now);

        await fixture.Retention.RunOnceAsync(CancellationToken.None);

        Assert.True(File.Exists(activePath));
        Assert.Equal(0, fixture.Health.GetHealth().LastRetentionDeletedFileCount);
    }

    private static async Task WriteAsync(
        MemDiagnosticTestFixture fixture,
        string eventCode)
    {
        var result = await fixture.Writer.WriteAsync(new MemDiagnosticWriteRequest(
            Severity: MemDiagnosticSeverities.Information,
            EventCode: eventCode,
            Source: "host-agent",
            Feature: "diagnostics",
            Message: eventCode));
        Assert.True(result.Stored);
    }

    private static async Task WriteLargeAsync(
        MemDiagnosticTestFixture fixture,
        string eventCode)
    {
        var result = await fixture.Writer.WriteAsync(new MemDiagnosticWriteRequest(
            Severity: MemDiagnosticSeverities.Warning,
            EventCode: eventCode,
            Source: "host-agent",
            Feature: "diagnostics",
            Message: new string('x', 1500)));
        Assert.True(result.Stored);
    }
}
