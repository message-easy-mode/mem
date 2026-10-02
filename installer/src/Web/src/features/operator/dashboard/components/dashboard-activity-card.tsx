import { Activity, ArrowUpRight, CheckCircle2, CircleAlert, Info, TriangleAlert } from "lucide-react"
import { Link } from "react-router-dom"

import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type { DashboardActivityItem, DashboardActivitySummary, DashboardTone } from "../api/dashboard.types"
import {
  getDashboardActivityDetail,
  getDashboardActivityRoute,
  getDashboardActivityTitle,
  getDashboardActivityTone,
} from "./dashboard-state"

type Props = {
  activity: DashboardActivitySummary
}

export function DashboardActivityCard({ activity }: Props) {
  const { language, t } = useI18n()
  const items = getNewestActivityItems(activity.items)

  return (
    <Card size="sm" role="region" aria-labelledby="dashboard-activity-title">
      <CardHeader className="pb-2">
        <div className="flex items-start gap-3">
          <div className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-sky-500/20 bg-sky-500/10 text-sky-300">
            <Activity className="h-4 w-4" />
          </div>
          <div>
            <CardTitle id="dashboard-activity-title" className="text-base">
              {t("dashboard.activity.title")}
            </CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("dashboard.activity.description")}
            </p>
          </div>
        </div>
      </CardHeader>
      <CardContent>
        {items.length === 0 ? (
          <div className="rounded-xl border border-dashed border-border bg-background/40 px-4 py-5">
            <p className="font-medium">{t("dashboard.activity.emptyTitle")}</p>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("dashboard.activity.emptyDescription")}
            </p>
          </div>
        ) : (
          <ol
            aria-label={t("dashboard.activity.title")}
            className="divide-y divide-border overflow-hidden rounded-xl border border-border"
          >
            {items.map((item) => (
              <DashboardActivityRow key={item.id} item={item} language={language} />
            ))}
          </ol>
        )}
      </CardContent>
    </Card>
  )
}

function DashboardActivityRow({
  item,
  language,
}: {
  item: DashboardActivityItem
  language: ReturnType<typeof useI18n>["language"]
}) {
  const { t } = useI18n()
  const tone = getDashboardActivityTone(item.severity)
  const route = getDashboardActivityRoute(item)
  const title = getDashboardActivityTitle(item, t)
  const detail = getDashboardActivityDetail(item, t)

  return (
    <li className="grid grid-cols-[auto_minmax(0,1fr)] items-start gap-x-3 gap-y-1 px-3 py-2 sm:grid-cols-[auto_minmax(0,1fr)_auto] sm:px-3">
      <div className={[activityIconClassName(tone), "row-span-2 sm:row-span-1"].join(" ")}>
        <ActivityStatusIcon tone={tone} />
      </div>
      <div className="min-w-0 flex-1">
        {route ? (
          <Link to={route} className="inline-flex items-center gap-1 text-sm font-medium hover:underline">
            {title}
            <ArrowUpRight className="h-3.5 w-3.5 text-muted-foreground" />
          </Link>
        ) : (
          <div className="text-sm font-medium">{title}</div>
        )}
        {detail ? <p className="mt-1 text-xs text-muted-foreground">{detail}</p> : null}
      </div>
      <time
        className="col-start-2 text-xs text-muted-foreground sm:col-start-3 sm:row-start-1 sm:shrink-0 sm:text-right"
        dateTime={item.occurredAtUtc}
      >
        {formatActivityTime(item.occurredAtUtc, language, t)}
      </time>
    </li>
  )
}

function ActivityStatusIcon({ tone }: { tone: DashboardTone }) {
  switch (tone) {
    case "danger":
      return <CircleAlert className="h-4 w-4" />
    case "warning":
      return <TriangleAlert className="h-4 w-4" />
    case "good":
      return <CheckCircle2 className="h-4 w-4" />
    default:
      return <Info className="h-4 w-4" />
  }
}

function activityIconClassName(tone: DashboardTone): string {
  switch (tone) {
    case "danger":
      return "mt-0.5 inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-red-500/20 bg-red-500/10 text-red-300"
    case "warning":
      return "mt-0.5 inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-amber-500/20 bg-amber-500/10 text-amber-300"
    case "good":
      return "mt-0.5 inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-emerald-500/20 bg-emerald-500/10 text-emerald-300"
    default:
      return "mt-0.5 inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-border bg-background/60 text-muted-foreground"
  }
}

function getNewestActivityItems(items: DashboardActivityItem[]): DashboardActivityItem[] {
  return [...items].sort((left, right) => {
    const timestampDifference = toTimestamp(right.occurredAtUtc) - toTimestamp(left.occurredAtUtc)
    return timestampDifference !== 0 ? timestampDifference : left.id.localeCompare(right.id)
  })
}

function toTimestamp(value: string): number {
  const timestamp = Date.parse(value)
  return Number.isFinite(timestamp) ? timestamp : 0
}

function formatActivityTime(
  value: string,
  language: ReturnType<typeof useI18n>["language"],
  t: ReturnType<typeof useI18n>["t"],
): string {
  try {
    return formatDateTime(value, language)
  } catch {
    return t("dashboard.activity.timeUnavailable")
  }
}
