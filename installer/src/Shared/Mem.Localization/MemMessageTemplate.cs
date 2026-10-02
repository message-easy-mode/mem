using System.Text;
using System.Text.RegularExpressions;

namespace Mem.Localization;

/// <summary>
/// Parses MEM named-token templates such as <c>{{catalogEntryId}}</c>.
/// Template shape is validated before interpolation so malformed catalogue
/// entries fail loudly during validation rather than silently reaching operators.
/// </summary>
internal static class MemMessageTemplate
{
    private static readonly Regex PlaceholderNamePattern = new(
        @"^[A-Za-z0-9_.-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static IReadOnlyCollection<string> GetPlaceholderNames(
        string template)
    {
        return Parse(template)
            .Select(placeholder => placeholder.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    internal static string Interpolate(
        string template,
        IReadOnlyDictionary<string, object?>? values,
        Func<object?, string> formatValue)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(formatValue);

        var placeholders = Parse(template);

        if (placeholders.Count == 0 || values is null || values.Count == 0)
        {
            return template;
        }

        var builder = new StringBuilder(template.Length);
        var cursor = 0;

        foreach (var placeholder in placeholders)
        {
            builder.Append(
                template,
                cursor,
                placeholder.StartIndex - cursor);

            if (values.TryGetValue(placeholder.Name, out var value))
            {
                builder.Append(formatValue(value));
            }
            else
            {
                builder.Append(
                    template,
                    placeholder.StartIndex,
                    placeholder.EndIndex - placeholder.StartIndex);
            }

            cursor = placeholder.EndIndex;
        }

        builder.Append(
            template,
            cursor,
            template.Length - cursor);

        return builder.ToString();
    }

    private static IReadOnlyList<Placeholder> Parse(
        string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var placeholders = new List<Placeholder>();
        var cursor = 0;

        while (cursor < template.Length)
        {
            var openingIndex = template.IndexOf(
                "{{",
                cursor,
                StringComparison.Ordinal);

            var closingIndex = template.IndexOf(
                "}}",
                cursor,
                StringComparison.Ordinal);

            if (closingIndex >= 0 &&
                (openingIndex < 0 || closingIndex < openingIndex))
            {
                throw new FormatException(
                    $"MEM localisation template contains an unmatched closing delimiter at index {closingIndex}.");
            }

            if (openingIndex < 0)
            {
                break;
            }

            var closingDelimiterIndex = template.IndexOf(
                "}}",
                openingIndex + 2,
                StringComparison.Ordinal);

            if (closingDelimiterIndex < 0)
            {
                throw new FormatException(
                    $"MEM localisation template contains an unterminated token beginning at index {openingIndex}.");
            }

            var name = template[
                    (openingIndex + 2)..closingDelimiterIndex]
                .Trim();

            if (!PlaceholderNamePattern.IsMatch(name))
            {
                throw new FormatException(
                    $"MEM localisation template contains an invalid token '{{{{{name}}}}}'.");
            }

            placeholders.Add(
                new Placeholder(
                    openingIndex,
                    closingDelimiterIndex + 2,
                    name));

            cursor = closingDelimiterIndex + 2;
        }

        return placeholders;
    }

    private sealed record Placeholder(
        int StartIndex,
        int EndIndex,
        string Name);
}
