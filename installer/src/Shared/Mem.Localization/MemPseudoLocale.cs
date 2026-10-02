using System.Text;
using System.Text.RegularExpressions;

namespace Mem.Localization;

/// <summary>
/// English-derived pseudo-locale rendering for MEM development and test use.
/// It intentionally decorates human catalogue text while retaining known
/// machine tokens so untranslated literals and width assumptions are obvious.
/// This is not a production language and must not be accepted by CLI language
/// selection or included in release-language completeness requirements.
/// </summary>
public static class MemPseudoLocale
{
    /// <summary>
    /// Conventional pseudo-locale identifier used by development tooling.
    /// </summary>
    public const string Code = "qps-ploc";

    private static readonly Regex ImmutableTokenPattern = new(
        @"\{\{[A-Za-z0-9_.-]+\}\}|https?://[^\s]+|\bMEM_[A-Z0-9_]+\b|\bdev-only-change-me\b|\b(?:en|de)\b|--[A-Za-z0-9-]+|\bmem(?:\s+[a-z][a-z0-9-]*){1,3}(?=\s|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Decorates literal human text with accented, expanded characters and a
    /// visible envelope. Named placeholders, CLI flags, environment variables,
    /// URLs, supported language codes, and recognised mem command names remain
    /// unchanged so test output continues to show stable machine contracts.
    /// </summary>
    public static string Transform(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length + 8);
        builder.Append('[');

        var cursor = 0;

        foreach (Match match in ImmutableTokenPattern.Matches(value))
        {
            AppendDecoratedSegment(
                builder,
                value,
                cursor,
                match.Index - cursor);

            builder.Append(match.Value);
            cursor = match.Index + match.Length;
        }

        AppendDecoratedSegment(
            builder,
            value,
            cursor,
            value.Length - cursor);

        builder.Append(']');
        return builder.ToString();
    }

    private static void AppendDecoratedSegment(
        StringBuilder builder,
        string value,
        int startIndex,
        int length)
    {
        for (var index = startIndex; index < startIndex + length; index++)
        {
            builder.Append(DecorateCharacter(value[index]));
        }
    }

    private static string DecorateCharacter(char value)
    {
        return value switch
        {
            'A' => "ÅÅ",
            'a' => "áá",
            'B' => "Ɓ",
            'b' => "ƀ",
            'C' => "Ç",
            'c' => "ç",
            'D' => "Ð",
            'd' => "ḓ",
            'E' => "ËË",
            'e' => "ëë",
            'F' => "Ƒ",
            'f' => "ƒ",
            'G' => "Ğ",
            'g' => "ğ",
            'H' => "Ħ",
            'h' => "ħ",
            'I' => "ÏÏ",
            'i' => "ïï",
            'J' => "Ĵ",
            'j' => "ĵ",
            'K' => "Ķ",
            'k' => "ķ",
            'L' => "Ŀ",
            'l' => "ľ",
            'M' => "Ṁ",
            'm' => "ṁ",
            'N' => "Ñ",
            'n' => "ñ",
            'O' => "ÔÔ",
            'o' => "ôô",
            'P' => "Þ",
            'p' => "þ",
            'Q' => "Ǫ",
            'q' => "ǫ",
            'R' => "Ř",
            'r' => "ř",
            'S' => "Š",
            's' => "š",
            'T' => "Ţ",
            't' => "ţ",
            'U' => "ÜÜ",
            'u' => "üü",
            'V' => "Ṽ",
            'v' => "ṽ",
            'W' => "Ŵ",
            'w' => "ŵ",
            'X' => "Ẋ",
            'x' => "ẋ",
            'Y' => "ŸŸ",
            'y' => "ÿÿ",
            'Z' => "Ž",
            'z' => "ž",
            _ => value.ToString()
        };
    }
}
