namespace Mem.Migrate.Web.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset utcNow;

    public ManualTimeProvider(DateTimeOffset utcNow)
    {
        this.utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow() => utcNow;

    public void Advance(TimeSpan amount) => utcNow += amount;
}
