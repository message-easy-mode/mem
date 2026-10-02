import { useEffect, useState, type ReactNode } from "react"
import {
  AlertTriangle,
  Cable,
  CircleHelp,
  LoaderCircle,
  RadioTower,
  RefreshCw,
  ShieldCheck,
  Unplug,
} from "lucide-react"

import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import {
  getRuntimeStackProblemCode,
  getRuntimeStackProblemDetail,
} from "../api/stacks.api"
import type {
  RuntimeStackTurnConnectResponse,
  RuntimeStackTurnDisconnectResponse,
  RuntimeStackOperationResponse,
  RuntimeStackOperationsResponse,
  RuntimeStackTurnConnectReviewResponse,
  RuntimeStackTurnInspectionResponse,
} from "../api/stacks.types"
import {
  useConnectRuntimeStackTurn,
  useDisconnectRuntimeStackTurn,
  useReviewRuntimeStackTurnConnect,
  useReviewRuntimeStackTurnDisconnect,
  useRuntimeStackOperations,
  useRuntimeStackTurn,
} from "../hooks/use-runtime-stacks"

type Props = {
  slugOrId: string | undefined
}

type TurnMutationKind = "connect" | "disconnect"
type TurnMutationReconciliationState = "idle" | "verifying" | "running" | "unconfirmed" | "failed"
type TurnMutationReconciliationOutcome = Exclude<TurnMutationReconciliationState, "idle" | "verifying"> | "succeeded"

