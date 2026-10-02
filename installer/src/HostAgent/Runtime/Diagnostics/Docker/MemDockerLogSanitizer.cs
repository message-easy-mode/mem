using HostAgent.Runtime.Diagnostics;
using System.Text.RegularExpressions;

namespace HostAgent.Runtime.Diagnostics.Docker;

internal sealed record MemDockerSanitizedLog(
    string Content,
    int ReturnedLines,
    bool Truncated,
    bool RedactionsApplied);

internal sealed class MemDockerLogSanitizer(MemDiagnosticRedactor redactor)
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex AnsiEscape = new(
        "\\u001B(?:\\[[0-?]*[ -/]*[@-~]|\\][^\\u0007]*(?:\\u0007|\\u001B\\\\))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    public MemDockerSanitizedLog Sanitize(
        string? source,
        int maximumLines,
        int maximumCharacters,
        IReadOnlyCollection<string>? exactSecrets = null,
        bool sourceWasTruncated = false)
    {
        if (maximumLines < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLines));
        }

        if (maximumCharacters < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        }

        var original = source ?? string.Empty;
        var normalized = original
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var withoutAnsi = AnsiEscape.Replace(normalized, string.Empty);
        var cleaned = new string(withoutAnsi
            .Where(character => !char.IsControl(character) || character is '\n' or '\t')
            .ToArray());
        var changed = !string.Equals(original, cleaned, StringComparison.Ordinal);
        var truncated = sourceWasTruncated;

        var sourceLines = cleaned.Split('\n');
        if (sourceLines.Length > maximumLines)
        {
            sourceLines = sourceLines[^maximumLines..];
            truncated = true;
        }

        var safeLines = new List<string>(sourceLines.Length);
        foreach (var line in sourceLines)
        {
            var safe = redactor.RedactText(
                line,
                maximumCharacters,
                exactSecrets);
            safeLines.Add(safe.Value);
            changed |= safe.Changed;
            truncated |= safe.Truncated;
        }

        var content = string.Join('\n', safeLines);
        if (content.Length > maximumCharacters)
        {
            content = content[^maximumCharacters..];
            var firstNewline = content.IndexOf('\n');
            if (firstNewline >= 0 && firstNewline + 1 < content.Length)
            {
                content = content[(firstNewline + 1)..];
            }

            truncated = true;
        }

        content = content.TrimEnd();
        var returnedLines = string.IsNullOrEmpty(content)
            ? 0
            : content.Count(character => character == '\n') + 1;

        return new MemDockerSanitizedLog(
            content,
            returnedLines,
            truncated,
            RedactionsApplied: changed);
    }
}
