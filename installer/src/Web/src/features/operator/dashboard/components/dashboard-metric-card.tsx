import type { LucideIcon } from "lucide-react"
import type { DashboardTone } from "../api/dashboard.types"
import { DashboardStatusPill } from "./dashboard-status-pill"

type Props = {
  title: string
  value: string
  helper: string
  icon: LucideIcon
  tone?: DashboardTone
  pill?: string
}

export function DashboardMetricCard({
  title,
  value,
  helper,
  icon: Icon,
  tone = "neutral",
  pill,
}: Props) {
  return (
    <section className="rounded-2xl border border-border bg-card/95 p-3 shadow-sm">
      <div className="flex items-start justify-between gap-3">
        <div className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border bg-background/60 text-muted-foreground">
          <Icon className="h-4 w-4" />
        </div>
        {pill && <DashboardStatusPill tone={tone}>{pill}</DashboardStatusPill>}
      </div>
      <div className="mt-3 text-2xl font-semibold leading-tight tracking-tight">{value}</div>
      <div className="mt-1 text-sm font-medium">{title}</div>
      <p className="mt-1.5 text-xs leading-relaxed text-muted-foreground">{helper}</p>
    </section>
  )
}
