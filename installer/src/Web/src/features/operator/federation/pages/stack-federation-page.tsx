import { useEffect, useMemo, useState } from "react"
import { Archive, CircleAlert, Info, ShieldAlert } from "lucide-react"
import { useParams } from "react-router-dom"

import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { StackWorkspaceHeader } from "@/features/operator/stacks/components/stack-workspace-header"
import { StackWorkspaceNavigation } from "@/features/operator/stacks/components/stack-workspace-navigation"
import {
  useBackupRuntimeStack,
  useDoctorRuntimeStack,
  useRuntimeStack,
} from "@/features/operator/stacks/hooks/use-runtime-stacks"

import {
  getFederationProblemCode,
  getFederationProblemDetail,
  isStepUpRequiredFederationProblem,
} from "../api/federation.api"
import type {
  ManagedFederationMode,
  RuntimeStackFederationApplyRequest,
  RuntimeStackFederationApplyResult,
  RuntimeStackFederationReview,
  RuntimeStackFederationState,
} from "../api/federation.types"
import { FederationCurrentState } from "../components/federation-current-state"
import { FederationOperationStatus } from "../components/federation-operation-status"
import { FederationOutcomeAlert } from "../components/federation-outcome-alert"
import { FederationPolicyEditor } from "../components/federation-policy-editor"
import { validateFederationPolicyDraft } from "../components/federation-policy-draft"
import { FederationReviewDialog } from "../components/federation-review-dialog"
import {
  useApplyRuntimeStackFederation,
  useReviewRuntimeStackFederation,
  useRuntimeStackFederation,
} from "../hooks/use-federation"

