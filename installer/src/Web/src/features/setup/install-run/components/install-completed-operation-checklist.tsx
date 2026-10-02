import { CheckCircle2, ChevronDown } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { cn } from "@/lib/utils"

import type { WorkflowStep } from "../api/install.types"
import { InstallProgressPhaseList } from "./install-progress-phase-list"

export function InstallCompletedOperationChecklist({
  steps,
  className,
}: {
  steps: WorkflowStep[]
  className?: string
}) {
  const { t } = useI18n()
  const completedSteps = steps
    .filter((step) => step.status === "Succeeded")
    .sort((left, right) => left.order - right.order)

  if (completedSteps.length === 0) return null

  return (
    <section
      role="region"
      aria-label={t("setup.activity.completedOperationsTitle")}
      className={cn(
        "rounded-xl border border-emerald-500/25 bg-background/40 p-4",
        className,
      )}
    >
      <div>
        <div className="text-sm font-semibold">
          {t("setup.activity.completedOperationsTitle")}
        </div>
        <p className="mt-1 text-xs text-muted-foreground">
          {t("setup.activity.completedOperationsDescription")}
        </p>
      </div>

      <div className="mt-4 divide-y divide-border/60">
        {completedSteps.map((step) => (
          <CompletedOperationRow key={step.order} step={step} />
        ))}
      </div>

      <div className="mt-4 text-xs font-medium text-muted-foreground">
        {t("setup.activity.completedOperationsCount", {
          count: completedSteps.length,
        })}
      </div>
    </section>
  )
}

function CompletedOperationRow({ step }: { step: WorkflowStep }) {
  const completedPhases =
    step.progress?.phases.filter((phase) => phase.status === "Succeeded") ?? []

  if (completedPhases.length === 0) {
    return (
      <div
        className="flex items-start gap-2 py-2 first:pt-0 last:pb-0"
        data-testid={`completed-operation-${step.order}`}
      >
        <CheckCircle2
          className="mt-0.5 h-4 w-4 shrink-0 text-emerald-300"
          aria-hidden="true"
        />
        <span className="text-sm font-medium">{step.title}</span>
      </div>
    )
  }

  return (
    <details
      className="group py-2 first:pt-0 last:pb-0"
      data-testid={`completed-operation-${step.order}`}
    >
      <summary className="flex cursor-pointer list-none items-start justify-between gap-3 rounded-md [&::-webkit-details-marker]:hidden">
        <span className="flex min-w-0 items-start gap-2">
          <CheckCircle2
            className="mt-0.5 h-4 w-4 shrink-0 text-emerald-300"
            aria-hidden="true"
          />
          <span className="text-sm font-medium">{step.title}</span>
        </span>
        <ChevronDown
          className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground transition-transform group-open:rotate-180"
          aria-hidden="true"
        />
      </summary>

      <div className="ml-2 mt-3 border-l border-emerald-500/20 pl-6">
        <InstallProgressPhaseList phases={completedPhases} />
      </div>
    </details>
  )
}
