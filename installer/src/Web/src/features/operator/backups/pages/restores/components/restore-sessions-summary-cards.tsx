import {
  CircleAlert,
  ClipboardList,
  Globe2,
  ServerCog,
} from "lucide-react"

import { formatNumber } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import type { UiLanguage } from "@/app/i18n/messages"
import { Card, CardContent } from "@/components/ui/card"
import type { RestoreAttemptListSummary } from "@/features/operator/backups/api/types/backups.types"

// The values remain the canonical server-projected counts. This component only
// controls their presentation so the restore-session inventory remains the
// dominant content on the page.
type RestoreSessionsSummaryCardsProps = {
  summary: RestoreAttemptListSummary | undefined
  hasFilters: boolean
}

type SummaryTone = "sky" | "violet" | "emerald" | "amber"

type SummaryMetricProps = {
  icon: typeof ClipboardList
  tone: SummaryTone
  label: string
  value: number
  detail: string
  language: UiLanguage
}

export function RestoreSessionsSummaryCards({
  summary,
  hasFilters,
}: RestoreSessionsSummaryCardsProps) {
  const { language, t } = useI18n()
  const summaryLabel = hasFilters
    ? t("restoreSessions.summary.matching")
    : t("restoreSessions.summary.all")

  return (
    <section aria-label={summaryLabel}>
      <Card className="overflow-hidden py-0">
        <CardContent className="grid gap-2 p-2 sm:grid-cols-2 xl:grid-cols-4">
          <RestoreSessionsSummaryMetric
            icon={ClipboardList}
            tone="sky"
            label={summaryLabel}
            value={summary?.totalSessions ?? 0}
            detail={
              hasFilters
                ? t("restoreSessions.summary.matchingDetail")
                : t("restoreSessions.summary.allDetail")
            }
            language={language}
          />
          <RestoreSessionsSummaryMetric
            icon={ServerCog}
            tone="violet"
            label={t("restoreSessions.summary.production")}
            value={summary?.productionRecreateCount ?? 0}
            detail={t("restoreSessions.summary.productionDetail")}
            language={language}
          />
          <RestoreSessionsSummaryMetric
            icon={Globe2}
            tone="emerald"
            label={t("restoreSessions.summary.publiclyVerified")}
            value={summary?.publiclyVerifiedCount ?? 0}
            detail={t("restoreSessions.summary.publiclyVerifiedDetail")}
            language={language}
          />
          <RestoreSessionsSummaryMetric
            icon={CircleAlert}
            tone="amber"
            label={t("restoreSessions.summary.needsAction")}
            value={summary?.needsActionCount ?? 0}
            detail={t("restoreSessions.summary.needsActionDetail")}
            language={language}
          />
        </CardContent>
      </Card>
    </section>
  )
}

function RestoreSessionsSummaryMetric({
  icon: Icon,
  tone,
  label,
  value,
  detail,
  language,
}: SummaryMetricProps) {
  const toneClasses: Record<SummaryTone, string> = {
    sky: "bg-sky-500/10 text-sky-400 ring-1 ring-sky-400/20",
    violet: "bg-violet-500/10 text-violet-400 ring-1 ring-violet-400/20",
    emerald: "bg-emerald-500/10 text-emerald-400 ring-1 ring-emerald-400/20",
    amber: "bg-amber-500/10 text-amber-400 ring-1 ring-amber-400/20",
  }

  return (
    <div className="min-w-0 rounded-lg bg-muted/30 px-3 py-3">
      <div className="flex items-center gap-2 text-xs font-medium text-muted-foreground">
        <span
          className={`inline-flex size-7 shrink-0 items-center justify-center rounded-md ${toneClasses[tone]}`}
        >
          <Icon className="size-3.5" aria-hidden="true" />
        </span>
        <span className="truncate">{label}</span>
      </div>
      <div className="mt-2 text-2xl font-semibold leading-none tracking-tight text-foreground">
        {formatNumber(value, language)}
      </div>
      <p className="mt-1 text-xs leading-4 text-muted-foreground">{detail}</p>
    </div>
  )
}
