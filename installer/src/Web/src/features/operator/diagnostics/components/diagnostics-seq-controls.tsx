import { useMutation, useQueryClient } from "@tanstack/react-query"
import {
  Activity,
  CheckCircle2,
  Copy,
  Play,
  Power,
  RefreshCw,
  RotateCw,
  Square,
  Trash2,
} from "lucide-react"
import { useRef, useState } from "react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
} from "@/components/ui/card"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import {
  getMemApiProblemCode,
  getMemApiProblemDetail,
} from "@/lib/api-problem"
import {
  changeDiagnosticsSeqDelivery,
  checkDiagnosticsSeqHealth,
  verifyDiagnosticsSeqDelivery,
  runDiagnosticsSeqRuntimeAction,
} from "../api/diagnostics.api"
import type {
  DiagnosticsSeqDeliveryVerificationResponse,
  DiagnosticsSeqOperationResponse,
  DiagnosticsSeqOverviewResponse,
  DiagnosticsSeqSetupReviewResponse,
} from "../api/diagnostics.types"

export type SeqControlAction =
  | "deploy"
  | "start"
  | "stop"
  | "restart"
  | "remove"
  | "enable-delivery"
  | "disable-delivery"
  | "health-check"

type ConfirmedAction = Exclude<SeqControlAction, "start" | "health-check">

