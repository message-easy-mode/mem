namespace Mem.Migrate.Core.Rehearsal;

public interface IRehearsalArtifactService
{
    Task<RehearsalArtifactReport> ExportAsync(RehearsalArtifactOptions options, CancellationToken cancellationToken);
}
