using Mem.Migrate.Application.Workflow;
using Mem.Migrate.Web.Workflow;

namespace Mem.Migrate.Web.Tests;

public sealed class SourcePackageDownloadCoordinatorTests
{
    [Fact]
    public async Task Active_download_blocks_deletion_until_response_lease_is_released()
    {
        var lifecycle = new FakeLifecycleService();
        var coordinator = new SourcePackageDownloadCoordinator(lifecycle);

        var lease = await coordinator.BeginAsync(
            "source-20260728-000000Z-11111111111111111111111111111111",
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SourceWorkflowConflictException>(
            () => coordinator.DeleteAsync(
                "source-20260728-000000Z-11111111111111111111111111111111",
                CancellationToken.None));

        Assert.Contains(
            "download is active",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(lifecycle.DeleteCalled);

        lease.Dispose();

        await coordinator.DeleteAsync(
            "source-20260728-000000Z-11111111111111111111111111111111",
            CancellationToken.None);

        Assert.True(lifecycle.DeleteCalled);
    }

    private sealed class FakeLifecycleService : ISourcePackageLifecycleService
    {
        public bool DeleteCalled { get; private set; }

        public Task<SourcePackageDownloadDescriptor> GetPackageDownloadAsync(
            string workflowId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SourcePackageDownloadDescriptor(
                FullPath: "/private/package.memmigration.zip.age",
                FileName: "package.memmigration.zip.age",
                ContentType: "application/octet-stream",
                SizeBytes: 1024,
                Sha256: new string('a', 64),
                LastModifiedUtc: DateTimeOffset.UtcNow));

        public Task<SourcePackageReportDescriptor> GetPackageReportAsync(
            string workflowId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SourceWorkflowView> DeletePackageAsync(
            string workflowId,
            CancellationToken cancellationToken)
        {
            DeleteCalled = true;
            return Task.FromResult<SourceWorkflowView>(null!);
        }
    }
}
