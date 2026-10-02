using Modules.Setup;

namespace Api.IntegrationTests.Setup;

public sealed class SetupUtcDateTimeTests
{
    [Fact]
    public void CORR_01_projects_unspecified_persisted_dates_as_UTC_offsets()
    {
        var persisted = new DateTime(2026, 8, 14, 6, 32, 29, DateTimeKind.Unspecified);

        var projected = SetupUtcDateTime.ToOffset(persisted);

        Assert.Equal(TimeSpan.Zero, projected.Offset);
        Assert.Equal(2026, projected.Year);
        Assert.Equal(6, projected.Hour);
        Assert.EndsWith("+00:00", projected.ToString("O"));
    }

    [Fact]
    public void CORR_01_preserves_null_optional_dates()
    {
        Assert.Null(SetupUtcDateTime.ToOffset((DateTime?)null));
    }
}
