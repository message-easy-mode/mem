namespace Mem.Localization;

/// <summary>
/// Validates the complete built-in localisation contract. It is intentionally
/// internal: release validation owns this gate, while callers consume the
/// stable message-code and localizer APIs instead.
/// </summary>
internal static class LocalizationCatalogValidator
{
    internal static IReadOnlyList<LocalizationCatalogValidationIssue> Validate(
        IReadOnlyDictionary<MemLanguage, IReadOnlyDictionary<string, string>> catalogues,
        IReadOnlyCollection<string> declaredKeys)
    {
        ArgumentNullException.ThrowIfNull(catalogues);
        ArgumentNullException.ThrowIfNull(declaredKeys);

        var issues = new List<LocalizationCatalogValidationIssue>();
        var declaredKeyList = declaredKeys.ToArray();
        var declaredKeySet = new HashSet<string>(
            declaredKeyList,
            StringComparer.Ordinal);

        foreach (var duplicateKey in declaredKeyList
                     .GroupBy(key => key, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key))
        {
            issues.Add(
                Issue(
                    "duplicate-declared-key",
                    null,
                    duplicateKey,
                    $"The message-key contract declares '{duplicateKey}' more than once."));
        }

        var placeholders = new Dictionary<
            MemLanguage,
            IReadOnlyDictionary<string, IReadOnlyCollection<string>>>();

        foreach (var language in Enum.GetValues<MemLanguage>())
        {
            if (!catalogues.TryGetValue(language, out var catalogue))
            {
                issues.Add(
                    Issue(
                        "missing-catalogue",
                        language,
                        null,
                        $"The {MemLanguageResolver.GetCode(language)} catalogue is missing."));

                continue;
            }

            foreach (var declaredKey in declaredKeySet)
            {
                if (!catalogue.ContainsKey(declaredKey))
                {
                    issues.Add(
                        Issue(
                            "missing-translation",
                            language,
                            declaredKey,
                            $"The {MemLanguageResolver.GetCode(language)} catalogue has no own translation for '{declaredKey}'."));
                }
            }

            foreach (var catalogueKey in catalogue.Keys)
            {
                if (!declaredKeySet.Contains(catalogueKey))
                {
                    issues.Add(
                        Issue(
                            "undeclared-resource-key",
                            language,
                            catalogueKey,
                            $"The {MemLanguageResolver.GetCode(language)} catalogue contains '{catalogueKey}', which is not in the message-key contract."));
                }
            }

            placeholders[language] = ValidateTemplates(
                language,
                catalogue,
                issues);
        }

        ValidatePlaceholderParity(
            catalogues,
            declaredKeySet,
            placeholders,
            issues);

        ValidatePluralVariants(
            catalogues,
            declaredKeySet,
            issues);

        return issues;
    }

    private static IReadOnlyDictionary<string, IReadOnlyCollection<string>>
        ValidateTemplates(
            MemLanguage language,
            IReadOnlyDictionary<string, string> catalogue,
            ICollection<LocalizationCatalogValidationIssue> issues)
    {
        var placeholders = new Dictionary<
            string,
            IReadOnlyCollection<string>>(
            StringComparer.Ordinal);

        foreach (var (key, template) in catalogue)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                issues.Add(
                    Issue(
                        "empty-translation",
                        language,
                        key,
                        $"The {MemLanguageResolver.GetCode(language)} translation for '{key}' is blank."));

                continue;
            }

            try
            {
                placeholders[key] = MemMessageTemplate.GetPlaceholderNames(
                    template);
            }
            catch (FormatException exception)
            {
                issues.Add(
                    Issue(
                        "malformed-template",
                        language,
                        key,
                        exception.Message));
            }
        }

        return placeholders;
    }

    private static void ValidatePlaceholderParity(
        IReadOnlyDictionary<MemLanguage, IReadOnlyDictionary<string, string>> catalogues,
        IReadOnlySet<string> declaredKeys,
        IReadOnlyDictionary<
            MemLanguage,
            IReadOnlyDictionary<string, IReadOnlyCollection<string>>> placeholders,
        ICollection<LocalizationCatalogValidationIssue> issues)
    {
        if (!catalogues.ContainsKey(MemLanguage.English) ||
            !placeholders.TryGetValue(
                MemLanguage.English,
                out var englishPlaceholders))
        {
            return;
        }

        foreach (var language in Enum.GetValues<MemLanguage>())
        {
            if (language == MemLanguage.English ||
                !catalogues.ContainsKey(language) ||
                !placeholders.TryGetValue(language, out var translatedPlaceholders))
            {
                continue;
            }

            foreach (var key in declaredKeys)
            {
                if (!englishPlaceholders.TryGetValue(key, out var expected) ||
                    !translatedPlaceholders.TryGetValue(key, out var actual))
                {
                    continue;
                }

                if (!expected.ToHashSet(StringComparer.Ordinal)
                        .SetEquals(actual))
                {
                    issues.Add(
                        Issue(
                            "placeholder-mismatch",
                            language,
                            key,
                            $"The {MemLanguageResolver.GetCode(language)} translation for '{key}' does not use the same named placeholders as English."));
                }
            }
        }
    }

    private static void ValidatePluralVariants(
        IReadOnlyDictionary<MemLanguage, IReadOnlyDictionary<string, string>> catalogues,
        IReadOnlySet<string> declaredKeys,
        ICollection<LocalizationCatalogValidationIssue> issues)
    {
        var pluralMessageKeys = declaredKeys
            .Select(
                key => MemPluralRules.TryGetPluralKeyParts(
                    key,
                    out var messageKey,
                    out _)
                    ? messageKey
                    : null)
            .Where(messageKey => messageKey is not null)
            .Select(messageKey => messageKey!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var language in Enum.GetValues<MemLanguage>())
        {
            if (!catalogues.TryGetValue(language, out var catalogue))
            {
                continue;
            }

            foreach (var messageKey in pluralMessageKeys)
            {
                foreach (var category in MemPluralRules.GetRequiredResourceCategories(language))
                {
                    var variantKey =
                        $"{messageKey}.{MemPluralRules.GetResourceSuffix(category)}";

                    if (!catalogue.ContainsKey(variantKey))
                    {
                        issues.Add(
                            Issue(
                                "missing-plural-variant",
                                language,
                                variantKey,
                                $"The {MemLanguageResolver.GetCode(language)} catalogue is missing required plural variant '{variantKey}'."));
                    }
                }
            }
        }
    }

    private static LocalizationCatalogValidationIssue Issue(
        string code,
        MemLanguage? language,
        string? key,
        string message)
    {
        return new LocalizationCatalogValidationIssue(
            code,
            language,
            key,
            message);
    }
}

internal sealed record LocalizationCatalogValidationIssue(
    string Code,
    MemLanguage? Language,
    string? Key,
    string Message);
