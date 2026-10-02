using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace Mem.Localization;

/// <summary>
/// Creates safe, stable machine-readable message descriptors for API problem
/// responses and structured events. Human prose belongs in the caller's raw
/// diagnostic/detail field; this descriptor carries only a semantic code and
/// small named scalar arguments.
/// </summary>
public static class MemStructuredMessage
{
    private static readonly Regex CodePattern = new(
        "^[a-z][a-z0-9-]*(?:\\.[a-z][a-z0-9-]*)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ArgumentNamePattern = new(
        "^[a-z][A-Za-z0-9]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static LocalizedMessage Create(
        string code,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        var normalizedCode = code?.Trim() ?? string.Empty;
        if (!CodePattern.IsMatch(normalizedCode))
        {
            throw new ArgumentException(
                "A MEM structured message code must use lowercase dot-separated segments containing letters, digits, or hyphens.",
                nameof(code));
        }

        var normalizedArguments = new SortedDictionary<string, object?>(
            StringComparer.Ordinal);

        if (arguments is not null)
        {
            foreach (var pair in arguments)
            {
                var normalizedName = pair.Key?.Trim() ?? string.Empty;
                if (!ArgumentNamePattern.IsMatch(normalizedName))
                {
                    throw new ArgumentException(
                        "A MEM structured message argument name must use lower camel case letters and digits.",
                        nameof(arguments));
                }

                if (!IsSupportedScalar(pair.Value))
                {
                    throw new ArgumentException(
                        $"Structured message argument '{normalizedName}' must be a null, string, scalar, Guid, enum, DateTime, or DateTimeOffset value.",
                        nameof(arguments));
                }

                if (!normalizedArguments.TryAdd(normalizedName, pair.Value))
                {
                    throw new ArgumentException(
                        $"Structured message argument '{normalizedName}' was supplied more than once.",
                        nameof(arguments));
                }
            }
        }

        return new LocalizedMessage(
            normalizedCode,
            new ReadOnlyDictionary<string, object?>(normalizedArguments));
    }

    private static bool IsSupportedScalar(object? value) => value is null or
        string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or
        float or double or decimal or Guid or DateTime or DateTimeOffset or Enum;
}
