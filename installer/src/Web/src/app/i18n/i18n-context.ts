import { createContext, useContext } from "react"

import type { TranslationKey, TranslationValues, UiLanguage } from "@/app/i18n/messages"

export type I18nContextValue = {
  language: UiLanguage
  intlLocale: string
  setLanguage: (language: UiLanguage) => void
  t: (key: TranslationKey, values?: TranslationValues) => string
}

export const I18nContext = createContext<I18nContextValue | undefined>(undefined)

export function useI18n(): I18nContextValue {
  const context = useContext(I18nContext)

  if (!context) {
    throw new Error("useI18n must be used within I18nProvider")
  }

  return context
}
