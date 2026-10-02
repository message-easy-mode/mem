import { FlaskConical, Laptop, Package } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"
import {
  runtimeIndicatorLabelKey,
  runtimeIndicatorShortLabelKey,
} from "../runtime-context-labels"
import { runtimeContextNeedsAttention } from "../runtime-context-state"
import { useRuntimeContext } from "../use-runtime-context"
import { RuntimeContextDetailsTrigger } from "./runtime-context-details"

function RuntimeIcon({ mode }: { mode: string }) {
  const className = "h-3.5 w-3.5 shrink-0"
  switch (mode) {
    case "local-development":
      return <Laptop className={className} aria-hidden="true" />
    case "containerized-development":
      return <Package className={className} aria-hidden="true" />
    case "automated-test":
      return <FlaskConical className={className} aria-hidden="true" />
    default:
      return <Laptop className={className} aria-hidden="true" />
  }
}

export function RuntimeContextIndicator() {
  const { t } = useI18n()
  const runtime = useRuntimeContext()
  const data = runtime.data

  if (
    !data?.showDevelopmentBanner ||
    runtimeContextNeedsAttention(data)
  ) {
    return null
  }

  return (
    <RuntimeContextDetailsTrigger>
      <Button
        type="button"
        variant="outline"
        size="sm"
        data-testid="runtime-context-indicator"
        aria-label={t("runtime.indicator.open", {
          runtime: t(runtimeIndicatorLabelKey(data.runtimeMode)),
        })}
        className={cn(
          "max-w-[15rem] shrink-0 border-sky-500/30 bg-sky-500/10 text-sky-700 shadow-none",
          "hover:border-sky-500/45 hover:bg-sky-500/15 hover:text-sky-800",
          "dark:text-sky-300 dark:hover:text-sky-200",
        )}
      >
        <RuntimeIcon mode={data.runtimeMode} />
        <span className="hidden truncate sm:inline">
          {t(runtimeIndicatorLabelKey(data.runtimeMode))}
        </span>
        <span className="sm:hidden">
          {t(runtimeIndicatorShortLabelKey(data.runtimeMode))}
        </span>
      </Button>
    </RuntimeContextDetailsTrigger>
  )
}