export function StackTurnStatusCard({ slugOrId }: Props) {
  const { language, t } = useI18n()
  const [reviewOpen, setReviewOpen] = useState(false)
  const [disconnectReviewOpen, setDisconnectReviewOpen] = useState(false)
  const [connectAttemptKey, setConnectAttemptKey] = useState<string | null>(null)
  const [disconnectAttemptKey, setDisconnectAttemptKey] = useState<string | null>(null)
  const [connectReconciliation, setConnectReconciliation] = useState<TurnMutationReconciliationState>("idle")
  const [disconnectReconciliation, setDisconnectReconciliation] = useState<TurnMutationReconciliationState>("idle")
  const safeSlug = slugOrId ?? ""
  const turnQuery = useRuntimeStackTurn(slugOrId)
  const operationsQuery = useRuntimeStackOperations(slugOrId)
  const reviewConnect = useReviewRuntimeStackTurnConnect(safeSlug)
  const connectTurn = useConnectRuntimeStackTurn(safeSlug)
  const reviewDisconnect = useReviewRuntimeStackTurnDisconnect(safeSlug)
  const disconnectTurn = useDisconnectRuntimeStackTurn(safeSlug)
  const resetConnectMutation = connectTurn.reset
  const resetDisconnectMutation = disconnectTurn.reset
  const resetConnectReview = reviewConnect.reset
  const resetDisconnectReview = reviewDisconnect.reset
  const turn = turnQuery.data
  const latestConnectOperation = operationsQuery.data?.operations.find(
    (operation) => operation.operation === "connect-stack-turn",
  )
  const latestDisconnectOperation = operationsQuery.data?.operations.find(
    (operation) => operation.operation === "disconnect-stack-turn",
  )
  const operationRunning = latestConnectOperation?.status === "running" ||
    latestDisconnectOperation?.status === "running"

  useEffect(() => {
    if (
      reviewConnect.error &&
      reviewConnect.submittedAt > 0 &&
      turnQuery.dataUpdatedAt > reviewConnect.submittedAt
    ) {
      resetConnectReview()
    }
  }, [
    resetConnectReview,
    reviewConnect.error,
    reviewConnect.submittedAt,
    turnQuery.dataUpdatedAt,
  ])

  useEffect(() => {
    if (
      reviewDisconnect.error &&
      reviewDisconnect.submittedAt > 0 &&
      turnQuery.dataUpdatedAt > reviewDisconnect.submittedAt
    ) {
      resetDisconnectReview()
    }
  }, [
    resetDisconnectReview,
    reviewDisconnect.error,
    reviewDisconnect.submittedAt,
    turnQuery.dataUpdatedAt,
  ])

  useEffect(() => {
    if (!connectAttemptKey || (connectReconciliation !== "running" && connectReconciliation !== "unconfirmed")) {
      return
    }

    const outcome = classifyTurnMutationOutcome(
      "connect",
      connectAttemptKey,
      turn,
      operationsQuery.data,
      reviewConnect.data?.mode,
    )

    if (outcome === "succeeded") {
      resetConnectMutation()
      setConnectAttemptKey(null)
      setConnectReconciliation("idle")
      setReviewOpen(false)
    } else if (outcome === "failed") {
      setConnectReconciliation("failed")
    }
  }, [
    connectAttemptKey,
    connectReconciliation,
    resetConnectMutation,
    operationsQuery.data,
    reviewConnect.data?.mode,
    turn,
  ])

  useEffect(() => {
    if (!disconnectAttemptKey || (disconnectReconciliation !== "running" && disconnectReconciliation !== "unconfirmed")) {
      return
    }

    const outcome = classifyTurnMutationOutcome(
      "disconnect",
      disconnectAttemptKey,
      turn,
      operationsQuery.data,
    )

    if (outcome === "succeeded") {
      resetDisconnectMutation()
      setDisconnectAttemptKey(null)
      setDisconnectReconciliation("idle")
      setDisconnectReviewOpen(false)
    } else if (outcome === "failed") {
      setDisconnectReconciliation("failed")
    }
  }, [
    disconnectAttemptKey,
    disconnectReconciliation,
    resetDisconnectMutation,
    operationsQuery.data,
    turn,
  ])

  function resetConnectAttempt() {
    resetConnectMutation()
    setConnectAttemptKey(null)
    setConnectReconciliation("idle")
  }

  function resetDisconnectAttempt() {
    resetDisconnectMutation()
    setDisconnectAttemptKey(null)
    setDisconnectReconciliation("idle")
  }

  function handleConnectDialogOpenChange(open: boolean) {
    setReviewOpen(open)
    if (!open) resetConnectAttempt()
  }

  function handleDisconnectDialogOpenChange(open: boolean) {
    setDisconnectReviewOpen(open)
    if (!open) resetDisconnectAttempt()
  }

  async function reconcileConnectOutcome(idempotencyKey: string) {
    setConnectReconciliation("verifying")
    const outcome = await refetchTurnMutationOutcome(
      "connect",
      idempotencyKey,
      turnQuery.refetch,
      operationsQuery.refetch,
      reviewConnect.data?.mode,
    )

    if (outcome === "succeeded") {
      resetConnectAttempt()
      setReviewOpen(false)
      return
    }

    setConnectReconciliation(outcome)
  }

  async function reconcileDisconnectOutcome(idempotencyKey: string) {
    setDisconnectReconciliation("verifying")
    const outcome = await refetchTurnMutationOutcome(
      "disconnect",
      idempotencyKey,
      turnQuery.refetch,
      operationsQuery.refetch,
    )

    if (outcome === "succeeded") {
      resetDisconnectAttempt()
      setDisconnectReviewOpen(false)
      return
    }

    setDisconnectReconciliation(outcome)
  }

  async function openConnectReview() {
    reviewConnect.reset()
    resetConnectAttempt()
    try {
      const review = await reviewConnect.mutateAsync()
      if (review.status === "no_change") {
        await turnQuery.refetch()
        return
      }
      setReviewOpen(true)
    } catch {
      // The mutation exposes a localized-safe detail below.
    }
  }

  async function confirmConnect() {
    const review = reviewConnect.data
    if (!review) return

    const idempotencyKey = connectAttemptKey ?? createTurnMutationIdempotencyKey("connect", safeSlug)
    setConnectAttemptKey(idempotencyKey)
    setConnectReconciliation("idle")

    try {
      const result = await connectTurn.mutateAsync({
        reviewHash: review.reviewHash,
        idempotencyKey,
        confirmConnectToPlatformTurn: true,
        confirmReplaceExternalTurn: review.mode === "replace-external",
      })
      if (result.status === "succeeded") {
        setConnectAttemptKey(null)
        setConnectReconciliation("idle")
        setReviewOpen(false)
        await turnQuery.refetch()
      } else if (result.status === "running") {
        await reconcileConnectOutcome(idempotencyKey)
      }
    } catch (error) {
      if (getRuntimeStackProblemCode(error) || getRuntimeStackProblemDetail(error)) {
        // The Host Agent returned an explicit problem response, so the dialog
        // can keep showing that authoritative server rejection.
        return
      }

      // A durable host mutation may have completed even when the browser cannot
      // consume its response. Clear the client-side error and reconcile against
      // the authoritative operation + TURN inspection before reporting failure.
      resetConnectMutation()
      await reconcileConnectOutcome(idempotencyKey)
    }
  }

  async function openDisconnectReview() {
    reviewDisconnect.reset()
    resetDisconnectAttempt()
    try {
      const review = await reviewDisconnect.mutateAsync()
      if (review.status === "no_change") {
        await turnQuery.refetch()
        return
      }
      setDisconnectReviewOpen(true)
    } catch {
      // The mutation exposes a localized-safe detail below.
    }
  }

  async function confirmDisconnect() {
    const review = reviewDisconnect.data
    if (!review) return

    const idempotencyKey = disconnectAttemptKey ?? createTurnMutationIdempotencyKey("disconnect", safeSlug)
    setDisconnectAttemptKey(idempotencyKey)
    setDisconnectReconciliation("idle")

    try {
      const result = await disconnectTurn.mutateAsync({
        reviewHash: review.reviewHash,
        idempotencyKey,
        confirmDisconnectFromPlatformTurn: true,
      })
      if (result.status === "succeeded") {
        setDisconnectAttemptKey(null)
        setDisconnectReconciliation("idle")
        setDisconnectReviewOpen(false)
        await turnQuery.refetch()
      } else if (result.status === "running") {
        await reconcileDisconnectOutcome(idempotencyKey)
      }
    } catch (error) {
      if (getRuntimeStackProblemCode(error) || getRuntimeStackProblemDetail(error)) {
        return
      }

      // Do not turn a browser/transport outcome into a server failure claim.
      resetDisconnectMutation()
      await reconcileDisconnectOutcome(idempotencyKey)
    }
  }

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2">
              <RadioTower className="h-5 w-5" />
              {t("stacks.voice.turnTitle")}
            </CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("stacks.voice.turnDescription")}
            </p>
          </div>

          {turn ? <TurnStatusBadge state={turn.state} /> : null}
        </div>
      </CardHeader>

      <CardContent className="space-y-4">
        {turnQuery.isLoading && !turn ? (
          <StatePanel tone="neutral">{t("stacks.voice.loading")}</StatePanel>
        ) : turnQuery.error ? (
          <>
            <StatePanel tone="danger">{t("stacks.voice.inspectionError")}</StatePanel>
            <Button variant="outline" size="sm" onClick={() => void turnQuery.refetch()}>
              <RefreshCw className="mr-2 h-4 w-4" />
              {t("stacks.voice.retryInspection")}
            </Button>
          </>
        ) : !turn ? (
          <StatePanel tone="neutral">{t("stacks.voice.unavailable")}</StatePanel>
        ) : (
          <>
            <StatePanel tone={stateTone(turn.state)}>{stateDescription(turn.state, t)}</StatePanel>

            <div className="grid gap-3 text-sm md:grid-cols-2 xl:grid-cols-4">
              <Info label={t("stacks.voice.stackConnection")} value={stateLabel(turn.state, t)} />
              <Info label={t("stacks.voice.management")} value={managementLabel(turn.management, t)} />
              <Info
                label={t("stacks.voice.platformService")}
                value={turn.platform ? readinessLabel(turn.platform.readiness, t) : t("stacks.voice.platformUnavailable")}
              />
              <Info
                label={t("stacks.voice.lastInspected")}
                value={formatDateTime(turn.inspectedAtUtc, language)}
              />
              {turn.liveConfiguration?.publicHost ? (
                <Info label={t("stacks.voice.turnPublicHost")} value={turn.liveConfiguration.publicHost} />
              ) : null}
            </div>

            {turn.liveConfiguration?.turnUris.length ? (
              <div className="rounded-xl border border-border bg-background/40 p-4">
                <div className="mb-3 text-sm font-medium">{t("stacks.voice.turnUris")}</div>
                <div className="space-y-2">
                  {turn.liveConfiguration.turnUris.map((uri) => (
                    <div
                      key={uri}
                      className="break-all rounded-lg border border-border bg-background/40 px-3 py-2 font-mono text-xs text-muted-foreground"
                    >
                      {uri}
                    </div>
                  ))}
                </div>
              </div>
            ) : null}

            {turn.platform && turn.platform.readiness !== "ready" ? (
              <StatePanel tone="warning">{t("stacks.voice.platformNotReady")}</StatePanel>
            ) : null}

            {turn.warnings.length ? (
              <div className="space-y-2">
                {turn.warnings.map((warning) => (
                  <StatePanel key={warning} tone="warning">{warning}</StatePanel>
                ))}
              </div>
            ) : null}

            {!connectTurn.data && latestConnectOperation?.status === "running" ? (
              <StatePanel tone="info">
                <span className="inline-flex items-center gap-2">
                  <LoaderCircle className="h-4 w-4 animate-spin" aria-hidden="true" />
                  {t("stacks.voice.connectOperationRunning")}
                </span>
              </StatePanel>
            ) : !connectTurn.data && latestConnectOperation?.status === "rolled_back" ? (
              <StatePanel tone="warning">{t("stacks.voice.connectRolledBack")}</StatePanel>
            ) : !connectTurn.data && latestConnectOperation?.lastError === "turn_connect_rollback_failed" ? (
              <StatePanel tone="danger">{t("stacks.voice.connectUnresolved")}</StatePanel>
            ) : !connectTurn.data && latestConnectOperation?.lastError === "turn_connect_candidate_validation_failed" ? (
              <StatePanel tone="danger">{t("stacks.voice.connectCandidateRejected")}</StatePanel>
            ) : !connectTurn.data && latestConnectOperation?.status === "failed" ? (
              <StatePanel tone="danger">{t("stacks.voice.connectDurableFailed")}</StatePanel>
            ) : null}

            {!disconnectTurn.data && latestDisconnectOperation?.status === "running" ? (
              <StatePanel tone="info">
                <span className="inline-flex items-center gap-2">
                  <LoaderCircle className="h-4 w-4 animate-spin" aria-hidden="true" />
                  {t("stacks.voice.disconnectOperationRunning")}
                </span>
              </StatePanel>
            ) : !disconnectTurn.data && latestDisconnectOperation?.status === "rolled_back" ? (
              <StatePanel tone="warning">{t("stacks.voice.disconnectRolledBack")}</StatePanel>
            ) : !disconnectTurn.data && latestDisconnectOperation?.lastError === "turn_disconnect_rollback_failed" ? (
              <StatePanel tone="danger">{t("stacks.voice.disconnectUnresolved")}</StatePanel>
            ) : !disconnectTurn.data && latestDisconnectOperation?.lastError === "turn_disconnect_candidate_validation_failed" ? (
              <StatePanel tone="danger">{t("stacks.voice.disconnectCandidateRejected")}</StatePanel>
            ) : !disconnectTurn.data && latestDisconnectOperation?.status === "failed" ? (
              <StatePanel tone="danger">{t("stacks.voice.disconnectDurableFailed")}</StatePanel>
            ) : null}

            {connectTurn.data ? <ConnectOutcome result={connectTurn.data} /> : null}
            {disconnectTurn.data ? <DisconnectOutcome result={disconnectTurn.data} /> : null}

            {reviewConnect.error ? (
              <Alert variant="destructive">
                <AlertTitle>{t("stacks.voice.connectReviewFailedTitle")}</AlertTitle>
                <AlertDescription>
                  {getRuntimeStackProblemDetail(reviewConnect.error) ?? t("stacks.voice.connectReviewFailedDescription")}
                </AlertDescription>
              </Alert>
            ) : null}

            {reviewDisconnect.error ? (
              <Alert variant="destructive">
                <AlertTitle>{t("stacks.voice.disconnectReviewFailedTitle")}</AlertTitle>
                <AlertDescription>
                  {getRuntimeStackProblemDetail(reviewDisconnect.error) ?? t("stacks.voice.disconnectReviewFailedDescription")}
                </AlertDescription>
              </Alert>
            ) : null}

            <StatePanel tone="info">{t("stacks.voice.inspectionNotice")}</StatePanel>

            <div className="flex flex-wrap gap-2">
              {canOfferConnect(turn) ? (
                <Button
                  type="button"
                  size="sm"
                  onClick={() => void openConnectReview()}
                  disabled={reviewConnect.isPending || connectTurn.isPending || operationRunning}
                >
                  <Cable className="mr-2 h-4 w-4" />
                  {reviewConnect.isPending
                    ? t("stacks.voice.reviewingConnect")
                    : turn.state === "external"
                      ? t("stacks.voice.replaceExternalAction")
                      : t("stacks.voice.connectAction")}
                </Button>
              ) : null}

              {canOfferDisconnect(turn) ? (
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  onClick={() => void openDisconnectReview()}
                  disabled={reviewDisconnect.isPending || disconnectTurn.isPending || operationRunning}
                >
                  <Unplug className="mr-2 h-4 w-4" />
                  {reviewDisconnect.isPending
                    ? t("stacks.voice.reviewingDisconnect")
                    : t("stacks.voice.disconnectAction")}
                </Button>
              ) : null}

              <Button
                variant="outline"
                size="sm"
                onClick={() => void turnQuery.refetch()}
                disabled={turnQuery.isFetching}
              >
                <RefreshCw className={turnQuery.isFetching ? "mr-2 h-4 w-4 animate-spin" : "mr-2 h-4 w-4"} />
                {t("stacks.voice.retryInspection")}
              </Button>
            </div>

            <details className="rounded-xl border border-border bg-background/30 p-4">
              <summary className="cursor-pointer text-sm font-medium">
                {t("stacks.voice.technicalDetails")}
              </summary>
              <div className="mt-4 space-y-4">
                <div className="grid gap-3 text-sm md:grid-cols-2 xl:grid-cols-3">
                  <Info
                    label={t("stacks.voice.matrixRuntime")}
                    value={turn.matrixRuntime.running && turn.matrixRuntime.identityMatches
                      ? t("stacks.voice.matrixRuntimeVerified")
                      : t("stacks.voice.matrixRuntimeUnavailable")}
                  />
                  <Info
                    label={t("stacks.voice.credentialMechanism")}
                    value={turn.liveConfiguration?.credentialMechanism ?? t("stacks.common.notRecorded")}
                  />
                  <Info
                    label={t("stacks.voice.sharedSecretMatch")}
                    value={booleanMatchLabel(turn.liveConfiguration?.sharedSecretMatchesPlatform ?? null, t)}
                  />
                  <Info
                    label={t("stacks.voice.configurationHash")}
                    value={turn.liveConfiguration?.fileSha256 ?? t("stacks.common.notRecorded")}
                  />
                  <Info
                    label={t("stacks.voice.persistedMetadata")}
                    value={turn.persistedMetadata.recorded
                      ? turn.persistedMetadata.matchesLiveConfiguration === false
                        ? t("stacks.voice.metadataMismatch")
                        : t("stacks.voice.metadataRecorded")
                      : t("stacks.voice.metadataMissing")}
                  />
                  <Info
                    label={t("stacks.voice.userLifetime")}
                    value={turn.liveConfiguration?.userLifetime ?? t("stacks.common.notRecorded")}
                  />
                </div>

                <div className="space-y-2">
                  {turn.diagnostics.map((diagnostic) => (
                    <div key={diagnostic.code} className="rounded-lg border border-border px-3 py-2 text-sm">
                      <div className="font-medium">{diagnosticStatusLabel(diagnostic.status, t)}</div>
                      <div className="mt-1 text-muted-foreground">{diagnostic.message}</div>
                    </div>
                  ))}
                </div>
              </div>
            </details>
          </>
        )}
      </CardContent>

      <ConfirmationDialog
        open={reviewOpen}
        onOpenChange={handleConnectDialogOpenChange}
        title={reviewConnect.data?.mode === "replace-external"
          ? t("stacks.voice.replaceExternalDialogTitle")
          : t("stacks.voice.connectDialogTitle")}
        description={reviewConnect.data
          ? reviewConnect.data.mode === "adopt-existing"
            ? t("stacks.voice.connectDialogDescriptionAdopt")
            : reviewConnect.data.mode === "replace-external"
              ? t("stacks.voice.connectDialogDescriptionReplaceExternal")
              : t("stacks.voice.connectDialogDescriptionConfigure")
          : t("stacks.voice.connectDialogDescription")}
        confirmLabel={reviewConnect.data?.mode === "replace-external"
          ? t("stacks.voice.replaceExternalConfirm")
          : t("stacks.voice.connectConfirm")}
        confirmingLabel={t("stacks.voice.connecting")}
        cancelLabel={t("stacks.voice.connectCancel")}
        confirmVariant={reviewConnect.data?.mode === "replace-external" ? "destructive" : "default"}
        onConfirm={() => void confirmConnect()}
        isConfirming={connectTurn.isPending || connectReconciliation === "verifying"}
        showProgress={connectTurn.isPending || connectReconciliation === "verifying" || connectReconciliation === "running"}
        confirmDisabled={connectReconciliation === "running" || connectReconciliation === "unconfirmed" || connectReconciliation === "failed"}
      >
        {reviewConnect.data ? (
          <div className="space-y-3 text-sm">
            <div className="grid gap-3 sm:grid-cols-2">
              <Info
                label={t("stacks.voice.connectMode")}
                value={reviewConnect.data.mode === "adopt-existing"
                  ? t("stacks.voice.connectModeAdopt")
                  : reviewConnect.data.mode === "replace-external"
                    ? t("stacks.voice.connectModeReplaceExternal")
                    : t("stacks.voice.connectModeConfigure")}
              />
              <Info
                label={t("stacks.voice.restartRequired")}
                value={reviewConnect.data.restartRequired
                  ? t("stacks.voice.restartRequiredYes")
                  : t("stacks.voice.restartRequiredNo")}
              />
              <Info
                label={t("stacks.voice.matrixServerName")}
                value={reviewConnect.data.matrixServerName}
              />
              <Info
                label={t("stacks.voice.turnPublicHost")}
                value={reviewConnect.data.platformPublicHost}
              />
              <Info
                label={t("stacks.voice.userLifetime")}
                value={reviewConnect.data.userLifetime}
              />
            </div>
            <div className="rounded-lg border border-border bg-background/30 px-3 py-2">
              <div className="mb-2 text-xs text-muted-foreground">{t("stacks.voice.turnUris")}</div>
              <div className="space-y-1">
                {reviewConnect.data.turnUris.map((uri) => (
                  <div key={uri} className="break-all font-mono text-xs">{uri}</div>
                ))}
              </div>
            </div>
            <div className="space-y-2">
              {localizedConsequences(reviewConnect.data.mode, t).map((consequence) => (
                <div key={consequence} className="rounded-lg border border-border px-3 py-2 text-muted-foreground">
                  {consequence}
                </div>
              ))}
            </div>
          </div>
        ) : null}

        {connectTurn.isPending && connectReconciliation === "idle" ? (
          <TurnMutationPendingAlert kind="connect" />
        ) : connectReconciliation !== "idle" ? (
          <TurnMutationReconciliationAlert
            kind="connect"
            state={connectReconciliation}
            operation={findTurnMutationOperation(operationsQuery.data, "connect", connectAttemptKey)}
            onVerify={() => connectAttemptKey ? void reconcileConnectOutcome(connectAttemptKey) : undefined}
          />
        ) : connectTurn.error ? (
          <Alert variant="destructive">
            <AlertTitle>{t("stacks.voice.connectFailedTitle")}</AlertTitle>
            <AlertDescription>
              {getRuntimeStackProblemDetail(connectTurn.error) ?? t("stacks.voice.connectFailedDescription")}
            </AlertDescription>
          </Alert>
        ) : null}
      </ConfirmationDialog>

      <ConfirmationDialog
        open={disconnectReviewOpen}
        onOpenChange={handleDisconnectDialogOpenChange}
        title={t("stacks.voice.disconnectDialogTitle")}
        description={t("stacks.voice.disconnectDialogDescription")}
        confirmLabel={t("stacks.voice.disconnectConfirm")}
        confirmingLabel={t("stacks.voice.disconnecting")}
        cancelLabel={t("stacks.voice.disconnectCancel")}
        onConfirm={() => void confirmDisconnect()}
        isConfirming={disconnectTurn.isPending || disconnectReconciliation === "verifying"}
        showProgress={disconnectTurn.isPending || disconnectReconciliation === "verifying" || disconnectReconciliation === "running"}
        confirmDisabled={disconnectReconciliation === "running" || disconnectReconciliation === "unconfirmed" || disconnectReconciliation === "failed"}
      >
        {reviewDisconnect.data ? (
          <div className="space-y-3 text-sm">
            <div className="grid gap-3 sm:grid-cols-2">
              <Info
                label={t("stacks.voice.disconnectMode")}
                value={t("stacks.voice.disconnectModeRemove")}
              />
              <Info
                label={t("stacks.voice.restartRequired")}
                value={reviewDisconnect.data.restartRequired
                  ? t("stacks.voice.restartRequiredYes")
                  : t("stacks.voice.restartRequiredNo")}
              />
              <Info
                label={t("stacks.voice.matrixServerName")}
                value={reviewDisconnect.data.matrixServerName}
              />
              <Info
                label={t("stacks.voice.turnPublicHost")}
                value={reviewDisconnect.data.platformPublicHost ?? t("stacks.common.notRecorded")}
              />
            </div>
            <div className="rounded-lg border border-border bg-background/30 px-3 py-2">
              <div className="mb-2 text-xs text-muted-foreground">{t("stacks.voice.turnUris")}</div>
              <div className="space-y-1">
                {reviewDisconnect.data.turnUris.map((uri) => (
                  <div key={uri} className="break-all font-mono text-xs">{uri}</div>
                ))}
              </div>
            </div>
            <div className="space-y-2">
              {[
                t("stacks.voice.disconnectConsequenceConfig"),
                t("stacks.voice.disconnectConsequenceRestart"),
                t("stacks.voice.disconnectConsequenceCalls"),
                t("stacks.voice.disconnectConsequenceIdentity"),
              ].map((consequence) => (
                <div key={consequence} className="rounded-lg border border-border px-3 py-2 text-muted-foreground">
                  {consequence}
                </div>
              ))}
            </div>
          </div>
        ) : null}

        {disconnectTurn.isPending && disconnectReconciliation === "idle" ? (
          <TurnMutationPendingAlert kind="disconnect" />
        ) : disconnectReconciliation !== "idle" ? (
          <TurnMutationReconciliationAlert
            kind="disconnect"
            state={disconnectReconciliation}
            operation={findTurnMutationOperation(operationsQuery.data, "disconnect", disconnectAttemptKey)}
            onVerify={() => disconnectAttemptKey ? void reconcileDisconnectOutcome(disconnectAttemptKey) : undefined}
          />
        ) : disconnectTurn.error ? (
          <Alert variant="destructive">
            <AlertTitle>{t("stacks.voice.disconnectFailedTitle")}</AlertTitle>
            <AlertDescription>
              {getRuntimeStackProblemDetail(disconnectTurn.error) ?? t("stacks.voice.disconnectFailedDescription")}
            </AlertDescription>
          </Alert>
        ) : null}
      </ConfirmationDialog>
    </Card>
  )
}

