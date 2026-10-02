using Mem.Localization;

namespace Mem.Localization.Tests.Localization;

public sealed class LocalizationCatalogValidatorTests
{
    private const string MessageKey =
        "cli.test.message";

    [Fact]
    public void Validate_flags_an_undeclared_resource_key()
    {
        var catalogues = CreateCatalogues(
            english: new Dictionary<string, string>
            {
                [MessageKey] = "Hello",
                ["cli.test.unused"] = "Unused"
            },
            german: new Dictionary<string, string>
            {
                [MessageKey] = "Hallo",
                ["cli.test.unused"] = "Nicht verwendet"
            });

        var issues = LocalizationCatalogValidator.Validate(
            catalogues,
            new[] { MessageKey });

        Assert.Contains(
            issues,
            issue => issue.Code == "undeclared-resource-key" &&
                     issue.Key == "cli.test.unused");
    }

    [Fact]
    public void Validate_flags_a_missing_own_german_translation_in_a_complete_module()
    {
        var catalogues = CreateCatalogues(
            english: new Dictionary<string, string>
            {
                [MessageKey] = "Hello"
            },
            german: new Dictionary<string, string>());

        var issues = LocalizationCatalogValidator.Validate(
            catalogues,
            new[] { MessageKey });

        Assert.Contains(
            issues,
            issue => issue.Code == "missing-translation" &&
                     issue.Language == MemLanguage.German &&
                     issue.Key == MessageKey);
    }

    [Fact]
    public void Validate_flags_a_blank_translation()
    {
        var catalogues = CreateCatalogues(
            english: new Dictionary<string, string>
            {
                [MessageKey] = "Hello"
            },
            german: new Dictionary<string, string>
            {
                [MessageKey] = "   "
            });

        var issues = LocalizationCatalogValidator.Validate(
            catalogues,
            new[] { MessageKey });

        Assert.Contains(
            issues,
            issue => issue.Code == "empty-translation" &&
                     issue.Language == MemLanguage.German &&
                     issue.Key == MessageKey);
    }

    [Fact]
    public void Validate_flags_a_placeholder_name_mismatch()
    {
        var catalogues = CreateCatalogues(
            english: new Dictionary<string, string>
            {
                [MessageKey] = "Hello {{operatorName}}"
            },
            german: new Dictionary<string, string>
            {
                [MessageKey] = "Hallo {{name}}"
            });

        var issues = LocalizationCatalogValidator.Validate(
            catalogues,
            new[] { MessageKey });

        Assert.Contains(
            issues,
            issue => issue.Code == "placeholder-mismatch" &&
                     issue.Language == MemLanguage.German &&
                     issue.Key == MessageKey);
    }

    [Theory]
    [InlineData("Hello {{operatorName")]
    [InlineData("Hello {{}}")]
    [InlineData("Hello }}")]
    public void Validate_flags_a_malformed_message_template(
        string template)
    {
        var catalogues = CreateCatalogues(
            english: new Dictionary<string, string>
            {
                [MessageKey] = template
            },
            german: new Dictionary<string, string>
            {
                [MessageKey] = "Hallo"
            });

        var issues = LocalizationCatalogValidator.Validate(
            catalogues,
            new[] { MessageKey });

        Assert.Contains(
            issues,
            issue => issue.Code == "malformed-template" &&
                     issue.Language == MemLanguage.English &&
                     issue.Key == MessageKey);
    }

    [Fact]
    public void Validate_flags_a_missing_required_plural_variant()
    {
        const string pluralMessageKey = "cli.test.items";
        const string oneKey = $"{pluralMessageKey}.one";
        const string otherKey = $"{pluralMessageKey}.other";

        var catalogues = CreateCatalogues(
            english: new Dictionary<string, string>
            {
                [otherKey] = "{{count}} items"
            },
            german: new Dictionary<string, string>
            {
                [otherKey] = "{{count}} Elemente"
            });

        var issues = LocalizationCatalogValidator.Validate(
            catalogues,
            new[] { oneKey, otherKey });

        Assert.Contains(
            issues,
            issue => issue.Code == "missing-plural-variant" &&
                     issue.Key == oneKey);
    }

    [Fact]
    public void Validate_flags_duplicate_declared_message_keys()
    {
        var issues = LocalizationCatalogValidator.Validate(
            CreateCatalogues(),
            new[] { MessageKey, MessageKey });

        Assert.Contains(
            issues,
            issue => issue.Code == "duplicate-declared-key" &&
                     issue.Key == MessageKey);
    }

    private static IReadOnlyDictionary<
        MemLanguage,
        IReadOnlyDictionary<string, string>>
        CreateCatalogues(
            IReadOnlyDictionary<string, string>? english = null,
            IReadOnlyDictionary<string, string>? german = null)
    {
        return new Dictionary<
            MemLanguage,
            IReadOnlyDictionary<string, string>>
        {
            [MemLanguage.English] = english ??
                new Dictionary<string, string>
                {
                    [MessageKey] = "Hello"
                },
            [MemLanguage.German] = german ??
                new Dictionary<string, string>
                {
                    [MessageKey] = "Hallo"
                }
        };
    }
}
