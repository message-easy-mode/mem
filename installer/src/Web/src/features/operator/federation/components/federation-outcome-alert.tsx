import { CheckCircle2, CircleAlert, CircleX, RotateCcw } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"

import type {
  FederationMode,
  RuntimeStackFederationApplyResult,
} from "../api/federation.types"

type FederationOutcomeAlertProps = {
  result: RuntimeStackFederationApplyResult | null
  problem: string | null
}

export function FederationOutcomeAlert({
  result,
  problem,
}: FederationOutcomeAlertProps) {
  const { t } = useI18n()

  if (problem) {
    return (
      <Alert variant="destructive">
        <CircleX className="h-4 w-4" />
        <AlertTitle>{t("federation.outcome.requestFailedTitle")}</AlertTitle>
        <AlertDescription>{problem}</AlertDescription>
      </Alert>
    )
  }

  if (!result || result.status === "running") return null

  if (result.status === "succeeded") {
    return (
      <Alert className="border-emerald-500/30 bg-emerald-500/10">
        <CheckCircle2 className="h-4 w-4" />
        <AlertTitle>{t("federation.outcome.succeededTitle")}</AlertTitle>
        <AlertDescription>
          {t("federation.outcome.succeededDescription", {
            mode: modeLabel(result.observedMode, t),
          })}
          <OperationId id={result.operationId} />
        </AlertDescription>
      </Alert>
    )
  }

  if (result.status === "candidate_rejected") {
    return (
      <Alert className="border-amber-500/30 bg-amber-500/10">
        <CircleAlert className="h-4 w-4" />
        <AlertTitle>{t("federation.outcome.candidateRejectedTitle")}</AlertTitle>
        <AlertDescription>
          {t("federation.outcome.candidateRejectedDescription")}
          <OperationId id={result.operationId} />
        </AlertDescription>
      </Alert>
    )
  }

  if (result.status === "rolled_back" || result.rollbackSucceeded === true) {
    return (
      <Alert className="border-amber-500/30 bg-amber-500/10">
        <RotateCcw className="h-4 w-4" />
        <AlertTitle>{t("federation.outcome.rolledBackTitle")}</AlertTitle>
        <AlertDescription>
          {t("federation.outcome.rolledBackDescription", {
            mode: modeLabel(result.observedMode, t),
          })}
          <OperationId id={result.operationId} />
        </AlertDescription>
      </Alert>
    )
  }

  const rollbackFailed = result.rollbackAttempted && result.rollbackSucceeded === false
  return (
    <Alert variant="destructive">
      <CircleX className="h-4 w-4" />
      <AlertTitle>
        {rollbackFailed
          ? t("federation.outcome.rollbackFailedTitle")
          : t("federation.outcome.failedBeforeMutationTitle")}
      </AlertTitle>
      <AlertDescription>
        {rollbackFailed
          ? t("federation.outcome.rollbackFailedDescription")
          : t("federation.outcome.failedBeforeMutationDescription")}
        <OperationId id={result.operationId} />
      </AlertDescription>
    </Alert>
  )
}

function OperationId({ id }: { id: string }) {
  const { t } = useI18n()
  return (
    <span className="mt-2 block font-mono text-xs">
      {t("federation.outcome.operationId", { id })}
    </span>
  )
}

function modeLabel(
  mode: FederationMode,
  t: (key: TranslationKey) => string,
) {
  switch (mode) {
    case "public": return t("federation.mode.public")
    case "restricted": return t("federation.mode.restricted")
    case "local_only": return t("federation.mode.localOnly")
    default: return t("federation.mode.unknown")
  }
}
