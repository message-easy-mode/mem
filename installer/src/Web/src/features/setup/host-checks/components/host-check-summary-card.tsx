import { AlertTriangle, CheckCircle2, CircleHelp, CircleSlash, Info, XCircle } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"

import type { HostCheckRunResponse } from "../api/host-checks.types"

export function HostCheckSummaryCard({ run }: { run: HostCheckRunResponse }) {
  const { t } = useI18n()
  const blocking = run.groups.flatMap((group) =>
    group.checks.filter((check) => check.blocking && check.status !== "Pass"),
  ).length

  const counts = [
    { key: "setup.checks.summary.passed" as const, value: run.summary.passed, icon: CheckCircle2 },
    { key: "setup.checks.summary.warnings" as const, value: run.summary.warnings, icon: AlertTriangle },
    { key: "setup.checks.summary.failed" as const, value: run.summary.failed, icon: XCircle },
    { key: "setup.checks.summary.unavailable" as const, value: run.summary.unavailable, icon: Info },
    { key: "setup.checks.summary.skipped" as const, value: run.summary.skipped, icon: CircleSlash },
    { key: "setup.checks.summary.unknown" as const, value: run.summary.unknown, icon: CircleHelp },
  ].filter((item) => item.key === "setup.checks.summary.passed" || item.value > 0)

  return (
    <Card className={blocking > 0 ? "border-red-500/30" : "border-emerald-500/20"}>
      <CardHeader className="pb-3">
        <CardTitle>
          {blocking > 0 ? t("setup.checks.needAttention") : t("setup.checks.complete")}
        </CardTitle>
        <CardDescription>
          {blocking > 0
            ? t("setup.checks.blocking", { count: blocking })
            : t("setup.checks.noBlockers")}
        </CardDescription>
      </CardHeader>

      <CardContent className="flex flex-wrap gap-2">
        {counts.map(({ key, value, icon: Icon }) => {
          const label = t(key)
          return (
            <div
              key={key}
              aria-label={`${value} ${label}`}
              className="inline-flex items-center gap-2 rounded-full border border-border bg-muted/30 px-3 py-1.5 text-sm"
            >
              <Icon className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
              <span className="font-medium">{value}</span>
              <span className="text-muted-foreground">{label}</span>
            </div>
          )
        })}
      </CardContent>
    </Card>
  )
}
