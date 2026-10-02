using System.Globalization;

namespace Mem.Localization;

public interface IMemLocalizer
{
    MemLanguage Language { get; }

    CultureInfo Culture { get; }

    string Get(string key);

    string Format(
        string key,
        IReadOnlyDictionary<string, object?>? values = null);

    string Format(LocalizedMessage message);

    string FormatPlural(
        string messageKey,
        decimal count,
        IReadOnlyDictionary<string, object?>? values = null);

    string FormatNumber(
        decimal value,
        string format = "N2");

    string FormatDateTime(
        DateTimeOffset value,
        string format = "G");
}
