import { AlertTriangle, CheckCircle2 } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

import type { InstallFailureGuidance } from "../api/install-failure-guidance"

export function InstallFailureGuidancePanel({ guidance }: { guidance: InstallFailureGuidance }) {
  const { t } = useI18n()
  return (
    <Card className="border-destructive/30 bg-destructive/5">
      <CardHeader>
        <div className="flex items-start gap-3">
          <div className="mt-0.5 rounded-full border border-destructive/30 bg-destructive/10 p-2 text-destructive">
            <AlertTriangle className="h-5 w-5" />
          </div>

          <div className="space-y-1">
            <CardTitle className="text-base">{guidance.title}</CardTitle>
            <p className="text-sm text-muted-foreground">{guidance.summary}</p>
          </div>
        </div>
      </CardHeader>

      <CardContent className="space-y-4">
        <div className="grid gap-3 md:grid-cols-2">
          <GuidanceBlock label={t("setup.activity.failedAtLabel")} value={guidance.failedPhase} />
          <GuidanceBlock label={t("setup.activity.whatFailed")} value={guidance.technicalReason} />
          <GuidanceBlock label={t("setup.activity.safeNextAction")} value={guidance.nextAction} />
          <GuidanceBlock label={t("setup.activity.retryBehaviour")} value={guidance.retryBehavior} />
        </div>

        {guidance.completedPhases.length > 0 && (
          <div className="rounded-lg border border-emerald-500/20 bg-emerald-500/5 p-3">
            <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
              {t("setup.activity.whatSucceeded")}
            </div>
            <ul className="mt-2 space-y-2 text-sm">
              {guidance.completedPhases.map((phase) => (
                <li key={phase} className="flex items-start gap-2">
                  <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-400" />
                  <span>{phase}</span>
                </li>
              ))}
            </ul>
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function GuidanceBlock({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border/80 bg-background/30 p-3">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-1 text-sm">{value}</div>
    </div>
  )
}
