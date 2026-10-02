import { Info } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"
import { useRuntimeContext } from "../use-runtime-context"
import { RuntimeContextDetailsTrigger } from "./runtime-context-details"
import { getMemReleaseCodename } from "../runtime-release-identity"

export function RuntimeContextSystemInformation({
  className,
}: {
  className?: string
}) {
  const { t } = useI18n()
  const runtime = useRuntimeContext()
  const data = runtime.data
  const commit = data?.commit?.trim() || null
  const codename = getMemReleaseCodename(data?.version)

  return (
    <div
      className={cn("border-t border-border pt-3", className)}
      data-testid="runtime-system-information"
    >
      {data ? (
        <div className="px-2 pb-2 text-[0.7rem] leading-relaxed text-muted-foreground">
          <div className="flex flex-wrap items-baseline gap-x-1.5">
            <span>{t("runtime.systemInfo.version", { version: data.version })}</span>
            {codename ? (
              <span className="font-medium text-muted-foreground/80">· {codename}</span>
            ) : null}
          </div>
          {commit ? (
            <div className="font-mono">
              {t("runtime.systemInfo.build", { build: commit.slice(0, 12) })}
            </div>
          ) : null}
        </div>
      ) : null}

      <RuntimeContextDetailsTrigger>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="w-full cursor-pointer justify-start border-border bg-card/60 px-2 text-muted-foreground shadow-sm hover:bg-muted hover:text-foreground"
        >
          <Info className="h-4 w-4" aria-hidden="true" />
          {t("runtime.systemInfo.open")}
        </Button>
      </RuntimeContextDetailsTrigger>
    </div>
  )
}
