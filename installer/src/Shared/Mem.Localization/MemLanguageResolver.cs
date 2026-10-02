using System.Globalization;

namespace Mem.Localization;

/// <summary>
/// Resolves the small, explicit set of languages supported by the current MEM release.
/// A caller should validate explicit command-line or environment values separately;
/// unsupported operating-system cultures intentionally fall back to English.
/// </summary>
public static class MemLanguageResolver
{
    private static readonly CultureInfo EnglishCulture =
        CultureInfo.GetCultureInfo("en-NZ");

    private static readonly CultureInfo GermanCulture =
        CultureInfo.GetCultureInfo("de-DE");

    public static bool TryParse(
        string? value,
        out MemLanguage language)
    {
        language = MemLanguage.English;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value
            .Trim()
            .Replace('_', '-');

        var separatorIndex = normalized.IndexOf('-');
        var primaryLanguage = separatorIndex >= 0
            ? normalized[..separatorIndex]
            : normalized;

        switch (primaryLanguage.ToLowerInvariant())
        {
            case "en":
                language = MemLanguage.English;
                return true;

            case "de":
                language = MemLanguage.German;
                return true;

            default:
                return false;
        }
    }

    public static MemLanguage ResolveSystemDefault(
        CultureInfo? culture = null)
    {
        return TryParse(
            (culture ?? CultureInfo.CurrentUICulture).Name,
            out var language)
            ? language
            : MemLanguage.English;
    }

    public static string GetCode(MemLanguage language)
    {
        return language switch
        {
            MemLanguage.English => "en",
            MemLanguage.German => "de",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };
    }

    public static CultureInfo GetCulture(MemLanguage language)
    {
        return language switch
        {
            MemLanguage.English => EnglishCulture,
            MemLanguage.German => GermanCulture,
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };
    }
}
