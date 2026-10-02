using Shared.ValueObjects;

namespace Shared.Services.Currency;
public class StaticRateConverter : ICurrencyConverter
{
    private readonly Dictionary<string, decimal> _rates = new(StringComparer.OrdinalIgnoreCase)
    {
        // === Identity mappings ===
        ["USD:USD"] = 1m,
        ["GBP:GBP"] = 1m,
        ["EUR:EUR"] = 1m,
        ["CAD:CAD"] = 1m,
        ["AUD:AUD"] = 1m,
        ["NZD:NZD"] = 1m,
        ["JPY:JPY"] = 1m,

        // === USD base conversions ===
        ["USD:GBP"] = 0.78m,
        ["USD:EUR"] = 0.92m,
        ["USD:CAD"] = 1.36m,
        ["USD:AUD"] = 1.55m,
        ["USD:NZD"] = 1.70m,
        ["USD:JPY"] = 150m,

        // === Reverse conversions (approximate mid-rates) ===
        ["GBP:USD"] = 1.28m,
        ["EUR:USD"] = 1.09m,
        ["CAD:USD"] = 0.74m,
        ["AUD:USD"] = 0.65m,
        ["NZD:USD"] = 0.59m,
        ["JPY:USD"] = 0.0067m,
    };

    public Money Convert(Money basePrice, string targetCurrency)
    {
        // 1️⃣ If already in target currency, return as-is
        if (basePrice.Currency.Equals(targetCurrency, StringComparison.OrdinalIgnoreCase))
            return basePrice;

        // 2️⃣ Direct lookup
        var key = $"{basePrice.Currency}:{targetCurrency}";
        if (_rates.TryGetValue(key, out var rate))
            return new Money(Math.Round(basePrice.Amount * rate, 2), targetCurrency);

        // 3️⃣ Fallback via USD if possible
        if (_rates.TryGetValue($"{basePrice.Currency}:USD", out var toUsd) &&
            _rates.TryGetValue($"USD:{targetCurrency}", out var fromUsd))
        {
            var usd = basePrice.Amount * toUsd;
            return new Money(Math.Round(usd * fromUsd, 2), targetCurrency);
        }

        // 4️⃣ Graceful fallback (should never happen with supported currencies)
        throw new InvalidOperationException($"No conversion rate found for {basePrice.Currency}->{targetCurrency}");
    }
}