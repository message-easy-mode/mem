using Shared.ValueObjects;

namespace Shared.Services.Currency;

public interface ICurrencyConverter
{
    Money Convert(Money basePrice, string targetCurrency);
}