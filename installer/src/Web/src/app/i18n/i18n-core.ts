import {
  translationCatalog,
  type TranslationKey,
  type TranslationValues,
  type UiLanguage,
} from "@/app/i18n/messages"

export const LANGUAGE_STORAGE_KEY = "mem.ui-language"

const languageToIntlLocale: Readonly<Record<UiLanguage, string>> = {
  en: "en-NZ",
  de: "de-DE",
}

const unavailableMessage: Readonly<Record<UiLanguage, string>> = {
  en: "Text unavailable",
  de: "Text nicht verfügbar",
}

function isUiLanguage(value: string | null | undefined): value is UiLanguage {
  return value === "en" || value === "de"
}

export function selectSupportedLanguage(languages: readonly string[]): UiLanguage {
  for (const language of languages) {
    const primaryLanguage = language.toLowerCase().split("-")[0]

    if (primaryLanguage === "de") {
      return "de"
    }

    if (primaryLanguage === "en") {
      return "en"
    }
  }

  return "en"
}

function readStoredLanguage(): UiLanguage | undefined {
  if (typeof window === "undefined") {
    return undefined
  }

  try {
    const storedLanguage = window.localStorage.getItem(LANGUAGE_STORAGE_KEY)
    return isUiLanguage(storedLanguage) ? storedLanguage : undefined
  } catch {
    return undefined
  }
}

function readBrowserLanguage(): UiLanguage {
  if (typeof navigator === "undefined") {
    return "en"
  }

  return selectSupportedLanguage(navigator.languages ?? [navigator.language])
}

export function resolveInitialLanguage(): UiLanguage {
  return readStoredLanguage() ?? readBrowserLanguage()
}

export function selectPluralMessage(
  language: UiLanguage,
  message: unknown,
  values?: TranslationValues,
): string {
  if (typeof message === "string") {
    return message
  }

  if (!message || typeof message !== "object" || Array.isArray(message)) {
    return unavailableMessage[language]
  }

  try {
    const pluralMessage = message as Readonly<Record<string, unknown>>
    const candidateCategories: string[] = []
    const count = values?.count

    if (typeof count === "number" && Number.isFinite(count)) {
      candidateCategories.push(
        new Intl.PluralRules(languageToIntlLocale[language]).select(count),
      )
    }

    candidateCategories.push("other", "one", "zero", "two", "few", "many")

    for (const category of new Set(candidateCategories)) {
      const candidate = pluralMessage[category]
      if (typeof candidate === "string") {
        return candidate
      }
    }
  } catch {
    // A malformed runtime value must never replace the current route with an exception screen.
  }

  return unavailableMessage[language]
}

export function interpolate(template: string, values?: TranslationValues): string {
  if (!values) {
    return template
  }

  // Canonical MEM catalog entries use {{name}}. Keep that as the authored
  // format, while also accepting the bounded legacy {name} form. Historical
  // catalog content already contains that form, and live restore-progress
  // validation proved that a single-brace token can otherwise survive into
  // operator-visible UI. Unknown placeholders remain untouched.
  return template.replace(
    /{{\s*([\w.-]+)\s*}}|{\s*([\w.-]+)\s*}/g,
    (match, canonicalKey: string | undefined, legacyKey: string | undefined) => {
      const key = canonicalKey ?? legacyKey
      if (!key) {
        return match
      }

      const value = values[key]
      return value === undefined ? match : String(value)
    },
  )
}

export function translate(
  language: UiLanguage,
  key: TranslationKey,
  values?: TranslationValues,
): string {
  const localizedCatalog = translationCatalog[language] as Partial<
    Readonly<Record<TranslationKey, unknown>>
  >
  const englishCatalog = translationCatalog.en as Partial<
    Readonly<Record<TranslationKey, unknown>>
  >
  const message = localizedCatalog[key] ?? englishCatalog[key]
  return interpolate(selectPluralMessage(language, message, values), values)
}

export function getIntlLocale(language: UiLanguage): string {
  return languageToIntlLocale[language]
}
