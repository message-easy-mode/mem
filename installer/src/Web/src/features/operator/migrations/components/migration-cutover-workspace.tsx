import {
  CheckCircle2,
  Loader2,
  Network,
  RefreshCw,
  ShieldCheck,
  TriangleAlert,
} from "lucide-react"
import { useMemo, useState, type ReactNode } from "react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import {
  isMigrationCutoverStepUpRequired,
  type ConfirmMigrationCutoverRequest,
  type MigrationCutoverState,
} from "@/features/operator/migrations/api/migration-cutover"
import type { MigrationSessionDetail } from "@/features/operator/migrations/api/migration-sessions"
import {
  useConfirmMigrationCutover,
  useCreateMigrationCutoverPreview,
  useExecuteMigrationCutover,
  useMigrationCutoverState,
  usePrepareMigrationCutoverCandidate,
  useRefreshMigrationCutoverReadiness,
  useRefreshMigrationPostCutover,
} from "@/features/operator/migrations/hooks/use-migration-cutover"

const confirmationAcknowledgements = [
  "acknowledgePreviewReviewed",
  "acknowledgeCandidateIsPrivateAndHealthy",
  "acknowledgePublicRouteExposureRisk",
  "acknowledgeNoAutomaticRollback",
  "acknowledgeFinalBackupRequired",
  "acknowledgeExecutionStillLocked",
] as const

type ConfirmationAcknowledgement = (typeof confirmationAcknowledgements)[number]

const confirmationLabels: Record<ConfirmationAcknowledgement, TranslationKey> = {
  acknowledgePreviewReviewed: "backupCatalog.cutover.ack.acknowledgePreviewReviewed",
  acknowledgeCandidateIsPrivateAndHealthy: "backupCatalog.cutover.ack.acknowledgeCandidateIsPrivateAndHealthy",
  acknowledgePublicRouteExposureRisk: "backupCatalog.cutover.ack.acknowledgePublicRouteExposureRisk",
  acknowledgeNoAutomaticRollback: "backupCatalog.cutover.ack.acknowledgeNoAutomaticRollback",
  acknowledgeFinalBackupRequired: "backupCatalog.cutover.ack.acknowledgeFinalBackupRequired",
  acknowledgeExecutionStillLocked: "backupCatalog.cutover.ack.acknowledgeExecutionStillLocked",
}

const executionAcknowledgements = [
  "acknowledgeConfirmationReviewed",
  "acknowledgeDockerNetworkMutation",
  "acknowledgeNpmMustNotJoinPrivateRestoreNetwork",
  "acknowledgeCreatesPublicRoutes",
  "acknowledgeNpmRoutesWillChange",
  "acknowledgeMatrixFederationExposureMayChange",
  "acknowledgeNoDnsMutation",
  "acknowledgeNoCertificateMutation",
  "acknowledgeNoRuntimePromotion",
  "acknowledgeNoAutomaticRollback",
  "acknowledgePostCutoverVerificationRequired",
] as const

type ExecutionAcknowledgement = (typeof executionAcknowledgements)[number]

const executionLabels: Record<ExecutionAcknowledgement, TranslationKey> = {
  acknowledgeConfirmationReviewed: "migrationWorkspace.cutover.executeAck.confirmationReviewed",
  acknowledgeDockerNetworkMutation: "migrationWorkspace.cutover.executeAck.dockerNetworkMutation",
  acknowledgeNpmMustNotJoinPrivateRestoreNetwork: "migrationWorkspace.cutover.executeAck.npmPrivateNetwork",
  acknowledgeCreatesPublicRoutes: "migrationWorkspace.cutover.executeAck.createsPublicRoutes",
  acknowledgeNpmRoutesWillChange: "migrationWorkspace.cutover.executeAck.npmRoutesChange",
  acknowledgeMatrixFederationExposureMayChange: "migrationWorkspace.cutover.executeAck.federationExposure",
  acknowledgeNoDnsMutation: "migrationWorkspace.cutover.executeAck.noDnsMutation",
  acknowledgeNoCertificateMutation: "migrationWorkspace.cutover.executeAck.noCertificateMutation",
  acknowledgeNoRuntimePromotion: "migrationWorkspace.cutover.executeAck.noRuntimePromotion",
  acknowledgeNoAutomaticRollback: "migrationWorkspace.cutover.executeAck.noAutomaticRollback",
  acknowledgePostCutoverVerificationRequired: "migrationWorkspace.cutover.executeAck.postVerification",
}

