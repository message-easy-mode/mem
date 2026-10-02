using Mem.Migrate.Core.Fingerprints;

namespace Mem.Migrate.UnitTests;

public sealed class SelectedSourceIdentityTests
{
    [Fact]
    public void Identity_is_stable_when_only_mutable_live_source_evidence_changes_elsewhere()
    {
        var stackId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var input = new SelectedSourceIdentityInput(
            ProductName: "MatrixEasyMode",
            ProductVersion: "0.1.0",
            SourceStackId: stackId,
            Slug: "tester",
            MatrixServerName: "matrix.example.test",
            MatrixPublicHost: "matrix.example.test",
            ElementPublicHost: "chat.example.test");

        var first = SelectedSourceIdentity.Compute(input);
        var second = SelectedSourceIdentity.Compute(input);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
    }

    [Theory]
    [InlineData("other-tester", "matrix.example.test", "matrix.example.test", "chat.example.test")]
    [InlineData("tester", "matrix-other.example.test", "matrix.example.test", "chat.example.test")]
    [InlineData("tester", "matrix.example.test", "matrix-other.example.test", "chat.example.test")]
    [InlineData("tester", "matrix.example.test", "matrix.example.test", "chat-other.example.test")]
    public void Identity_changes_when_selected_stack_identity_changes(
        string slug,
        string matrixServerName,
        string matrixPublicHost,
        string elementPublicHost)
    {
        var stackId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var baseline = SelectedSourceIdentity.Compute(new SelectedSourceIdentityInput(
            "MatrixEasyMode",
            "0.1.0",
            stackId,
            "tester",
            "matrix.example.test",
            "matrix.example.test",
            "chat.example.test"));
        var changed = SelectedSourceIdentity.Compute(new SelectedSourceIdentityInput(
            "MatrixEasyMode",
            "0.1.0",
            stackId,
            slug,
            matrixServerName,
            matrixPublicHost,
            elementPublicHost));

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void Identity_changes_for_a_different_stack_id()
    {
        var first = SelectedSourceIdentity.Compute(new SelectedSourceIdentityInput(
            "MatrixEasyMode",
            "0.1.0",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "tester",
            "matrix.example.test",
            "matrix.example.test",
            "chat.example.test"));
        var second = SelectedSourceIdentity.Compute(new SelectedSourceIdentityInput(
            "MatrixEasyMode",
            "0.1.0",
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "tester",
            "matrix.example.test",
            "matrix.example.test",
            "chat.example.test"));

        Assert.NotEqual(first, second);
    }
}
