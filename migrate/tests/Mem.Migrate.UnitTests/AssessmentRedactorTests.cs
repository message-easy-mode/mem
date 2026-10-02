using Mem.Migrate.Core.Security;

namespace Mem.Migrate.UnitTests;

public sealed class AssessmentRedactorTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("AdminAccessToken")]
    [InlineData("private_key")]
    [InlineData("Authorization")]
    public void Sensitive_keys_are_recognised(string key)
    {
        Assert.True(AssessmentRedactor.IsSensitiveKey(key));
    }

    [Fact]
    public void Text_secrets_are_redacted()
    {
        var source =
            "Authorization: Bearer abc.def password=SuperSecret token:abcdef";

        var result = AssessmentRedactor.RedactText(source);

        Assert.DoesNotContain("abc.def", result, StringComparison.Ordinal);
        Assert.DoesNotContain("SuperSecret", result, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdef", result, StringComparison.Ordinal);
        Assert.Contains("<redacted>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Paths_are_hidden_by_default()
    {
        var result = AssessmentRedactor.DisplayPath(
            "/var/lib/mem/instances/demo/synapse/homeserver.db",
            includeSensitivePaths: false);

        Assert.Equal("<path:homeserver.db>", result);
    }
}