export function StackFederationPage() {
  const { slugOrId } = useParams()
  const { language, t } = useI18n()
  const [trackingOperation, setTrackingOperation] = useState(false)
  const [trackingOperationId, setTrackingOperationId] = useState<string | null>(null)
  const [trackingPreviousOperationId, setTrackingPreviousOperationId] = useState<string | null>(null)
  const [requestDisconnected, setRequestDisconnected] = useState(false)
  const [draftMode, setDraftMode] = useState<ManagedFederationMode>("public")
  const [draftDomains, setDraftDomains] = useState("")
  const [draftFingerprint, setDraftFingerprint] = useState<string | null>(null)
  const [review, setReview] = useState<RuntimeStackFederationReview | null>(null)
  const [reviewOpen, setReviewOpen] = useState(false)
  const [pendingApply, setPendingApply] = useState<RuntimeStackFederationApplyRequest | null>(null)
  const [stepUpPending, setStepUpPending] = useState(false)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [outcome, setOutcome] = useState<RuntimeStackFederationApplyResult | null>(null)
  const [applyProblem, setApplyProblem] = useState<string | null>(null)

  const stackQuery = useRuntimeStack(slugOrId)
  const federationQuery = useRuntimeStackFederation(slugOrId, trackingOperation)
  const reviewMutation = useReviewRuntimeStackFederation(slugOrId ?? "")
  const applyMutation = useApplyRuntimeStackFederation(slugOrId ?? "")
  const doctor = useDoctorRuntimeStack(slugOrId ?? "")
  const backup = useBackupRuntimeStack(slugOrId ?? "")
  const stack = stackQuery.data
  const state = federationQuery.data
  const federationLoadProblemCode = getFederationProblemCode(federationQuery.error)
  const validation = useMemo(
    () => validateFederationPolicyDraft(draftMode, draftDomains),
    [draftDomains, draftMode],
  )

  const latestOperationRunning = state?.latestOperation?.status === "running"
  const operationActive = applyMutation.isPending || trackingOperation || latestOperationRunning
  const managementReason = state ? federationManagementReason(state, t) : null
  const managementDisabled = Boolean(managementReason) || operationActive

  useEffect(() => {
    if (!state?.stateFingerprint || reviewOpen || stepUpPending || applyMutation.isPending) {
      return
    }

    if (draftFingerprint === state.stateFingerprint) {
      return
    }

    if (state.mode === "public" || state.mode === "restricted" || state.mode === "local_only") {
      setDraftMode(state.mode)
      setDraftDomains(state.mode === "restricted" ? state.allowlist.join("\n") : "")
      setDraftFingerprint(state.stateFingerprint)
      setReview(null)
      reviewMutation.reset()
    }
  }, [
    applyMutation.isPending,
    draftFingerprint,
    reviewMutation,
    reviewOpen,
    state,
    stepUpPending,
  ])

  useEffect(() => {
    if (!stepUpPending || reviewOpen) return
    const timer = window.setTimeout(() => setStepUpOpen(true), 0)
    return () => window.clearTimeout(timer)
  }, [reviewOpen, stepUpPending])

  useEffect(() => {
    if (!trackingOperation || applyMutation.isPending) return

    const latest = state?.latestOperation
    if (!latest) return

    const matchesTrackedOperation = trackingOperationId
      ? latest.id === trackingOperationId
      : latest.id !== trackingPreviousOperationId
    if (!matchesTrackedOperation) return

    if (latest.status !== "running") {
      setTrackingOperation(false)
      setTrackingOperationId(null)
      setTrackingPreviousOperationId(null)
      setRequestDisconnected(false)
      void federationQuery.refetch()
    }
  }, [
    applyMutation.isPending,
    federationQuery,
    state?.latestOperation,
    trackingOperation,
    trackingOperationId,
    trackingPreviousOperationId,
  ])

  function refreshWorkspace() {
    void Promise.all([stackQuery.refetch(), federationQuery.refetch()])
  }

  function resetDraft() {
    if (!state || (
      state.mode !== "public" &&
      state.mode !== "restricted" &&
      state.mode !== "local_only"
    )) return
    setDraftMode(state.mode)
    setDraftDomains(state.mode === "restricted" ? state.allowlist.join("\n") : "")
    setDraftFingerprint(state.stateFingerprint)
    setReview(null)
    setOutcome(null)
    setApplyProblem(null)
    reviewMutation.reset()
    applyMutation.reset()
  }

  function reviewChange() {
    if (!slugOrId || !validation.valid) return

    setReview(null)
    setOutcome(null)
    setApplyProblem(null)
    setPendingApply(null)
    reviewMutation.mutate(
      {
        mode: draftMode,
        allowlist: validation.canonicalAllowlist,
      },
      {
        onSuccess: (result) => {
          setReview(result)
          setReviewOpen(true)
        },
      },
    )
  }

  function confirmApply() {
    if (!review || review.noChange || !slugOrId) return

    const request: RuntimeStackFederationApplyRequest = {
      mode: review.proposedMode,
      allowlist: review.canonicalAllowlist,
      reviewHash: review.reviewHash,
      idempotencyKey: `federation-${slugOrId}-${crypto.randomUUID()}`,
    }

    setPendingApply(request)
    setReviewOpen(false)
    performApply(request)
  }

  function performApply(request: RuntimeStackFederationApplyRequest) {
    setTrackingPreviousOperationId(state?.latestOperation?.id ?? null)
    setTrackingOperationId(null)
    setTrackingOperation(true)
    setRequestDisconnected(false)
    setOutcome(null)
    setApplyProblem(null)

    applyMutation.mutate(request, {
      onSuccess: (result) => {
        setOutcome(result)
        setPendingApply(result.status === "running" ? request : null)
        setTrackingOperation(result.status === "running")
        setTrackingOperationId(result.status === "running" ? result.operationId : null)
        setTrackingPreviousOperationId(result.status === "running"
          ? state?.latestOperation?.id ?? null
          : null)
        setRequestDisconnected(false)
        void federationQuery.refetch()
      },
      onError: (error) => {
        if (isStepUpRequiredFederationProblem(error)) {
          applyMutation.reset()
          setTrackingOperation(false)
          setTrackingOperationId(null)
          setTrackingPreviousOperationId(null)
          setStepUpPending(true)
          return
        }

        const code = getFederationProblemCode(error)
        const detail = getFederationProblemDetail(error)

        if (!code) {
          // The bounded backend transaction continues after mutation begins even
          // when the browser request disconnects. Poll the durable operation.
          setApplyProblem(null)
          setRequestDisconnected(true)
          setTrackingOperation(true)
          setTrackingOperationId(null)
        } else {
          setApplyProblem(federationApplyProblemMessage(code, detail, error, t))
          setPendingApply(null)
          setTrackingOperation(false)
          setTrackingOperationId(null)
          setTrackingPreviousOperationId(null)
        }
      },
    })
  }

  function resumeAfterStepUp() {
    const request = pendingApply
    setStepUpOpen(false)
    setStepUpPending(false)
    if (request) performApply(request)
  }

  const reviewError = reviewMutation.error
    ? federationReviewProblemMessage(
        getFederationProblemCode(reviewMutation.error),
        getFederationProblemDetail(reviewMutation.error),
        reviewMutation.error,
        t,
      )
    : null

  return (
    <div className="space-y-6">
      <StackWorkspaceHeader
        slugOrId={slugOrId}
        displayName={stack?.slug ?? slugOrId ?? t("stacks.common.stack")}
        status={stack?.status}
        matrixPublicBaseUrl={stack?.matrix?.publicBaseUrl}
        elementPublicBaseUrl={stack?.element?.publicBaseUrl}
        onRefresh={refreshWorkspace}
        refreshing={stackQuery.isFetching || federationQuery.isFetching}
        onRunDoctor={() => doctor.mutate()}
        doctorPending={doctor.isPending}
        onCreateBackup={() => backup.mutate()}
        backupPending={backup.isPending}
      />

      {slugOrId ? <StackWorkspaceNavigation slugOrId={slugOrId} activeArea="federation" /> : null}

      <section>
        <h2 className="text-xl font-semibold tracking-tight">{t("federation.title")}</h2>
        <p className="mt-1 max-w-4xl text-sm text-muted-foreground">{t("federation.description")}</p>
      </section>

      {stackQuery.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("federation.stackLoadErrorTitle")}</AlertTitle>
          <AlertDescription>{stackQuery.error.message}</AlertDescription>
        </Alert>
      ) : null}

      {federationQuery.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("federation.loadErrorTitle")}</AlertTitle>
          <AlertDescription>
            {federationLoadProblemCode === "federation_stack_not_found"
              ? t("federation.error.stackNotFound")
              : federationLoadProblemCode === "federation_state_unavailable"
                ? t("federation.error.stateUnavailable")
                : getFederationProblemDetail(federationQuery.error) ?? federationQuery.error.message}
          </AlertDescription>
        </Alert>
      ) : null}

      {doctor.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.diagnostics.doctorFailedTitle")}</AlertTitle>
          <AlertDescription>{doctor.error.message}</AlertDescription>
        </Alert>
      ) : null}

      {backup.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.workspace.backupFailedTitle")}</AlertTitle>
          <AlertDescription>{backup.error.message}</AlertDescription>
        </Alert>
      ) : null}

      {backup.data ? (
        <Alert className="border-emerald-500/20 bg-emerald-500/10">
          <Archive className="h-4 w-4" />
          <AlertTitle>{t("stacks.workspace.backupCreatedTitle")}</AlertTitle>
          <AlertDescription>
            {t("stacks.workspace.backupCreatedDescription", {
              backupId: backup.data.backupId,
              date: formatDateTime(backup.data.createdAtUtc, language),
            })}
          </AlertDescription>
        </Alert>
      ) : null}

      <FederationOutcomeAlert result={outcome} problem={applyProblem} />
      <FederationOperationStatus
        active={operationActive}
        requestedMode={pendingApply?.mode ?? outcome?.requestedMode ?? null}
        state={state}
        requestDisconnected={requestDisconnected}
      />

      {state ? <FederationStateAlert state={state} /> : null}

      {!state && federationQuery.isLoading ? (
        <Card>
          <CardContent className="py-6 text-sm text-muted-foreground">
            {t("federation.loading")}
          </CardContent>
        </Card>
      ) : state ? (
        <>
          <FederationCurrentState state={state} />
          <FederationPolicyEditor
            mode={draftMode}
            domainText={draftDomains}
            validation={validation}
            disabled={managementDisabled}
            disabledReason={managementReason ?? (operationActive
              ? t("federation.editor.operationRunning")
              : null)}
            reviewPending={reviewMutation.isPending}
            reviewError={reviewError}
            onModeChange={(mode) => {
              setDraftMode(mode)
              if (mode !== "restricted") setDraftDomains("")
              setReview(null)
              reviewMutation.reset()
            }}
            onDomainTextChange={(value) => {
              setDraftDomains(value)
              setReview(null)
              reviewMutation.reset()
            }}
            onReset={resetDraft}
            onReview={reviewChange}
          />
        </>
      ) : null}

      <Card>
        <CardHeader>
          <CardTitle>{t("federation.modes.title")}</CardTitle>
          <CardDescription>{t("federation.modes.description")}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 lg:grid-cols-3">
          <ModeSummary title={t("federation.mode.public")} description={t("federation.mode.publicDescription")} />
          <ModeSummary title={t("federation.mode.restricted")} description={t("federation.mode.restrictedDescription")} />
          <ModeSummary title={t("federation.mode.localOnly")} description={t("federation.mode.localOnlyDescription")} />
        </CardContent>
      </Card>

      <Alert>
        <Info className="h-4 w-4" />
        <AlertTitle>{t("federation.scope.title")}</AlertTitle>
        <AlertDescription>{t("federation.scope.description")}</AlertDescription>
      </Alert>

      <FederationReviewDialog
        open={reviewOpen}
        review={review}
        onOpenChange={setReviewOpen}
        onConfirm={confirmApply}
      />

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={(open) => {
          setStepUpOpen(open)
          if (!open) setStepUpPending(false)
        }}
        onVerified={resumeAfterStepUp}
      />
    </div>
  )
}

