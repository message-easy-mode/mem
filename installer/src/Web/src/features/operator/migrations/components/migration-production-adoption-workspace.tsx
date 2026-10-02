import { formatMigrationDateTime } from "./migration-time"
import { useMemo, useState } from "react"
import {
  AlertTriangle,
  CheckCircle2,
  Database,
  Download,
  Loader2,
  Network,
  RefreshCw,
  RotateCcw,
  Server,
  ShieldCheck,
  UploadCloud,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import {
  downloadMigrationProductionSourceHandoff,
  isMigrationProductionAdoptionStepUpRequired,
  type ExecuteMigrationProductionCutoverRequest,
  type ExecuteMigrationProductionRollbackRequest,
  type MaterializeMigrationProductionRuntimeRequest,
  type MigrationProductionAdoptionPlan,
  type RunMigrationProductionVerificationRequest,
  type MigrationProductionSourceRestorationCompletionEnvelope,
} from "@/features/operator/migrations/api/migration-production-adoption"
import type { MigrationSessionDetail } from "@/features/operator/migrations/api/migration-sessions"
import {
  useExecuteMigrationProductionCutover,
  useExecuteMigrationProductionRollback,
  useImportMigrationProductionSourceCompletion,
  useMaterializeMigrationProductionRuntime,
  useMigrationProductionAdoptionState,
  usePrepareMigrationProductionAdoptionPlan,
  usePrepareMigrationProductionCutoverPreview,
  usePrepareMigrationProductionRollbackPreview,
  useRunMigrationProductionVerification,
} from "@/features/operator/migrations/hooks/use-migration-production-adoption"

const acknowledgementKeys = [
  "acknowledgeCreatesNormalRuntimeRecords",
  "acknowledgeMutatesProductionPostgres",
  "acknowledgeStartsProductionContainers",
  "acknowledgeNoPublicRoutes",
  "acknowledgeNoAutomaticRollback",
] as const

type AcknowledgementKey = typeof acknowledgementKeys[number]
type Acknowledgements = Record<AcknowledgementKey, boolean>

const initialAcknowledgements: Acknowledgements = {
  acknowledgeCreatesNormalRuntimeRecords: false,
  acknowledgeMutatesProductionPostgres: false,
  acknowledgeStartsProductionContainers: false,
  acknowledgeNoPublicRoutes: false,
  acknowledgeNoAutomaticRollback: false,
}

const commonCutoverAcknowledgementKeys = [
  "acknowledgePrivateRuntimeHealthy",
  "acknowledgeRouteSnapshotReviewed",
  "acknowledgeCreatesPublicRoutes",
  "acknowledgeNoDnsMutation",
  "acknowledgeNoCertificateMutation",
  "acknowledgeRollbackIsNextSlice",
  "acknowledgePostCutoverVerificationRequired",
] as const

type CutoverAcknowledgementKey =
  | "acknowledgeSourceFrozen"
  | "acknowledgeProductionAuthority"
  | typeof commonCutoverAcknowledgementKeys[number]
type CutoverAcknowledgements = Record<CutoverAcknowledgementKey, boolean>

const initialCutoverAcknowledgements: CutoverAcknowledgements = {
  acknowledgeSourceFrozen: false,
  acknowledgeProductionAuthority: false,
  acknowledgePrivateRuntimeHealthy: false,
  acknowledgeRouteSnapshotReviewed: false,
  acknowledgeCreatesPublicRoutes: false,
  acknowledgeNoDnsMutation: false,
  acknowledgeNoCertificateMutation: false,
  acknowledgeRollbackIsNextSlice: false,
  acknowledgePostCutoverVerificationRequired: false,
}

const rollbackAcknowledgementKeys = [
  "acknowledgeMigrationNotAccepted",
  "acknowledgeRestoresPreCutoverRoutes",
  "acknowledgeStopsTargetContainers",
  "acknowledgePreservesTargetData",
  "acknowledgeSourceRemainsFrozen",
  "acknowledgeSourceRestorationRequiresHandoff",
  "acknowledgeNoAutomaticSourceHostMutation",
] as const

type RollbackAcknowledgementKey = typeof rollbackAcknowledgementKeys[number]
type RollbackAcknowledgements = Record<RollbackAcknowledgementKey, boolean>

const initialRollbackAcknowledgements: RollbackAcknowledgements = {
  acknowledgeMigrationNotAccepted: false,
  acknowledgeRestoresPreCutoverRoutes: false,
  acknowledgeStopsTargetContainers: false,
  acknowledgePreservesTargetData: false,
  acknowledgeSourceRemainsFrozen: false,
  acknowledgeSourceRestorationRequiresHandoff: false,
  acknowledgeNoAutomaticSourceHostMutation: false,
}

export function MigrationProductionAdoptionWorkspace({
  detail,
  productionAuthorityType,
}: {
  detail: MigrationSessionDetail
  productionAuthorityType?: string | null
}) {
  const { t } = useI18n()
  const migrationId = detail.session.migrationId
  const adoptionAvailable = Boolean(productionAuthorityType) ||
    ["cutover", "acceptance", "completed"].includes(detail.session.phase)
  const cutoverAuthorityAcknowledgementKey: CutoverAcknowledgementKey =
    productionAuthorityType === "operator-attested-snapshot"
      ? "acknowledgeProductionAuthority"
      : "acknowledgeSourceFrozen"
  const activeCutoverAcknowledgementKeys = useMemo<readonly CutoverAcknowledgementKey[]>(
    () => [cutoverAuthorityAcknowledgementKey, ...commonCutoverAcknowledgementKeys],
    [cutoverAuthorityAcknowledgementKey],
  )
  const stateQuery = useMigrationProductionAdoptionState(migrationId, adoptionAvailable)
  const prepareMutation = usePrepareMigrationProductionAdoptionPlan(migrationId)
  const materializeMutation = useMaterializeMigrationProductionRuntime(migrationId)
  const cutoverPreviewMutation = usePrepareMigrationProductionCutoverPreview(migrationId)
  const cutoverExecutionMutation = useExecuteMigrationProductionCutover(migrationId)
  const productionVerificationMutation = useRunMigrationProductionVerification(migrationId)
  const rollbackPreviewMutation = usePrepareMigrationProductionRollbackPreview(migrationId)
  const rollbackExecutionMutation = useExecuteMigrationProductionRollback(migrationId)
  const sourceCompletionMutation = useImportMigrationProductionSourceCompletion(migrationId)
  const [targetStackSlug, setTargetStackSlug] = useState<string | null>(null)
  const [elementPublicHost, setElementPublicHost] = useState<string | null>(null)
  const [acknowledgements, setAcknowledgements] = useState<Acknowledgements>(initialAcknowledgements)
  const [cutoverAcknowledgements, setCutoverAcknowledgements] = useState<CutoverAcknowledgements>(initialCutoverAcknowledgements)
  const [rollbackAcknowledgements, setRollbackAcknowledgements] = useState<RollbackAcknowledgements>(initialRollbackAcknowledgements)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [pendingMaterialization, setPendingMaterialization] = useState(false)
  const [pendingCutoverRequest, setPendingCutoverRequest] = useState<ExecuteMigrationProductionCutoverRequest | null>(null)
  const [pendingRollback, setPendingRollback] = useState(false)
  const [pendingSourceHandoff, setPendingSourceHandoff] = useState(false)
  const [pendingSourceCompletion, setPendingSourceCompletion] = useState(false)
  const [sourceHandoffPending, setSourceHandoffPending] = useState(false)
  const [sourceCompletionFile, setSourceCompletionFile] = useState<File | null>(null)
  const [sourceCompletionEnvelope, setSourceCompletionEnvelope] = useState<MigrationProductionSourceRestorationCompletionEnvelope | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  const plan = stateQuery.data?.plan
  const allAcknowledged = useMemo(
    () => acknowledgementKeys.every((key) => acknowledgements[key]),
    [acknowledgements],
  )
  const allCutoverAcknowledged = useMemo(
    () => activeCutoverAcknowledgementKeys.every((key) => cutoverAcknowledgements[key]),
    [activeCutoverAcknowledgementKeys, cutoverAcknowledgements],
  )
  const allRollbackAcknowledged = useMemo(
    () => rollbackAcknowledgementKeys.every((key) => rollbackAcknowledgements[key]),
    [rollbackAcknowledgements],
  )
  const planEditable = !plan || ["prepared", "blocked"].includes(plan.status)

  async function preparePlan() {
    try {
      setActionError(null)
      const result = await prepareMutation.mutateAsync({
        targetStackSlug: targetStackSlug?.trim() || plan?.targetStackSlug || null,
        elementPublicHost: elementPublicHost?.trim() || plan?.elementPublicHost || null,
      })
      setTargetStackSlug(result.plan?.targetStackSlug ?? null)
      setElementPublicHost(result.plan?.elementPublicHost ?? null)
    } catch (caught) {
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  function createMaterializationRequest(): MaterializeMigrationProductionRuntimeRequest {
    return {
      operator: null,
      note: null,
      executePrivateProductionMaterialization: true,
      ...acknowledgements,
    }
  }

  async function materializeRuntime() {
    try {
      setActionError(null)
      await materializeMutation.mutateAsync(createMaterializationRequest())
      setPendingMaterialization(false)
    } catch (caught) {
      if (isMigrationProductionAdoptionStepUpRequired(caught)) {
        setPendingMaterialization(true)
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  async function prepareCutoverPreview() {
    try {
      setActionError(null)
      await cutoverPreviewMutation.mutateAsync({ previewLifetimeMinutes: 15 })
      setCutoverAcknowledgements(initialCutoverAcknowledgements)
    } catch (caught) {
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  function createCutoverRequest(): ExecuteMigrationProductionCutoverRequest {
    const previewId = plan?.cutover.preview.previewId
    if (!previewId) {
      throw new Error(t("migrationWorkspace.adoption.cutoverPreviewRequired"))
    }
    return {
      operator: null,
      note: null,
      previewId,
      executeNpmRouteMutation: true,
      ...cutoverAcknowledgements,
    }
  }

  async function executeCutover(request: ExecuteMigrationProductionCutoverRequest = createCutoverRequest()) {
    try {
      setActionError(null)
      await cutoverExecutionMutation.mutateAsync(request)
      setPendingCutoverRequest(null)
    } catch (caught) {
      if (isMigrationProductionAdoptionStepUpRequired(caught)) {
        // Freeze the exact reviewed route snapshot and acknowledgements while
        // the operator completes identity verification.
        setPendingCutoverRequest(request)
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  async function runProductionVerification() {
    const request: RunMigrationProductionVerificationRequest = {
      operator: null,
      note: null,
      freshnessMinutes: null,
    }
    try {
      setActionError(null)
      await productionVerificationMutation.mutateAsync(request)
    } catch (caught) {
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  async function prepareRollbackPreview() {
    try {
      setActionError(null)
      await rollbackPreviewMutation.mutateAsync({ previewLifetimeMinutes: 15 })
    } catch (caught) {
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  function createRollbackRequest(): ExecuteMigrationProductionRollbackRequest {
    const previewId = plan?.rollback.preview.previewId
    if (!previewId) {
      throw new Error(t("migrationWorkspace.adoption.rollbackPreviewRequired"))
    }
    return {
      operator: null,
      note: null,
      previewId,
      executeTargetRollback: true,
      ...rollbackAcknowledgements,
    }
  }

  async function executeRollback() {
    try {
      setActionError(null)
      await rollbackExecutionMutation.mutateAsync(createRollbackRequest())
      setPendingRollback(false)
    } catch (caught) {
      if (isMigrationProductionAdoptionStepUpRequired(caught)) {
        setPendingRollback(true)
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  async function downloadSourceHandoff() {
    try {
      setActionError(null)
      setSourceHandoffPending(true)
      const handoff = await downloadMigrationProductionSourceHandoff(migrationId)
      const objectUrl = URL.createObjectURL(handoff.blob)
      const link = document.createElement("a")
      link.href = objectUrl
      link.download = handoff.fileName
      document.body.appendChild(link)
      link.click()
      link.remove()
      URL.revokeObjectURL(objectUrl)
      setPendingSourceHandoff(false)
    } catch (caught) {
      if (isMigrationProductionAdoptionStepUpRequired(caught)) {
        setPendingSourceHandoff(true)
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    } finally {
      setSourceHandoffPending(false)
    }
  }


  async function importSourceCompletion() {
    try {
      setActionError(null)
      let envelope = sourceCompletionEnvelope
      if (!envelope) {
        if (!sourceCompletionFile) {
          throw new Error(t("migrationWorkspace.adoption.rollbackCompletionFileRequired"))
        }
        const parsed = JSON.parse(await sourceCompletionFile.text()) as unknown
        if (!parsed || typeof parsed !== "object") {
          throw new Error(t("migrationWorkspace.adoption.rollbackCompletionFileInvalid"))
        }
        envelope = parsed as MigrationProductionSourceRestorationCompletionEnvelope
        setSourceCompletionEnvelope(envelope)
      }
      await sourceCompletionMutation.mutateAsync(envelope)
      setPendingSourceCompletion(false)
    } catch (caught) {
      if (isMigrationProductionAdoptionStepUpRequired(caught)) {
        setPendingSourceCompletion(true)
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  const actionPending = prepareMutation.isPending || materializeMutation.isPending ||
    cutoverPreviewMutation.isPending || cutoverExecutionMutation.isPending ||
    productionVerificationMutation.isPending ||
    rollbackPreviewMutation.isPending || rollbackExecutionMutation.isPending ||
    sourceCompletionMutation.isPending || sourceHandoffPending

  return (
    <Card className="border-sky-500/30 bg-sky-500/[0.02]">
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2">
              <Server className="h-5 w-5 shrink-0 text-sky-400" aria-hidden="true" />
              {t("migrationWorkspace.adoption.title")}
            </CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("migrationWorkspace.adoption.description")}
            </p>
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={() => void stateQuery.refetch()}
            disabled={!adoptionAvailable || stateQuery.isFetching || actionPending}
          >
            <RefreshCw
              className={stateQuery.isFetching ? "mr-2 h-4 w-4 animate-spin" : "mr-2 h-4 w-4"}
              aria-hidden="true"
            />
            {t("migrationWorkspace.refresh")}
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-5">
        <Alert>
          <ShieldCheck className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.adoption.boundaryTitle")}</AlertTitle>
          <AlertDescription>{t("migrationWorkspace.adoption.boundaryDescription")}</AlertDescription>
        </Alert>

        {!adoptionAvailable ? (
          <Alert>
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.adoption.notReadyTitle")}</AlertTitle>
            <AlertDescription>{t("migrationWorkspace.adoption.notReadyDescription")}</AlertDescription>
          </Alert>
        ) : null}

        {stateQuery.isLoading && adoptionAvailable ? (
          <div className="flex items-center gap-2 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
            {t("migrationWorkspace.adoption.loading")}
          </div>
        ) : null}

        {stateQuery.isError ? (
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.adoption.loadErrorTitle")}</AlertTitle>
            <AlertDescription>
              {stateQuery.error instanceof Error
                ? stateQuery.error.message
                : t("migrationWorkspace.adoption.errorGeneric")}
            </AlertDescription>
          </Alert>
        ) : null}

        {actionError ? (
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.adoption.actionErrorTitle")}</AlertTitle>
            <AlertDescription>{actionError}</AlertDescription>
          </Alert>
        ) : null}

        <div className="grid gap-3 lg:grid-cols-[minmax(0,18rem)_minmax(0,24rem)_auto] lg:items-end">
          <label className="space-y-2 text-sm">
            <span className="font-medium">{t("migrationWorkspace.adoption.targetSlug")}</span>
            <Input
              value={targetStackSlug ?? plan?.targetStackSlug ?? ""}
              onChange={(event) => setTargetStackSlug(event.target.value)}
              disabled={!adoptionAvailable || !planEditable || actionPending}
              placeholder={t("migrationWorkspace.adoption.targetSlugPlaceholder")}
            />
          </label>
          <div className="space-y-2 text-sm">
            <label htmlFor="migration-element-public-host" className="font-medium">
              {t("migrationWorkspace.adoption.elementHost")}
            </label>
            <Input
              id="migration-element-public-host"
              value={elementPublicHost ?? plan?.elementPublicHost ?? ""}
              onChange={(event) => setElementPublicHost(event.target.value)}
              disabled={!adoptionAvailable || !planEditable || actionPending}
              placeholder={t("migrationWorkspace.adoption.targetElementHostPlaceholder")}
              aria-describedby="migration-element-public-host-help"
              autoCapitalize="none"
              autoCorrect="off"
              spellCheck={false}
            />
            <p id="migration-element-public-host-help" className="text-xs leading-5 text-muted-foreground">
              {t("migrationWorkspace.adoption.targetElementHostHelp")}
            </p>
          </div>
          <Button
            onClick={() => void preparePlan()}
            disabled={!adoptionAvailable || !planEditable || actionPending}
          >
            {prepareMutation.isPending ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" />
            ) : (
              <Server className="mr-2 h-4 w-4" aria-hidden="true" />
            )}
            {plan
              ? t("migrationWorkspace.adoption.recheck")
              : t("migrationWorkspace.adoption.prepare")}
          </Button>
        </div>

        {plan ? (
          <AdoptionPlan
            plan={plan}
            acknowledgements={acknowledgements}
            onAcknowledgementChange={(key, checked) =>
              setAcknowledgements((current) => ({ ...current, [key]: checked }))}
            allAcknowledged={allAcknowledged}
            materializationPending={materializeMutation.isPending}
            onMaterialize={() => void materializeRuntime()}
            productionAuthorityType={productionAuthorityType}
            cutoverAcknowledgementKeys={activeCutoverAcknowledgementKeys}
            cutoverAcknowledgements={cutoverAcknowledgements}
            onCutoverAcknowledgementChange={(key, checked) =>
              setCutoverAcknowledgements((current) => ({ ...current, [key]: checked }))}
            allCutoverAcknowledged={allCutoverAcknowledged}
            cutoverPreviewPending={cutoverPreviewMutation.isPending}
            cutoverExecutionPending={cutoverExecutionMutation.isPending}
            onPrepareCutoverPreview={() => void prepareCutoverPreview()}
            onExecuteCutover={() => void executeCutover()}
            productionVerificationPending={productionVerificationMutation.isPending}
            onRunProductionVerification={() => void runProductionVerification()}
            rollbackAcknowledgements={rollbackAcknowledgements}
            onRollbackAcknowledgementChange={(key, checked) =>
              setRollbackAcknowledgements((current) => ({ ...current, [key]: checked }))}
            allRollbackAcknowledged={allRollbackAcknowledged}
            rollbackPreviewPending={rollbackPreviewMutation.isPending}
            rollbackExecutionPending={rollbackExecutionMutation.isPending}
            sourceHandoffPending={sourceHandoffPending}
            sourceCompletionPending={sourceCompletionMutation.isPending}
            sourceCompletionFile={sourceCompletionFile}
            onSourceCompletionFileChange={(file) => {
              setSourceCompletionFile(file)
              setSourceCompletionEnvelope(null)
            }}
            onImportSourceCompletion={() => void importSourceCompletion()}
            onPrepareRollbackPreview={() => void prepareRollbackPreview()}
            onExecuteRollback={() => void executeRollback()}
            onDownloadSourceHandoff={() => void downloadSourceHandoff()}
          />
        ) : (
          <div className="rounded-lg border border-dashed p-4 text-sm text-muted-foreground">
            {t("migrationWorkspace.adoption.empty")}
          </div>
        )}
      </CardContent>

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={(open) => {
          setStepUpOpen(open)
          if (!open) {
            setPendingMaterialization(false)
            setPendingCutoverRequest(null)
            setPendingRollback(false)
            setPendingSourceHandoff(false)
            setPendingSourceCompletion(false)
          }
        }}
        onVerified={() => {
          setStepUpOpen(false)
          if (pendingMaterialization) {
            setPendingMaterialization(false)
            void materializeRuntime()
            return
          }
          if (pendingCutoverRequest) {
            const request = pendingCutoverRequest
            setPendingCutoverRequest(null)
            void executeCutover(request)
            return
          }
          if (pendingRollback) {
            setPendingRollback(false)
            void executeRollback()
            return
          }
          if (pendingSourceHandoff) {
            setPendingSourceHandoff(false)
            void downloadSourceHandoff()
            return
          }
          if (pendingSourceCompletion) {
            setPendingSourceCompletion(false)
            void importSourceCompletion()
          }
        }}
      />
    </Card>
  )
}

function AdoptionPlan({
  plan,
  acknowledgements,
  onAcknowledgementChange,
  allAcknowledged,
  materializationPending,
  onMaterialize,
  productionAuthorityType,
  cutoverAcknowledgementKeys,
  cutoverAcknowledgements,
  onCutoverAcknowledgementChange,
  allCutoverAcknowledged,
  cutoverPreviewPending,
  cutoverExecutionPending,
  onPrepareCutoverPreview,
  onExecuteCutover,
  productionVerificationPending,
  onRunProductionVerification,
  rollbackAcknowledgements,
  onRollbackAcknowledgementChange,
  allRollbackAcknowledged,
  rollbackPreviewPending,
  rollbackExecutionPending,
  sourceHandoffPending,
  sourceCompletionPending,
  sourceCompletionFile,
  onSourceCompletionFileChange,
  onImportSourceCompletion,
  onPrepareRollbackPreview,
  onExecuteRollback,
  onDownloadSourceHandoff,
}: {
  plan: MigrationProductionAdoptionPlan
  acknowledgements: Acknowledgements
  onAcknowledgementChange: (key: AcknowledgementKey, checked: boolean) => void
  allAcknowledged: boolean
  materializationPending: boolean
  onMaterialize: () => void
  productionAuthorityType?: string | null
  cutoverAcknowledgementKeys: readonly CutoverAcknowledgementKey[]
  cutoverAcknowledgements: CutoverAcknowledgements
  onCutoverAcknowledgementChange: (key: CutoverAcknowledgementKey, checked: boolean) => void
  allCutoverAcknowledged: boolean
  cutoverPreviewPending: boolean
  cutoverExecutionPending: boolean
  onPrepareCutoverPreview: () => void
  onExecuteCutover: () => void
  productionVerificationPending: boolean
  onRunProductionVerification: () => void
  rollbackAcknowledgements: RollbackAcknowledgements
  onRollbackAcknowledgementChange: (key: RollbackAcknowledgementKey, checked: boolean) => void
  allRollbackAcknowledged: boolean
  rollbackPreviewPending: boolean
  rollbackExecutionPending: boolean
  sourceHandoffPending: boolean
  sourceCompletionPending: boolean
  sourceCompletionFile: File | null
  onSourceCompletionFileChange: (file: File | null) => void
  onImportSourceCompletion: () => void
  onPrepareRollbackPreview: () => void
  onExecuteRollback: () => void
  onDownloadSourceHandoff: () => void
}) {
  const { t } = useI18n()
  const ready = plan.status === "prepared" && plan.collisionFree
  const materialized = plan.materialization.status === "private-runtime-ready"
  const materializationFailed = plan.materialization.status === "failed"
  const publicAwaitingVerification = plan.cutover.execution.status === "public-awaiting-verification"
  const verificationRunning = plan.productionVerification.status === "running"
  const verificationPassed = plan.productionVerification.status === "passed"
  const verificationFailed = plan.productionVerification.status === "failed"
  const rollbackExecuting = plan.rollback.execution.status === "executing"
  const rollbackFailed = plan.rollback.execution.status === "failed"
  const targetRolledBack = plan.rollback.execution.status === "target-rolled-back-awaiting-source"
  const coordinatedRollbackComplete = plan.rollback.completion.status === "coordinated-rollback-complete"

  return (
    <div className="space-y-4 rounded-xl border p-4">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-mono text-xs">{plan.adoptionPlanId}</span>
        <Badge
          variant={
            rollbackFailed || verificationFailed || materializationFailed
              ? "destructive"
              : ready || materialized || publicAwaitingVerification || verificationRunning || verificationPassed || targetRolledBack || coordinatedRollbackComplete
                ? "secondary"
                : "destructive"
          }
        >
          {coordinatedRollbackComplete
            ? t("migrationWorkspace.adoption.statusCoordinatedRollbackComplete")
            : targetRolledBack
              ? t("migrationWorkspace.adoption.statusTargetRolledBack")
            : rollbackExecuting
              ? t("migrationWorkspace.adoption.statusRollbackExecuting")
              : rollbackFailed
                ? t("migrationWorkspace.adoption.statusRollbackFailed")
                : verificationRunning
                  ? t("migrationWorkspace.adoption.statusVerificationRunning")
                  : verificationPassed
                      ? t("migrationWorkspace.adoption.statusVerificationPassed")
                      : verificationFailed
                        ? t("migrationWorkspace.adoption.statusVerificationFailed")
                        : publicAwaitingVerification
                          ? t("migrationWorkspace.adoption.statusPublicAwaitingVerification")
                          : materialized
                    ? t("migrationWorkspace.adoption.statusPrivateRuntimeReady")
                    : ready
                      ? t("migrationWorkspace.adoption.statusPrepared")
                      : materializationFailed
                        ? t("migrationWorkspace.adoption.statusMaterializationFailed")
                        : t("migrationWorkspace.adoption.statusBlocked")}
        </Badge>
        <Badge variant="outline">
          {t("migrationWorkspace.adoption.revision", { revision: plan.revisionNumber })}
        </Badge>
      </div>

      {targetRolledBack || coordinatedRollbackComplete || rollbackExecuting || rollbackFailed ? null : materialized ? (
        <Alert>
          <CheckCircle2 className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.adoption.materializedTitle")}</AlertTitle>
          <AlertDescription>{t("migrationWorkspace.adoption.materializedDescription")}</AlertDescription>
        </Alert>
      ) : materializationFailed ? (
        <Alert variant="destructive">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.adoption.materializationFailedTitle")}</AlertTitle>
          <AlertDescription>{plan.materialization.failureSummary ?? plan.blockerSummary}</AlertDescription>
        </Alert>
      ) : ready ? (
        <Alert>
          <CheckCircle2 className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.adoption.readyTitle")}</AlertTitle>
          <AlertDescription>{t("migrationWorkspace.adoption.readyDescription")}</AlertDescription>
        </Alert>
      ) : (
        <Alert variant="destructive">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.adoption.blockedTitle")}</AlertTitle>
          <AlertDescription>{plan.blockerSummary ?? t("migrationWorkspace.adoption.blockedDescription")}</AlertDescription>
        </Alert>
      )}

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Fact label={t("migrationWorkspace.adoption.stackSlug")} value={plan.targetStackSlug} />
        <Fact label={t("migrationWorkspace.adoption.runtimeStackId")} value={plan.runtimeStackId} mono />
        <Fact label={t("migrationWorkspace.adoption.matrixHost")} value={plan.matrixPublicHost} />
        <Fact label={t("migrationWorkspace.adoption.elementHost")} value={plan.elementPublicHost} />
        <Fact label={t("migrationWorkspace.adoption.matrixContainer")} value={plan.matrixContainerName} mono />
        <Fact label={t("migrationWorkspace.adoption.elementContainer")} value={plan.elementContainerName} mono />
        <Fact label={t("migrationWorkspace.adoption.runtimeNetwork")} value={plan.runtimeNetworkName} mono />
        <Fact label={t("migrationWorkspace.adoption.planHash")} value={plan.planSha256} mono />
      </div>

      <div className="grid gap-4 xl:grid-cols-2">
        <section className="rounded-lg border p-4">
          <h4 className="flex items-center gap-2 font-medium">
            <Database className="h-4 w-4" aria-hidden="true" />
            {t("migrationWorkspace.adoption.databaseTitle")}
          </h4>
          <div className="mt-3 grid gap-3 sm:grid-cols-2">
            <Fact label={t("migrationWorkspace.adoption.databaseName")} value={plan.databaseName} mono />
            <Fact label={t("migrationWorkspace.adoption.databaseUser")} value={plan.databaseUsername} mono />
            <Fact label={t("migrationWorkspace.adoption.databaseHost")} value={`${plan.databaseHost}:${plan.databasePort}`} mono />
            <Fact label={t("migrationWorkspace.adoption.secretKind")} value={plan.databasePasswordSecretKind} mono />
          </div>
        </section>

        <section className="rounded-lg border p-4">
          <h4 className="flex items-center gap-2 font-medium">
            <Network className="h-4 w-4" aria-hidden="true" />
            {t("migrationWorkspace.adoption.routesTitle")}
          </h4>
          <div className="mt-3 space-y-3">
            {plan.routes.map((route) => (
              <div key={route.serviceKey} className="rounded-md bg-muted/40 p-3 text-sm">
                <div className="font-medium">{route.publicHost}</div>
                <div className="mt-1 font-mono text-xs text-muted-foreground">
                  {route.forwardScheme}://{route.forwardHost}:{route.forwardPort}
                </div>
                <div className="mt-1 text-xs text-muted-foreground">
                  {t("migrationWorkspace.adoption.routeDeferred")}
                </div>
              </div>
            ))}
          </div>
        </section>
      </div>

      {plan.collisions.length > 0 ? (
        <section className="space-y-2">
          <h4 className="font-medium">{t("migrationWorkspace.adoption.collisionsTitle")}</h4>
          {plan.collisions.map((collision) => (
            <Alert variant="destructive" key={`${collision.code}-${collision.resourceValue}`}>
              <AlertTriangle className="h-4 w-4" />
              <AlertTitle>{collision.resourceValue}</AlertTitle>
              <AlertDescription>{collision.detail}</AlertDescription>
            </Alert>
          ))}
        </section>
      ) : null}

      <div className="grid gap-3 sm:grid-cols-3">
        <Fact label={t("migrationWorkspace.staging.users")} value={String(plan.expectedUsersCount ?? "—")} />
        <Fact label={t("migrationWorkspace.staging.rooms")} value={String(plan.expectedRoomsCount ?? "—")} />
        <Fact label={t("migrationWorkspace.staging.events")} value={String(plan.expectedEventsCount ?? "—")} />
      </div>

      {ready ? (
        <section className="space-y-3 rounded-lg border border-amber-500/40 p-4">
          <div>
            <h4 className="font-medium">{t("migrationWorkspace.adoption.materializeTitle")}</h4>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("migrationWorkspace.adoption.materializeDescription")}
            </p>
          </div>
          <MaterializationAcknowledgements
            values={acknowledgements}
            disabled={materializationPending}
            onChange={onAcknowledgementChange}
          />
          <Button
            variant="destructive"
            disabled={!allAcknowledged || materializationPending}
            onClick={onMaterialize}
          >
            {materializationPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Server className="mr-2 h-4 w-4" />}
            {t("migrationWorkspace.adoption.materialize")}
          </Button>
        </section>
      ) : null}

      {plan.materialization.status !== "not-started" ? (
        <MaterializationEvidence plan={plan} />
      ) : null}

      {materialized ? (
        <ProductionCutover
          plan={plan}
          acknowledgementKeys={cutoverAcknowledgementKeys}
          acknowledgements={cutoverAcknowledgements}
          onAcknowledgementChange={onCutoverAcknowledgementChange}
          allAcknowledged={allCutoverAcknowledged}
          previewPending={cutoverPreviewPending}
          executionPending={cutoverExecutionPending}
          onPreparePreview={onPrepareCutoverPreview}
          onExecute={onExecuteCutover}
        />
      ) : null}

      {materialized && (
        plan.cutover.execution.status === "public-awaiting-verification" ||
        plan.productionVerification.status !== "not-started"
      ) ? (
        <ProductionVerification
          plan={plan}
          pending={productionVerificationPending}
          onRun={onRunProductionVerification}
        />
      ) : null}

      {materialized && (
        plan.cutover.execution.status === "public-awaiting-verification" ||
        plan.rollback.preview.previewId !== null ||
        plan.rollback.execution.status !== "not-started"
      ) ? productionAuthorityType === "operator-attested-snapshot" ? (
        <Alert>
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.adoption.simplifiedRollbackTitle")}</AlertTitle>
          <AlertDescription>{t("migrationWorkspace.adoption.simplifiedRollbackDescription")}</AlertDescription>
        </Alert>
      ) : (
        <ProductionRollback
          plan={plan}
          acknowledgements={rollbackAcknowledgements}
          onAcknowledgementChange={onRollbackAcknowledgementChange}
          allAcknowledged={allRollbackAcknowledged}
          previewPending={rollbackPreviewPending}
          executionPending={rollbackExecutionPending}
          sourceHandoffPending={sourceHandoffPending}
          sourceCompletionPending={sourceCompletionPending}
          sourceCompletionFile={sourceCompletionFile}
          onSourceCompletionFileChange={onSourceCompletionFileChange}
          onImportSourceCompletion={onImportSourceCompletion}
          onPreparePreview={onPrepareRollbackPreview}
          onExecute={onExecuteRollback}
          onDownloadSourceHandoff={onDownloadSourceHandoff}
        />
      ) : null}
    </div>
  )
}

function MaterializationAcknowledgements({
  values,
  disabled,
  onChange,
}: {
  values: Acknowledgements
  disabled: boolean
  onChange: (key: AcknowledgementKey, checked: boolean) => void
}) {
  const { t } = useI18n()
  const labels: Record<AcknowledgementKey, string> = {
    acknowledgeCreatesNormalRuntimeRecords: t("migrationWorkspace.adoption.ackRuntimeRecords"),
    acknowledgeMutatesProductionPostgres: t("migrationWorkspace.adoption.ackPostgres"),
    acknowledgeStartsProductionContainers: t("migrationWorkspace.adoption.ackContainers"),
    acknowledgeNoPublicRoutes: t("migrationWorkspace.adoption.ackNoRoutes"),
    acknowledgeNoAutomaticRollback: t("migrationWorkspace.adoption.ackNoRollback"),
  }

  return (
    <div className="space-y-2">
      {acknowledgementKeys.map((key) => (
        <label key={key} className="flex items-start gap-2 text-sm">
          <input
            type="checkbox"
            className="mt-1"
            checked={values[key]}
            disabled={disabled}
            onChange={(event) => onChange(key, event.target.checked)}
          />
          <span>{labels[key]}</span>
        </label>
      ))}
    </div>
  )
}

function MaterializationEvidence({ plan }: { plan: MigrationProductionAdoptionPlan }) {
  const { t } = useI18n()
  const evidence = plan.materialization
  const items = [
    [t("migrationWorkspace.adoption.materializationDatabase"), evidence.productionDatabaseImported],
    [t("migrationWorkspace.adoption.materializationMatrix"), evidence.matrixContainerStarted && evidence.matrixHealthPassed],
    [t("migrationWorkspace.adoption.materializationElement"), evidence.elementContainerStarted && evidence.elementHealthPassed],
    [t("migrationWorkspace.adoption.materializationManifest"), evidence.runtimeManifestSaved],
    [t("migrationWorkspace.adoption.materializationOwnership"), evidence.databaseOwnershipSaved && evidence.runtimeRecordsCreated],
    [t("migrationWorkspace.adoption.materializationUsers"), evidence.userInventorySynchronized],
    [t("migrationWorkspace.adoption.materializationNoRoutes"), !evidence.publicRoutesCreated],
  ] as const

  return (
    <section className="space-y-3 rounded-lg border p-4">
      <div className="flex flex-wrap items-center gap-2">
        <h4 className="font-medium">{t("migrationWorkspace.adoption.materializationEvidenceTitle")}</h4>
        {evidence.materializationId ? <span className="font-mono text-xs">{evidence.materializationId}</span> : null}
      </div>
      <div className="grid gap-2 sm:grid-cols-2">
        {items.map(([label, passed]) => (
          <div key={label} className="flex items-center gap-2 text-sm">
            {passed ? <CheckCircle2 className="h-4 w-4 text-emerald-500" /> : <AlertTriangle className="h-4 w-4 text-muted-foreground" />}
            <span>{label}</span>
          </div>
        ))}
      </div>
    </section>
  )
}

function ProductionCutover({
  plan,
  acknowledgementKeys,
  acknowledgements,
  onAcknowledgementChange,
  allAcknowledged,
  previewPending,
  executionPending,
  onPreparePreview,
  onExecute,
}: {
  plan: MigrationProductionAdoptionPlan
  acknowledgementKeys: readonly CutoverAcknowledgementKey[]
  acknowledgements: CutoverAcknowledgements
  onAcknowledgementChange: (key: CutoverAcknowledgementKey, checked: boolean) => void
  allAcknowledged: boolean
  previewPending: boolean
  executionPending: boolean
  onPreparePreview: () => void
  onExecute: () => void
}) {
  const { t } = useI18n()
  const preview = plan.cutover.preview
  const execution = plan.cutover.execution
  const previewReady = preview.status === "ready" && Boolean(preview.previewId)
  const publicAwaitingVerification = execution.status === "public-awaiting-verification"
  const failed = execution.status === "failed"
  const rollbackStarted = plan.rollback.preview.previewId !== null ||
    plan.rollback.execution.status !== "not-started"

  return (
    <section className="space-y-4 rounded-lg border border-red-500/40 p-4">
      <div>
        <h4 className="flex items-center gap-2 font-medium">
          <Network className="h-4 w-4" aria-hidden="true" />
          {t("migrationWorkspace.adoption.cutoverTitle")}
        </h4>
        <p className="mt-1 text-sm text-muted-foreground">
          {t("migrationWorkspace.adoption.cutoverDescription")}
        </p>
      </div>

      {publicAwaitingVerification ? (
        <Alert>
          <CheckCircle2 className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.adoption.cutoverPublicTitle")}</AlertTitle>
          <AlertDescription>{t("migrationWorkspace.adoption.cutoverPublicDescription")}</AlertDescription>
        </Alert>
      ) : failed ? (
        <Alert variant="destructive">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.adoption.cutoverFailedTitle")}</AlertTitle>
          <AlertDescription>{execution.failureSummary}</AlertDescription>
        </Alert>
      ) : null}

      <div className="flex flex-wrap items-center gap-2">
        <Button
          variant="outline"
          disabled={previewPending || executionPending || publicAwaitingVerification || rollbackStarted}
          onClick={onPreparePreview}
        >
          {previewPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <RefreshCw className="mr-2 h-4 w-4" />}
          {preview.previewId
            ? t("migrationWorkspace.adoption.cutoverRefreshPreview")
            : t("migrationWorkspace.adoption.cutoverPreparePreview")}
        </Button>
        {preview.previewId ? <span className="font-mono text-xs">{preview.previewId}</span> : null}
        <Badge variant={previewReady ? "secondary" : "outline"}>
          {preview.status}
        </Badge>
      </div>

      {preview.blockers.map((blocker) => (
        <Alert variant="destructive" key={blocker}>
          <AlertTriangle className="h-4 w-4" />
          <AlertDescription>{blocker}</AlertDescription>
        </Alert>
      ))}

      {preview.routes.length > 0 ? (
        <div className="grid gap-3 xl:grid-cols-2">
          {preview.routes.map((route) => (
            <div key={route.serviceKey} className="rounded-md bg-muted/40 p-3 text-sm">
              <div className="font-medium">{route.publicHost}</div>
              <div className="mt-1 font-mono text-xs">
                {route.desiredForwardHost}:{route.desiredForwardPort}
              </div>
              <div className="mt-2 text-xs text-muted-foreground">
                {route.existingRouteFound
                  ? `${route.existingForwardHost ?? "—"}:${route.existingForwardPort ?? "—"} → ${route.action}`
                  : t("migrationWorkspace.adoption.cutoverRouteCreate")}
              </div>
              {route.selectedCertificateId ? (
                <div className="mt-2 text-xs text-muted-foreground">
                  {t("migrationWorkspace.adoption.cutoverCertificate", {
                    certificate: route.selectedCertificateName ?? route.selectedCertificateRecordId ?? "—",
                    id: route.selectedCertificateId,
                  })}
                </div>
              ) : null}
            </div>
          ))}
        </div>
      ) : null}

      {previewReady && !publicAwaitingVerification && !rollbackStarted ? (
        <>
          <CutoverAcknowledgements
            acknowledgementKeys={acknowledgementKeys}
            values={acknowledgements}
            disabled={executionPending}
            onChange={onAcknowledgementChange}
          />
          <Button
            variant="destructive"
            disabled={!allAcknowledged || executionPending}
            onClick={onExecute}
          >
            {executionPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Network className="mr-2 h-4 w-4" />}
            {t("migrationWorkspace.adoption.cutoverExecute")}
          </Button>
        </>
      ) : null}

      {execution.executionId ? (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <Fact label={t("migrationWorkspace.adoption.cutoverExecutionId")} value={execution.executionId} mono />
          <Fact label={t("migrationWorkspace.adoption.cutoverMatrixRouteId")} value={execution.matrixRouteId ?? "—"} mono />
          <Fact label={t("migrationWorkspace.adoption.cutoverElementRouteId")} value={execution.elementRouteId ?? "—"} mono />
          <Fact label={t("migrationWorkspace.adoption.cutoverStatus")} value={execution.status} />
        </div>
      ) : null}
    </section>
  )
}

function CutoverAcknowledgements({
  acknowledgementKeys,
  values,
  disabled,
  onChange,
}: {
  acknowledgementKeys: readonly CutoverAcknowledgementKey[]
  values: CutoverAcknowledgements
  disabled: boolean
  onChange: (key: CutoverAcknowledgementKey, checked: boolean) => void
}) {
  const { t } = useI18n()
  const labels: Record<CutoverAcknowledgementKey, string> = {
    acknowledgeSourceFrozen: t("migrationWorkspace.adoption.cutoverAckFrozen"),
    acknowledgeProductionAuthority: t("migrationWorkspace.adoption.cutoverAckAuthority"),
    acknowledgePrivateRuntimeHealthy: t("migrationWorkspace.adoption.cutoverAckHealthy"),
    acknowledgeRouteSnapshotReviewed: t("migrationWorkspace.adoption.cutoverAckSnapshot"),
    acknowledgeCreatesPublicRoutes: t("migrationWorkspace.adoption.cutoverAckRoutes"),
    acknowledgeNoDnsMutation: t("migrationWorkspace.adoption.cutoverAckNoDns"),
    acknowledgeNoCertificateMutation: t("migrationWorkspace.adoption.cutoverAckNoCertificate"),
    acknowledgeRollbackIsNextSlice: t("migrationWorkspace.adoption.cutoverAckRollback"),
    acknowledgePostCutoverVerificationRequired: t("migrationWorkspace.adoption.cutoverAckVerification"),
  }

  return (
    <div className="space-y-2">
      {acknowledgementKeys.map((key) => (
        <label key={key} className="flex items-start gap-2 text-sm">
          <input
            type="checkbox"
            className="mt-1"
            checked={values[key]}
            disabled={disabled}
            onChange={(event) => onChange(key, event.target.checked)}
          />
          <span>{labels[key]}</span>
        </label>
      ))}
    </div>
  )
}

function ProductionVerification({
  plan,
  pending,
  onRun,
}: {
  plan: MigrationProductionAdoptionPlan
  pending: boolean
  onRun: () => void
}) {
  const { intlLocale, t } = useI18n()
  const verification = plan.productionVerification
  const running = verification.status === "running"
  const passed = verification.status === "passed"
  const failed = verification.status === "failed"
  const rollbackStarted = plan.rollback.execution.status !== "not-started" ||
    plan.rollback.completion.status === "coordinated-rollback-complete"
  const publicRuntimeActive = plan.cutover.execution.status === "public-awaiting-verification"
  const canRun = publicRuntimeActive && !rollbackStarted && !running && !pending
  const runLabel = verification.verificationId
    ? t("migrationWorkspace.adoption.verificationRerun")
    : t("migrationWorkspace.adoption.verificationRun")

  return (
    <section className="space-y-4 rounded-lg border border-emerald-500/30 p-4">
      <div>
        <h4 className="flex items-center gap-2 font-medium">
          <ShieldCheck className="h-4 w-4" aria-hidden="true" />
          {t("migrationWorkspace.adoption.verificationTitle")}
        </h4>
        <p className="mt-1 text-sm text-muted-foreground">
          {t("migrationWorkspace.adoption.verificationDescription")}
        </p>
      </div>

      <Alert>
        <ShieldCheck className="h-4 w-4" />
        <AlertTitle>{t("migrationWorkspace.adoption.verificationBoundaryTitle")}</AlertTitle>
        <AlertDescription>{t("migrationWorkspace.adoption.verificationBoundaryDescription")}</AlertDescription>
      </Alert>

      {rollbackStarted ? (
        <Alert variant="destructive">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.adoption.verificationUnavailableTitle")}</AlertTitle>
          <AlertDescription>{t("migrationWorkspace.adoption.verificationUnavailableDescription")}</AlertDescription>
        </Alert>
      ) : running ? (
        <Alert>
          <Loader2 className="h-4 w-4 animate-spin" />
          <AlertTitle>{t("migrationWorkspace.adoption.verificationRunningTitle")}</AlertTitle>
          <AlertDescription>{t("migrationWorkspace.adoption.verificationRunningDescription")}</AlertDescription>
        </Alert>
      ) : passed ? (
        <Alert>
          <CheckCircle2 className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.adoption.verificationPassedTitle")}</AlertTitle>
          <AlertDescription>{t("migrationWorkspace.adoption.verificationPassedDescription")}</AlertDescription>
        </Alert>
      ) : failed ? (
        <Alert variant="destructive">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.adoption.verificationFailedTitle")}</AlertTitle>
          <AlertDescription>
            {verification.failureSummary ?? t("migrationWorkspace.adoption.verificationFailedDescription")}
          </AlertDescription>
        </Alert>
      ) : (
        <Alert>
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.adoption.verificationWaitingTitle")}</AlertTitle>
          <AlertDescription>{t("migrationWorkspace.adoption.verificationWaitingDescription")}</AlertDescription>
        </Alert>
      )}

      {publicRuntimeActive && !rollbackStarted ? (
        <Button className="w-full" disabled={!canRun} onClick={onRun}>
          {pending || running ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <ShieldCheck className="mr-2 h-4 w-4" />}
          {runLabel}
        </Button>
      ) : null}

      {verification.verificationId ? (
        <div className="space-y-4 rounded-md border p-3">
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <Fact label={t("migrationWorkspace.adoption.verificationId")} value={verification.verificationId} mono />
            <Fact label={t("migrationWorkspace.adoption.verificationStatus")} value={verification.status} />
            <Fact label={t("migrationWorkspace.adoption.verificationStarted")} value={formatDate(verification.startedAtUtc, intlLocale)} />
            <Fact label={t("migrationWorkspace.adoption.verificationCompleted")} value={formatDate(verification.completedAtUtc, intlLocale)} />
            <Fact label={t("migrationWorkspace.adoption.verificationEvidenceHash")} value={verification.evidenceSha256 ?? "—"} mono />
            <Fact label={t("migrationWorkspace.adoption.verificationReadinessReport")} value={verification.readinessReportId ?? "—"} mono />
            <Fact
              label={t("migrationWorkspace.adoption.verificationCounts")}
              value={t("migrationWorkspace.adoption.verificationCountsValue", {
                checks: verification.checkCount,
                failed: verification.failedCheckCount,
              })}
            />
          </div>

          {verification.checks.length > 0 ? (
            <div className="space-y-2">
              <h5 className="text-sm font-medium">{t("migrationWorkspace.adoption.verificationChecksTitle")}</h5>
              {verification.checks.map((check, index) => (
                <div key={`${check.code}-${index}`} className="rounded-md bg-muted/40 p-3 text-sm">
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div>
                      <div className="font-medium">{check.name}</div>
                      <div className="mt-1 font-mono text-[11px] text-muted-foreground">{check.code}</div>
                    </div>
                    <Badge variant={check.success ? "secondary" : "destructive"}>
                      {check.success
                        ? t("migrationWorkspace.adoption.verificationCheckPassed")
                        : t("migrationWorkspace.adoption.verificationCheckFailed")}
                    </Badge>
                  </div>
                  <p className="mt-2 text-sm text-muted-foreground">{check.detail}</p>
                  {check.url || check.statusCode !== null ? (
                    <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 font-mono text-[11px] text-muted-foreground">
                      {check.statusCode !== null ? (
                        <span>{t("migrationWorkspace.adoption.verificationHttpStatus", { status: check.statusCode })}</span>
                      ) : null}
                      {check.url ? <span className="break-all">{check.url}</span> : null}
                    </div>
                  ) : null}
                </div>
              ))}
            </div>
          ) : null}
        </div>
      ) : null}

      <p className="text-xs text-muted-foreground">
        {t("migrationWorkspace.adoption.verificationRefreshSafe")}
      </p>
    </section>
  )
}

function ProductionRollback({
  plan,
  acknowledgements,
  onAcknowledgementChange,
  allAcknowledged,
  previewPending,
  executionPending,
  sourceHandoffPending,
  sourceCompletionPending,
  sourceCompletionFile,
  onSourceCompletionFileChange,
  onImportSourceCompletion,
  onPreparePreview,
  onExecute,
  onDownloadSourceHandoff,
}: {
  plan: MigrationProductionAdoptionPlan
  acknowledgements: RollbackAcknowledgements
  onAcknowledgementChange: (key: RollbackAcknowledgementKey, checked: boolean) => void
  allAcknowledged: boolean
  previewPending: boolean
  executionPending: boolean
  sourceHandoffPending: boolean
  sourceCompletionPending: boolean
  sourceCompletionFile: File | null
  onSourceCompletionFileChange: (file: File | null) => void
  onImportSourceCompletion: () => void
  onPreparePreview: () => void
  onExecute: () => void
  onDownloadSourceHandoff: () => void
}) {
  const { t } = useI18n()
  const preview = plan.rollback.preview
  const execution = plan.rollback.execution
  const previewReady = preview.status === "ready" && Boolean(preview.previewId)
  const executing = execution.status === "executing"
  const failed = execution.status === "failed"
  const targetRolledBack = execution.status === "target-rolled-back-awaiting-source"
  const completion = plan.rollback.completion
  const coordinatedRollbackComplete = completion.status === "coordinated-rollback-complete"

  const evidence = [
    [t("migrationWorkspace.adoption.rollbackEvidenceRoutes"), execution.routesRestored],
    [t("migrationWorkspace.adoption.rollbackEvidenceOwnership"), execution.runtimeRoutesRemoved],
    [t("migrationWorkspace.adoption.rollbackEvidenceContainers"), execution.targetContainersStopped],
    [
      t("migrationWorkspace.adoption.rollbackEvidenceCompensation"),
      !execution.targetRouteCompensationAttempted || execution.targetRouteCompensationCompleted,
    ],
  ] as const

  return (
    <section className="space-y-4 rounded-lg border border-amber-500/50 bg-amber-500/[0.02] p-4">
      <div>
        <h4 className="flex items-center gap-2 font-medium">
          <RotateCcw className="h-4 w-4" aria-hidden="true" />
          {t("migrationWorkspace.adoption.rollbackTitle")}
        </h4>
        <p className="mt-1 text-sm text-muted-foreground">
          {t("migrationWorkspace.adoption.rollbackDescription")}
        </p>
      </div>

      <Alert>
        <ShieldCheck className="h-4 w-4" />
        <AlertTitle>{t("migrationWorkspace.adoption.rollbackBoundaryTitle")}</AlertTitle>
        <AlertDescription>{t("migrationWorkspace.adoption.rollbackBoundaryDescription")}</AlertDescription>
      </Alert>

      {coordinatedRollbackComplete ? (
        <Alert>
          <CheckCircle2 className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.adoption.rollbackClosureCompleteTitle")}</AlertTitle>
          <AlertDescription>{t("migrationWorkspace.adoption.rollbackClosureCompleteDescription")}</AlertDescription>
        </Alert>
      ) : targetRolledBack ? (
        <Alert>
          <CheckCircle2 className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.adoption.rollbackCompleteTitle")}</AlertTitle>
          <AlertDescription>{t("migrationWorkspace.adoption.rollbackCompleteDescription")}</AlertDescription>
        </Alert>
      ) : failed ? (
        <Alert variant="destructive">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.adoption.rollbackFailedTitle")}</AlertTitle>
          <AlertDescription>{execution.failureSummary}</AlertDescription>
        </Alert>
      ) : executing ? (
        <Alert>
          <Loader2 className="h-4 w-4 animate-spin" />
          <AlertTitle>{t("migrationWorkspace.adoption.rollbackExecutingTitle")}</AlertTitle>
          <AlertDescription>{t("migrationWorkspace.adoption.rollbackExecutingDescription")}</AlertDescription>
        </Alert>
      ) : null}

      <div className="flex flex-wrap items-center gap-2">
        <Button
          variant="outline"
          disabled={previewPending || executionPending || executing || targetRolledBack || coordinatedRollbackComplete}
          onClick={onPreparePreview}
        >
          {previewPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <RefreshCw className="mr-2 h-4 w-4" />}
          {preview.previewId
            ? t("migrationWorkspace.adoption.rollbackRefreshPreview")
            : t("migrationWorkspace.adoption.rollbackPreparePreview")}
        </Button>
        {preview.previewId ? <span className="font-mono text-xs">{preview.previewId}</span> : null}
        <Badge variant={previewReady ? "secondary" : "outline"}>{preview.status}</Badge>
      </div>

      {preview.blockers.map((blocker) => (
        <Alert variant="destructive" key={blocker}>
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.adoption.rollbackBlockerTitle")}</AlertTitle>
          <AlertDescription>{blocker}</AlertDescription>
        </Alert>
      ))}

      {preview.routes.length > 0 ? (
        <div className="grid gap-3 xl:grid-cols-2">
          {preview.routes.map((route) => (
            <div key={route.serviceKey} className="rounded-md bg-muted/40 p-3 text-sm">
              <div className="flex items-center justify-between gap-3">
                <div className="font-medium">{route.publicHost}</div>
                <Badge variant={route.matchesExpectedTarget ? "secondary" : "destructive"}>
                  {route.matchesExpectedTarget
                    ? t("migrationWorkspace.adoption.rollbackRouteMatched")
                    : t("migrationWorkspace.adoption.rollbackRouteChanged")}
                </Badge>
              </div>
              <div className="mt-1 font-mono text-xs">
                {route.currentForwardHost ?? "—"}:{route.currentForwardPort ?? "—"}
              </div>
              <div className="mt-2 text-xs text-muted-foreground">
                {t("migrationWorkspace.adoption.rollbackRestoreAction", { action: route.restoreAction })}
              </div>
            </div>
          ))}
        </div>
      ) : null}

      {previewReady && !executing && !targetRolledBack ? (
        <>
          <RollbackAcknowledgements
            values={acknowledgements}
            disabled={executionPending}
            onChange={onAcknowledgementChange}
          />
          <Button
            variant="destructive"
            disabled={!allAcknowledged || executionPending}
            onClick={onExecute}
          >
            {executionPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <RotateCcw className="mr-2 h-4 w-4" />}
            {t("migrationWorkspace.adoption.rollbackExecute")}
          </Button>
        </>
      ) : null}

      {execution.executionId ? (
        <div className="space-y-3 rounded-md border p-3">
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <Fact label={t("migrationWorkspace.adoption.rollbackExecutionId")} value={execution.executionId} mono />
            <Fact label={t("migrationWorkspace.adoption.rollbackStatus")} value={execution.status} />
            <Fact label={t("migrationWorkspace.adoption.rollbackHandoffId")} value={execution.sourceHandoffId ?? "—"} mono />
            <Fact label={t("migrationWorkspace.adoption.rollbackHandoffHash")} value={execution.sourceHandoffSha256 ?? "—"} mono />
          </div>
          <div className="grid gap-2 sm:grid-cols-2">
            {evidence.map(([label, passed]) => (
              <div key={label} className="flex items-center gap-2 text-sm">
                {passed ? <CheckCircle2 className="h-4 w-4 text-emerald-500" /> : <AlertTriangle className="h-4 w-4 text-muted-foreground" />}
                <span>{label}</span>
              </div>
            ))}
          </div>
        </div>
      ) : null}

      {targetRolledBack && execution.sourceHandoffId ? (
        <div className="space-y-3 rounded-md border border-sky-500/30 p-3">
          <div>
            <div className="font-medium">{t("migrationWorkspace.adoption.rollbackHandoffTitle")}</div>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("migrationWorkspace.adoption.rollbackHandoffDescription")}
            </p>
          </div>
          <Button
            variant="outline"
            disabled={sourceHandoffPending}
            onClick={onDownloadSourceHandoff}
          >
            {sourceHandoffPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Download className="mr-2 h-4 w-4" />}
            {t("migrationWorkspace.adoption.rollbackDownloadHandoff")}
          </Button>
        </div>
      ) : null}

      {(targetRolledBack || coordinatedRollbackComplete) && execution.sourceHandoffId ? (
        <div className="space-y-3 rounded-md border border-sky-500/30 p-3">
          <div>
            <div className="font-medium">{t("migrationWorkspace.adoption.rollbackCompletionTitle")}</div>
            <p className="mt-1 text-sm text-muted-foreground">
              {coordinatedRollbackComplete
                ? t("migrationWorkspace.adoption.rollbackCompletionImported")
                : t("migrationWorkspace.adoption.rollbackCompletionDescription")}
            </p>
          </div>
          {!coordinatedRollbackComplete ? (
            <div className="space-y-3">
              <Input
                type="file"
                accept="application/json,.json"
                aria-label={t("migrationWorkspace.adoption.rollbackCompletionChoose")}
                disabled={sourceCompletionPending}
                onChange={(event) => onSourceCompletionFileChange(event.target.files?.[0] ?? null)}
              />
              <Button
                variant="outline"
                disabled={!sourceCompletionFile || sourceCompletionPending}
                onClick={onImportSourceCompletion}
              >
                {sourceCompletionPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <UploadCloud className="mr-2 h-4 w-4" />}
                {t("migrationWorkspace.adoption.rollbackCompletionImport")}
              </Button>
            </div>
          ) : (
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
              <Fact label={t("migrationWorkspace.adoption.rollbackCompletionAttempt")} value={completion.restorationAttemptId ?? "—"} mono />
              <Fact label={t("migrationWorkspace.adoption.rollbackCompletionHash")} value={completion.completionSha256 ?? "—"} mono />
              <Fact label={t("migrationWorkspace.adoption.rollbackCompletionSource")} value={completion.sourceRestored ? t("common.yes") : t("common.no")} />
              <Fact label={t("migrationWorkspace.adoption.rollbackCompletionTarget")} value={completion.targetRollbackStillIntact ? t("common.yes") : t("common.no")} />
            </div>
          )}
          {coordinatedRollbackComplete && completion.developmentExternalControlPlane ? (
            <Alert>
              <AlertTriangle className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.adoption.rollbackCompletionDevelopmentTitle")}</AlertTitle>
              <AlertDescription>{t("migrationWorkspace.adoption.rollbackCompletionDevelopmentDescription")}</AlertDescription>
            </Alert>
          ) : null}
        </div>
      ) : null}

      <p className="text-xs text-muted-foreground">
        {t("migrationWorkspace.adoption.rollbackRefreshSafe")}
      </p>
    </section>
  )
}

function RollbackAcknowledgements({
  values,
  disabled,
  onChange,
}: {
  values: RollbackAcknowledgements
  disabled: boolean
  onChange: (key: RollbackAcknowledgementKey, checked: boolean) => void
}) {
  const { t } = useI18n()
  const labels: Record<RollbackAcknowledgementKey, string> = {
    acknowledgeMigrationNotAccepted: t("migrationWorkspace.adoption.rollbackAckNotAccepted"),
    acknowledgeRestoresPreCutoverRoutes: t("migrationWorkspace.adoption.rollbackAckRoutes"),
    acknowledgeStopsTargetContainers: t("migrationWorkspace.adoption.rollbackAckContainers"),
    acknowledgePreservesTargetData: t("migrationWorkspace.adoption.rollbackAckPreservesData"),
    acknowledgeSourceRemainsFrozen: t("migrationWorkspace.adoption.rollbackAckSourceFrozen"),
    acknowledgeSourceRestorationRequiresHandoff: t("migrationWorkspace.adoption.rollbackAckHandoff"),
    acknowledgeNoAutomaticSourceHostMutation: t("migrationWorkspace.adoption.rollbackAckNoSourceMutation"),
  }

  return (
    <div className="space-y-2">
      {rollbackAcknowledgementKeys.map((key) => (
        <label key={key} className="flex items-start gap-2 text-sm">
          <input
            type="checkbox"
            className="mt-1"
            checked={values[key]}
            disabled={disabled}
            onChange={(event) => onChange(key, event.target.checked)}
          />
          <span>{labels[key]}</span>
        </label>
      ))}
    </div>
  )
}

function formatDate(value: string | null, locale: string) {
  return formatMigrationDateTime(value, locale)
}

function Fact({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="min-w-0">
      <div className="text-[11px] uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className={mono ? "mt-1 break-all font-mono text-xs" : "mt-1 break-words text-sm"}>{value}</div>
    </div>
  )
}
