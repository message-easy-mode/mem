using HostAgent.Runtime.Diagnostics;
using HostAgent.Runtime.Diagnostics.Docker;

namespace HostAgent.Tests.Runtime.Diagnostics.Docker;

public sealed class MemDockerLogSanitizerTests
{
    [Fact]
    public void Removes_ansi_control_characters_and_redacts_generic_and_exact_secrets()
    {
        var sanitizer = CreateSanitizer();
        var source = "\u001b[31mfailed\u001b[0m password=hunter2 \u0001\n" +
                     "opaque-value-is top-secret-value\n";

        var result = sanitizer.Sanitize(
            source,
            maximumLines: 100,
            maximumCharacters: 30000,
            exactSecrets: ["top-secret-value"]);

        Assert.DoesNotContain("\u001b", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("\u0001", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret-value", result.Content, StringComparison.Ordinal);
        Assert.Contains("password=[redacted]", result.Content, StringComparison.Ordinal);
        Assert.Contains("[redacted]", result.Content, StringComparison.Ordinal);
        Assert.True(result.RedactionsApplied);
    }

    [Fact]
    public void Keeps_the_newest_bounded_lines()
    {
        var sanitizer = CreateSanitizer();
        var source = string.Join('\n', Enumerable.Range(1, 8).Select(index => $"line-{index}"));

        var result = sanitizer.Sanitize(
            source,
            maximumLines: 3,
            maximumCharacters: 30000);

        Assert.Equal("line-6\nline-7\nline-8", result.Content);
        Assert.Equal(3, result.ReturnedLines);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Keeps_the_newest_complete_context_when_character_bounded()
    {
        var sanitizer = CreateSanitizer();
        var source = "oldest-line\nmiddle-line\nnewest-line";

        var result = sanitizer.Sanitize(
            source,
            maximumLines: 100,
            maximumCharacters: 22);

        Assert.DoesNotContain("oldest-line", result.Content, StringComparison.Ordinal);
        Assert.Contains("newest-line", result.Content, StringComparison.Ordinal);
        Assert.True(result.Truncated);
    }

    private static MemDockerLogSanitizer CreateSanitizer()
    {
        var options = new MemDiagnosticsOptions
        {
            MaximumDetailCharacters = 30000
        };
        return new MemDockerLogSanitizer(new MemDiagnosticRedactor(options));
    }
}