type PendingStepUp = "candidate" | "execution" | null

export function MigrationCutoverWorkspace({ detail }: { detail: MigrationSessionDetail }) {
  const { t } = useI18n()
  const migrationId = detail.session.migrationId
  const stateQuery = useMigrationCutoverState(migrationId)
  const candidateMutation = usePrepareMigrationCutoverCandidate(migrationId)
  const previewMutation = useCreateMigrationCutoverPreview(migrationId)
  const confirmationMutation = useConfirmMigrationCutover(migrationId)
  const readinessMutation = useRefreshMigrationCutoverReadiness(migrationId)
  const executionMutation = useExecuteMigrationCutover(migrationId)
  const postCutoverMutation = useRefreshMigrationPostCutover(migrationId)
  const [confirmationAcks, setConfirmationAcks] = useState<Record<ConfirmationAcknowledgement, boolean>>(
    () => Object.fromEntries(confirmationAcknowledgements.map((key) => [key, false])) as Record<ConfirmationAcknowledgement, boolean>,
  )
  const [executionAcks, setExecutionAcks] = useState<Record<ExecutionAcknowledgement, boolean>>(
    () => Object.fromEntries(executionAcknowledgements.map((key) => [key, false])) as Record<ExecutionAcknowledgement, boolean>,
  )
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [pendingStepUp, setPendingStepUp] = useState<PendingStepUp>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  const state = stateQuery.data
  const allConfirmationAcknowledged = useMemo(
    () => confirmationAcknowledgements.every((key) => confirmationAcks[key]),
    [confirmationAcks],
  )
  const allExecutionAcknowledged = useMemo(
    () => executionAcknowledgements.every((key) => executionAcks[key]),
    [executionAcks],
  )
  const actionPending = candidateMutation.isPending || previewMutation.isPending ||
    confirmationMutation.isPending || readinessMutation.isPending ||
    executionMutation.isPending || postCutoverMutation.isPending

  async function prepareCandidate() {
    try {
      setActionError(null)
      await candidateMutation.mutateAsync()
    } catch (caught) {
      if (isMigrationCutoverStepUpRequired(caught)) {
        setPendingStepUp("candidate")
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  async function executeCutover() {
    try {
      setActionError(null)
      await executionMutation.mutateAsync({
        operator: null,
        note: null,
        executeNpmRouteMutation: true,
        executeCutoverIngressNetworkMutation: true,
        ...executionAcks,
      })
    } catch (caught) {
      if (isMigrationCutoverStepUpRequired(caught)) {
        setPendingStepUp("execution")
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  function resumeAfterStepUp() {
    const action = pendingStepUp
    setStepUpOpen(false)
    setPendingStepUp(null)
    if (action === "candidate") void prepareCandidate()
    if (action === "execution") void executeCutover()
  }

  return (
    <Card className="border-amber-500/35 bg-amber-500/[0.025]">
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2">
              <Network className="h-5 w-5 text-amber-400" aria-hidden="true" />
              {t("migrationWorkspace.cutover.title")}
            </CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("migrationWorkspace.cutover.description")}
            </p>
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={() => void stateQuery.refetch()}
            disabled={stateQuery.isFetching || actionPending}
          >
            <RefreshCw className={stateQuery.isFetching ? "mr-2 h-4 w-4 animate-spin" : "mr-2 h-4 w-4"} aria-hidden="true" />
            {t("migrationWorkspace.refresh")}
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-5">
        <Alert>
          <ShieldCheck className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.cutover.sessionOwnedTitle")}</AlertTitle>
          <AlertDescription>{t("migrationWorkspace.cutover.sessionOwnedDescription")}</AlertDescription>
        </Alert>

        {stateQuery.isLoading ? (
          <div className="flex items-center gap-2 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
            {t("migrationWorkspace.cutover.loading")}
          </div>
        ) : null}

        {stateQuery.isError ? (
          <Alert variant="destructive">
            <TriangleAlert className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.cutover.loadErrorTitle")}</AlertTitle>
            <AlertDescription>{stateQuery.error instanceof Error ? stateQuery.error.message : t("migrationWorkspace.cutover.errorGeneric")}</AlertDescription>
          </Alert>
        ) : null}

        {actionError ? (
          <Alert variant="destructive">
            <TriangleAlert className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.cutover.actionErrorTitle")}</AlertTitle>
            <AlertDescription>{actionError}</AlertDescription>
          </Alert>
        ) : null}

        {state ? <CutoverSummary state={state} /> : null}

        {state?.readiness.finalCaptureRequired ? (
          <Alert variant="destructive">
            <TriangleAlert className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.cutover.finalCaptureTitle")}</AlertTitle>
            <AlertDescription>{t("migrationWorkspace.cutover.finalCaptureDescription")}</AlertDescription>
          </Alert>
        ) : null}

        <Step number="1" title={t("backupCatalog.cutover.candidateTitle")} detail={t("migrationWorkspace.cutover.candidateDescription")}>
          <Button
            disabled={!state || actionPending || Boolean(state.latestExecution)}
            onClick={() => { setPendingStepUp("candidate"); setStepUpOpen(true) }}
          >
            {candidateMutation.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" /> : null}
            {state?.productionCandidate ? t("backupCatalog.cutover.resumeCandidate") : t("backupCatalog.cutover.createCandidate")}
          </Button>
          {state?.productionCandidate ? (
            <Evidence rows={[
              [t("backupCatalog.cutover.candidateId"), state.productionCandidate.candidateId],
              [t("backupCatalog.cutover.status"), state.productionCandidate.status],
              [t("backupCatalog.cutover.private"), state.productionCandidate.safety.privateOnly ? t("common.yes") : t("common.no")],
            ]} />
          ) : null}
        </Step>

        <Step number="2" title={t("backupCatalog.cutover.previewTitle")} detail={t("migrationWorkspace.cutover.previewDescription")}>
          <Button
            variant="outline"
            disabled={!state?.productionCandidate || actionPending || Boolean(state.latestExecution)}
            onClick={() => void previewMutation.mutateAsync().catch((caught) => setActionError(caught instanceof Error ? caught.message : String(caught)))}
          >
            {previewMutation.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" /> : null}
            {t("backupCatalog.cutover.createPreview")}
          </Button>
          {state?.preview ? (
            <Evidence rows={[
              [t("backupCatalog.cutover.previewId"), state.preview.previewId],
              [t("backupCatalog.cutover.status"), state.preview.status],
              [t("backupCatalog.cutover.blockers"), String(state.preview.blockers.length)],
            ]} />
          ) : null}
        </Step>

        <Step number="3" title={t("backupCatalog.cutover.confirmTitle")} detail={t("migrationWorkspace.cutover.confirmDescription")}>
          <Acknowledgements
            keys={confirmationAcknowledgements}
            values={confirmationAcks}
            labels={confirmationLabels}
            disabled={Boolean(state?.latestExecution)}
            onChange={(key, checked) => setConfirmationAcks((current) => ({ ...current, [key]: checked }))}
          />
          <Button
            variant="outline"
            disabled={!state?.preview || !allConfirmationAcknowledged || actionPending || Boolean(state.latestExecution)}
            onClick={() => {
              const request: ConfirmMigrationCutoverRequest = { operator: null, note: null, ...confirmationAcks }
              void confirmationMutation.mutateAsync(request).catch((caught) => setActionError(caught instanceof Error ? caught.message : String(caught)))
            }}
          >
            {confirmationMutation.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" /> : null}
            {t("backupCatalog.cutover.saveConfirmation")}
          </Button>
          {state?.confirmation ? (
            <Evidence rows={[
              [t("backupCatalog.cutover.confirmationId"), state.confirmation.confirmationId],
              [t("backupCatalog.cutover.status"), state.confirmation.status],
              [t("backupCatalog.cutover.executionAvailable"), state.confirmation.executionAvailable ? t("common.yes") : t("common.no")],
            ]} />
          ) : null}
        </Step>

        <Step number="4" title={t("backupCatalog.cutover.readinessTitle")} detail={t("migrationWorkspace.cutover.readinessDescription")}>
          <Button
            variant="outline"
            disabled={!state?.confirmation || actionPending}
            onClick={() => void readinessMutation.mutateAsync().catch((caught) => setActionError(caught instanceof Error ? caught.message : String(caught)))}
          >
            {readinessMutation.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" /> : null}
            {t("backupCatalog.cutover.checkReadiness")}
          </Button>
          {state ? <ReadinessEvidence state={state} /> : null}
        </Step>

        <Step number="5" title={t("backupCatalog.cutover.executionTitle")} detail={t("migrationWorkspace.cutover.executionDescription")}>
          <Alert variant="destructive">
            <TriangleAlert className="h-4 w-4" />
            <AlertTitle>{t("backupCatalog.cutover.executionDangerTitle")}</AlertTitle>
            <AlertDescription>{t("migrationWorkspace.cutover.executionDangerDescription")}</AlertDescription>
          </Alert>
          <Acknowledgements
            keys={executionAcknowledgements}
            values={executionAcks}
            labels={executionLabels}
            disabled={!state?.readiness.executionReady || Boolean(state.latestExecution)}
            onChange={(key, checked) => setExecutionAcks((current) => ({ ...current, [key]: checked }))}
          />
          <Button
            variant="destructive"
            disabled={!state?.readiness.executionReady || !allExecutionAcknowledged || actionPending || Boolean(state.latestExecution)}
            onClick={() => { setPendingStepUp("execution"); setStepUpOpen(true) }}
          >
            {executionMutation.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" /> : null}
            {t("backupCatalog.cutover.execute")}
          </Button>
          {state?.latestExecution ? <ExecutionEvidence state={state} /> : null}
        </Step>

        <Step number="6" title={t("backupCatalog.cutover.postCutoverTitle")} detail={t("migrationWorkspace.cutover.postCutoverDescription")}>
          <Button
            variant="outline"
            disabled={!state?.latestExecution || actionPending}
            onClick={() => void postCutoverMutation.mutateAsync().catch((caught) => setActionError(caught instanceof Error ? caught.message : String(caught)))}
          >
            {postCutoverMutation.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" /> : null}
            {t("backupCatalog.cutover.refreshPostCutover")}
          </Button>
          {state?.latestExecution ? <ExecutionEvidence state={state} /> : null}
        </Step>

        <OperatorStepUpDialog
          open={stepUpOpen}
          onOpenChange={setStepUpOpen}
          onVerified={resumeAfterStepUp}
        />
      </CardContent>
    </Card>
  )
}

function CutoverSummary({ state }: { state: MigrationCutoverState }) {
  const { t } = useI18n()
  return (
    <Evidence rows={[
      [t("migrationWorkspace.cutover.captureKind"), state.capture.kind],
      [t("migrationWorkspace.cutover.finalEligible"), state.capture.finalCutoverEligible ? t("common.yes") : t("common.no")],
      [t("migrationWorkspace.cutover.stagingRun"), state.staging.stagingRunId],
      [t("migrationWorkspace.cutover.candidateArtifact"), state.candidateArtifactId],
      [t("migrationWorkspace.cutover.catalogCreated"), state.backupCatalogItemCreated ? t("common.yes") : t("common.no")],
      [t("migrationWorkspace.cutover.restoreCreated"), state.restoreSessionCreated ? t("common.yes") : t("common.no")],
    ]} />
  )
}

function ReadinessEvidence({ state }: { state: MigrationCutoverState }) {
  const { t } = useI18n()
  const ready = state.readiness.executionReady
  return (
    <div className="space-y-3">
      <Alert variant={ready ? "default" : "destructive"}>
        {ready ? <CheckCircle2 className="h-4 w-4" /> : <TriangleAlert className="h-4 w-4" />}
        <AlertTitle>{ready ? t("backupCatalog.cutover.ready") : t("backupCatalog.cutover.blocked")}</AlertTitle>
        <AlertDescription>{state.readiness.detail}</AlertDescription>
      </Alert>
      <Evidence rows={[
        [t("migrationWorkspace.cutover.candidateReady"), state.readiness.candidateReady ? t("common.yes") : t("common.no")],
        [t("migrationWorkspace.cutover.previewReady"), state.readiness.previewReady ? t("common.yes") : t("common.no")],
        [t("migrationWorkspace.cutover.confirmationReady"), state.readiness.confirmationReady ? t("common.yes") : t("common.no")],
        [t("migrationWorkspace.cutover.routesReady"), state.readiness.routesReady ? t("common.yes") : t("common.no")],
      ]} />
      {state.readiness.blockers.length ? <MessageList title={t("backupCatalog.cutover.blockers")} values={state.readiness.blockers} /> : null}
      {state.readiness.warnings.length ? <MessageList title={t("backupCatalog.cutover.warnings")} values={state.readiness.warnings} /> : null}
    </div>
  )
}

function ExecutionEvidence({ state }: { state: MigrationCutoverState }) {
  const { t } = useI18n()
  const execution = state.latestExecution
  if (!execution) return null
  return (
    <div className="space-y-3">
      <Evidence rows={[
        [t("backupCatalog.cutover.executionId"), execution.executionId],
        [t("backupCatalog.cutover.status"), execution.status],
        [t("backupCatalog.cutover.routesSucceeded"), execution.routes.allRequestedRoutesSucceeded ? t("common.yes") : t("common.no")],
        [t("migrationWorkspace.cutover.rollbackStatus"), state.rollback.status],
      ]} />
      {execution.blockers.length ? <MessageList title={t("backupCatalog.cutover.blockers")} values={execution.blockers} /> : null}
      {execution.warnings.length ? <MessageList title={t("backupCatalog.cutover.warnings")} values={execution.warnings} /> : null}
      {execution.errors.length ? <MessageList title={t("backupCatalog.cutover.errors")} values={execution.errors} /> : null}
    </div>
  )
}

function Acknowledgements<TKey extends string>({
  keys,
  values,
  labels,
  disabled,
  onChange,
}: {
  keys: readonly TKey[]
  values: Record<TKey, boolean>
  labels: Record<TKey, TranslationKey>
  disabled: boolean
  onChange: (key: TKey, checked: boolean) => void
}) {
  const { t } = useI18n()
  return (
    <div className="space-y-2">
      {keys.map((key) => (
        <label key={key} className="flex items-start gap-2 text-sm">
          <input
            type="checkbox"
            className="mt-1"
            checked={values[key]}
            disabled={disabled}
            onChange={(event) => onChange(key, event.target.checked)}
          />
          <span>{t(labels[key])}</span>
        </label>
      ))}
    </div>
  )
}

function Step({ number, title, detail, children }: { number: string; title: string; detail: string; children: ReactNode }) {
  return (
    <section className="space-y-3 rounded-lg border border-amber-500/30 p-4">
      <div className="flex gap-3">
        <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-muted text-sm font-semibold">{number}</span>
        <div><h3 className="font-medium">{title}</h3><p className="text-sm text-muted-foreground">{detail}</p></div>
      </div>
      <div className="space-y-3 pl-10">{children}</div>
    </section>
  )
}

function Evidence({ rows }: { rows: [string, string][] }) {
  return (
    <dl className="grid gap-2 rounded-md border p-3 text-sm sm:grid-cols-2">
      {rows.map(([label, value]) => (
        <div key={label} className="min-w-0">
          <dt className="text-xs text-muted-foreground">{label}</dt>
          <dd className="break-all font-mono text-xs">{value}</dd>
        </div>
      ))}
    </dl>
  )
}

function MessageList({ title, values }: { title: string; values: string[] }) {
  return (
    <div>
      <p className="text-sm font-medium">{title}</p>
      <ul className="mt-1 list-disc space-y-1 pl-5 text-sm text-muted-foreground">
        {values.map((value) => <li key={value}>{value}</li>)}
      </ul>
    </div>
  )
}
