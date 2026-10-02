using Mem.Migrate.Web.Assessment;

namespace Mem.Migrate.Web.Tests;

public sealed class SourceStackSelectionStoreTests
{
    [Fact]
    public void Selection_is_scoped_to_the_assessment_that_produced_it()
    {
        var store = new SourceStackSelectionStore();

        store.Select("assessment-01", "stack-01");

        Assert.Equal("stack-01", store.GetSelectedSourceStackId("assessment-01"));
        Assert.Null(store.GetSelectedSourceStackId("assessment-02"));

        store.Clear();
        Assert.Null(store.GetSelectedSourceStackId("assessment-01"));
    }
}
