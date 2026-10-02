using Mem.Migrate.Application.Assessment;

namespace Mem.Migrate.Web.Tests;

public sealed class SourcePreflightServiceTests
{
    [Fact]
    public async Task Ready_host_can_run_a_first_assessment()
    {
        using var temp = new TemporaryDirectory();
        var service = new SourcePreflightService(
            new FakeAssessmentService(),
            Path.Combine(temp.Path, "work"),
            Path.Combine(temp.Path, "artifacts"),
            resolveExecutable: command => $"/usr/bin/{command}",
            isSupportedOperatingSystem: () => true);

        var result = await service.RunAsync(false, CancellationToken.None);

        Assert.True(result.CanRunAssessment);
        Assert.Equal("ready-with-warnings", result.Status);
        Assert.Contains(result.Checks, check => check.Code == "source-assessment" && check.Status == "not-run");
        Assert.DoesNotContain(result.Checks, check => check.Status == "blocked");
    }

    [Fact]
    public async Task Missing_docker_blocks_assessment()
    {
        using var temp = new TemporaryDirectory();
        var service = new SourcePreflightService(
            new FakeAssessmentService(),
            Path.Combine(temp.Path, "work"),
            Path.Combine(temp.Path, "artifacts"),
            resolveExecutable: command => command == "age" ? "/usr/bin/age" : null,
            isSupportedOperatingSystem: () => true);

        var result = await service.RunAsync(false, CancellationToken.None);

        Assert.False(result.CanRunAssessment);
        Assert.Equal("blocked", result.Status);
        Assert.Contains(result.Checks, check => check.Code == "docker-command" && check.Status == "blocked");
    }

    private sealed class FakeAssessmentService : ISourceAssessmentApplicationService
    {
        public Task<SourceAssessmentView> RunAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SourceAssessmentView?> GetLatestAsync(CancellationToken cancellationToken) =>
            Task.FromResult<SourceAssessmentView?>(null);

        public Task<SourceAssessmentReport?> GetLatestReportAsync(CancellationToken cancellationToken) =>
            Task.FromResult<SourceAssessmentReport?>(null);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mem-web-preflight-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
