using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Shared.ValueObjects;

namespace Shared.Services.Currency;

public class LiveFxRateConverter : ICurrencyConverter
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly StaticRateConverter _fallback;
    private readonly string _appId; // e.g. for OpenExchangeRates.org

    public LiveFxRateConverter(HttpClient httpClient, IMemoryCache cache, IConfiguration config)
    {
        _httpClient = httpClient;
        _cache = cache;
        _fallback = new StaticRateConverter();

        // Example: from appsettings.json → "Fx:AppId": "YOUR_API_KEY"
        _appId = config["Fx:AppId"] ?? throw new InvalidOperationException("FX AppId missing in config.");
    }

    public Money Convert(Money source, string targetCurrency)
    {
        if (source.Currency.Equals(targetCurrency, StringComparison.OrdinalIgnoreCase))
            return source;

        try
        {
            var rates = GetRatesAsync().GetAwaiter().GetResult();

            if (!rates.TryGetValue(source.Currency.ToUpperInvariant(), out var fromRate) ||
                !rates.TryGetValue(targetCurrency.ToUpperInvariant(), out var toRate))
            {
                // Fallback to static converter if missing
                return _fallback.Convert(source, targetCurrency);
            }

            decimal usdAmount = source.Amount / fromRate;
            decimal targetAmount = usdAmount * toRate;

            return new Money(Math.Round(targetAmount, 2), targetCurrency);
        }
        catch
        {
            // On any failure, fallback to static converter
            return _fallback.Convert(source, targetCurrency);
        }
    }

    private async Task<Dictionary<string, decimal>> GetRatesAsync()
    {
        // Cached for 12 hours
        if (_cache.TryGetValue("fx:rates", out Dictionary<string, decimal>? cached))
            return cached!;

        var url = $"https://openexchangerates.org/api/latest.json?app_id={_appId}";
        var response = await _httpClient.GetFromJsonAsync<ExchangeRatesResponse>(url);

        if (response?.Rates is null || response.Rates.Count == 0)
            throw new InvalidOperationException("No FX rates returned.");

        _cache.Set("fx:rates", response.Rates, TimeSpan.FromHours(12));
        return response.Rates;
    }

    private class ExchangeRatesResponse
    {
        public string Base { get; set; } = "USD";
        public Dictionary<string, decimal> Rates { get; set; } = new();
    }
}