export function DiagnosticsSeqControls({
  data,
  setupReview,
}: {
  data: DiagnosticsSeqOverviewResponse
  setupReview: DiagnosticsSeqSetupReviewResponse | undefined
}) {
  const { t } = useI18n()
  const queryClient = useQueryClient()
  const [confirmation, setConfirmation] = useState<ConfirmedAction | null>(null)
  const stepUpActionRef = useRef<SeqControlAction | null>(null)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [outcome, setOutcome] = useState<DiagnosticsSeqOperationResponse | null>(null)
  const [error, setError] = useState<unknown>(null)
  const restart = data.delivery.restart ?? {
    kind: "unsupported",
    guidanceCode: "restart_supervisor_unknown",
    command: null,
    commandAvailable: false,
  }

  const operation = useMutation({
    mutationFn: executeAction,
    onSuccess: (result) => {
      queryClient.setQueryData(["diagnostics", "seq"], result.overview)
      setOutcome(result)
      setError(null)
      setConfirmation(null)
      stepUpActionRef.current = null
    },
    onError: (nextError, action) => {
      if (getMemApiProblemCode(nextError) === "step_up_required") {
        setConfirmation(null)
        stepUpActionRef.current = action
        setStepUpOpen(true)
        setError(null)
        return
      }

      setError(nextError)
    },
  })

  const deliveryVerification = useMutation({
    mutationFn: verifyDiagnosticsSeqDelivery,
    onSuccess: (result: DiagnosticsSeqDeliveryVerificationResponse) => {
      queryClient.setQueryData(["diagnostics", "seq"], result.overview)
      setError(null)
    },
    onError: (nextError) => setError(nextError),
  })

  const copyRestartCommand = async () => {
    const command = restart.command
    if (!command || !restart.commandAvailable) return

    try {
      await navigator.clipboard.writeText(command)
    } catch {
      // The server-authored command remains visible when clipboard access is unavailable.
    }
  }

  const copyVerificationQuery = async (verificationId: string) => {
    try {
      await navigator.clipboard.writeText(`VerificationId = '${verificationId}'`)
    } catch {
      // The query remains visible when clipboard access is unavailable.
    }
  }

  const run = (action: SeqControlAction) => {
    stepUpActionRef.current = null
    setOutcome(null)
    setError(null)
    operation.mutate(action)
  }

  const confirm = (action: ConfirmedAction) => {
    setOutcome(null)
    setError(null)
    setConfirmation(action)
  }

  const canDeploy = data.capabilities.canDeploy && setupReview?.readyForDeployment === true
  const hasOwnerControls =
    data.capabilities.canDeploy ||
    data.capabilities.canStart ||
    data.capabilities.canStop ||
    data.capabilities.canRestart ||
    data.capabilities.canRemove ||
    data.capabilities.canEnableDelivery ||
    data.capabilities.canDisableDelivery

  const removalWaitingForDelivery =
    data.runtime.managed &&
    data.runtime.present &&
    !data.capabilities.canRemove &&
    (data.delivery.enabled || data.delivery.desiredEnabled)
  const controlsPending = operation.isPending || deliveryVerification.isPending

  return (
    <section aria-labelledby="seq-controls-title" className="space-y-4">
      <div>
        <h2 id="seq-controls-title" className="text-lg font-semibold">
          {t("diagnostics.seq.controls.title")}
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          {t("diagnostics.seq.controls.description")}
        </p>
      </div>

      {data.delivery.restartRequired && data.delivery.activationState !== "unavailable" ? (
        <Alert
          className="border-amber-500/40 bg-amber-500/10 text-amber-950 dark:text-amber-100"
          data-seq-restart-attention
        >
          <RefreshCw className="h-4 w-4 text-amber-600 dark:text-amber-300" />
          <AlertTitle className="font-semibold">{t("diagnostics.seq.delivery.restartTitle")}</AlertTitle>
          <AlertDescription className="space-y-3 text-amber-950/80 dark:text-amber-100/80">
            <p>
              {data.delivery.desiredEnabled
                ? t("diagnostics.seq.delivery.enablePending")
                : t("diagnostics.seq.delivery.disablePending")}
            </p>
            <p>{restartGuidance(restart.guidanceCode, t)}</p>
            {restart.commandAvailable && restart.command ? (
              <div className="flex flex-wrap items-center gap-2 rounded-md border px-3 py-2">
                <code className="min-w-0 flex-1 overflow-x-auto text-xs">
                  {restart.command}
                </code>
                <Button type="button" variant="outline" size="sm" onClick={() => void copyRestartCommand()}>
                  <Copy className="mr-2 h-3.5 w-3.5" />
                  {t("diagnostics.seq.delivery.copyRestartCommand")}
                </Button>
              </div>
            ) : null}
          </AlertDescription>
        </Alert>
      ) : null}

      {error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("diagnostics.seq.controls.failedTitle")}</AlertTitle>
          <AlertDescription>{operationError(error, t)}</AlertDescription>
        </Alert>
      ) : null}

      {outcome ? (
        <Alert>
          <Activity className="h-4 w-4" />
          <AlertTitle>{t("diagnostics.seq.controls.completedTitle")}</AlertTitle>
          <AlertDescription>
            <span>{operationLabel(outcome.operation, t)}</span>
            <span className="mt-1 block font-mono text-xs">{outcome.operationId}</span>
          </AlertDescription>
        </Alert>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <div className="flex items-start justify-between gap-3">
              <div>
                <h3 className="text-base leading-snug font-medium">
                  {t("diagnostics.seq.runtimeControls.title")}
                </h3>
                <CardDescription className="mt-1">
                  {t("diagnostics.seq.runtimeControls.description")}
                </CardDescription>
              </div>
              <Badge variant="outline">{runtimeStateLabel(data.runtime.state, t)}</Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            {hasOwnerControls ? (
              <div className="flex flex-wrap gap-2">
                {canDeploy ? (
                  <Button type="button" disabled={controlsPending} onClick={() => confirm("deploy")}>
                    <Power className="mr-2 h-4 w-4" />
                    {t("diagnostics.seq.action.deploy")}
                  </Button>
                ) : null}
                {data.capabilities.canStart ? (
                  <Button
                    type="button"
                    variant="outline"
                    disabled={controlsPending}
                    onClick={() => run("start")}
                  >
                    <Play className="mr-2 h-4 w-4" />
                    {t("diagnostics.seq.action.start")}
                  </Button>
                ) : null}
                {data.capabilities.canStop ? (
                  <Button type="button" variant="outline" disabled={controlsPending} onClick={() => confirm("stop")}>
                    <Square className="mr-2 h-4 w-4" />
                    {t("diagnostics.seq.action.stop")}
                  </Button>
                ) : null}
                {data.capabilities.canRestart ? (
                  <Button type="button" variant="outline" disabled={controlsPending} onClick={() => confirm("restart")}>
                    <RotateCw className="mr-2 h-4 w-4" />
                    {t("diagnostics.seq.action.restart")}
                  </Button>
                ) : null}
                {data.capabilities.canRemove ? (
                  <Button type="button" variant="destructive" disabled={controlsPending} onClick={() => confirm("remove")}>
                    <Trash2 className="mr-2 h-4 w-4" />
                    {t("diagnostics.seq.action.remove")}
                  </Button>
                ) : null}
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">
                {t("diagnostics.seq.runtimeControls.ownerOnly")}
              </p>
            )}

            {data.capabilities.canDeploy && !canDeploy ? (
              <p className="text-xs text-muted-foreground">
                {t("diagnostics.seq.runtimeControls.reviewFirst")}
              </p>
            ) : null}

            {removalWaitingForDelivery ? (
              <p className="text-xs text-muted-foreground">
                {t("diagnostics.seq.runtimeControls.removeBlocked")}
              </p>
            ) : null}

            {data.capabilities.canCheckHealth ? (
              <Button
                type="button"
                variant="outline"
                disabled={controlsPending}
                onClick={() => run("health-check")}
              >
                <Activity className="mr-2 h-4 w-4" />
                {t("diagnostics.seq.action.healthCheck")}
              </Button>
            ) : null}

            <p className="text-xs text-muted-foreground">
              {t("diagnostics.seq.runtimeControls.dataRetention")}
            </p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <div className="flex items-start justify-between gap-3">
              <div>
                <h3 className="text-base leading-snug font-medium">
                  {t("diagnostics.seq.deliveryControls.title")}
                </h3>
                <CardDescription className="mt-1">
                  {t("diagnostics.seq.deliveryControls.description")}
                </CardDescription>
              </div>
              <Badge variant="outline">
                {data.delivery.enabled
                  ? t("diagnostics.seq.status.enabled")
                  : t("diagnostics.seq.status.disabled")}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <dl className="grid gap-3 text-sm sm:grid-cols-2">
              <div>
                <dt className="text-xs text-muted-foreground">
                  {t("diagnostics.seq.deliveryControls.current")}
                </dt>
                <dd className="mt-1 font-medium">
                  {enabledLabel(data.delivery.enabled, t)}
                </dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground">
                  {t("diagnostics.seq.deliveryControls.desired")}
                </dt>
                <dd className="mt-1 font-medium">
                  {enabledLabel(data.delivery.desiredEnabled, t)}
                </dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground">
                  {t("diagnostics.seq.deliveryControls.activation")}
                </dt>
                <dd className="mt-1 font-medium">
                  {activationLabel(data.delivery.activationState, t)}
                </dd>
              </div>
            </dl>

            <div className="flex flex-wrap gap-2">
              {data.capabilities.canEnableDelivery ? (
                <Button type="button" disabled={controlsPending} onClick={() => confirm("enable-delivery")}>
                  {t("diagnostics.seq.action.enableDelivery")}
                </Button>
              ) : null}
              {data.capabilities.canDisableDelivery ? (
                <Button type="button" variant="outline" disabled={controlsPending} onClick={() => confirm("disable-delivery")}>
                  {t("diagnostics.seq.action.disableDelivery")}
                </Button>
              ) : null}
              {data.capabilities.canVerifyDelivery && data.delivery.activationState !== "verified" ? (
                <Button
                  type="button"
                  variant="outline"
                  disabled={controlsPending}
                  onClick={() => deliveryVerification.mutate()}
                >
                  <Activity className="mr-2 h-4 w-4" />
                  {deliveryVerification.isPending
                    ? t("diagnostics.seq.delivery.verifying")
                    : t("diagnostics.seq.delivery.verifyAction")}
                </Button>
              ) : null}
            </div>

            {!data.capabilities.canEnableDelivery && !data.capabilities.canDisableDelivery ? (
              <p className="text-sm text-muted-foreground">
                {t("diagnostics.seq.deliveryControls.ownerOnly")}
              </p>
            ) : null}

            {data.delivery.activationState === "verified" && data.delivery.lastActivationVerificationId ? (
              <Alert>
                <CheckCircle2 className="h-4 w-4" />
                <AlertTitle>{t("diagnostics.seq.delivery.verifiedTitle")}</AlertTitle>
                <AlertDescription className="space-y-2">
                  <p>{t("diagnostics.seq.delivery.verifiedDescription")}</p>
                  <div className="flex flex-wrap items-center gap-2 rounded-md border px-3 py-2">
                    <code className="min-w-0 flex-1 break-all text-xs">
                      VerificationId = '{data.delivery.lastActivationVerificationId}'
                    </code>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() => void copyVerificationQuery(data.delivery.lastActivationVerificationId!)}
                    >
                      <Copy className="mr-2 h-3.5 w-3.5" />
                      {t("diagnostics.seq.delivery.copyVerificationQuery")}
                    </Button>
                  </div>
                </AlertDescription>
              </Alert>
            ) : null}

            <p className="text-xs text-muted-foreground">
              {t("diagnostics.seq.deliveryControls.nativeContinues")}
            </p>
          </CardContent>
        </Card>
      </div>

      <ConfirmationDialog
        open={confirmation !== null}
        onOpenChange={(open) => {
          if (!open && !operation.isPending) setConfirmation(null)
        }}
        title={confirmation ? confirmationTitle(confirmation, t) : ""}
        description={confirmation ? confirmationDescription(confirmation, t) : ""}
        confirmLabel={confirmation ? confirmationLabel(confirmation, t) : ""}
        confirmingLabel={t("diagnostics.seq.action.working")}
        cancelLabel={t("diagnostics.seq.action.cancel")}
        confirmVariant={confirmation === "remove" ? "destructive" : "default"}
        onConfirm={() => {
          if (confirmation) run(confirmation)
        }}
        isConfirming={operation.isPending}
      />

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={setStepUpOpen}
        onVerified={() => {
          const action = stepUpActionRef.current
          stepUpActionRef.current = null
          if (action) run(action)
        }}
      />
    </section>
  )
}

