using System.Globalization;

namespace Shared.ValueObjects;

public static class MoneyFormatter
{
    public static string Format(Money money, string countryCode)
    {
        var culture = countryCode.ToUpperInvariant() switch
        {
            "US" or "CA" => new CultureInfo("en-US"),
            "GB" => new CultureInfo("en-GB"),
            "DE" => new CultureInfo("de-DE"),
            "FR" => new CultureInfo("fr-FR"),
            "ES" => new CultureInfo("es-ES"),
            "AU" or "NZ" => new CultureInfo("en-AU"),
            "JP" => new CultureInfo("ja-JP"),
            "IN" => new CultureInfo("en-IN"),
            "ZA" => new CultureInfo("en-ZA"),
            "ZM" => new CultureInfo("en-ZM"),
            _ => CultureInfo.InvariantCulture
        };

        return string.Format(culture, "{0:C}", money.Amount);
    }

    public static string Pretty(Money money, string? cultureCode = null)
    {
        var culture = !string.IsNullOrWhiteSpace(cultureCode)
            ? new CultureInfo(cultureCode)
            : CultureInfo.InvariantCulture;

        var formatted = string.Format(culture, "{0:C}", money.Amount);
        return $"{formatted} {money.Currency}";
    }

    
}