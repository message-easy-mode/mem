import { useI18n } from "@/app/i18n/i18n-context"
import type { WorkflowStep } from "../api/install.types"
import { InstallStepCard } from "./install-step-card"

export function InstallStepList({ steps }: { steps: WorkflowStep[] }) {
  const { t } = useI18n()
  if (steps.length === 0) {
    return (
      <div className="rounded-xl border border-border bg-card p-4 text-sm text-muted-foreground">
        {t("setup.activity.noSteps")}
      </div>
    )
  }

  return (
    <div className="space-y-3">
      {steps.map((step) => (
        <InstallStepCard key={step.order} step={step} />
      ))}
    </div>
  )
}