async function executeAction(action: SeqControlAction) {
  switch (action) {
    case "enable-delivery":
      return changeDiagnosticsSeqDelivery(true)
    case "disable-delivery":
      return changeDiagnosticsSeqDelivery(false)
    case "health-check":
      return checkDiagnosticsSeqHealth()
    default:
      return runDiagnosticsSeqRuntimeAction(action)
  }
}

type Translate = ReturnType<typeof useI18n>["t"]

function confirmationTitle(action: ConfirmedAction, t: Translate) {
  return t(`diagnostics.seq.confirm.${action}.title`)
}

function confirmationDescription(action: ConfirmedAction, t: Translate) {
  return t(`diagnostics.seq.confirm.${action}.description`)
}

function confirmationLabel(action: ConfirmedAction, t: Translate) {
  return t(`diagnostics.seq.confirm.${action}.action`)
}

const operationTranslationKeys: Readonly<Record<string, TranslationKey>> = {
  "seq.deploy": "diagnostics.seq.operation.seq.deploy",
  "seq.start": "diagnostics.seq.operation.seq.start",
  "seq.stop": "diagnostics.seq.operation.seq.stop",
  "seq.restart": "diagnostics.seq.operation.seq.restart",
  "seq.remove": "diagnostics.seq.operation.seq.remove",
  "seq.delivery.enable": "diagnostics.seq.operation.seq.delivery.enable",
  "seq.delivery.disable": "diagnostics.seq.operation.seq.delivery.disable",
  "seq.health-check": "diagnostics.seq.operation.seq.health-check",
}

