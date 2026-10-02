using Mem.Migrate.Web.Security;

namespace Mem.Migrate.Web.Tests;

public sealed class SourceAssistantSessionStoreTests
{
    [Fact]
    public void Session_uses_an_opaque_cookie_and_sliding_idle_timeout()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero));
        var store = new SourceAssistantSessionStore(
            time,
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(4));
        var created = store.Create();

        Assert.NotEqual(created.CookieToken, created.CsrfToken);
        Assert.True(store.TryGet(created.CookieToken, out var initial));
        Assert.NotNull(initial);

        time.Advance(TimeSpan.FromMinutes(20));
        Assert.True(store.TryGet(created.CookieToken, out var refreshed));
        Assert.Equal(time.GetUtcNow() + TimeSpan.FromMinutes(30), refreshed!.ExpiresAtUtc);

        time.Advance(TimeSpan.FromMinutes(31));
        Assert.False(store.TryGet(created.CookieToken, out _));
    }

    [Fact]
    public void Sliding_refresh_never_extends_past_the_absolute_session_lifetime()
    {
        var startedAt = new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);
        var time = new ManualTimeProvider(startedAt);
        var store = new SourceAssistantSessionStore(
            time,
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(90));
        var created = store.Create();

        time.Advance(TimeSpan.FromMinutes(20));
        Assert.True(store.TryGet(created.CookieToken, out var first));
        Assert.Equal(startedAt + TimeSpan.FromMinutes(50), first!.ExpiresAtUtc);

        time.Advance(TimeSpan.FromMinutes(20));
        Assert.True(store.TryGet(created.CookieToken, out var second));
        Assert.Equal(startedAt + TimeSpan.FromMinutes(70), second!.ExpiresAtUtc);

        time.Advance(TimeSpan.FromMinutes(20));
        Assert.True(store.TryGet(created.CookieToken, out var capped));
        Assert.Equal(startedAt + TimeSpan.FromMinutes(90), capped!.ExpiresAtUtc);
        Assert.Equal(startedAt + TimeSpan.FromMinutes(90), capped.AbsoluteExpiresAtUtc);

        time.Advance(TimeSpan.FromMinutes(29));
        Assert.True(store.TryGet(created.CookieToken, out var finalRefresh));
        Assert.Equal(startedAt + TimeSpan.FromMinutes(90), finalRefresh!.ExpiresAtUtc);

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(store.TryGet(created.CookieToken, out _));
    }

    [Fact]
    public void Revoked_session_cannot_be_reused()
    {
        var store = new SourceAssistantSessionStore(
            TimeProvider.System,
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(4));
        var created = store.Create();

        store.Revoke(created.CookieToken);

        Assert.False(store.TryGet(created.CookieToken, out _));
    }
}
