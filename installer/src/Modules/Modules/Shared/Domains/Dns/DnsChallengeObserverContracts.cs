namespace Modules.Shared.Domains.Dns;

public interface IAuthoritativeDnsChallengeObserver
{
    Task<DnsChallengeObservationResult> CheckDesecTxtAsync(
        string fullRecordName,
        string expectedValue,
        CancellationToken cancellationToken);

    Task<DnsChallengeObservationResult> WaitForDesecTxtAsync(
        string fullRecordName,
        string expectedValue,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? heartbeat = null);
}

public interface IPublicDnsChallengeObserver
{
    Task<DnsChallengeObservationResult> CheckTxtAsync(
        string fullRecordName,
        string expectedValue,
        CancellationToken cancellationToken);

    Task<DnsChallengeObservationResult> WaitForTxtAsync(
        string fullRecordName,
        string expectedValue,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? heartbeat = null);
}
