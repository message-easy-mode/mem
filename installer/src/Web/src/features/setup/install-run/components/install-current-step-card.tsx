import { useI18n } from "@/app/i18n/i18n-context"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type { WorkflowStep } from "../api/install.types"

export function InstallCurrentStepCard({
  step,
  failedPhase,
}: {
  step: WorkflowStep | null
  failedPhase?: string | null
}) {
  const { t } = useI18n()
  if (!step) return null

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          {step.status === "Failed"
            ? t("setup.activity.failedPhase")
            : t("setup.activity.currentCheck")}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <div>
          {step.status === "Failed" && failedPhase && (
            <div className="mb-2 text-xs font-medium uppercase tracking-wide text-destructive">
              {t("setup.activity.failedAt", { phase: failedPhase })}
            </div>
          )}
          <div className="font-medium">{step.title}</div>
          {step.category && (
            <div className="mt-1 text-xs uppercase tracking-wide text-muted-foreground">
              {step.category}
            </div>
          )}
        </div>

        {step.message && <div className="text-sm">{step.message}</div>}

        {step.status === "WaitingForUser" && step.humanActionPrompt && (
          <div className="rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-sm">
            {step.humanActionPrompt}
          </div>
        )}

        {step.status === "Failed" && step.errorMessage && (
          <div className="rounded-lg border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive">
            {step.errorMessage}
          </div>
        )}
      </CardContent>
    </Card>
  )
}