function TurnMutationPendingAlert({ kind }: { kind: TurnMutationKind }) {
  const { t } = useI18n()

  return (
    <Alert aria-live="polite">
      <LoaderCircle className="h-4 w-4 animate-spin" aria-hidden="true" />
      <AlertTitle>
        {kind === "connect"
          ? t("stacks.voice.connectProgressTitle")
          : t("stacks.voice.disconnectProgressTitle")}
      </AlertTitle>
      <AlertDescription>
        {kind === "connect"
          ? t("stacks.voice.connectProgressDescription")
          : t("stacks.voice.disconnectProgressDescription")}
      </AlertDescription>
    </Alert>
  )
}

function TurnMutationReconciliationAlert({
  kind,
  state,
  operation,
  onVerify,
}: {
  kind: TurnMutationKind
  state: TurnMutationReconciliationState
  operation: RuntimeStackOperationResponse | undefined
  onVerify: () => void
}) {
  const { t } = useI18n()
  const verifying = state === "verifying"

  if (state === "failed") {
    return (
      <Alert variant="destructive">
        <AlertTitle>
          {kind === "connect" ? t("stacks.voice.connectFailedTitle") : t("stacks.voice.disconnectFailedTitle")}
        </AlertTitle>
        <AlertDescription>
          {kind === "connect"
            ? connectOperationFailureDescription(operation, t)
            : disconnectOperationFailureDescription(operation, t)}
        </AlertDescription>
      </Alert>
    )
  }

  const showSpinner = state === "verifying" || state === "running"

  return (
    <Alert aria-live="polite">
      {showSpinner ? (
        <LoaderCircle className="h-4 w-4 animate-spin" aria-hidden="true" />
      ) : null}
      <AlertTitle>
        {state === "running"
          ? kind === "connect"
            ? t("stacks.voice.connectOutcomeRunningTitle")
            : t("stacks.voice.disconnectOutcomeRunningTitle")
          : state === "unconfirmed"
            ? kind === "connect"
              ? t("stacks.voice.connectOutcomeUnconfirmedTitle")
              : t("stacks.voice.disconnectOutcomeUnconfirmedTitle")
            : kind === "connect"
              ? t("stacks.voice.connectOutcomeVerifyingTitle")
              : t("stacks.voice.disconnectOutcomeVerifyingTitle")}
      </AlertTitle>
      <AlertDescription>
        <div className="space-y-3">
          <p>
            {state === "running"
              ? kind === "connect"
                ? t("stacks.voice.connectOutcomeRunningDescription")
                : t("stacks.voice.disconnectOutcomeRunningDescription")
              : state === "unconfirmed"
                ? kind === "connect"
                  ? t("stacks.voice.connectOutcomeUnconfirmedDescription")
                  : t("stacks.voice.disconnectOutcomeUnconfirmedDescription")
                : t("stacks.voice.turnOutcomeVerifyingDescription")}
          </p>
          {state === "running" || state === "unconfirmed" ? (
            <Button type="button" variant="outline" size="sm" onClick={onVerify} disabled={verifying}>
              <RefreshCw className="mr-2 h-4 w-4" />
              {t("stacks.voice.turnOutcomeVerifyAgain")}
            </Button>
          ) : null}
        </div>
      </AlertDescription>
    </Alert>
  )
}

