import { isMigrationProductionAdoptionStepUpRequired, makeMigrationServerLive, prepareMigrationProductionCutoverPreview, getMigrationProductionAdoptionState } from "../api/migration-production-adoption"
import { useMigrationGuidedState, useGuidedMigrationCommand, useGuidedMigrationEvidence } from "./migration-guided-state-context"
import { isMigrationOutcomeUncertain } from "../api/migration-guided-state"
import { useEffect, useRef, useState } from "react"
import {
  AlertTriangle,
  CheckCircle2,
  Globe2,
  Loader2,
  RefreshCw,
  Route,
  ShieldCheck,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import type {
  MakeMigrationServerLiveRequest,
  MigrationProductionAdoptionPlan,
} from "@/features/operator/migrations/api/migration-production-adoption"

type Props = {
  migrationId: string
  assuranceMode: "simplified" | "final-frozen"
  onChanged: () => Promise<unknown>
}

export function MigrationGoLiveWorkspace({
  migrationId,
  assuranceMode,
  onChanged,
}: Props) {
  const { t } = useI18n()
  const guide = useMigrationGuidedState()
  const guided = guide.workspace.guided
  const stage = guide.workspace.stages.find((item) => item.code === "make-new-server-live")!
  const stateQuery = useGuidedMigrationEvidence("make-server-live", `production-adoption:${guided.operationRevisions["review-go-live"]}`, () => getMigrationProductionAdoptionState(migrationId))
  const previewMutation = useGuidedMigrationCommand("review-go-live", (input: Parameters<typeof prepareMigrationProductionCutoverPreview>[1]) => prepareMigrationProductionCutoverPreview(migrationId, input))
  const makeLiveMutation = useGuidedMigrationCommand("make-server-live", (request: MakeMigrationServerLiveRequest) => makeMigrationServerLive(migrationId, request))
  const previewRequested = useRef(false)
  const submitting = useRef(false)
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [pendingGoLiveRequest, setPendingGoLiveRequest] = useState<MakeMigrationServerLiveRequest | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  // Optional route/check evidence may lag or be unavailable. Only the guided
  // snapshot decides which branch and action the operator sees.
  const evidence = stateQuery.data?.plan
  const plan = evidence?.adoptionPlanId === guided.adoptionPlanId ? evidence : null
  const verification = plan?.productionVerification
  const execution = plan?.cutover.execution
  const preview = plan?.cutover.preview.previewId === guided.cutoverPreviewId ? plan?.cutover.preview : null
  const verifiedLive = stage.state === "completed"
  const verificationFailed = stage.failureOutcome?.failedComponent === "production-verification"
  const running = stage.state === "running" || makeLiveMutation.isPending
  const compensatedFailure = stage.failureOutcome?.failedComponent === "public-route-cutover" && stage.failureOutcome.safeToRetry
  const unresolvedFailure = stage.failureOutcome !== null && !verificationFailed && !compensatedFailure
  const publicAwaitingVerification = stage.primaryAction?.code === "run-live-checks"
  const privateRuntimeReady = guided.privateRuntimeReady && !guided.publicRoutesCreated
  const previewReady = guided.cutoverPreviewStatus === "ready" && Boolean(guided.cutoverPreviewId)
  const previewBlocked = guided.cutoverPreviewStatus === "blocked" || guided.cutoverPreviewStatus === "expired"
  const previewSettled = previewReady || previewBlocked
  const canMakeLive = guide.allows("make-server-live", "review-go-live-again", "run-live-checks", "rerun-live-checks")

  useEffect(() => {
    if (!guide.allows("review-go-live") ||
        !privateRuntimeReady ||
        verifiedLive ||
        verificationFailed ||
        publicAwaitingVerification ||
        running ||
        actionError !== null ||
        compensatedFailure ||
        unresolvedFailure ||
        previewSettled ||
        previewRequested.current) {
      return
    }

    previewRequested.current = true
    void previewMutation.mutateAsync({})
      .then(async () => {
        setActionError(null)
        await onChanged().catch(() => undefined)
      })
      .catch((caught) => {
        if (isMigrationOutcomeUncertain(caught)) return
        previewRequested.current = false
        setActionError(caught instanceof Error ? caught.message : String(caught))
      })
  }, [
    actionError,
    compensatedFailure,
    onChanged,
    guide,
    previewMutation,
    previewSettled,
    privateRuntimeReady,
    publicAwaitingVerification,
    running,
    unresolvedFailure,
    verificationFailed,
    verifiedLive,
  ])

  function confirmedRequest(): MakeMigrationServerLiveRequest {
    return {
      confirmMovePublicTraffic: true,
      confirmStopUsingOldServer: true,
      confirmRunLiveVerification: true,
    }
  }

  async function retryPreview() {
    if (!guide.allows("review-go-live")) return
    try {
      setActionError(null)
      previewRequested.current = true
      await previewMutation.mutateAsync({})
      await onChanged().catch(() => undefined)
    } catch (caught) {
      if (isMigrationOutcomeUncertain(caught)) return
      previewRequested.current = false
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  async function makeLive(request: MakeMigrationServerLiveRequest) {
    if (submitting.current || !canMakeLive) return
    submitting.current = true
    try {
      setActionError(null)
      await makeLiveMutation.mutateAsync(request)
      setConfirmOpen(false)
    } catch (caught) {
      if (isMigrationOutcomeUncertain(caught)) return
      setConfirmOpen(false)
      if (isMigrationProductionAdoptionStepUpRequired(caught)) {
        // Preserve the exact public-cutover decision that the operator already
        // confirmed. Fresh identity verification authorizes this request only;
        // it must not substitute a newly edited or different action.
        setPendingGoLiveRequest(request)
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    } finally {
      submitting.current = false
    }
    await onChanged().catch(() => undefined)
  }

  useEffect(() => {
    if (stage.state === "running" || verifiedLive || verificationFailed || stage.state === "failed") setConfirmOpen(false)
  }, [stage.state, verifiedLive, verificationFailed])

  return (
    <>
      <Card className={verifiedLive ? "border-emerald-500/35 bg-emerald-500/[0.025]" : undefined}>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Globe2 className="h-5 w-5" aria-hidden="true" />
            {verifiedLive
              ? t("migrationWorkspace.goLive.verifiedTitle")
              : t("migrationWorkspace.goLive.title")}
          </CardTitle>
          <p className="text-sm text-muted-foreground">
            {verifiedLive
              ? t("migrationWorkspace.goLive.verifiedDescription")
              : t("migrationWorkspace.goLive.description")}
          </p>
        </CardHeader>
        <CardContent className="space-y-5">
          {stateQuery.isError ? (
            <Alert>
              <AlertTriangle className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.guided.evidenceTitle")}</AlertTitle>
              <AlertDescription>
                {t("migrationWorkspace.guided.evidenceUnavailable")}
              </AlertDescription>
            </Alert>
          ) : null}

          {actionError ? (
            <Alert variant="destructive">
              <AlertTriangle className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.goLive.actionErrorTitle")}</AlertTitle>
              <AlertDescription>{actionError}</AlertDescription>
            </Alert>
          ) : null}

          {verifiedLive ? (
            <>
              <Alert>
                <CheckCircle2 className="h-4 w-4" />
                <AlertTitle>{t("migrationWorkspace.goLive.verifiedAlertTitle")}</AlertTitle>
                <AlertDescription>
                  {t("migrationWorkspace.goLive.verifiedAlertDescription", {
                    checks: guide.workspace.verification.checkCount,
                  })}
                </AlertDescription>
              </Alert>
              {plan ? <LiveSummary plan={plan} /> : <PublicAddressSummary /> }
              {plan ? <TechnicalEvidence plan={plan} /> : null}
            </>
          ) : unresolvedFailure ? (
            <>
              <Alert variant="destructive">
                <AlertTriangle className="h-4 w-4" />
                <AlertTitle>{t("migrationWorkspace.goLive.recoveryTitle")}</AlertTitle>
                <AlertDescription>
                  {execution?.failureSummary ??
                    t("migrationWorkspace.goLive.recoveryDescription")}
                </AlertDescription>
              </Alert>
              <p className="text-sm text-muted-foreground">
                {t("migrationWorkspace.goLive.recoveryNext")}
              </p>
              {plan ? <TechnicalEvidence plan={plan} /> : null}
            </>
          ) : compensatedFailure ? (
            <>
              <Alert>
                <ShieldCheck className="h-4 w-4" />
                <AlertTitle>{t("migrationWorkspace.goLive.compensatedTitle")}</AlertTitle>
                <AlertDescription>
                  {execution?.failureSummary ??
                    t("migrationWorkspace.goLive.compensatedDescription")}
                </AlertDescription>
              </Alert>
              {plan ? <RouteReview plan={plan} /> : <PublicAddressSummary /> }
              <Button className="w-full" disabled={!canMakeLive || stepUpOpen} onClick={() => setConfirmOpen(true)}>
                <RefreshCw className="mr-2 h-4 w-4" aria-hidden="true" />
                {t("migrationWorkspace.goLive.retry")}
              </Button>
              {plan ? <TechnicalEvidence plan={plan} /> : null}
            </>
          ) : verificationFailed ? (
            <>
              <Alert variant="destructive">
                <AlertTriangle className="h-4 w-4" />
                <AlertTitle>{t("migrationWorkspace.goLive.verificationFailedTitle")}</AlertTitle>
                <AlertDescription>
                  {verification?.failureSummary ??
                    t("migrationWorkspace.goLive.verificationFailedDescription")}
                </AlertDescription>
              </Alert>
              <p className="text-sm text-muted-foreground">
                {t("migrationWorkspace.goLive.verificationFailedBoundary")}
              </p>
              <Button
                className="w-full"
                onClick={() => void makeLive(confirmedRequest())}
                disabled={!canMakeLive || makeLiveMutation.isPending || stepUpOpen}
              >
                {makeLiveMutation.isPending
                  ? <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  : <ShieldCheck className="mr-2 h-4 w-4" />}
                {t("migrationWorkspace.goLive.runChecksAgain")}
              </Button>
              {plan ? <TechnicalEvidence plan={plan} /> : null}
            </>
          ) : running || publicAwaitingVerification ? (
            <>
              <Alert role="status" aria-live="polite" aria-busy="true">
                <Loader2 className="h-4 w-4 animate-spin" />
                <AlertTitle>
                  {publicAwaitingVerification
                    ? t("migrationWorkspace.goLive.verifyingTitle")
                    : t("migrationWorkspace.goLive.runningTitle")}
                </AlertTitle>
                <AlertDescription>
                  {publicAwaitingVerification
                    ? t("migrationWorkspace.goLive.verifyingDescription")
                    : t("migrationWorkspace.goLive.runningDescription")}
                </AlertDescription>
              </Alert>
              {plan ? <RouteReview plan={plan} /> : <PublicAddressSummary />}
              {publicAwaitingVerification ? <Button disabled={!canMakeLive || stepUpOpen} onClick={() => void makeLive(confirmedRequest())}>
                {t("migrationWorkspace.goLive.runChecksAgain")}
              </Button> : null}
            </>
          ) : privateRuntimeReady ? (
            <>
              <Alert>
                <ShieldCheck className="h-4 w-4" />
                <AlertTitle>{t("migrationWorkspace.goLive.boundaryTitle")}</AlertTitle>
                <AlertDescription>{t("migrationWorkspace.goLive.boundaryDescription")}</AlertDescription>
              </Alert>

              {previewMutation.isPending || (!previewSettled && actionError === null) ? (
                <div className="flex items-center gap-2 rounded-xl border p-4 text-sm text-muted-foreground">
                  <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
                  {t("migrationWorkspace.goLive.preparingReview")}
                </div>
              ) : previewReady || previewBlocked ? (
                plan ? <RouteReview plan={plan} /> : <PublicAddressSummary />
              ) : null}

              {previewBlocked ? (
                <Alert variant="destructive">
                  <AlertTriangle className="h-4 w-4" />
                  <AlertTitle>{t("migrationWorkspace.goLive.blockedTitle")}</AlertTitle>
                  <AlertDescription className="space-y-2">
                    <p>{t("migrationWorkspace.goLive.blockedDescription")}</p>
                    {preview?.blockers.map((blocker) => (
                      <p key={blocker}>{blocker}</p>
                    ))}
                  </AlertDescription>
                </Alert>
              ) : null}

              {previewBlocked || (actionError !== null && !previewReady) ? (
                <Button
                  variant="outline"
                  className="w-full"
                  onClick={() => void retryPreview()}
                  disabled={previewMutation.isPending || !guide.allows("review-go-live")}
                >
                  {previewMutation.isPending ? (
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" />
                  ) : (
                    <RefreshCw className="mr-2 h-4 w-4" aria-hidden="true" />
                  )}
                  {previewMutation.isPending
                    ? t("migrationWorkspace.goLive.retryingReview")
                    : t("migrationWorkspace.goLive.retryReview")}
                </Button>
              ) : null}

              <Button
                className="w-full"
                onClick={() => setConfirmOpen(true)}
                disabled={!canMakeLive || !previewReady || previewMutation.isPending || stepUpOpen}
              >
                <Globe2 className="mr-2 h-4 w-4" aria-hidden="true" />
                {t("migrationWorkspace.goLive.makeLive")}
              </Button>
            </>
          ) : (
            <Alert variant="destructive">
              <AlertTriangle className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.goLive.notReadyTitle")}</AlertTitle>
              <AlertDescription>{t("migrationWorkspace.goLive.notReadyDescription")}</AlertDescription>
            </Alert>
          )}
        </CardContent>
      </Card>

      <ConfirmationDialog
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title={t("migrationWorkspace.goLive.confirmTitle")}
        description={t("migrationWorkspace.goLive.confirmDescription")}
        confirmLabel={t("migrationWorkspace.goLive.makeLive")}
        confirmingLabel={t("migrationWorkspace.goLive.runningAction")}
        cancelLabel={t("migrationWorkspace.goLive.cancel")}
        onConfirm={() => void makeLive(confirmedRequest())}
        isConfirming={makeLiveMutation.isPending}
        showProgress={makeLiveMutation.isPending}
        confirmDisabled={!canMakeLive || running || stepUpOpen || verifiedLive || unresolvedFailure}
      >
        {makeLiveMutation.isPending ? (
          <p role="status" aria-live="polite">{t("migrationWorkspace.handoff.goLiveProgress")}</p>
        ) : null}
        <div className="space-y-2 rounded-lg border p-3 text-sm">
          <p>{t(assuranceMode === "final-frozen"
            ? "migrationWorkspace.goLive.confirmFinalSource"
            : "migrationWorkspace.goLive.confirmSimplifiedSource")}</p>
          <p>{t("migrationWorkspace.goLive.confirmTraffic")}</p>
          <p>{t("migrationWorkspace.goLive.confirmVerification")}</p>
        </div>
      </ConfirmationDialog>

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={(open) => {
          setStepUpOpen(open)
          if (!open) {
            setPendingGoLiveRequest(null)
          }
        }}
        onVerified={() => {
          setStepUpOpen(false)
          const request = pendingGoLiveRequest
          setPendingGoLiveRequest(null)
          if (request) {
            void makeLive(request)
          }
        }}
      />
    </>
  )
}

function RouteReview({ plan }: { plan: MigrationProductionAdoptionPlan }) {
  const { t } = useI18n()
  const routes = plan.cutover.preview.routes

  return (
    <div className="space-y-3 rounded-xl border p-4 sm:p-5">
      <div className="flex items-center gap-2 font-semibold">
        <Route className="h-4 w-4" aria-hidden="true" />
        {t("migrationWorkspace.goLive.routeReviewTitle")}
      </div>
      <div className="grid gap-3 lg:grid-cols-2">
        {(routes.length > 0 ? routes : plan.routes).map((route) => {
          const selectedCertificate =
            "selectedCertificateId" in route && route.selectedCertificateId
              ? route.selectedCertificateName ??
                route.selectedCertificateRecordId ??
                `#${route.selectedCertificateId}`
              : null
          return (
            <div key={route.serviceKey} className="rounded-lg border p-3">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div>
                  <div className="font-medium">{route.publicHost}</div>
                  <div className="mt-1 text-xs text-muted-foreground">
                    {"desiredForwardHost" in route
                      ? `${route.desiredForwardHost}:${route.desiredForwardPort}`
                      : `${route.forwardHost}:${route.forwardPort}`}
                  </div>
                </div>
                <Badge variant="outline">
                  {"action" in route ? route.action : t("migrationWorkspace.goLive.routeCreateOrUpdate")}
                </Badge>
              </div>
              <div className="mt-3 text-xs text-muted-foreground">
                {selectedCertificate
                  ? t("migrationWorkspace.goLive.certificate", { certificate: selectedCertificate })
                  : t("migrationWorkspace.goLive.certificatePending")}
              </div>
            </div>
          )
        })}
      </div>
      <p className="text-xs text-muted-foreground">
        {t("migrationWorkspace.goLive.noDnsOrCertificateMutation")}
      </p>
    </div>
  )
}

function LiveSummary({ plan }: { plan: MigrationProductionAdoptionPlan }) {
  const { t } = useI18n()
  return (
    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
      <Fact label={t("migrationWorkspace.goLive.matrixAddress")} value={plan.matrixPublicHost} />
      <Fact label={t("migrationWorkspace.goLive.elementAddress")} value={plan.elementPublicHost} />
      <Fact
        label={t("migrationWorkspace.goLive.matrixRoute")}
        value={plan.cutover.execution.matrixRouteId ?? "—"}
        mono
      />
      <Fact
        label={t("migrationWorkspace.goLive.elementRoute")}
        value={plan.cutover.execution.elementRouteId ?? "—"}
        mono
      />
    </div>
  )
}

function TechnicalEvidence({ plan }: { plan: MigrationProductionAdoptionPlan }) {
  const { t } = useI18n()
  const verification = plan.productionVerification
  return (
    <details className="rounded-lg border bg-muted/10">
      <summary className="cursor-pointer px-4 py-3 text-sm font-medium">
        {t("migrationWorkspace.goLive.technicalDetails")}
      </summary>
      <div className="space-y-4 border-t px-4 py-4">
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <Fact
            label={t("migrationWorkspace.goLive.previewId")}
            value={plan.cutover.preview.previewId ?? "—"}
            mono
          />
          <Fact
            label={t("migrationWorkspace.goLive.executionId")}
            value={plan.cutover.execution.executionId ?? "—"}
            mono
          />
          <Fact
            label={t("migrationWorkspace.goLive.verificationId")}
            value={verification.verificationId ?? "—"}
            mono
          />
          <Fact
            label={t("migrationWorkspace.goLive.verificationCounts")}
            value={`${verification.checkCount - verification.failedCheckCount}/${verification.checkCount}`}
          />
        </div>
        {verification.checks.length > 0 ? (
          <div className="space-y-2">
            {verification.checks.map((check) => (
              <div key={check.code} className="rounded-lg border p-3 text-sm">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="font-medium">{check.name}</div>
                  <Badge variant={check.success ? "secondary" : "destructive"}>
                    {check.success
                      ? t("migrationWorkspace.goLive.checkPassed")
                      : t("migrationWorkspace.goLive.checkFailed")}
                  </Badge>
                </div>
                <p className="mt-2 text-muted-foreground">{check.detail}</p>
              </div>
            ))}
          </div>
        ) : null}
      </div>
    </details>
  )
}

function PublicAddressSummary() {
  const { workspace } = useMigrationGuidedState()
  const { t } = useI18n()
  return <div className="grid gap-3 sm:grid-cols-2">
    <Fact label={t("migrationWorkspace.goLive.matrixAddress")} value={workspace.target.matrixHost ?? "—"} />
    <Fact label={t("migrationWorkspace.goLive.elementAddress")} value={workspace.target.elementHost ?? "—"} />
  </div>
}

function Fact({
  label,
  value,
  mono = false,
}: {
  label: string
  value: string
  mono?: boolean
}) {
  return (
    <div className="rounded-lg border p-3">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className={mono
        ? "mt-1 break-all font-mono text-sm font-medium"
        : "mt-1 break-words text-sm font-medium"}>
        {value}
      </div>
    </div>
  )
}
