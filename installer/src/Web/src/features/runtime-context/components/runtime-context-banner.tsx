import { AlertTriangle } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { runtimeModeKey, stateRootLabelKey, uiDeliveryKey } from "../runtime-context-labels"
import { runtimeContextNeedsAttention } from "../runtime-context-state"
import { useRuntimeContext } from "../use-runtime-context"

export function RuntimeContextWarningBanner() {
  const { t } = useI18n()
  const runtime = useRuntimeContext()
  const data = runtime.data

  if (!data || !runtimeContextNeedsAttention(data)) {
    return null
  }

  const instance = data.controlPlaneInstanceId.slice(0, 8)

  return (
    <div
      role="alert"
      data-testid="runtime-context-warning-banner"
      className="border-b border-amber-500/60 bg-amber-500/[0.08] px-4 py-2.5 text-sm text-foreground sm:px-6 lg:px-8"
    >
      <div className="mx-auto flex max-w-[1760px] flex-wrap items-center gap-x-3 gap-y-2">
        <span className="flex items-center gap-2 font-semibold">
          <AlertTriangle className="h-4 w-4 shrink-0 text-amber-500" aria-hidden="true" />
          {t("runtime.warning.title")}
        </span>
        <Badge variant="outline" className="border-amber-500/40 bg-amber-500/10 text-foreground">
          {t(runtimeModeKey(data.runtimeMode))}
        </Badge>
        <span className="min-w-0 flex-1 text-muted-foreground">
          {t("runtime.warning.description", {
            ui: t(uiDeliveryKey(data.uiDeliveryMode)),
            state: t(stateRootLabelKey(data.stateRootKind, data.stateRootProfile)),
            instance,
          })}
        </span>
        <span className="font-medium text-amber-700 dark:text-amber-300">
          {t("runtime.warning.message")}
        </span>
        <Button asChild type="button" variant="outline" size="sm" className="ml-auto border-amber-500/30">
          <Link to="/diagnostics">{t("runtime.warning.action")}</Link>
        </Button>
      </div>
    </div>
  )
}
