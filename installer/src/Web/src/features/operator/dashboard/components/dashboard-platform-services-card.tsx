import { Boxes, ServerCog } from "lucide-react"
import { useI18n } from "@/app/i18n/i18n-context"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type { DashboardPlatformSummary } from "../api/dashboard.types"
import {
  getDashboardServiceDescription,
  getDashboardServiceName,
  getDashboardStateLabel,
  getDashboardTone,
} from "./dashboard-state"
import { DashboardStatusPill } from "./dashboard-status-pill"

type Props = {
  platform: DashboardPlatformSummary
}

export function DashboardPlatformServicesCard({ platform }: Props) {
  const { t } = useI18n()
  const verificationLimitedCount = platform.services.filter(
    (service) =>
      service.requirement === "required" && service.state === "verification_limited",
  ).length

  return (
    <Card size="sm">
      <CardHeader className="pb-2">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex items-center gap-3">
            <div className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-indigo-500/20 bg-indigo-500/10 text-indigo-300">
              <Boxes className="h-4 w-4" />
            </div>
            <div>
              <CardTitle className="text-base">{t("dashboard.platform.title")}</CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">
                {t("dashboard.platform.summary", {
                  running: platform.runningRequiredServiceCount,
                  total: platform.requiredServiceCount,
                })}
              </p>
              {verificationLimitedCount > 0 && (
                <p className="mt-1 text-xs text-muted-foreground">
                  {verificationLimitedCount === 1
                    ? t("dashboard.platform.verificationLimited.one")
                    : t("dashboard.platform.verificationLimited.many", {
                        count: verificationLimitedCount,
                      })}
                </p>
              )}
            </div>
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-2">
        <div className="flex items-center justify-between rounded-xl border border-border bg-background/40 px-3 py-2 text-sm">
          <span className="flex items-center gap-2 text-muted-foreground">
            <ServerCog className="h-4 w-4" />
            {t("dashboard.platform.docker")}
          </span>
          <DashboardStatusPill tone={getDashboardTone(platform.docker.state)}>
            {getDashboardStateLabel(platform.docker.state, t)}
          </DashboardStatusPill>
        </div>

        <div className="divide-y divide-border overflow-hidden rounded-xl border border-border">
          {platform.services.map((service) => (
            <div key={service.key} className="flex items-center justify-between gap-3 px-3 py-2.5">
              <div className="min-w-0">
                <div className="text-sm font-medium">{getDashboardServiceName(service.key, t)}</div>
                <p className="mt-0.5 text-xs text-muted-foreground">
                  {getDashboardServiceDescription(service.key, t)}
                </p>
              </div>
              <DashboardStatusPill tone={getDashboardTone(service.state)}>
                {getDashboardStateLabel(service.state, t)}
              </DashboardStatusPill>
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  )
}
