import { useEffect, useMemo, useState, type PropsWithChildren } from "react"

import { I18nContext, type I18nContextValue } from "@/app/i18n/i18n-context"
import {
  getIntlLocale,
  LANGUAGE_STORAGE_KEY,
  resolveInitialLanguage,
  translate,
} from "@/app/i18n/i18n-core"

export function I18nProvider({ children }: PropsWithChildren) {
  const [language, setLanguageState] = useState(resolveInitialLanguage)

  useEffect(() => {
    document.documentElement.lang = language
    document.documentElement.dir = "ltr"

    try {
      window.localStorage.setItem(LANGUAGE_STORAGE_KEY, language)
    } catch {
      // Local storage is an optional convenience. The selected language remains active for this session.
    }
  }, [language])

  const value = useMemo<I18nContextValue>(
    () => ({
      language,
      intlLocale: getIntlLocale(language),
      setLanguage: setLanguageState,
      t: (key, values) => translate(language, key, values),
    }),
    [language],
  )

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>
}
