import { CircleAlert, ClipboardList, Server, CheckCircle2 } from "lucide-react"

import { formatNumber } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import type { UiLanguage } from "@/app/i18n/messages"
import { Card } from "@/components/ui/card"
import { cn } from "@/lib/utils"
import type { MigrationSessionInventorySummary } from "@/features/operator/migrations/api/migration-sessions"
import type {
  MigrationSessionActionFilter,
  MigrationSessionLifecycle,
} from "./migration-sessions-query"

type SummarySelection = {
  lifecycle: MigrationSessionLifecycle
  action: MigrationSessionActionFilter
}

type MigrationSessionsSummaryCardsProps = {
  summary: MigrationSessionInventorySummary | undefined
  lifecycle: MigrationSessionLifecycle
  action: MigrationSessionActionFilter
  includeArchived: boolean
  onSelect: (selection: SummarySelection) => void
}

type SummaryTone = "sky" | "violet" | "amber" | "emerald"

type SummaryCardProps = {
  icon: typeof ClipboardList
  tone: SummaryTone
  label: string
  value: number | undefined
  detail: string
  language: UiLanguage
  selected: boolean
  onClick: () => void
}

export function MigrationSessionsSummaryCards({
  summary,
  lifecycle,
  action,
  includeArchived,
  onSelect,
}: MigrationSessionsSummaryCardsProps) {
  const { language, t } = useI18n()

  return (
    <div className="space-y-2">
      <div className="migration-summary-grid">
        <SummaryCard
          icon={ClipboardList}
          tone="sky"
          label={t("migrationWorkspace.inventory.summary.sessions")}
          value={summary?.totalSessions}
          detail={t("migrationWorkspace.inventory.summary.sessionsDetail")}
          language={language}
          selected={!includeArchived && lifecycle === "all" && action === "all"}
          onClick={() => onSelect({ lifecycle: "all", action: "all" })}
        />
        <SummaryCard
          icon={Server}
          tone="violet"
          label={t("migrationWorkspace.inventory.summary.active")}
          value={summary?.activeCount}
          detail={t("migrationWorkspace.inventory.summary.activeDetail")}
          language={language}
          selected={!includeArchived && lifecycle === "active" && action === "all"}
          onClick={() => onSelect({ lifecycle: "active", action: "all" })}
        />
        <SummaryCard
          icon={CircleAlert}
          tone="amber"
          label={t("migrationWorkspace.inventory.summary.attention")}
          value={summary?.needsActionCount}
          detail={t("migrationWorkspace.inventory.summary.attentionDetail")}
          language={language}
          selected={!includeArchived && lifecycle === "all" && action === "review"}
          onClick={() => onSelect({ lifecycle: "all", action: "review" })}
        />
        <SummaryCard
          icon={CheckCircle2}
          tone="emerald"
          label={t("migrationWorkspace.inventory.summary.completed")}
          value={summary?.completedCount}
          detail={t("migrationWorkspace.inventory.summary.completedDetail")}
          language={language}
          selected={!includeArchived && lifecycle === "completed" && action === "all"}
          onClick={() => onSelect({ lifecycle: "completed", action: "all" })}
        />
      </div>
      <p className="text-xs text-muted-foreground">{t("migrationWorkspace.inventory.summary.scope")}</p>
    </div>
  )
}

function SummaryCard({
  icon: Icon,
  tone,
  label,
  value,
  detail,
  language,
  selected,
  onClick,
}: SummaryCardProps) {
  const toneClasses: Record<SummaryTone, string> = {
    sky: "bg-sky-500/10 text-sky-400 ring-1 ring-sky-400/20",
    violet: "bg-violet-500/10 text-violet-400 ring-1 ring-violet-400/20",
    amber: "bg-amber-500/10 text-amber-400 ring-1 ring-amber-400/20",
    emerald: "bg-emerald-500/10 text-emerald-400 ring-1 ring-emerald-400/20",
  }

  return (
    <Card className={cn("overflow-hidden py-0 transition-colors", selected && "border-primary/50 bg-primary/5")}>
      <button
        type="button"
        className="flex min-h-14 w-full items-center rounded-xl px-3 py-2 text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-wait"
        aria-pressed={selected}
        aria-label={`${label}: ${value === undefined ? "—" : formatNumber(value, language)}`}
        title={detail}
        disabled={value === undefined}
        onClick={onClick}
      >
        <div className="flex w-full min-w-0 items-center gap-2">
          <span className={cn("inline-flex size-7 shrink-0 items-center justify-center rounded-md", toneClasses[tone])}>
            <Icon className="size-3.5" aria-hidden="true" />
          </span>
          <span className="min-w-0 flex-1 break-words text-xs font-medium text-muted-foreground">{label}</span>
          <span className="shrink-0 text-2xl font-semibold leading-none tracking-tight text-foreground">
            {value === undefined ? "—" : formatNumber(value, language)}
          </span>
        </div>
      </button>
    </Card>
  )
}
