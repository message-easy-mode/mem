namespace Mem.Migrate.Core.Conversion;

public interface IPrivateCandidateService
{
    Task<CandidateReport> VerifyAsync(CandidateOptions options, CancellationToken cancellationToken);
}