const runtimeStateTranslationKeys: Readonly<Record<string, TranslationKey>> = {
  "not-configured": "diagnostics.seq.runtimeStatus.not-configured",
  absent: "diagnostics.seq.runtimeStatus.absent",
  running: "diagnostics.seq.runtimeStatus.running",
  stopped: "diagnostics.seq.runtimeStatus.stopped",
  "unmanaged-conflict": "diagnostics.seq.runtimeStatus.unmanaged-conflict",
  "control-plane-mismatch": "diagnostics.seq.runtimeStatus.control-plane-mismatch",
  "identity-mismatch": "diagnostics.seq.runtimeStatus.identity-mismatch",
  "record-only": "diagnostics.seq.runtimeStatus.record-only",
  unavailable: "diagnostics.seq.runtimeStatus.unavailable",
}

const problemTranslationKeys: Readonly<Record<string, TranslationKey>> = {
  seq_setup_not_ready: "diagnostics.seq.problem.seq_setup_not_ready",
  seq_operation_in_progress: "diagnostics.seq.problem.seq_operation_in_progress",
  seq_managed_runtime_not_found: "diagnostics.seq.problem.seq_managed_runtime_not_found",
  seq_runtime_not_running: "diagnostics.seq.problem.seq_runtime_not_running",
  seq_unmanaged_container: "diagnostics.seq.problem.seq_unmanaged_container",
  seq_control_plane_ownership_mismatch: "diagnostics.seq.problem.seq_control_plane_ownership_mismatch",
  seq_container_identity_mismatch: "diagnostics.seq.problem.seq_container_identity_mismatch",
  seq_runtime_record_conflict: "diagnostics.seq.problem.seq_runtime_record_conflict",
  seq_delivery_must_be_disabled: "diagnostics.seq.problem.seq_delivery_must_be_disabled",
  seq_api_key_unavailable: "diagnostics.seq.problem.seq_api_key_unavailable",
  "diagnostics.seq_api_key_unavailable": "diagnostics.seq.problem.diagnostics.seq_api_key_unavailable",
  seq_ingestion_url_invalid: "diagnostics.seq.problem.seq_ingestion_url_invalid",
  seq_host_port_invalid: "diagnostics.seq.problem.seq_host_port_invalid",
  seq_health_verification_failed: "diagnostics.seq.problem.seq_health_verification_failed",
  "diagnostics.seq_health_failed": "diagnostics.seq.problem.diagnostics.seq_health_failed",
  "diagnostics.seq_probe_timeout": "diagnostics.seq.problem.diagnostics.seq_probe_timeout",
  "diagnostics.seq_unavailable": "diagnostics.seq.problem.diagnostics.seq_unavailable",
  seq_delivery_state_write_failed: "diagnostics.seq.problem.seq_delivery_state_write_failed",
  seq_delivery_state_path_invalid: "diagnostics.seq.problem.seq_delivery_state_path_invalid",
  seq_operation_failed: "diagnostics.seq.problem.seq_operation_failed",
  seq_configuration_invalid: "diagnostics.seq.problem.seq_configuration_invalid",
  seq_admin_password_hash_unavailable: "diagnostics.seq.problem.seq_admin_password_hash_unavailable",
  "diagnostics.seq_admin_password_hash_unavailable": "diagnostics.seq.problem.diagnostics.seq_admin_password_hash_unavailable",
  seq_approved_image_missing: "diagnostics.seq.problem.seq_approved_image_missing",
  seq_approved_image_invalid: "diagnostics.seq.problem.seq_approved_image_invalid",
  seq_start_verification_failed: "diagnostics.seq.problem.seq_start_verification_failed",
  seq_stop_verification_failed: "diagnostics.seq.problem.seq_stop_verification_failed",
  seq_restart_verification_failed: "diagnostics.seq.problem.seq_restart_verification_failed",
  "diagnostics.seq_health_url_invalid": "diagnostics.seq.problem.diagnostics.seq_health_url_invalid",
  seq_delivery_state_invalid: "diagnostics.seq.problem.seq_delivery_state_invalid",
  seq_delivery_state_read_failed: "diagnostics.seq.problem.seq_delivery_state_read_failed",
  seq_delivery_restart_required: "diagnostics.seq.problem.seq_delivery_restart_required",
  seq_delivery_not_enabled: "diagnostics.seq.problem.seq_delivery_not_enabled",
  seq_delivery_verification_failed: "diagnostics.seq.problem.seq_delivery_verification_failed",
  seq_delivery_startup_prerequisites_unavailable: "diagnostics.seq.problem.seq_delivery_startup_prerequisites_unavailable",
  seq_runtime_not_ready_for_delivery: "diagnostics.seq.problem.seq_runtime_not_ready_for_delivery",
  seq_connection_not_verified: "diagnostics.seq.problem.seq_connection_not_verified",
  seq_delivery_information_logging_disabled: "diagnostics.seq.problem.seq_delivery_information_logging_disabled",
  "diagnostics.seq_sink_configuration_failed": "diagnostics.seq.problem.diagnostics.seq_sink_configuration_failed",
}


