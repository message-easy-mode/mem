import { AlertTriangle, Info, TriangleAlert } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import type { DashboardCapabilities, DashboardNotice } from "../api/dashboard.types"
import {
  getDashboardActionLabel,
  getDashboardActionRoute,
  getDashboardNoticeDescription,
  getDashboardNoticeTitle,
  getDashboardSeverityTone,
} from "./dashboard-state"

type Props = {
  notices: DashboardNotice[]
  capabilities: DashboardCapabilities
}

export function DashboardNotices({ notices, capabilities }: Props) {
  if (notices.length === 0) {
    return null
  }

  return (
    <div className="grid gap-2.5">
      {notices.map((notice) => (
        <DashboardNoticeRow key={notice.id} notice={notice} capabilities={capabilities} />
      ))}
    </div>
  )
}

function DashboardNoticeRow({ notice, capabilities }: { notice: DashboardNotice; capabilities: DashboardCapabilities }) {
  const { t } = useI18n()
  const tone = getDashboardSeverityTone(notice.severity)
  const route = notice.action ? getDashboardActionRoute(notice.action, capabilities) : null
  const Icon = tone === "danger" ? TriangleAlert : tone === "warning" ? AlertTriangle : Info

  return (
    <section className={noticeClassName(tone)}>
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex gap-2.5">
          <Icon className="mt-0.5 h-4 w-4 shrink-0" />
          <div>
            <h2 className="text-sm font-semibold">{getDashboardNoticeTitle(notice, t)}</h2>
            <p className="mt-0.5 text-sm text-muted-foreground">
              {getDashboardNoticeDescription(notice, t)}
            </p>
          </div>
        </div>
        {route && notice.action ? (
          <Button variant="outline" size="sm" asChild>
            <Link to={route}>{getDashboardActionLabel(notice.action, t)}</Link>
          </Button>
        ) : null}
      </div>
    </section>
  )
}

function noticeClassName(tone: ReturnType<typeof getDashboardSeverityTone>): string {
  switch (tone) {
    case "danger":
      return "rounded-2xl border border-red-500/20 bg-red-500/10 px-4 py-3 text-red-100"
    case "warning":
      return "rounded-2xl border border-amber-500/20 bg-amber-500/10 px-4 py-3 text-amber-100"
    default:
      return "rounded-2xl border border-border bg-card/95 px-4 py-3"
  }
}
