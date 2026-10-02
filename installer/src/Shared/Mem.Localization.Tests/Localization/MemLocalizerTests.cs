using Mem.Localization;

namespace Mem.Localization.Tests.Localization;

public sealed class MemLocalizerTests
{
    [Fact]
    public void Get_reads_the_german_catalogue()
    {
        var localizer = new MemLocalizer(
            MemLanguage.German);

        var message = localizer.Get(
            CliMessageKeys.BackupCatalogTitle);

        Assert.Equal("Sicherungskatalog", message);
    }

    [Fact]
    public void Format_interpolates_named_arguments_without_changing_missing_tokens()
    {
        var localizer = new MemLocalizer(
            MemLanguage.German);

        var rendered = localizer.Format(
            CliMessageKeys.ErrorUnsupportedLanguage,
            new Dictionary<string, object?>
            {
                ["language"] = "fr"
            });

        Assert.Equal(
            "Nicht unterstützte Sprache 'fr'. Unterstützte Werte: en, de.",
            rendered);
    }

    [Theory]
    [InlineData(MemLanguage.English, 1, "1 matching backup")]
    [InlineData(MemLanguage.English, 2, "2 matching backups")]
    [InlineData(MemLanguage.German, 1, "1 passende Sicherung")]
    [InlineData(MemLanguage.German, 2, "2 passende Sicherungen")]
    public void FormatPlural_uses_the_selected_languages_plural_rule(
        MemLanguage language,
        decimal count,
        string expected)
    {
        var localizer = new MemLocalizer(language);

        var rendered = localizer.FormatPlural(
            CliMessageKeys.BackupMatchingCount,
            count);

        Assert.Equal(expected, rendered);
    }

    [Theory]
    [InlineData(MemLanguage.English, "12,345.67")]
    [InlineData(MemLanguage.German, "12.345,67")]
    public void FormatNumber_uses_the_selected_culture(
        MemLanguage language,
        string expected)
    {
        var localizer = new MemLocalizer(language);

        var rendered = localizer.FormatNumber(
            12345.67m);

        Assert.Equal(expected, rendered);
    }

    [Fact]
    public void Format_accepts_a_structured_message_descriptor()
    {
        var localizer = new MemLocalizer(
            MemLanguage.English);

        var rendered = localizer.Format(
            new LocalizedMessage(
                CliMessageKeys.ErrorUnsupportedLanguage,
                new Dictionary<string, object?>
                {
                    ["language"] = "fr"
                }));

        Assert.Equal(
            "Unsupported language 'fr'. Supported values: en, de.",
            rendered);
    }
}