function createTurnMutationIdempotencyKey(kind: TurnMutationKind, slug: string) {
  return `turn-${kind}-${slug}-${crypto.randomUUID()}`
}

async function refetchTurnMutationOutcome(
  kind: TurnMutationKind,
  idempotencyKey: string,
  refetchTurn: () => Promise<{ data?: RuntimeStackTurnInspectionResponse }>,
  refetchOperations: () => Promise<{ data?: RuntimeStackOperationsResponse }>,
  connectMode?: RuntimeStackTurnConnectReviewResponse["mode"],
): Promise<TurnMutationReconciliationOutcome> {
  const [turnResult, operationsResult] = await Promise.allSettled([
    refetchTurn(),
    refetchOperations(),
  ])

  const turn = turnResult.status === "fulfilled" ? turnResult.value.data : undefined
  const operations = operationsResult.status === "fulfilled" ? operationsResult.value.data : undefined

  return classifyTurnMutationOutcome(kind, idempotencyKey, turn, operations, connectMode)
}

function classifyTurnMutationOutcome(
  kind: TurnMutationKind,
  idempotencyKey: string,
  turn: RuntimeStackTurnInspectionResponse | undefined,
  operations: RuntimeStackOperationsResponse | undefined,
  connectMode?: RuntimeStackTurnConnectReviewResponse["mode"],
): TurnMutationReconciliationOutcome {
  const operation = findTurnMutationOperation(operations, kind, idempotencyKey)
  if (operation) {
    if (operation.status === "succeeded") return "succeeded"
    if (["running", "queued", "accepted", "pending"].includes(operation.status)) return "running"
    if (["failed", "rolled_back", "cancelled", "canceled"].includes(operation.status)) return "failed"
  }

  if (kind === "connect" && turn) {
    const exactManagedConnection = turn.state === "connected" &&
      turn.management === "mem-managed" &&
      turn.persistedMetadata.recorded &&
      turn.persistedMetadata.configured === true &&
      turn.persistedMetadata.matchesLiveConfiguration !== false

    if (connectMode === "adopt-existing") {
      if (exactManagedConnection && turn.persistedMetadata.configurationSource === "platform-coturn") {
        return "succeeded"
      }
    } else if (exactManagedConnection) {
      return "succeeded"
    }
  }

  if (kind === "disconnect" && turn?.state === "not-connected" &&
      turn.management === "none" &&
      turn.liveConfiguration?.anyTurnSettings === false) {
    return "succeeded"
  }

  return "unconfirmed"
}

