# Mem.Localization

Offline localisation primitives shared by MEM C# components. The initial
consumer is `mem-cli`; the API may use `LocalizedMessage` for future structured
operator events without turning the API into a runtime translation service.

## Product-language boundary

`Mem.Localization` localises human presentation only. The following remain
stable English machine contracts regardless of selected display language:

```text
commands and flags
JSON property names and enum/status values
HTTP paths, query parameter names, request bodies and headers
IDs and slugs
exit codes
API error/event codes
raw diagnostics, logs, evidence and exception text
```

Human-facing headings, labels, warnings, local validation messages, summaries,
counts, and culture-formatted values may be localised.

## Production languages

The current release languages are deliberately explicit:

```text
en -> English wording with en-NZ formatting
de -> German wording with de-DE formatting
```

`MemLanguageResolver.TryParse` accepts only these production language families.
A regional value such as `de-CH` resolves to German wording; unsupported system
cultures fall back to English.

## Catalogues and validation

CLI resources live in:

```text
Resources/CliMessages.resx
Resources/CliMessages.de.resx
```

`LocalizationCatalogValidator` and `Mem.Localization.Tests` enforce declared
key coverage, English/German completeness, non-blank translations, named-token
shape, plural variants, and duplicate declaration protection.

Named tokens use this syntax:

```text
{{catalogEntryId}}
{{count}}
```

Missing runtime arguments intentionally leave their token visible. Catalogue
validation must catch malformed templates before they reach operators.

## English–German terminology

The maintained operator-facing terminology reference is:

```text
Locales/Glossary.en-de.md
```

Review this glossary whenever a new capitalised product term is introduced or
when CLI, Web, documentation, and support wording could otherwise diverge.

## Test-only pseudo-locale

`MemPseudoLocalizer` is an English-derived development/testing overlay with the
conventional code:

```text
qps-ploc
```

It decorates and expands English catalogue literals, making hard-coded English
and width assumptions obvious, while retaining known machine tokens such as
named placeholders, CLI flags, environment variables, URLs, supported language
codes, and recognised `mem` command names.

It is intentionally **not** a production language:

```text
--language qps-ploc       -> unsupported
MEM_CLI_LANGUAGE=qps-ploc -> unsupported
```

Use it only by direct dependency injection in tests or development harnesses:

```csharp
var localizer = new MemPseudoLocalizer();

var rendered = localizer.Format(
    CliMessageKeys.BackupListTitle);
```

The pseudo locale does not have `.resx` resources, does not count toward release
language completeness, and must never be documented as a customer/operator
locale.

## Structured API messages and restore events

New or materially touched API workflows may add an optional `LocalizedMessage`
descriptor beside their existing error code and raw diagnostic detail. The
server does not translate it. Trusted clients can later render a recognised
message code locally while retaining the original diagnostic text for support
and audit.

The full compatibility rules, code/argument conventions, and L16 adoption
scope are documented in:

```text
StructuredApiMessageConvention.md
```

Use `MemStructuredMessage.Create` and constants in `MemMessageCodes` for
forward-only API message descriptors. Restore structured events use the same
code-plus-arguments shape through their existing `eventCode` and `details`
fields.

## Web alignment

The Web application already owns a typed TypeScript catalogue and browser-side
localisation runtime. The agreed phased bridge for consuming optional structured
API message descriptors without a React rewrite is documented in:

```text
WebLocalizationAlignment.md
```

This document is a design/migration note only. It does not make the Web depend
on .NET resources, does not establish the API as a translation service, and does
not change the Web language preference model. Reconfirm its Web file mapping
against the latest Web source before starting a React implementation slice.

