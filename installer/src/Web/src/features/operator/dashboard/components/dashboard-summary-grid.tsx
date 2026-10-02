import { DatabaseBackup, Globe2, MessageSquarePlus, ServerCog } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { DashboardOverviewResponse } from "../api/dashboard.types"
import { getDashboardStateLabel, getDashboardTone } from "./dashboard-state"
import { DashboardMetricCard } from "./dashboard-metric-card"

type Props = {
  overview: DashboardOverviewResponse
}

export function DashboardSummaryGrid({ overview }: Props) {
  const { t } = useI18n()
  const publicAccessDisplayState =
    overview.publicAccess.state === "ready" &&
    overview.publicAccess.certificate.state === "renewing"
      ? "renewing"
      : overview.publicAccess.state
  const publicAccessState = getDashboardStateLabel(publicAccessDisplayState, t)
  const recoveryState = getDashboardStateLabel(overview.recovery.state, t)

  return (
    <div className="grid gap-3 md:grid-cols-2 2xl:grid-cols-4">
      <DashboardMetricCard
        title={t("dashboard.summary.chatServers.title")}
        value={String(overview.stacks.total)}
        helper={t("dashboard.summary.chatServers.helper")}
        icon={MessageSquarePlus}
        tone={overview.stacks.total > 0 ? "good" : "neutral"}
        pill={t("dashboard.summary.chatServers.pill")}
      />
      <DashboardMetricCard
        title={t("dashboard.summary.publicAccess.title")}
        value={publicAccessState}
        helper={publicAccessHelper(overview.publicAccess, t)}
        icon={Globe2}
        tone={getDashboardTone(overview.publicAccess.state)}
        pill={publicAccessState}
      />
      <DashboardMetricCard
        title={t("dashboard.summary.platformServices.title")}
        value={`${overview.platform.runningRequiredServiceCount}/${overview.platform.requiredServiceCount}`}
        helper={t("dashboard.summary.platformServices.helper")}
        icon={ServerCog}
        tone={getDashboardTone(overview.platform.state)}
        pill={getDashboardStateLabel(overview.platform.state, t)}
      />
      <DashboardMetricCard
        title={t("dashboard.summary.recovery.title")}
        value={recoveryState}
        helper={recoveryHelper(
          overview.recovery.managedStackCount,
          overview.recovery.stacksWithValidRecoveryPointCount,
          t,
        )}
        icon={DatabaseBackup}
        tone={getDashboardTone(overview.recovery.state)}
        pill={recoveryState}
      />
    </div>
  )
}

function publicAccessHelper(
  publicAccess: DashboardOverviewResponse["publicAccess"],
  t: ReturnType<typeof useI18n>["t"],
): string {
  if (publicAccess.certificate.state === "staging") {
    return t("dashboard.summary.publicAccess.stagingCertificate")
  }

  if (publicAccess.certificate.state === "renewing") {
    return publicAccess.mainDomain
      ? t("dashboard.summary.publicAccess.renewing", { domain: publicAccess.mainDomain })
      : t("dashboard.summary.publicAccess.renewingNoDomain")
  }

  return publicAccess.mainDomain
    ? t("dashboard.summary.publicAccess.configuredDomain", { domain: publicAccess.mainDomain })
    : t("dashboard.summary.publicAccess.noDomain")
}

function recoveryHelper(
  managedStackCount: number,
  stacksWithValidRecoveryPointCount: number,
  t: ReturnType<typeof useI18n>["t"],
): string {
  if (managedStackCount === 0) {
    return t("dashboard.summary.recovery.noManagedStacks")
  }

  return t("dashboard.summary.recovery.coverage", {
    covered: stacksWithValidRecoveryPointCount,
    total: managedStackCount,
  })
}