function findTurnMutationOperation(
  operations: RuntimeStackOperationsResponse | undefined,
  kind: TurnMutationKind,
  idempotencyKey: string | null,
) {
  if (!idempotencyKey) return undefined
  const operationName = kind === "connect" ? "connect-stack-turn" : "disconnect-stack-turn"
  return operations?.operations.find(
    (operation) => operation.operation === operationName && operation.idempotencyKey === idempotencyKey,
  )
}

function connectOperationFailureDescription(
  operation: RuntimeStackOperationResponse | undefined,
  t: ReturnType<typeof useI18n>["t"],
) {
  if (operation?.lastError === "turn_connect_rollback_failed") return t("stacks.voice.connectUnresolved")
  if (operation?.lastError === "turn_connect_candidate_validation_failed") return t("stacks.voice.connectCandidateRejected")
  if (operation?.status === "rolled_back") return t("stacks.voice.connectRolledBack")
  return t("stacks.voice.connectDurableFailed")
}

function disconnectOperationFailureDescription(
  operation: RuntimeStackOperationResponse | undefined,
  t: ReturnType<typeof useI18n>["t"],
) {
  if (operation?.lastError === "turn_disconnect_rollback_failed") return t("stacks.voice.disconnectUnresolved")
  if (operation?.lastError === "turn_disconnect_candidate_validation_failed") return t("stacks.voice.disconnectCandidateRejected")
  if (operation?.status === "rolled_back") return t("stacks.voice.disconnectRolledBack")
  return t("stacks.voice.disconnectDurableFailed")
}

