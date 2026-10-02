using Mem.Localization;

namespace Mem.Localization.Tests.Localization;

public sealed class MemPseudoLocalizerTests
{
    [Fact]
    public void Locale_code_is_the_conventional_test_only_pseudo_locale_code()
    {
        var localizer = new MemPseudoLocalizer();

        Assert.Equal(
            "qps-ploc",
            localizer.LocaleCode);
    }

    [Fact]
    public void Get_decorates_english_catalogue_text_and_preserves_supported_cli_tokens()
    {
        var localizer = new MemPseudoLocalizer();
        var english = new MemLocalizer(MemLanguage.English);

        var rendered = localizer.Get(
            CliMessageKeys.HelpOptionLanguage);

        Assert.StartsWith("[", rendered);
        Assert.EndsWith("]", rendered);
        Assert.NotEqual(
            english.Get(CliMessageKeys.HelpOptionLanguage),
            rendered);
        Assert.Contains("--language", rendered);
        Assert.Contains("en|de", rendered);
    }

    [Fact]
    public void Transform_preserves_placeholders_flags_environment_urls_and_command_names()
    {
        var rendered = MemPseudoLocale.Transform(
            "Run mem backups inspect {{catalogEntryId}} with --language via MEM_CLI_LANGUAGE at http://localhost:7105.");

        Assert.StartsWith("[", rendered);
        Assert.EndsWith("]", rendered);
        Assert.Contains("mem backups inspect", rendered);
        Assert.Contains("{{catalogEntryId}}", rendered);
        Assert.Contains("--language", rendered);
        Assert.Contains("MEM_CLI_LANGUAGE", rendered);
        Assert.Contains("http://localhost:7105", rendered);
    }

    [Fact]
    public void Format_preserves_dynamic_argument_values_while_transforming_catalogue_text()
    {
        var localizer = new MemPseudoLocalizer();

        var rendered = localizer.Format(
            CliMessageKeys.ErrorUnsupportedLanguage,
            new Dictionary<string, object?>
            {
                ["language"] = "fr"
            });

        Assert.StartsWith("[", rendered);
        Assert.EndsWith("]", rendered);
        Assert.Contains("'fr'", rendered);
        Assert.DoesNotContain("Unsupported language", rendered);
    }

    [Fact]
    public void Format_plural_uses_english_plural_selection_and_retains_count_values()
    {
        var localizer = new MemPseudoLocalizer();

        var one = localizer.FormatPlural(
            CliMessageKeys.BackupMatchingCount,
            1);
        var other = localizer.FormatPlural(
            CliMessageKeys.BackupMatchingCount,
            2);

        Assert.StartsWith("[", one);
        Assert.EndsWith("]", one);
        Assert.Contains("1", one);
        Assert.Contains("2", other);
        Assert.NotEqual(one, other);
    }

    [Fact]
    public void Format_number_uses_the_english_base_culture()
    {
        var localizer = new MemPseudoLocalizer();

        Assert.Equal(
            "12,345.67",
            localizer.FormatNumber(12345.67m));
    }

    [Fact]
    public void Production_language_resolver_rejects_the_test_only_pseudo_locale()
    {
        var parsed = MemLanguageResolver.TryParse(
            MemPseudoLocale.Code,
            out _);

        Assert.False(parsed);
    }
}
