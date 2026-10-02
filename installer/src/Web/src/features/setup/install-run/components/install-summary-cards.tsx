import { useI18n } from "@/app/i18n/i18n-context"
import type { WorkflowStep } from "../api/install.types"

function SummaryTile({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border/80 bg-background/30 p-4">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className="mt-1 text-sm font-medium">{value}</div>
    </div>
  )
}

export function InstallSummaryCards({ steps }: { steps: WorkflowStep[] }) {
  const { t } = useI18n()
  const succeededCount = steps.filter((x) => x.status === "Succeeded").length
  const runningCount = steps.filter((x) => x.status === "Running").length
  const waitingCount = steps.filter((x) => x.status === "WaitingForUser").length
  const failedCount = steps.filter((x) => x.status === "Failed").length

  return (
    <div className="grid gap-3 md:grid-cols-4">
      <SummaryTile label={t("setup.activity.completedSteps")} value={String(succeededCount)} />
      <SummaryTile label={t("setup.activity.runningSteps")} value={String(runningCount)} />
      <SummaryTile label={t("setup.activity.actionRequired")} value={String(waitingCount)} />
      <SummaryTile label={t("setup.activity.failedSteps")} value={String(failedCount)} />
    </div>
  )
}
