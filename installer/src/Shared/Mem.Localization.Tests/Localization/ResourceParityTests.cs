using System.Reflection;
using Mem.Localization;

namespace Mem.Localization.Tests.Localization;

public sealed class ResourceParityTests
{
    [Fact]
    public void Current_catalogues_pass_the_complete_localisation_contract()
    {
        var issues = LocalizationCatalogValidator.Validate(
            GetCurrentCatalogues(),
            CliMessageKeys.All);

        Assert.Empty(issues);
    }

    [Fact]
    public void Every_public_cli_message_key_constant_is_either_a_resource_key_or_a_plural_base()
    {
        var constantKeys = typeof(CliMessageKeys)
            .GetFields(
                BindingFlags.Public |
                BindingFlags.Static)
            .Where(
                field => field.IsLiteral &&
                         !field.IsInitOnly &&
                         field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        var declaredResourceKeys = CliMessageKeys.All
            .ToHashSet(StringComparer.Ordinal);

        var pluralBases = declaredResourceKeys
            .Select(
                key => MemPluralRules.TryGetPluralKeyParts(
                    key,
                    out var messageKey,
                    out _)
                    ? messageKey
                    : null)
            .Where(messageKey => messageKey is not null)
            .Select(messageKey => messageKey!)
            .ToHashSet(StringComparer.Ordinal);

        var expectedResourceKeys = constantKeys
            .Except(pluralBases, StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        var actualResourceKeys = declaredResourceKeys
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedResourceKeys, actualResourceKeys);
    }

    private static IReadOnlyDictionary<
        MemLanguage,
        IReadOnlyDictionary<string, string>>
        GetCurrentCatalogues()
    {
        return new Dictionary<
            MemLanguage,
            IReadOnlyDictionary<string, string>>
        {
            [MemLanguage.English] =
                MemLocalizer.GetOwnCatalogMessages(
                    MemLanguage.English),
            [MemLanguage.German] =
                MemLocalizer.GetOwnCatalogMessages(
                    MemLanguage.German)
        };
    }
}
