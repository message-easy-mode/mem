namespace Modules.Shared.Domains.Dns;

public interface IDnsChallengeProvider
{
    string ProviderName { get; }

    Task<DnsChallengeResult> UpsertTxtChallengeAsync(
        DnsChallengeRequest request,
        CancellationToken cancellationToken);

    Task<DnsChallengeResult> DeleteTxtChallengeAsync(
        DnsChallengeRequest request,
        CancellationToken cancellationToken);
}