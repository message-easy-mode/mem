import { CircleAlert, LoaderCircle } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

import type {
  ManagedFederationMode,
  RuntimeStackFederationState,
} from "../api/federation.types"

type FederationOperationStatusProps = {
  active: boolean
  requestedMode: ManagedFederationMode | null
  state: RuntimeStackFederationState | undefined
  requestDisconnected: boolean
}

export function FederationOperationStatus({
  active,
  requestedMode,
  state,
  requestDisconnected,
}: FederationOperationStatusProps) {
  const { t } = useI18n()
  if (!active) return null

  const operation = state?.latestOperation
  const requestedModeLabel = requestedMode === "restricted"
    ? t("federation.mode.restricted")
    : requestedMode === "public"
      ? t("federation.mode.public")
      : requestedMode === "local_only"
        ? t("federation.mode.localOnly")
        : t("federation.operation.policyChange")

  return (
    <Card>
      <CardHeader>
        <div className="flex items-center gap-3">
          <LoaderCircle className="h-5 w-5 animate-spin" aria-hidden="true" />
          <CardTitle>{t("federation.operation.title")}</CardTitle>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="text-sm">
          {t("federation.operation.requestedTransition", { mode: requestedModeLabel })}
        </p>
        <div className="grid gap-3 sm:grid-cols-2">
          <OperationFact
            label={t("federation.operation.phase")}
            value={operation?.currentStep
              ? t(operationStepKey(operation.currentStep))
              : t("federation.operation.starting")}
          />
          <OperationFact
            label={t("federation.operation.id")}
            value={operation?.id ?? t("federation.operation.pendingId")}
            mono={Boolean(operation?.id)}
          />
        </div>
        <p className="text-sm text-muted-foreground">
          {t("federation.operation.journalled")}
        </p>
        {requestDisconnected ? (
          <Alert className="border-amber-500/30 bg-amber-500/10">
            <CircleAlert className="h-4 w-4" />
            <AlertTitle>{t("federation.operation.disconnectedTitle")}</AlertTitle>
            <AlertDescription>{t("federation.operation.disconnectedDescription")}</AlertDescription>
          </Alert>
        ) : null}
      </CardContent>
    </Card>
  )
}

function OperationFact({
  label,
  value,
  mono = false,
}: {
  label: string
  value: string
  mono?: boolean
}) {
  return (
    <div className="rounded-lg border bg-muted/20 p-3">
      <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </p>
      <p className={mono ? "mt-2 break-all font-mono text-xs" : "mt-2 text-sm font-medium"}>
        {value}
      </p>
    </div>
  )
}

function operationStepKey(step: string): TranslationKey {
  switch (step) {
    case "snapshotting-current-state": return "federation.operation.step.snapshotting"
    case "validating-candidate-config": return "federation.operation.step.validatingCandidate"
    case "validating-review-before-mutation": return "federation.operation.step.validatingReview"
    case "applying-ingress-guard": return "federation.operation.step.applyingIngressGuard"
    case "verifying-ingress-guard": return "federation.operation.step.verifyingIngressGuard"
    case "replacing-active-config": return "federation.operation.step.replacingConfig"
    case "restarting-matrix": return "federation.operation.step.restartingMatrix"
    case "waiting-for-client-readiness": return "federation.operation.step.waitingClientReadiness"
    case "applying-public-ingress": return "federation.operation.step.applyingPublicIngress"
    case "verifying-effective-state": return "federation.operation.step.verifying"
    case "rollback-started": return "federation.operation.step.rollbackStarted"
    case "restoring-ingress-guard": return "federation.operation.step.restoringIngressGuard"
    case "restoring-config": return "federation.operation.step.restoringConfig"
    case "restarting-matrix-after-config-restore": return "federation.operation.step.restartingAfterRestore"
    case "restoring-ingress": return "federation.operation.step.restoringIngress"
    case "verifying-rollback": return "federation.operation.step.verifyingRollback"
    default: return "federation.operation.step.working"
  }
}