function ConnectOutcome({ result }: { result: RuntimeStackTurnConnectResponse }) {
  const { t } = useI18n()
  if (result.status === "succeeded") {
    return (
      <StatePanel tone="success">
        {result.mode === "adopt-existing"
          ? t("stacks.voice.connectAdoptedSuccess")
          : result.mode === "replace-external"
            ? t("stacks.voice.connectReplacedExternalSuccess")
            : t("stacks.voice.connectConfiguredSuccess")}
      </StatePanel>
    )
  }
  if (result.status === "rolled_back") {
    return <StatePanel tone="warning">{t("stacks.voice.connectRolledBack")}</StatePanel>
  }
  if (result.status === "running") {
    return <StatePanel tone="info">{t("stacks.voice.connectOperationRunning")}</StatePanel>
  }
  if (result.status === "candidate_rejected") {
    return <StatePanel tone="danger">{t("stacks.voice.connectCandidateRejected")}</StatePanel>
  }
  if (result.status === "unresolved") {
    return <StatePanel tone="danger">{t("stacks.voice.connectUnresolved")}</StatePanel>
  }
  return <StatePanel tone="danger">{t("stacks.voice.connectFailedSafe")}</StatePanel>
}

function DisconnectOutcome({ result }: { result: RuntimeStackTurnDisconnectResponse }) {
  const { t } = useI18n()
  if (result.status === "succeeded") {
    return <StatePanel tone="success">{t("stacks.voice.disconnectSuccess")}</StatePanel>
  }
  if (result.status === "rolled_back") {
    return <StatePanel tone="warning">{t("stacks.voice.disconnectRolledBack")}</StatePanel>
  }
  if (result.status === "running") {
    return <StatePanel tone="info">{t("stacks.voice.disconnectOperationRunning")}</StatePanel>
  }
  if (result.status === "candidate_rejected") {
    return <StatePanel tone="danger">{t("stacks.voice.disconnectCandidateRejected")}</StatePanel>
  }
  if (result.status === "unresolved") {
    return <StatePanel tone="danger">{t("stacks.voice.disconnectUnresolved")}</StatePanel>
  }
  return <StatePanel tone="danger">{t("stacks.voice.disconnectFailedSafe")}</StatePanel>
}

