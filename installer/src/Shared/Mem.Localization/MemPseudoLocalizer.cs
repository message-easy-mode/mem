using System.Globalization;

namespace Mem.Localization;

/// <summary>
/// Test/development localiser that renders the neutral English catalogue through
/// <see cref="MemPseudoLocale"/>. It is deliberately separate from
/// <see cref="MemLanguage"/> so production language selection remains limited
/// to the release languages accepted by <see cref="MemLanguageResolver"/>.
/// </summary>
public sealed class MemPseudoLocalizer : IMemLocalizer
{
    private readonly MemLocalizer _english = new(MemLanguage.English);

    /// <summary>
    /// Gets the conventional pseudo-locale code for diagnostics and test names.
    /// </summary>
    public string LocaleCode => MemPseudoLocale.Code;

    /// <summary>
    /// Pseudo rendering is an English-derived testing overlay, so the existing
    /// <see cref="IMemLocalizer"/> language and culture contracts remain English.
    /// Consumers that need to identify the overlay should use <see cref="LocaleCode"/>.
    /// </summary>
    public MemLanguage Language => MemLanguage.English;

    public CultureInfo Culture => _english.Culture;

    public string Get(string key)
    {
        return MemPseudoLocale.Transform(
            _english.Get(key));
    }

    public string Format(
        string key,
        IReadOnlyDictionary<string, object?>? values = null)
    {
        return MemMessageTemplate.Interpolate(
            Get(key),
            values,
            FormatValue);
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
            MemLanguage.English,
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
        return _english.FormatNumber(
            value,
            format);
    }

    public string FormatDateTime(
        DateTimeOffset value,
        string format = "G")
    {
        return _english.FormatDateTime(
            value,
            format);
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