function restartGuidance(
  guidanceCode: string,
  t: (key: TranslationKey, values?: Record<string, string | number>) => string,
) {
  const key = `diagnostics.seq.delivery.guidance.${guidanceCode}` as TranslationKey
  return t(key)
}

function operationLabel(operation: string, t: Translate) {
  const key = operationTranslationKeys[operation]
  return key ? t(key) : t("diagnostics.seq.operation.completed")
}

function runtimeStateLabel(state: string, t: Translate) {
  const key = runtimeStateTranslationKeys[state]
  return key ? t(key) : t("diagnostics.seq.runtimeStatus.unknown")
}

function enabledLabel(enabled: boolean, t: Translate) {
  return enabled
    ? t("diagnostics.seq.status.enabled")
    : t("diagnostics.seq.status.disabled")
}

function activationLabel(state: string | undefined, t: Translate) {
  switch (state) {
    case "restart-pending":
      return t("diagnostics.seq.delivery.activation.restartPending")
    case "verification-required":
      return t("diagnostics.seq.delivery.activation.verificationRequired")
    case "verified":
      return t("diagnostics.seq.delivery.activation.verified")
    case "unavailable":
      return t("diagnostics.seq.delivery.activation.unavailable")
    default:
      return t("diagnostics.seq.delivery.activation.disabled")
  }
}

function operationError(error: unknown, t: Translate) {
  const code = getMemApiProblemCode(error)
  const key = code ? problemTranslationKeys[code] : undefined
  if (key) return t(key)

  return getMemApiProblemDetail(error, t("diagnostics.seq.controls.failedDescription"))
}