function localizedConsequences(
  mode: "configure" | "adopt-existing" | "replace-external" | "no-change",
  t: ReturnType<typeof useI18n>["t"],
) {
  if (mode === "adopt-existing") {
    return [
      t("stacks.voice.connectConsequenceAdoptConfig"),
      t("stacks.voice.connectConsequenceAdoptRestart"),
      t("stacks.voice.connectConsequenceAssociation"),
      t("stacks.voice.connectConsequenceIdentity"),
    ]
  }

  if (mode === "replace-external") {
    return [
      t("stacks.voice.connectConsequenceReplaceSnapshot"),
      t("stacks.voice.connectConsequenceReplaceConfig"),
      t("stacks.voice.connectConsequenceReplaceRestart"),
      t("stacks.voice.connectConsequenceReplaceRollback"),
      t("stacks.voice.connectConsequenceIdentity"),
    ]
  }

  return [
    t("stacks.voice.connectConsequenceConfigureConfig"),
    t("stacks.voice.connectConsequenceConfigureRestart"),
    t("stacks.voice.connectConsequenceAssociation"),
    t("stacks.voice.connectConsequenceIdentity"),
  ]
}

function canOfferConnect(turn: RuntimeStackTurnInspectionResponse) {
  if (turn.platform?.readiness !== "ready") return false
  if (turn.state === "not-connected") return true

  const liveMatchesPlatform = turn.liveConfiguration?.supported === true &&
    turn.liveConfiguration.anyTurnSettings &&
    turn.liveConfiguration.sharedSecretMatchesPlatform === true &&
    sameUris(turn.liveConfiguration.turnUris, turn.platform.turnUris)

  if (turn.state === "external") {
    return turn.management === "external-observed" &&
      turn.liveConfiguration?.supported === true &&
      turn.liveConfiguration.anyTurnSettings
  }

  if (turn.persistedMetadata.recorded &&
      turn.persistedMetadata.configurationSource === "migration-source-preserved" &&
      liveMatchesPlatform) {
    return true
  }

  return turn.state === "drift" &&
    turn.management === "mem-managed" &&
    turn.persistedMetadata.recorded === false &&
    liveMatchesPlatform
}

