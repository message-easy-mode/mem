import { RefreshCw, ShieldCheck } from "lucide-react"
import { useLocation } from "react-router-dom"
import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { DashboardHostCard } from "./components/dashboard-host-card"
import { useRuntimeContext } from "@/features/runtime-context/use-runtime-context"
import { DashboardIncidentStrip } from "./components/dashboard-incident-strip"
import { DashboardManagedStacksCard } from "./components/dashboard-managed-stacks-card"
import { DashboardOnboardingCard } from "./components/dashboard-onboarding-card"
import { DashboardPlatformServicesCard } from "./components/dashboard-platform-services-card"
import { DashboardQuickActions } from "./components/dashboard-quick-actions"
import { DashboardRecoveryCard } from "./components/dashboard-recovery-card"
import {
  DashboardLoadErrorState,
  DashboardLoadingState,
  DashboardRefreshErrorNotice,
} from "./components/dashboard-state-view"
import { DashboardSummaryGrid } from "./components/dashboard-summary-grid"
import { useDashboardOverview } from "./hooks/use-dashboard-overview"

export function DashboardPage() {
  const { t } = useI18n()
  const location = useLocation()
  const dashboard = useDashboardOverview()
  const runtime = useRuntimeContext()
  const redirectedFromLockedSetup = Boolean(
    (location.state as { firstTimeSetupLocked?: boolean } | null)?.firstTimeSetupLocked,
  )
  const data = dashboard.data
  const refresh = () => void dashboard.refetch()

  if (dashboard.isLoading && !data) {
    return (
      <div className="space-y-5">
        <PageHeader refreshing={dashboard.isFetching} onRefresh={refresh} />
        <DashboardLoadingState />
      </div>
    )
  }

  if (!data) {
    return (
      <div className="space-y-5">
        <PageHeader refreshing={dashboard.isFetching} onRefresh={refresh} />
        <DashboardLoadErrorState
          error={dashboard.error}
          onRetry={refresh}
          retrying={dashboard.isFetching}
        />
      </div>
    )
  }

  return (
    <div className="space-y-5">
      <PageHeader refreshing={dashboard.isFetching} onRefresh={refresh} />

      {redirectedFromLockedSetup ? (
        <Alert>
          <ShieldCheck className="h-4 w-4" />
          <AlertTitle>{t("startup.setupLockedTitle")}</AlertTitle>
          <AlertDescription>{t("startup.setupLockedDescription")}</AlertDescription>
        </Alert>
      ) : null}

      {dashboard.isError ? (
        <DashboardRefreshErrorNotice
          error={dashboard.error}
          generatedAtUtc={data.generatedAtUtc}
          onRetry={refresh}
          retrying={dashboard.isFetching}
        />
      ) : null}

      <DashboardIncidentStrip />
      <DashboardSummaryGrid overview={data} />

      <div className="grid gap-4 2xl:grid-cols-[minmax(0,1.35fr)_minmax(360px,1fr)]">
        {data.stacks.total === 0 ? (
          <DashboardOnboardingCard
            onboarding={data.onboarding}
            capabilities={data.capabilities}
          />
        ) : (
          <DashboardManagedStacksCard stacks={data.stacks} />
        )}
        <DashboardPlatformServicesCard platform={data.platform} />
      </div>

      <div className="grid gap-4 2xl:grid-cols-[minmax(0,1.35fr)_minmax(360px,1fr)]">
        <DashboardRecoveryCard recovery={data.recovery} />
        <DashboardQuickActions capabilities={data.capabilities} />
      </div>

      <DashboardHostCard
        host={data.host}
        exposure={runtime.data?.controlPlaneExposure ?? null}
      />
    </div>
  )
}

function PageHeader({
  refreshing,
  onRefresh,
}: {
  refreshing: boolean
  onRefresh: () => void
}) {
  const { t } = useI18n()

  return (
    <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">{t("dashboard.title")}</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          {t("dashboard.description")}
        </p>
      </div>
      <div className="flex flex-wrap gap-2">
        <Button
          variant="outline"
          size="sm"
          type="button"
          onClick={onRefresh}
          disabled={refreshing}
        >
          <RefreshCw className={refreshing ? "mr-2 h-4 w-4 animate-spin" : "mr-2 h-4 w-4"} />
          {t("dashboard.refresh")}
        </Button>
      </div>
    </div>
  )
}
