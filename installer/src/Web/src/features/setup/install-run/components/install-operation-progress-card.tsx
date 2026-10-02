import { useEffect, useState } from "react"

import { Clock3, Loader2 } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

import type { WorkflowStep } from "../api/install.types"

import {
  InstallProgressPhaseList,
  installProgressPhaseLabel,
} from "./install-progress-phase-list"

const STALE_ACTIVITY_MS = 20_000

export function InstallOperationProgressCard({
  step,
  setupStartedAtUtc,
}: {
  step: WorkflowStep
  setupStartedAtUtc?: string | null
}) {
  const { t } = useI18n()
  const progress = step.progress
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [])

  if (!progress) {
    return (
      <Card className="border-sky-500/30 bg-sky-500/10">
        <CardHeader>
          <CardTitle className="text-base">
            {t("setup.activity.currentOperation")}
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="flex items-center gap-3">
            <Loader2 className="h-5 w-5 animate-spin" />
            <div className="font-medium">{t("setup.activity.startingStep")}</div>
          </div>
          <WorkingNotice stale={false} />
        </CardContent>
      </Card>
    )
  }

  const activeLabel = progress.phaseCode
    ? installProgressPhaseLabel(progress.phaseCode, t)
    : t("setup.activity.startingStep")
  const elapsed = formatElapsed(setupStartedAtUtc ?? progress.stepStartedAtUtc, now, t)
  const lastActivity = formatRelative(progress.lastActivityAtUtc, now, t)
  const stale = isStale(progress.lastActivityAtUtc, now)

  return (
    <Card
      className="border-sky-500/30 bg-sky-500/10"
      data-testid="installation-operation-progress"
    >
      <CardHeader className="space-y-2">
        <div className="flex items-start justify-between gap-4">
          <div>
            <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
              {t("setup.activity.currentOperation")}
            </div>
            <CardTitle className="mt-1 text-lg">{activeLabel}</CardTitle>
          </div>
          <Loader2 className="mt-1 h-5 w-5 shrink-0 animate-spin text-sky-300" />
        </div>
      </CardHeader>

      <CardContent className="space-y-4">
        <div className="grid gap-3 sm:grid-cols-3">
          <Metric
            label={t("setup.activity.elapsed")}
            value={elapsed}
          />
          <Metric
            label={t("setup.activity.lastActivity")}
            value={lastActivity}
          />
          <Metric
            label={t("setup.activity.attemptLabel")}
            value={progress.attemptNumber.toString()}
          />
        </div>

        <WorkingNotice stale={stale} />

        {progress.phases.length > 0 && (
          <div className="space-y-2 rounded-lg border border-border/70 bg-background/30 p-3">
            <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
              {t("setup.activity.operationProgress")}
            </div>
            <InstallProgressPhaseList phases={progress.phases} />
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function WorkingNotice({ stale }: { stale: boolean }) {
  const { t } = useI18n()
  return (
    <div className="flex items-start gap-2 rounded-lg border border-sky-500/20 bg-background/30 p-3 text-sm">
      <Clock3 className="mt-0.5 h-4 w-4 shrink-0 text-sky-300" />
      <div>
        <div className="font-medium">
          {t(stale ? "setup.activity.stillWaiting" : "setup.activity.stillWorking")}
        </div>
        <div className="mt-1 text-muted-foreground">
          {t(stale ? "setup.activity.noRecentActivity" : "setup.activity.noActionRequired")}
        </div>
      </div>
    </div>
  )
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border/70 bg-background/30 p-3">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className="mt-1 text-sm font-medium">{value}</div>
    </div>
  )
}

function formatElapsed(
  startedAtUtc: string,
  now: number,
  t: (key: TranslationKey, values?: Readonly<Record<string, string | number>>) => string,
) {
  const started = new Date(startedAtUtc).getTime()
  if (!Number.isFinite(started)) return "-"

  const seconds = Math.max(0, Math.floor((now - started) / 1000))
  if (seconds < 60) {
    return t("setup.activity.seconds", { count: seconds })
  }

  const minutes = Math.floor(seconds / 60)
  const remainingSeconds = seconds % 60
  return t("setup.activity.minutesSeconds", {
    minutes,
    seconds: remainingSeconds,
  })
}

function formatRelative(
  value: string,
  now: number,
  t: (key: TranslationKey, values?: Readonly<Record<string, string | number>>) => string,
) {
  const timestamp = new Date(value).getTime()
  if (!Number.isFinite(timestamp)) return "-"

  const seconds = Math.max(0, Math.floor((now - timestamp) / 1000))
  if (seconds < 5) return t("setup.activity.justNow")
  if (seconds < 60) return t("setup.activity.secondsAgo", { count: seconds })

  const minutes = Math.floor(seconds / 60)
  return t("setup.activity.minutesAgo", { count: minutes })
}

function isStale(value: string, now: number) {
  const timestamp = new Date(value).getTime()
  return Number.isFinite(timestamp) && now - timestamp >= STALE_ACTIVITY_MS
}
