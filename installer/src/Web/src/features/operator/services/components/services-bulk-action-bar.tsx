import { Play, Square, X } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"

type Props = {
  selectedCount: number
  canStart: boolean
  canStop: boolean
  busy?: boolean
  onStartSelected: () => void
  onStopSelected: () => void
  onClearSelection: () => void
}

export function ServicesBulkActionBar({
  selectedCount,
  canStart,
  canStop,
  busy = false,
  onStartSelected,
  onStopSelected,
  onClearSelection,
}: Props) {
  const { t } = useI18n()

  if (selectedCount === 0) return null

  return (
    <div className="sticky top-[88px] z-30 rounded-2xl border border-border bg-card/95 p-3 shadow-sm backdrop-blur">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="text-sm">
          <span className="font-medium text-foreground">
            {t("services.bulk.selected", { count: selectedCount })}
          </span>
        </div>

        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            size="sm"
            disabled={!canStart || busy}
            onClick={onStartSelected}
          >
            <Play className="mr-2 h-4 w-4" />
            {t("services.bulk.startSelected")}
          </Button>

          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={!canStop || busy}
            onClick={onStopSelected}
          >
            <Square className="mr-2 h-4 w-4" />
            {t("services.bulk.stopSelected")}
          </Button>

          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={busy}
            onClick={onClearSelection}
          >
            <X className="mr-2 h-4 w-4" />
            {t("services.bulk.clear")}
          </Button>
        </div>
      </div>
    </div>
  )
}