function FederationStateAlert({ state }: { state: RuntimeStackFederationState }) {
  const { t } = useI18n()

  if (state.configurationState === "healthy") {
    if (state.warnings.length === 0) return null

    return (
      <Alert className="border-amber-500/30 bg-amber-500/10">
        <CircleAlert className="h-4 w-4" />
        <AlertTitle>{t("federation.alert.warningTitle")}</AlertTitle>
        <AlertDescription>{localizedProblemSummary(state, t("federation.alert.warningDescription"), t)}</AlertDescription>
      </Alert>
    )
  }

  if (state.configurationState === "unavailable") {
    return (
      <Alert variant="destructive">
        <ShieldAlert className="h-4 w-4" />
        <AlertTitle>{t("federation.alert.unavailableTitle")}</AlertTitle>
        <AlertDescription>{localizedProblemSummary(state, t("federation.alert.unavailableDescription"), t)}</AlertDescription>
      </Alert>
    )
  }

  if (state.configurationState === "custom_unsupported") {
    return (
      <Alert className="border-amber-500/30 bg-amber-500/10">
        <CircleAlert className="h-4 w-4" />
        <AlertTitle>{t("federation.alert.unsupportedTitle")}</AlertTitle>
        <AlertDescription>{localizedProblemSummary(state, t("federation.alert.unsupportedDescription"), t)}</AlertDescription>
      </Alert>
    )
  }

  return (
    <Alert className="border-amber-500/30 bg-amber-500/10">
      <CircleAlert className="h-4 w-4" />
      <AlertTitle>{t("federation.alert.incompleteTitle")}</AlertTitle>
      <AlertDescription>{localizedProblemSummary(state, t("federation.alert.incompleteDescription"), t)}</AlertDescription>
    </Alert>
  )
}

