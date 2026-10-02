using Shared.Services.Currency;

namespace Shared.ValueObjects;

public static class CurrencyExtensions
{
    /// <summary>
    /// Converts a Money value into another currency using the provided converter.
    /// </summary>
    public static Money In(this Money money, string targetCurrency, ICurrencyConverter converter)
        => converter.Convert(money, targetCurrency);
}