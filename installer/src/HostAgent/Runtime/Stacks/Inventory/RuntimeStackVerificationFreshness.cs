namespace HostAgent.Runtime.Stacks.Inventory;

/// <summary>
/// Classifies whether persisted Runtime Stack verification evidence is still recent enough
/// to support an operator-facing healthy/setting-up status. This does not probe live runtime
/// state; it only judges the freshness of the last persisted verification observation.
/// </summary>
public static class RuntimeStackVerificationFreshness
{
    public const string Current = "current";
    public const string Stale = "stale";

    // Runtime-stack readiness is not a continuous health probe. A 72-hour window keeps
    // recent evidence useful while preventing multi-day-old verification from reading as current health.
    public static readonly TimeSpan MaximumAge = TimeSpan.FromHours(72);
    private static readonly TimeSpan MaximumFutureClockSkew = TimeSpan.FromMinutes(5);

    public static string Classify(
        DateTimeOffset lastVerifiedAtUtc,
        DateTimeOffset observedAtUtc) =>
        IsStale(lastVerifiedAtUtc, observedAtUtc)
            ? Stale
            : Current;

    public static bool IsStale(
        DateTimeOffset lastVerifiedAtUtc,
        DateTimeOffset observedAtUtc)
    {
        if (lastVerifiedAtUtc > observedAtUtc.Add(MaximumFutureClockSkew))
        {
            return true;
        }

        return observedAtUtc - lastVerifiedAtUtc > MaximumAge;
    }
}