function localizedProblemSummary(
  state: RuntimeStackFederationState,
  fallback: string,
  t: (key: TranslationKey) => string,
) {
  const problem = state.problems[0] ?? state.warnings[0]
  if (!problem) return fallback

  const key = problemDetailKey(problem.code)
  return key ? t(key) : problem.detail
}

function problemDetailKey(code: string): TranslationKey | null {
  switch (code) {
    case "federation_manifest_incomplete": return "federation.problem.manifestIncomplete"
    case "federation_config_missing": return "federation.problem.configMissing"
    case "federation_config_unavailable": return "federation.problem.configUnavailable"
    case "federation_config_ambiguous": return "federation.problem.configAmbiguous"
    case "federation_config_custom_unsupported": return "federation.problem.configCustomUnsupported"
    case "federation_matrix_container_missing": return "federation.problem.containerMissing"
    case "federation_matrix_container_identity_mismatch": return "federation.problem.containerIdentityMismatch"
    case "federation_matrix_container_unavailable": return "federation.problem.containerUnavailable"
    case "federation_matrix_not_running": return "federation.problem.matrixNotRunning"
    case "federation_matrix_network_mismatch": return "federation.problem.matrixNetworkMismatch"
    case "federation_ingress_missing": return "federation.problem.ingressMissing"
    case "federation_ingress_unavailable": return "federation.problem.ingressUnavailable"
    case "federation_ingress_custom_unsupported": return "federation.problem.ingressCustomUnsupported"
    case "federation_ingress_policy_mismatch": return "federation.problem.ingressPolicyMismatch"
    case "federation_local_only_incomplete": return "federation.problem.localOnlyIncomplete"
    case "federation_direct_host_port_exposed": return "federation.problem.directPortExposed"
    case "federation_alternate_route_detected": return "federation.problem.alternateRouteDetected"
    case "federation_ingress_target_mismatch": return "federation.problem.ingressTargetMismatch"
    case "federation_ingress_disabled": return "federation.problem.ingressDisabled"
    case "federation_certificate_not_observed": return "federation.problem.certificateNotObserved"
    default: return null
  }
}

