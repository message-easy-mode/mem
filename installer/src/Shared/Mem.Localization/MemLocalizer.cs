using System.Collections;
using System.Globalization;
using System.Resources;

namespace Mem.Localization;

/// <summary>
/// Offline MEM message rendering for human-facing CLI/API presentation.
/// Missing language-specific keys fall back to the neutral English resource;
/// missing message codes fail loudly so they cannot become silent UI defects.
/// </summary>
public sealed class MemLocalizer : IMemLocalizer
{
    private const string CliResourceBaseName =
        "Mem.Localization.Resources.CliMessages";

    private static readonly ResourceManager CliResourceManager = new(
        CliResourceBaseName,
        typeof(MemLocalizer).Assembly);

    public MemLocalizer(MemLanguage language)
    {
        Language = language;
        Culture = MemLanguageResolver.GetCulture(language);
    }

    public MemLanguage Language { get; }

    public CultureInfo Culture { get; }

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return CliResourceManager.GetString(key, Culture)
            ?? CliResourceManager.GetString(
                key,
                MemLanguageResolver.GetCulture(MemLanguage.English))
            ?? throw new KeyNotFoundException(
                $"No MEM localisation message exists for key '{key}'.");
    }

    public string Format(
        string key,
        IReadOnlyDictionary<string, object?>? values = null)
    {
        return Interpolate(
            Get(key),
            values);
    }

    public string Format(LocalizedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Format(
            message.Code,
            message.Arguments);
    }

    public string FormatPlural(
        string messageKey,
        decimal count,
        IReadOnlyDictionary<string, object?>? values = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageKey);

        var mergedValues = values is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(values, StringComparer.Ordinal);

        if (!mergedValues.ContainsKey("count"))
        {
            mergedValues["count"] = count;
        }

        var category = MemPluralRules.GetCategory(
            Language,
            count);

        var key = $"{messageKey}.{MemPluralRules.GetResourceSuffix(category)}";

        return Format(
            key,
            mergedValues);
    }

    public string FormatNumber(
        decimal value,
        string format = "N2")
    {
        return value.ToString(
            format,
            Culture);
    }

    public string FormatDateTime(
        DateTimeOffset value,
        string format = "G")
    {
        return value.ToString(
            format,
            Culture);
    }

    internal static IReadOnlyCollection<string> GetOwnCatalogKeys(
        MemLanguage language)
    {
        return GetOwnCatalogMessages(language)
            .Keys
            .ToArray();
    }

    internal static IReadOnlyDictionary<string, string> GetOwnCatalogMessages(
        MemLanguage language)
    {
        var resourceSet = CliResourceManager.GetResourceSet(
            GetCatalogCulture(language),
            createIfNotExists: true,
            tryParents: false)
            ?? throw new InvalidOperationException(
                $"Could not load the {MemLanguageResolver.GetCode(language)} MEM localisation resource set.");

        var messages = new Dictionary<string, string>(
            StringComparer.Ordinal);

        foreach (DictionaryEntry entry in resourceSet)
        {
            if (entry.Key is not string key)
            {
                continue;
            }

            if (entry.Value is not string value)
            {
                throw new InvalidOperationException(
                    $"MEM localisation resource '{key}' in the {MemLanguageResolver.GetCode(language)} catalogue is not a string.");
            }

            messages.Add(key, value);
        }

        return messages;
    }

    internal static bool HasOwnTranslation(
        MemLanguage language,
        string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return GetOwnCatalogMessages(language)
            .ContainsKey(key);
    }

    private static CultureInfo GetCatalogCulture(MemLanguage language)
    {
        return language switch
        {
            MemLanguage.English => CultureInfo.InvariantCulture,
            MemLanguage.German => CultureInfo.GetCultureInfo("de"),
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };
    }

    private string Interpolate(
        string template,
        IReadOnlyDictionary<string, object?>? values)
    {
        return MemMessageTemplate.Interpolate(
            template,
            values,
            FormatValue);
    }

    private string FormatValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, Culture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }
}
