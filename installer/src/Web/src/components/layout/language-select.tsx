import { Languages } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Select } from "@/components/ui/select"

export function LanguageSelect({ id = "mem-language" }: { id?: string }) {
  const { language, setLanguage, t } = useI18n()

  return (
    <div className="inline-flex items-center gap-2">
      <Languages aria-hidden className="h-4 w-4 text-muted-foreground" />
      <label htmlFor={id} className="sr-only">
        {t("language.label")}
      </label>
      <Select
        id={id}
        value={language}
        onChange={(event) => setLanguage(event.target.value as typeof language)}
        aria-label={t("language.label")}
        className="min-w-24 bg-background"
      >
        <option value="en">{t("language.english")}</option>
        <option value="de">{t("language.german")}</option>
      </Select>
    </div>
  )
}