function federationManagementReason(
  state: RuntimeStackFederationState,
  t: (key: TranslationKey) => string,
) {
  if (state.configurationState === "custom_unsupported") {
    return t("federation.editor.unavailableCustom")
  }
  if (state.configurationState === "unavailable") {
    return t("federation.editor.unavailableState")
  }
  if (state.mode !== "public" && state.mode !== "restricted" && state.mode !== "local_only") {
    return t("federation.editor.unavailableUnknown")
  }
  if (state.ingressMode !== "normal" && state.ingressMode !== "local_only") {
    return t("federation.editor.unavailableIngress")
  }
  if (state.configurationState !== "healthy" && state.configurationState !== "incomplete") {
    return t("federation.editor.unavailableIncomplete")
  }
  if (
    !state.matrixContainerRunning ||
    state.matrixDirectHostPortExposed ||
    !state.canonicalRouteEnabled ||
    !state.canonicalRouteTargetsMatrix
  ) {
    return t("federation.error.stateMismatch")
  }
  return null
}

function federationReviewProblemMessage(
  code: string | null,
  detail: string | null,
  error: Error,
  t: (key: TranslationKey) => string,
) {
  switch (code) {
    case "federation_policy_invalid": return detail ?? t("federation.error.policyInvalid")
    case "federation_config_custom_unsupported": return t("federation.error.customUnsupported")
    case "federation_state_unavailable": return t("federation.error.stateUnavailable")
    case "federation_effective_state_mismatch": return t("federation.error.stateMismatch")
    default: return detail ?? error.message
  }
}

function federationApplyProblemMessage(
  code: string | null,
  detail: string | null,
  error: Error,
  t: (key: TranslationKey) => string,
) {
  switch (code) {
    case "federation_review_stale": return t("federation.error.reviewStale")
    case "federation_operation_in_progress": return t("federation.error.operationInProgress")
    case "federation_no_change": return t("federation.error.noChange")
    case "federation_idempotency_conflict": return t("federation.error.idempotencyConflict")
    case "federation_ingress_update_required": return t("federation.error.ingressUpdateRequired")
    case "federation_ingress_update_failed": return t("federation.error.ingressUpdateFailed")
    case "federation_client_readiness_failed": return t("federation.error.clientReadinessFailed")
    case "federation_direct_host_port_exposed": return t("federation.error.directPortExposed")
    case "federation_alternate_route_detected": return t("federation.error.alternateRouteDetected")
    case "federation_effective_state_mismatch": return t("federation.error.stateMismatch")
    case "federation_state_unavailable": return t("federation.error.stateUnavailable")
    default: return detail ?? error.message
  }
}

function ModeSummary({ title, description }: { title: string; description: string }) {
  return (
    <div className="rounded-lg border bg-muted/20 p-4">
      <h3 className="font-medium">{title}</h3>
      <p className="mt-2 text-sm text-muted-foreground">{description}</p>
    </div>
  )
}
