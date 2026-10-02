import { ArchiveRestore, Clock3, DatabaseBackup, ShieldCheck } from "lucide-react"
import { Link } from "react-router-dom"

import { formatBytes, formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type { DashboardRecoverySummary } from "../api/dashboard.types"
import { getDashboardStateLabel, getDashboardTone } from "./dashboard-state"
import { DashboardStatusPill } from "./dashboard-status-pill"

type Props = {
  recovery: DashboardRecoverySummary
}

export function DashboardRecoveryCard({ recovery }: Props) {
  const { language, t } = useI18n()
  const primaryRoute = recovery.priorityRestoreSessionId
    ? `/restores/${encodeURIComponent(recovery.priorityRestoreSessionId)}`
    : "/backups"
  const primaryLabel = recovery.priorityRestoreSessionId
    ? t("dashboard.action.openRestoreWorkspace")
    : t("dashboard.action.openBackups")

  return (
    <Card size="sm" role="region" aria-labelledby="dashboard-recovery-title">
      <CardHeader className="pb-2">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex items-center gap-3">
            <div className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-violet-500/20 bg-violet-500/10 text-violet-300">
              <ArchiveRestore className="h-4 w-4" />
            </div>
            <div>
              <CardTitle id="dashboard-recovery-title" className="text-base">
                {t("dashboard.recovery.title")}
              </CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">
                {recovery.managedStackCount === 0
                  ? t("dashboard.recovery.noStacks")
                  : t("dashboard.recovery.coverage", {
                      covered: recovery.stacksWithValidRecoveryPointCount,
                      total: recovery.managedStackCount,
                    })}
              </p>
            </div>
          </div>
          <DashboardStatusPill tone={getDashboardTone(recovery.state)}>
            {getDashboardStateLabel(recovery.state, t)}
          </DashboardStatusPill>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          <RecoveryMetric
            icon={ShieldCheck}
            label={t("dashboard.recovery.validCatalog")}
            value={`${recovery.validCatalogEntryCount}/${recovery.catalogEntryCount}`}
            detail={t("dashboard.recovery.catalogDetail", {
              warning: recovery.warningCatalogEntryCount,
              invalid: recovery.invalidCatalogEntryCount,
            })}
          />
          <RecoveryMetric
            icon={Clock3}
            label={t("dashboard.recovery.latestBackup")}
            value={
              recovery.latestCapturedAtUtc
                ? formatDateTime(recovery.latestCapturedAtUtc, language)
                : t("dashboard.recovery.noneRecorded")
            }
            detail={
              recovery.totalPayloadBytes === null
                ? t("dashboard.recovery.payloadUnknown")
                : t("dashboard.recovery.payloadSize", {
                    value: formatBytes(recovery.totalPayloadBytes, language),
                  })
            }
          />
          <RecoveryMetric
            icon={DatabaseBackup}
            label={t("dashboard.recovery.restoreWork")}
            className="sm:col-span-2 lg:col-span-1"
            value={String(recovery.activeRestoreCount + recovery.attentionRestoreCount)}
            detail={t("dashboard.recovery.restoreDetail", {
              active: recovery.activeRestoreCount,
              attention: recovery.attentionRestoreCount,
            })}
          />
        </div>

        <div className="flex flex-wrap gap-2">
          <Button asChild>
            <Link to={primaryRoute}>{primaryLabel}</Link>
          </Button>
          <Button variant="outline" asChild>
            <Link to="/restores">{t("dashboard.action.openRestores")}</Link>
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}

function RecoveryMetric({
  icon: Icon,
  label,
  value,
  detail,
  className,
}: {
  icon: typeof ShieldCheck
  label: string
  value: string
  detail: string
  className?: string
}) {
  return (
    <div
      className={[
        "rounded-xl border border-border bg-background/40 p-3",
        className,
      ]
        .filter(Boolean)
        .join(" ")}
    >
      <div className="flex items-center gap-2 text-xs font-medium uppercase tracking-wide text-muted-foreground">
        <Icon className="h-3.5 w-3.5" />
        {label}
      </div>
      <div className="mt-2 text-sm font-semibold">{value}</div>
      <p className="mt-1 text-xs text-muted-foreground">{detail}</p>
    </div>
  )
}
