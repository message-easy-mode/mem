using Mem.Localization;

namespace Mem.Localization.Tests.Localization;

public sealed class MemStructuredMessageTests
{
    [Fact]
    public void Create_returns_a_stable_code_with_sorted_named_scalar_arguments()
    {
        var message = MemStructuredMessage.Create(
            "restore.private-test.started",
            new Dictionary<string, object?>
            {
                ["catalogEntryId"] = "bkp_test_001",
                ["restoreSessionId"] = "20260703-071259Z-example",
                ["warningCount"] = 2
            });

        Assert.Equal("restore.private-test.started", message.Code);
        Assert.Equal(
            new[] { "catalogEntryId", "restoreSessionId", "warningCount" },
            message.Arguments.Keys);
        Assert.Equal("bkp_test_001", message.Arguments["catalogEntryId"]);
        Assert.Equal(2, message.Arguments["warningCount"]);
    }

    [Fact]
    public void Create_rejects_a_malformed_code()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            MemStructuredMessage.Create("Restore Attempt Not Found"));

        Assert.Equal("code", exception.ParamName);
    }

    [Fact]
    public void Create_rejects_an_argument_name_outside_lower_camel_case()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            MemStructuredMessage.Create(
                "restore.attempt.not-found",
                new Dictionary<string, object?>
                {
                    ["restore-session-id"] = "20260703-071259Z-example"
                }));

        Assert.Equal("arguments", exception.ParamName);
    }

    [Fact]
    public void Create_rejects_a_complex_argument_value()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            MemStructuredMessage.Create(
                "restore.attempt.not-found",
                new Dictionary<string, object?>
                {
                    ["restoreSessionId"] = new object()
                }));

        Assert.Equal("arguments", exception.ParamName);
    }
}
