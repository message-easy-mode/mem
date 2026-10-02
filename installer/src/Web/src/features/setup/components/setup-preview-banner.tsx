import { Eye } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"

export function SetupPreviewBanner({ runtimeMode }: { runtimeMode: string }) {
  const { t } = useI18n()

  return (
    <Alert className="mb-6 border-amber-500/30 bg-amber-500/5">
      <Eye className="h-4 w-4 text-amber-300" aria-hidden="true" />
      <AlertTitle>{t("setup.preview.title")}</AlertTitle>
      <AlertDescription className="space-y-1">
        <p>{t("setup.preview.description")}</p>
        <p className="font-mono text-xs text-muted-foreground">
          {t("setup.preview.runtime", { runtime: runtimeMode })}
        </p>
      </AlertDescription>
    </Alert>
  )
}