function canOfferDisconnect(turn: RuntimeStackTurnInspectionResponse) {
  return turn.state === "connected" &&
    turn.management === "mem-managed" &&
    turn.persistedMetadata.recorded &&
    turn.persistedMetadata.configured === true &&
    turn.liveConfiguration?.supported === true &&
    turn.liveConfiguration.anyTurnSettings &&
    turn.liveConfiguration.memManagedMarkerPresent
}

function sameUris(left: string[], right: string[]) {
  const normalize = (value: string) => value.trim().toLowerCase()
  return left.map(normalize).sort().join("\n") === right.map(normalize).sort().join("\n")
}

function TurnStatusBadge({ state }: { state: RuntimeStackTurnInspectionResponse["state"] }) {
  const { t } = useI18n()
  const styles = {
    connected: "border-emerald-500/20 bg-emerald-500/10 text-emerald-100",
    "not-connected": "border-amber-500/20 bg-amber-500/10 text-amber-100",
    external: "border-sky-500/20 bg-sky-500/10 text-sky-100",
    drift: "border-destructive/30 bg-destructive/10 text-destructive-foreground",
    unknown: "border-border bg-muted/40 text-muted-foreground",
  }[state]

  const Icon = state === "connected"
    ? ShieldCheck
    : state === "not-connected"
      ? Unplug
      : state === "external"
        ? Cable
        : state === "drift"
          ? AlertTriangle
          : CircleHelp

  return (
    <div className={`inline-flex w-fit items-center gap-2 rounded-full border px-3 py-1 text-xs ${styles}`}>
      <Icon className="h-3.5 w-3.5" />
      {stateLabel(state, t)}
    </div>
  )
}

function Info({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border bg-background/30 px-3 py-2">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="mt-1 break-words font-medium">{value}</div>
    </div>
  )
}

function StatePanel({ tone, children }: { tone: "neutral" | "success" | "warning" | "danger" | "info"; children: ReactNode }) {
  const styles = {
    neutral: "border-border bg-muted/30 text-muted-foreground",
    success: "border-emerald-500/20 bg-emerald-500/10 text-emerald-100",
    warning: "border-amber-500/20 bg-amber-500/10 text-amber-100",
    danger: "border-destructive/30 bg-destructive/10 text-destructive-foreground",
    info: "border-sky-500/20 bg-sky-500/10 text-sky-100",
  }[tone]
  return <div className={`rounded-xl border p-4 text-sm ${styles}`}>{children}</div>
}

function stateTone(state: RuntimeStackTurnInspectionResponse["state"]): "neutral" | "success" | "warning" | "danger" | "info" {
  if (state === "connected") return "success"
  if (state === "not-connected") return "warning"
  if (state === "external") return "info"
  if (state === "drift") return "danger"
  return "neutral"
}

function stateDescription(state: RuntimeStackTurnInspectionResponse["state"], t: ReturnType<typeof useI18n>["t"]) {
  if (state === "connected") return t("stacks.voice.state.connected.description")
  if (state === "not-connected") return t("stacks.voice.state.not-connected.description")
  if (state === "external") return t("stacks.voice.state.external.description")
  if (state === "drift") return t("stacks.voice.state.drift.description")
  return t("stacks.voice.state.unknown.description")
}

function stateLabel(state: RuntimeStackTurnInspectionResponse["state"], t: ReturnType<typeof useI18n>["t"]) {
  if (state === "connected") return t("stacks.voice.state.connected.label")
  if (state === "not-connected") return t("stacks.voice.state.not-connected.label")
  if (state === "external") return t("stacks.voice.state.external.label")
  if (state === "drift") return t("stacks.voice.state.drift.label")
  return t("stacks.voice.state.unknown.label")
}

function managementLabel(management: RuntimeStackTurnInspectionResponse["management"], t: ReturnType<typeof useI18n>["t"]) {
  if (management === "mem-managed") return t("stacks.voice.management.mem-managed")
  if (management === "external-observed") return t("stacks.voice.management.external-observed")
  if (management === "none") return t("stacks.voice.management.none")
  return t("stacks.voice.management.unknown")
}

function readinessLabel(readiness: string, t: ReturnType<typeof useI18n>["t"]) {
  return readiness === "ready" ? t("stacks.voice.platformReady") : t("stacks.voice.platformSetupIncomplete")
}

function booleanMatchLabel(value: boolean | null, t: ReturnType<typeof useI18n>["t"]) {
  if (value === true) return t("stacks.voice.match")
  if (value === false) return t("stacks.voice.mismatch")
  return t("stacks.voice.notCompared")
}

function diagnosticStatusLabel(status: string, t: ReturnType<typeof useI18n>["t"]) {
  if (status === "passed") return t("stacks.voice.diagnosticPassed")
  if (status === "failed") return t("stacks.voice.diagnosticFailed")
  if (status === "warning") return t("stacks.voice.diagnosticWarning")
  return t("stacks.voice.diagnosticUnavailable")
}
