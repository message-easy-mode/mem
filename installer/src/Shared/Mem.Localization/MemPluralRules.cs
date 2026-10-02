namespace Mem.Localization;

internal static class MemPluralRules
{
    private static readonly IReadOnlyList<MemPluralCategory>
        EnglishAndGermanRequiredResourceCategories =
            Array.AsReadOnly(
                new[]
                {
                    MemPluralCategory.One,
                    MemPluralCategory.Other
                });

    public static MemPluralCategory GetCategory(
        MemLanguage language,
        decimal count)
    {
        return language switch
        {
            MemLanguage.English or MemLanguage.German =>
                count == 1
                    ? MemPluralCategory.One
                    : MemPluralCategory.Other,
            _ => MemPluralCategory.Other
        };
    }

    internal static IReadOnlyList<MemPluralCategory>
        GetRequiredResourceCategories(
            MemLanguage language)
    {
        return language switch
        {
            MemLanguage.English or MemLanguage.German =>
                EnglishAndGermanRequiredResourceCategories,
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };
    }

    internal static bool TryGetPluralKeyParts(
        string key,
        out string messageKey,
        out MemPluralCategory category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        messageKey = string.Empty;
        category = default;

        var separatorIndex = key.LastIndexOf('.');
        if (separatorIndex <= 0 || separatorIndex == key.Length - 1)
        {
            return false;
        }

        var suffix = key[(separatorIndex + 1)..];

        if (!TryParseResourceSuffix(suffix, out category))
        {
            return false;
        }

        messageKey = key[..separatorIndex];
        return true;
    }

    public static string GetResourceSuffix(
        MemPluralCategory category)
    {
        return category switch
        {
            MemPluralCategory.Zero => "zero",
            MemPluralCategory.One => "one",
            MemPluralCategory.Two => "two",
            MemPluralCategory.Few => "few",
            MemPluralCategory.Many => "many",
            MemPluralCategory.Other => "other",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
        };
    }

    private static bool TryParseResourceSuffix(
        string suffix,
        out MemPluralCategory category)
    {
        category = suffix switch
        {
            "zero" => MemPluralCategory.Zero,
            "one" => MemPluralCategory.One,
            "two" => MemPluralCategory.Two,
            "few" => MemPluralCategory.Few,
            "many" => MemPluralCategory.Many,
            "other" => MemPluralCategory.Other,
            _ => default
        };

        return suffix is "zero" or "one" or "two" or "few" or "many" or "other";
    }
}
