using System.Globalization;
using Mem.Localization;

namespace Mem.Localization.Tests.Localization;

public sealed class MemLanguageResolverTests
{
    [Theory]
    [InlineData("en", MemLanguage.English)]
    [InlineData("en-NZ", MemLanguage.English)]
    [InlineData("en_US", MemLanguage.English)]
    [InlineData("de", MemLanguage.German)]
    [InlineData("de-DE", MemLanguage.German)]
    [InlineData("de_AT", MemLanguage.German)]
    public void TryParse_accepts_supported_primary_and_regional_language_codes(
        string input,
        MemLanguage expected)
    {
        var parsed = MemLanguageResolver.TryParse(
            input,
            out var actual);

        Assert.True(parsed);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TryParse_rejects_unsupported_language_codes()
    {
        var parsed = MemLanguageResolver.TryParse(
            "fr-FR",
            out _);

        Assert.False(parsed);
    }

    [Fact]
    public void ResolveSystemDefault_uses_german_for_a_german_system_culture()
    {
        var language = MemLanguageResolver.ResolveSystemDefault(
            CultureInfo.GetCultureInfo("de-CH"));

        Assert.Equal(MemLanguage.German, language);
    }

    [Fact]
    public void ResolveSystemDefault_falls_back_to_english_for_an_unsupported_system_culture()
    {
        var language = MemLanguageResolver.ResolveSystemDefault(
            CultureInfo.GetCultureInfo("fr-FR"));

        Assert.Equal(MemLanguage.English, language);
    }

    [Theory]
    [InlineData(MemLanguage.English, "en", "en-NZ")]
    [InlineData(MemLanguage.German, "de", "de-DE")]
    public void Language_metadata_is_stable(
        MemLanguage language,
        string expectedCode,
        string expectedCulture)
    {
        Assert.Equal(
            expectedCode,
            MemLanguageResolver.GetCode(language));
        Assert.Equal(
            expectedCulture,
            MemLanguageResolver.GetCulture(language).Name);
    }
}
