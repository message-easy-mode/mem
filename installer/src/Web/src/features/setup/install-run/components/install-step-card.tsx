import { Link, useParams } from "react-router-dom"
import {
  AlertTriangle,
  CheckCircle2,
  Circle,
  Loader2,
  Settings2,
  XCircle,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { workflowStepStatusTranslationKey } from "@/features/setup/setup-status-i18n"
import { cn } from "@/lib/utils"

import { InstallProgressPhaseList } from "./install-progress-phase-list"

import {
  getStepStatusTone,
  type WorkflowStep,
  type WorkflowStepStatus,
} from "../api/install.types"

function getStepIcon(status: WorkflowStepStatus) {
  switch (status) {
    case "Pending":
      return Circle
    case "Running":
      return Loader2
    case "Succeeded":
      return CheckCircle2
    case "WaitingForUser":
      return AlertTriangle
    case "Failed":
      return XCircle
    default:
      return Circle
  }
}

export function InstallStepCard({ step }: { step: WorkflowStep }) {
  const { t, intlLocale } = useI18n()
  const { installationId } = useParams()
  const tone = getStepStatusTone(step.status)

  const wrapperClass =
    tone === "success"
      ? "border-primary/20 bg-primary/5"
      : tone === "danger"
        ? "border-destructive/30 bg-destructive/10"
        : tone === "warning"
          ? "border-amber-500/30 bg-amber-500/10"
          : tone === "info"
            ? "border-sky-500/30 bg-sky-500/10"
            : "border-border/80 bg-background/30"

  const Icon = getStepIcon(step.status)
  const isIngressTlsGate =
    step.status === "WaitingForUser" &&
    step.title.toLowerCase().includes("ingress") &&
    step.title.toLowerCase().includes("tls")

  return (
    <div className={cn("rounded-lg border p-4", wrapperClass)}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex gap-3">
          <div className="mt-0.5">
            <Icon
              className={cn(
                "h-5 w-5",
                step.status === "Running" && "animate-spin",
              )}
            />
          </div>

          <div>
            <div className="font-medium">
              {step.order}. {step.title}
            </div>

            <div className="mt-1 text-sm text-muted-foreground">
              {t(workflowStepStatusTranslationKey(step.status))}
              {step.category ? ` • ${step.category}` : ""}
            </div>
          </div>
        </div>

        <div className="text-xs text-muted-foreground">
          {t("setup.common.attempt", { count: step.attemptCount })}
        </div>
      </div>

      {step.notes && step.status === "Pending" && (
        <div className="mt-3 text-sm text-muted-foreground">{step.notes}</div>
      )}

      {step.message && <div className="mt-3 text-sm">{step.message}</div>}

      {step.progress?.phases.length ? (
        <div
          className="mt-4 space-y-2 rounded-lg border border-border/70 bg-background/30 p-3"
          data-testid={`installation-step-progress-${step.order}`}
        >
          <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
            {t("setup.activity.operationProgress")}
          </div>
          <InstallProgressPhaseList phases={step.progress.phases} />
        </div>
      ) : null}

      {step.status === "WaitingForUser" && (
        <div className="mt-3 space-y-3 rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-sm">
          <div>
            {step.humanActionPrompt ??
              step.errorMessage ??
              t("setup.activity.stepFallback")}
          </div>

          {isIngressTlsGate && installationId && (
            <Button asChild size="sm" variant="outline">
              <Link to={`/setup/${installationId}/ingress-tls`}>
                <Settings2 className="mr-2 h-4 w-4" />
                {t("setup.activity.openDomains")}
              </Link>
            </Button>
          )}
        </div>
      )}

      {step.status === "Failed" && step.errorMessage && (
        <div className="mt-3 rounded-lg border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive">
          {step.errorMessage}
        </div>
      )}

      <div className="mt-3 grid gap-2 text-xs text-muted-foreground md:grid-cols-2">
        <div>{t("setup.common.started")}: {formatDateTime(step.startedAtUtc, intlLocale)}</div>
        <div>{t("setup.common.completed")}: {formatDateTime(step.completedAtUtc, intlLocale)}</div>
      </div>
    </div>
  )
}

function formatDateTime(value: string | null | undefined, locale: string) {
  if (!value) return "-"

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value

  return date.toLocaleString(locale)
}
