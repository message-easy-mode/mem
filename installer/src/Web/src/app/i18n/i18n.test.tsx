import { afterEach, describe, expect, it } from "vitest"
import { render, screen } from "@testing-library/react"

import { useI18n } from "@/app/i18n/i18n-context"
import {
  interpolate,
  LANGUAGE_STORAGE_KEY,
  selectPluralMessage,
  selectSupportedLanguage,
  translate,
} from "@/app/i18n/i18n-core"
import { I18nProvider } from "@/app/i18n/i18n-provider"
import type { TranslationKey } from "@/app/i18n/messages"

function LanguageProbe() {
  const { language, t } = useI18n()

  return (
    <div>
      <span data-testid="language">{language}</span>
      <span data-testid="message">{t("unlock.submit")}</span>
    </div>
  )
}

afterEach(() => {
  window.localStorage.clear()
  document.documentElement.lang = ""
})

describe("selectSupportedLanguage", () => {
  it("selects German from a regional browser preference", () => {
    expect(selectSupportedLanguage(["de-AT", "en-NZ"])).toBe("de")
  })

  it("falls back to English when no supported preference is present", () => {
    expect(selectSupportedLanguage(["fr-FR"])).toBe("en")
  })
})

describe("interpolate", () => {
  it("renders canonical and legacy placeholders without exposing template tokens", () => {
    expect(interpolate("{{count}}s", { count: 12 })).toBe("12s")
    expect(interpolate("{count}s ago", { count: 7 })).toBe("7s ago")
    expect(
      interpolate("{{minutes}}m {seconds}s", { minutes: 2, seconds: 4 }),
    ).toBe("2m 4s")
    expect(interpolate("{missing}", { count: 1 })).toBe("{missing}")
  })
})

describe("selectPluralMessage", () => {
  it("keeps plain strings and plural objects render-safe", () => {
    expect(selectPluralMessage("en", "Ready")).toBe("Ready")
    expect(
      selectPluralMessage("en", { other: "{{count}} records" }, { count: 1 }),
    ).toBe("{{count}} records")
    expect(
      selectPluralMessage("en", { one: "One record" }, { count: 2 }),
    ).toBe("One record")
  })

  it("uses a localized safe fallback for missing or malformed messages", () => {
    const throwingMessage = new Proxy({}, {
      get: () => {
        throw new Error("malformed translation object")
      },
    })

    expect(selectPluralMessage("en", undefined)).toBe("Text unavailable")
    expect(selectPluralMessage("de", { label: "unexpected" })).toBe(
      "Text nicht verfügbar",
    )
    expect(selectPluralMessage("en", throwingMessage)).toBe("Text unavailable")
    expect(
      translate("en", "missing.translation" as TranslationKey),
    ).toBe("Text unavailable")
  })
})

describe("Setup release localization", () => {
  it("keeps the development-only Setup preview explicit in English and German", () => {
    expect(translate("en", "setup.preview.title")).toBe(
      "Setup preview — development only",
    )
    expect(translate("de", "setup.preview.title")).toBe(
      "Einrichtungsvorschau — nur Entwicklung",
    )
    expect(translate("de", "navigation.setup.checkServer")).toBe("Server prüfen")
  })

  it("keeps every first-time Setup stage represented in both release languages", () => {
    const stageKeys: TranslationKey[] = [
      "setup.start.fresh.title",
      "setup.checks.title",
      "setup.domain.title",
      "setup.review.title",
      "setup.install.title",
      "setup.activity.title",
      "setup.verify.title",
      "setup.finish.title",
      "setup.troubleshooting.title",
    ]

    for (const key of stageKeys) {
      const english = translate("en", key)
      const german = translate("de", key)

      expect(english).not.toBe("Text unavailable")
      expect(german).not.toBe("Text nicht verfügbar")
    }

    // Some operator terms, such as "Domain", are intentionally identical in
    // English and German. Parity means every key is represented safely, not
    // that every translated string must have different spelling.
    expect(translate("de", "setup.install.start")).toBe("Plattform installieren")
    expect(translate("de", "setup.activity.npmSaveContinue")).toBe(
      "Zugangsdaten speichern und fortfahren",
    )
    expect(translate("de", "setup.troubleshooting.openDocs")).toBe(
      "Dokumentation zur Fehlerbehebung öffnen",
    )
    expect(translate("de", "setup.finish.finishOpenDashboard")).toBe(
      "Einrichtung abschließen und Dashboard öffnen",
    )
  })
})

describe("Diagnostics localization", () => {
  it("keeps the Logging Health and containment labels aligned in English and German", () => {
    expect(translate("en", "diagnostics.health.storageStatus")).toBe(
      "Storage capacity",
    )
    expect(translate("de", "diagnostics.health.storageStatus")).toBe(
      "Speicherkapazität",
    )
    expect(translate("en", "diagnostics.health.openSeq")).toBe(
      "Open advanced Seq search",
    )
    expect(translate("de", "diagnostics.health.openSeq")).toBe(
      "Erweiterte Seq-Suche öffnen",
    )
    expect(translate("en", "diagnostics.errorBoundary.sectionTitle")).toBe(
      "A diagnostics section could not be displayed",
    )
    expect(translate("de", "diagnostics.errorBoundary.sectionTitle")).toBe(
      "Ein Diagnosebereich konnte nicht angezeigt werden",
    )
    expect(
      translate("en", "diagnostics.incidents.eventsAvailable", { count: 22 }),
    ).toBe(
      "No incidents require attention. 22 technical events are available for review.",
    )
    expect(
      translate("de", "diagnostics.incidents.eventsAvailable", { count: 22 }),
    ).toBe(
      "Keine Vorfälle erfordern Aufmerksamkeit. 22 technische Ereignisse stehen zur Prüfung bereit.",
    )
    expect(translate("en", "diagnostics.events.loadedCount", { count: 2 })).toBe(
      "2 events loaded",
    )
    expect(translate("de", "diagnostics.events.loadedCount", { count: 2 })).toBe(
      "2 Ereignisse geladen",
    )
  })
})

describe("I18nProvider", () => {
  it("uses a saved language preference and updates the document language", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    render(
      <I18nProvider>
        <LanguageProbe />
      </I18nProvider>,
    )

    expect(screen.getByTestId("language")).toHaveTextContent("de")
    expect(screen.getByTestId("message")).toHaveTextContent("Control Plane entsperren")
    expect(document.documentElement.lang).toBe("de")
  })
})
