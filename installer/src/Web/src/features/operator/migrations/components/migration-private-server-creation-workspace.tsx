import { createMigrationPrivateServer, getMigrationProductionAdoptionState } from "../api/migration-production-adoption"
import { useMigrationGuidedState, useGuidedMigrationCommand, useGuidedMigrationEvidence } from "./migration-guided-state-context"
import { isMigrationOutcomeUncertain } from "../api/migration-guided-state"
import { useEffect, useRef, useState } from "react"
import {
  AlertTriangle,
  CheckCircle2,
  Loader2,
  RefreshCw,
  Server,
  ShieldCheck,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import {
  isMigrationProductionAdoptionStepUpRequired,
  type CreateMigrationPrivateServerRequest,
} from "@/features/operator/migrations/api/migration-production-adoption"
import {
  useMigrationPrivateServerTargetReview,
  useReviewMigrationPrivateServerTarget,
} from "@/features/operator/migrations/hooks/use-migration-production-adoption"

type Props = {
  migrationId: string
  matrixServerName: string | null
  assuranceMode: "simplified" | "final-frozen"
  onChanged: () => Promise<unknown>
}

type ReviewedValues = {
  targetStackSlug: string
  elementPublicHost: string
}

export function MigrationPrivateServerCreationWorkspace({
  migrationId,
  matrixServerName,
  assuranceMode,
  onChanged,
}: Props) {
  const { t } = useI18n()
  const guide = useMigrationGuidedState()
  const guided = guide.workspace.guided
  const stage = guide.workspace.stages.find((item) => item.code === "create-new-server")!
  const stateQuery = useGuidedMigrationEvidence("create-new-server", "production-adoption", () => getMigrationProductionAdoptionState(migrationId))
  const reviewMutation = useReviewMigrationPrivateServerTarget(migrationId)
  const createMutation = useGuidedMigrationCommand("create-new-server", (request: CreateMigrationPrivateServerRequest) => createMigrationPrivateServer(migrationId, request))
  const privateRuntimeReady = guided.privateRuntimeReady || stage.state === "completed"
  const materializationFailed = stage.state === "failed"
  const materializing = createMutation.isPending || stage.state === "running"
  const automaticReviewEnabled =
    guide.allows("confirm-tested-data", "prepare-new-server", "create-new-server", "edit-new-server-details") &&
    !privateRuntimeReady &&
    !materializationFailed &&
    !materializing
  const automaticReviewQuery = useMigrationPrivateServerTargetReview(
    migrationId,
    {
      targetStackSlug: guide.workspace.target.stackSlug,
      elementPublicHost: guide.workspace.target.elementHost,
    },
    automaticReviewEnabled,
  )
  const [targetStackSlug, setTargetStackSlug] = useState("")
  const [elementPublicHost, setElementPublicHost] = useState("")
  const [reviewedValues, setReviewedValues] = useState<ReviewedValues | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [confirmedRequest, setConfirmedRequest] = useState<CreateMigrationPrivateServerRequest | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const automaticReviewApplied = useRef(false)
  const submitting = useRef(false)

  const plan = stateQuery.data?.plan?.adoptionPlanId === guided.adoptionPlanId ? stateQuery.data.plan : null
  const review = reviewMutation.data ?? automaticReviewQuery.data ?? null
  const effectiveMatrixServerName = review?.matrixServerName ?? matrixServerName ?? ""

  useEffect(() => {
    const result = automaticReviewQuery.data
    if (!result || automaticReviewApplied.current) {
      return
    }

    automaticReviewApplied.current = true
    setActionError(null)
    setTargetStackSlug(result.targetStackSlug)
    setElementPublicHost(result.elementPublicHost)
    setReviewedValues({
      targetStackSlug: result.targetStackSlug,
      elementPublicHost: result.elementPublicHost,
    })
  }, [automaticReviewQuery.data])

  useEffect(() => {
    if (!automaticReviewQuery.isError) {
      return
    }

    const caught: unknown = automaticReviewQuery.error
    if (isMigrationOutcomeUncertain(caught)) {
      return
    }

    setReviewedValues(null)
    setActionError(caught instanceof Error ? caught.message : String(caught))
  }, [automaticReviewQuery.error, automaticReviewQuery.isError])

  async function reviewTarget(
    requestedStackSlug: string | null = targetStackSlug,
    requestedElementHost: string | null = elementPublicHost,
  ) {
    try {
      setActionError(null)
      const result = await reviewMutation.mutateAsync({
        targetStackSlug: requestedStackSlug?.trim() || null,
        elementPublicHost: requestedElementHost?.trim() || null,
      })
      setTargetStackSlug(result.targetStackSlug)
      setElementPublicHost(result.elementPublicHost)
      setReviewedValues({
        targetStackSlug: result.targetStackSlug,
        elementPublicHost: result.elementPublicHost,
      })
    } catch (caught) {
      if (isMigrationOutcomeUncertain(caught)) return
      setReviewedValues(null)
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  function updateTargetStackSlug(value: string) {
    setTargetStackSlug(value)
    setReviewedValues(null)
    setActionError(null)
  }

  function updateElementPublicHost(value: string) {
    setElementPublicHost(value)
    setReviewedValues(null)
    setActionError(null)
  }

  function buildConfirmedRequest(): CreateMigrationPrivateServerRequest {
    return {
      targetStackSlug: targetStackSlug.trim() || null,
      elementPublicHost: elementPublicHost.trim() || null,
      confirmVerifiedSnapshotIsAuthoritative: true,
      confirmLaterSourceWritesAreNotIncluded: true,
      confirmCreatePrivateServer: true,
    }
  }

  async function createPrivateServer(request: CreateMigrationPrivateServerRequest) {
    if (submitting.current || !guide.allows("confirm-tested-data", "prepare-new-server", "create-new-server", "edit-new-server-details")) return
    submitting.current = true
    try {
      setActionError(null)
      await createMutation.mutateAsync(request)
      setConfirmOpen(false)
      setConfirmedRequest(null)
    } catch (caught) {
      if (isMigrationOutcomeUncertain(caught)) return
      setConfirmOpen(false)
      if (isMigrationProductionAdoptionStepUpRequired(caught)) {
        setConfirmedRequest(request)
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    } finally {
      submitting.current = false
    }
    // Read failure is not failure of the accepted creation. Queries retain and
    // display their own error; the workspace observer also rediscovers progress.
    await onChanged().catch(() => undefined)
  }

  useEffect(() => {
    if (stage.state === "running" || privateRuntimeReady || materializationFailed) {
      setConfirmOpen(false)
    }
  }, [stage.state, privateRuntimeReady, materializationFailed])

  const reviewIsCurrent =
    reviewedValues !== null &&
    reviewedValues.targetStackSlug === targetStackSlug.trim() &&
    reviewedValues.elementPublicHost === elementPublicHost.trim()
  const canCreate =
    reviewIsCurrent &&
    review?.collisionFree === true &&
    guide.allows("confirm-tested-data", "prepare-new-server", "create-new-server", "edit-new-server-details") &&
    !createMutation.isPending &&
    !stepUpOpen &&
    !materializing


  return (
    <>
      <Card className={privateRuntimeReady ? "border-emerald-500/35 bg-emerald-500/[0.025]" : undefined}>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Server className="h-5 w-5 shrink-0" aria-hidden="true" />
            {privateRuntimeReady
              ? t("migrationWorkspace.privateServer.readyTitle")
              : t("migrationWorkspace.privateServer.title")}
          </CardTitle>
          <p className="text-sm text-muted-foreground">
            {privateRuntimeReady
              ? t("migrationWorkspace.privateServer.readyDescription")
              : t("migrationWorkspace.privateServer.description")}
          </p>
        </CardHeader>
        <CardContent className="space-y-5">
          {stateQuery.isError ? (
            <Alert>
              <AlertTriangle className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.guided.evidenceTitle")}</AlertTitle>
              <AlertDescription>{t("migrationWorkspace.guided.evidenceUnavailable")}</AlertDescription>
            </Alert>
          ) : null}

          {actionError ? (
            <Alert variant="destructive">
              <AlertTriangle className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.privateServer.actionErrorTitle")}</AlertTitle>
              <AlertDescription>{actionError}</AlertDescription>
            </Alert>
          ) : null}

          {privateRuntimeReady ? (
            <>
              <Alert>
                <CheckCircle2 className="h-4 w-4" />
                <AlertTitle>{t("migrationWorkspace.privateServer.createdTitle")}</AlertTitle>
                <AlertDescription>{t("migrationWorkspace.privateServer.createdDescription")}</AlertDescription>
              </Alert>
              <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                <Fact label={t("migrationWorkspace.privateServer.matrixAddress")} value={guide.workspace.source.matrixServerName ?? "—"} />
                <Fact label={t("migrationWorkspace.privateServer.stackName")} value={guide.workspace.target.stackSlug ?? "—"} />
                <Fact label={t("migrationWorkspace.privateServer.elementAddress")} value={guide.workspace.target.elementHost ?? "—"} />
                <Fact label={t("migrationWorkspace.privateServer.publicRoutes")} value={t("migrationWorkspace.privateServer.none")} />
              </div>
              {plan ? <details className="rounded-lg border bg-muted/10">
                <summary className="cursor-pointer px-4 py-3 text-sm font-medium">
                  {t("migrationWorkspace.privateServer.technicalDetails")}
                </summary>
                <div className="grid gap-3 border-t px-4 py-4 sm:grid-cols-2 xl:grid-cols-4">
                  <Fact label={t("migrationWorkspace.privateServer.runtimeStackId")} value={plan.runtimeStackId} mono />
                  <Fact label={t("migrationWorkspace.privateServer.database")} value={plan.databaseName} mono />
                  <Fact label={t("migrationWorkspace.privateServer.matrixContainer")} value={plan.matrixContainerName} mono />
                  <Fact label={t("migrationWorkspace.privateServer.elementContainer")} value={plan.elementContainerName} mono />
                </div>
              </details> : null}
            </>
          ) : materializing ? (
            <div role="status" aria-live="polite" aria-busy="true" className="flex items-start gap-3 rounded-xl border bg-muted/15 p-4 text-sm">
              <Loader2 className="mt-0.5 h-4 w-4 shrink-0 animate-spin text-primary" aria-hidden="true" />
              <div className="min-w-0">
                <div className="font-medium">{t("migrationWorkspace.privateServer.creating")}</div>
                <p className="mt-1 leading-6 text-muted-foreground">
                  {t("migrationWorkspace.privateServer.confirmPrivate")}
                </p>
              </div>
            </div>
          ) : materializationFailed ? (
            <Alert variant="destructive">
              <AlertTriangle className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.privateServer.failedTitle")}</AlertTitle>
              <AlertDescription>{plan?.materialization.failureSummary ?? stateQuery.data?.detail}</AlertDescription>
            </Alert>
          ) : (
            <>
              <Alert>
                <ShieldCheck className="h-4 w-4" />
                <AlertTitle>{t("migrationWorkspace.privateServer.boundaryTitle")}</AlertTitle>
                <AlertDescription>{t("migrationWorkspace.privateServer.boundaryDescription")}</AlertDescription>
              </Alert>

              {automaticReviewEnabled &&
              !automaticReviewApplied.current &&
              !automaticReviewQuery.isError &&
              reviewMutation.data === undefined ? (
                <div className="flex items-center gap-2 rounded-lg border p-4 text-sm text-muted-foreground">
                  <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
                  {t("migrationWorkspace.privateServer.reviewing")}
                </div>
              ) : (
                <>
                  <div className="grid gap-4 xl:grid-cols-3">
                    <div className="min-w-0 space-y-2">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <Label htmlFor="migration-matrix-address">{t("migrationWorkspace.privateServer.matrixAddress")}</Label>
                        <FieldStatus status={review?.matrixAddressStatus ?? "pending"} />
                      </div>
                      <Input id="migration-matrix-address" value={effectiveMatrixServerName} disabled />
                      <p className="text-xs text-muted-foreground">{t("migrationWorkspace.privateServer.matrixLocked")}</p>
                    </div>
                    <div className="min-w-0 space-y-2">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <Label htmlFor="migration-stack-name">{t("migrationWorkspace.privateServer.stackName")}</Label>
                        <FieldStatus status={reviewIsCurrent ? review?.stackNameStatus ?? "pending" : "changed"} />
                      </div>
                      <Input
                        id="migration-stack-name"
                        value={targetStackSlug}
                        onChange={(event) => updateTargetStackSlug(event.target.value)}
                        placeholder={t("migrationWorkspace.privateServer.stackPlaceholder")}
                      />
                    </div>
                    <div className="min-w-0 space-y-2">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <Label htmlFor="migration-element-address">{t("migrationWorkspace.privateServer.elementAddress")}</Label>
                        <FieldStatus status={reviewIsCurrent ? review?.elementAddressStatus ?? "pending" : "changed"} />
                      </div>
                      <Input
                        id="migration-element-address"
                        value={elementPublicHost}
                        onChange={(event) => updateElementPublicHost(event.target.value)}
                        placeholder={t("migrationWorkspace.privateServer.elementPlaceholder")}
                      />
                    </div>
                  </div>

                  {review?.collisions.length && reviewIsCurrent ? (
                    <Alert variant="destructive">
                      <AlertTriangle className="h-4 w-4" />
                      <AlertTitle>{t("migrationWorkspace.privateServer.collisionTitle")}</AlertTitle>
                      <AlertDescription>
                        <ul className="mt-2 space-y-2">
                          {review.collisions.map((collision) => (
                            <li key={`${collision.field}:${collision.code}:${collision.resourceValue}`}>
                              {collision.detail}
                            </li>
                          ))}
                        </ul>
                      </AlertDescription>
                    </Alert>
                  ) : review?.collisionFree && reviewIsCurrent ? (
                    <Alert>
                      <CheckCircle2 className="h-4 w-4" />
                      <AlertTitle>{t("migrationWorkspace.privateServer.availableTitle")}</AlertTitle>
                      <AlertDescription>{review.detail}</AlertDescription>
                    </Alert>
                  ) : null}

                  {review?.suggestedTargetStackSlug && reviewIsCurrent ? (
                    <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border bg-muted/10 p-4">
                      <div>
                        <div className="text-sm font-medium">{t("migrationWorkspace.privateServer.suggestionTitle")}</div>
                        <div className="mt-1 font-mono text-sm">{review.suggestedTargetStackSlug}</div>
                      </div>
                      <Button
                        variant="outline"
                        onClick={() => {
                          updateTargetStackSlug(review.suggestedTargetStackSlug ?? "")
                          void reviewTarget(review.suggestedTargetStackSlug, elementPublicHost)
                        }}
                      >
                        {t("migrationWorkspace.privateServer.useSuggestion")}
                      </Button>
                    </div>
                  ) : null}

                  <div className="grid gap-3 sm:grid-cols-2">
                    <Button
                      variant="outline"
                      onClick={() => void reviewTarget()}
                      disabled={materializing || reviewMutation.isPending || !targetStackSlug.trim() || !elementPublicHost.trim()}
                    >
                      {reviewMutation.isPending ? (
                        <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" />
                      ) : (
                        <RefreshCw className="mr-2 h-4 w-4" aria-hidden="true" />
                      )}
                      {t("migrationWorkspace.privateServer.reviewAction")}
                    </Button>
                    <Button
                      onClick={() => setConfirmOpen(true)}
                      disabled={!canCreate}
                    >
                      {createMutation.isPending || stateQuery.data?.status === "materializing" ? (
                        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                      ) : (
                        <Server className="mr-2 h-4 w-4" />
                      )}
                      {stateQuery.data?.status === "materializing"
                        ? t("migrationWorkspace.privateServer.creating")
                        : t("migrationWorkspace.privateServer.create")}
                    </Button>
                  </div>

                  {!reviewIsCurrent && targetStackSlug.trim() && elementPublicHost.trim() ? (
                    <p className="text-sm text-muted-foreground">
                      {t("migrationWorkspace.privateServer.reviewRequired")}
                    </p>
                  ) : null}
                </>
              )}
            </>
          )}
        </CardContent>
      </Card>

      <ConfirmationDialog
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title={t("migrationWorkspace.privateServer.confirmTitle")}
        description={t("migrationWorkspace.privateServer.confirmDescription")}
        confirmLabel={t("migrationWorkspace.privateServer.confirmAction")}
        confirmingLabel={t("migrationWorkspace.privateServer.creating")}
        cancelLabel={t("migrationWorkspace.privateServer.cancel")}
        onConfirm={() => void createPrivateServer(buildConfirmedRequest())}
        isConfirming={createMutation.isPending}
        showProgress={createMutation.isPending}
        confirmDisabled={materializing || privateRuntimeReady || materializationFailed || stepUpOpen}
      >
        {createMutation.isPending ? (
          <p role="status" aria-live="polite">{t("migrationWorkspace.handoff.privateProgress")}</p>
        ) : null}
        <div className="space-y-2 rounded-lg border p-3 text-sm">
          <p>{t(assuranceMode === "final-frozen"
            ? "migrationWorkspace.privateServer.confirmFinalSnapshot"
            : "migrationWorkspace.privateServer.confirmSnapshot")}</p>
          <p>{t(assuranceMode === "final-frozen"
            ? "migrationWorkspace.privateServer.confirmFinalWrites"
            : "migrationWorkspace.privateServer.confirmWrites")}</p>
          <p>{t("migrationWorkspace.privateServer.confirmPrivate")}</p>
        </div>
      </ConfirmationDialog>

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={(open) => {
          setStepUpOpen(open)
          if (!open) setConfirmedRequest(null)
        }}
        onVerified={() => {
          setStepUpOpen(false)
          if (confirmedRequest) void createPrivateServer(confirmedRequest)
        }}
      />
    </>
  )
}

function FieldStatus({ status }: { status: string }) {
  const { t } = useI18n()
  if (status === "available" || status === "locked-available") {
    return <Badge variant="outline" className="border-emerald-500/30 text-emerald-700 dark:text-emerald-300">{t("migrationWorkspace.privateServer.available")}</Badge>
  }
  if (status === "conflict" || status === "locked-conflict") {
    return <Badge variant="destructive">{t("migrationWorkspace.privateServer.conflict")}</Badge>
  }
  if (status === "changed") {
    return <Badge variant="outline">{t("migrationWorkspace.privateServer.notReviewed")}</Badge>
  }
  return <Badge variant="outline">{t("migrationWorkspace.privateServer.checking")}</Badge>
}

function Fact({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-lg border p-3">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className={mono ? "mt-1 break-all font-mono text-sm font-medium" : "mt-1 break-words text-sm font-medium"}>{value}</div>
    </div>
  )
}
