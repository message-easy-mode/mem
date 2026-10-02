import { MigrationStagingRetirementDialog } from "./migration-staging-retirement-dialog"
import { formatMigrationDateTime } from "./migration-time"
import { startMigrationConversion, getMigrationConversionOptions, listMigrationConversionAttempts } from "../api/migration-conversions"
import { startMigrationStaging, listMigrationStagingRuns } from "../api/migration-staging"
import { useMigrationGuidedState, useGuidedMigrationCommand, useGuidedMigrationEvidence } from "./migration-guided-state-context"
import { isMigrationOutcomeUncertain } from "../api/migration-guided-state"
import { useState } from "react"
import {
  AlertCircle,
  CheckCircle2,
  Database,
  Loader2,
  RefreshCw,
  RotateCcw,
  Server,
  ShieldCheck,
  Trash2,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import type { MigrationConversionAttempt } from "@/features/operator/migrations/api/migration-conversions"
import type { MigrationSessionDetail } from "@/features/operator/migrations/api/migration-sessions"
import {
  isMigrationStagingStepUpRequired,
  type MigrationStagingRun,
} from "@/features/operator/migrations/api/migration-staging"
import { MigrationBoundSourceStack } from "@/features/operator/migrations/components/migration-bound-source-stack"

type PendingStagingAction =
  | { kind: "start"; retryOfStagingRunId?: string }
  | null

export function MigrationPrepareAndTestWorkspace({
  detail,
  onChanged,
}: {
  detail: MigrationSessionDetail
  onChanged?: () => Promise<unknown>
}) {
  const { intlLocale, t } = useI18n()
  const guide = useMigrationGuidedState()
  const guided = guide.workspace.guided
  const stage = guide.workspace.stages.find((item) => item.code === "prepare-and-test")!
  const migrationId = detail.session.migrationId
  const packageReady = detail.package?.status === "package-validated"
  const optionsQuery = useGuidedMigrationEvidence("conversion", "conversion-options", () => getMigrationConversionOptions(migrationId), packageReady)
  const attemptsQuery = useGuidedMigrationEvidence("conversion", "conversion-attempts", () => listMigrationConversionAttempts(migrationId), packageReady)
  const runsQuery = useGuidedMigrationEvidence("private-test", "staging-runs", () => listMigrationStagingRuns(migrationId), packageReady)
  const conversionMutation = useGuidedMigrationCommand("conversion", (input: Parameters<typeof startMigrationConversion>[1]) => startMigrationConversion(migrationId, input))
  const stagingMutation = useGuidedMigrationCommand("private-test", (retryOfStagingRunId: string | undefined) => startMigrationStaging(migrationId, { retryOfStagingRunId }))
  const [retirementRunId, setRetirementRunId] = useState<string | null>(null)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [pendingStagingAction, setPendingStagingAction] = useState<PendingStagingAction>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  const attempts = attemptsQuery.data ?? []
  const runs = runsQuery.data ?? []
  const latestAttempt = attempts.find((attempt) => attempt.conversionAttemptId === guided.conversionAttemptId && attempt.status === guided.conversionStatus)
  const activeAttempt = guided.conversionStatus === "pending" || guided.conversionStatus === "running" ? latestAttempt : undefined
  const candidateAttempt = guided.hasVerifiedCandidate ? latestAttempt : undefined
  const latestRun = runs.find((run) => run.stagingRunId === guided.stagingRunId && run.status === guided.stagingStatus)
  const reviewRunId = latestRun?.retirementReviewAvailable ? latestRun.stagingRunId
    : (guided.nextAction?.code === "review-staging-retirement" || ["retiring", "retirement-needs-attention"].includes(guided.stagingStatus ?? ""))
      ? guided.stagingRunId : null
  const activeRun = guided.stagingStatus === "pending" || guided.stagingStatus === "running" ? latestRun : undefined
  const verifiedRun = guided.stagingStatus === "verified" ? latestRun : undefined
  const conversionRetry = guided.conversionStatus === "failed" || guided.conversionStatus === "cancelled"
    ? latestAttempt ?? null : null
  const conversionOptions = optionsQuery.data
  const stagingRetry = ["failed", "failed-cleaned"].includes(guided.stagingStatus ?? "")
    ? latestRun ?? null : null
  const removedRun = guided.stagingStatus === "destroyed" ? latestRun ?? null : null
  const canStartPrivateTest = guide.allows("start-private-test", "retry-private-test", "recreate-private-test")
  const operationPending = conversionMutation.isPending || stagingMutation.isPending

  const refresh = async () => {
    await Promise.all([
      optionsQuery.refetch(),
      attemptsQuery.refetch(),
      runsQuery.refetch(),
      onChanged?.() ?? Promise.resolve(),
    ])
  }

  const startConversion = async (retryOfConversionAttemptId?: string) => {
    if (!guide.allows("start-conversion", "retry-conversion")) return
    try {
      setActionError(null)
      await conversionMutation.mutateAsync({ retryOfConversionAttemptId: retryOfConversionAttemptId ?? (guide.workspace.guided.nextAction?.code === "retry-conversion" ? guided.conversionAttemptId ?? undefined : undefined) })
      await onChanged?.().catch(() => undefined)
    } catch (caught) {
      if (isMigrationOutcomeUncertain(caught)) return
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  const startStaging = async (retryOfStagingRunId?: string) => {
    if (!canStartPrivateTest) return
    try {
      setActionError(null)
      await stagingMutation.mutateAsync(retryOfStagingRunId ?? (["retry-private-test", "recreate-private-test"].includes(guide.workspace.guided.nextAction?.code ?? "") ? guided.stagingRunId ?? undefined : undefined))
      setPendingStagingAction(null)
      await onChanged?.().catch(() => undefined)
    } catch (caught) {
      if (isMigrationOutcomeUncertain(caught)) return
      if (isMigrationStagingStepUpRequired(caught)) {
        setPendingStagingAction({ kind: "start", retryOfStagingRunId })
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  const resumeStagingAction = () => {
    const action = pendingStagingAction
    setStepUpOpen(false)
    if (action?.kind === "start") {
      void startStaging(action.retryOfStagingRunId)
    }
  }

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2">
              <Database className="h-5 w-5 shrink-0" aria-hidden="true" />
              {t("migrationWorkspace.prepare.title")}
            </CardTitle>
            <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
              {t("migrationWorkspace.prepare.description")}
            </p>
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={() => void refresh()}
            disabled={!packageReady || optionsQuery.isFetching || attemptsQuery.isFetching || runsQuery.isFetching}
          >
            <RefreshCw
              className={
                optionsQuery.isFetching || attemptsQuery.isFetching || runsQuery.isFetching
                  ? "mr-2 h-4 w-4 animate-spin"
                  : "mr-2 h-4 w-4"
              }
              aria-hidden="true"
            />
            {t("migrationWorkspace.refresh")}
          </Button>
        </div>
      </CardHeader>

      <CardContent className="space-y-5">
        {!packageReady ? (
          <Alert>
            <AlertCircle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.prepare.packageRequiredTitle")}</AlertTitle>
            <AlertDescription>{t("migrationWorkspace.prepare.packageRequiredDescription")}</AlertDescription>
          </Alert>
        ) : null}

        {optionsQuery.isError || attemptsQuery.isError || runsQuery.isError ? (
          <Alert>
            <AlertCircle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.guided.evidenceTitle")}</AlertTitle>
            <AlertDescription>{t("migrationWorkspace.guided.evidenceUnavailable")}</AlertDescription>
          </Alert>
        ) : null}

        {actionError ? (
          <Alert variant="destructive">
            <AlertCircle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.prepare.actionErrorTitle")}</AlertTitle>
            <AlertDescription>{actionError}</AlertDescription>
          </Alert>
        ) : null}

        {guided.stagingStatus === "retiring" || guided.stagingStatus === "retirement-needs-attention" ? (
          <Alert><AlertCircle className="h-4 w-4" />
            <AlertTitle>{t(guided.stagingStatus === "retiring" ? "migrationRetirement.inProgress" : "migrationRetirement.needsAttention")}</AlertTitle>
            <AlertDescription>{t("migrationRetirement.durable")}</AlertDescription>
          </Alert>
        ) : stage.state === "running" ? (
          <OperationStatus
            title={t(guided.hasVerifiedCandidate ? "migrationWorkspace.prepare.testingTitle" : "migrationWorkspace.prepare.preparingTitle")}
            description={t(guided.hasVerifiedCandidate ? "migrationWorkspace.prepare.testingDescription" : "migrationWorkspace.prepare.preparingDescription")}
          />
        ) : !guided.hasVerifiedCandidate && stage.state !== "completed" ? (
          <div className="space-y-4">
            {conversionOptions ? (
              <MigrationBoundSourceStack stack={conversionOptions.boundSourceStack} />
            ) : null}
            <ConversionStep
              activeAttempt={activeAttempt}
              failedAttempt={conversionRetry}
              pending={conversionMutation.isPending}
              canStart={guide.allows("start-conversion", "retry-conversion")}
              onStart={(retryId) => void startConversion(retryId)}
            />
          </div>
        ) : guided.stagingStatus === "verified" || stage.state === "completed" ? (
          verifiedRun ? <PrivateTestSuccess run={verifiedRun} /> : (
            <Alert><CheckCircle2 className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.prepare.privateTestPassedTitle")}</AlertTitle>
              <AlertDescription>{t("migrationWorkspace.guided.evidenceUnavailable")}</AlertDescription>
            </Alert>
          )
        ) : (
          <PrivateTestStep
            activeRun={activeRun}
            failedRun={stagingRetry}
            removedRun={removedRun}
            canStart={canStartPrivateTest}
            pending={operationPending}
            onStart={(retryId) => void startStaging(retryId)}
          />
        )}

        {reviewRunId ? (
          <div className="flex flex-wrap items-center gap-3 rounded-lg border p-3">
            <Button variant="outline" disabled={guide.blocked}
              onClick={() => setRetirementRunId(reviewRunId)}>
              <Trash2 className="mr-2 h-4 w-4 shrink-0" aria-hidden="true" />
              {t(["retiring", "retirement-needs-attention", "destroyed"].includes(guided.stagingStatus ?? "")
                ? "migrationRetirement.view" : "migrationRetirement.review")}
            </Button>
            <p className="text-sm text-muted-foreground">{t("migrationRetirement.reviewHint")}</p>
          </div>
        ) : null}

        {guided.hasVerifiedCandidate && guided.stagingStatus !== "verified" ? (
          <div className="flex items-center gap-3 rounded-lg border bg-muted/15 p-3 text-sm">
            <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-500" aria-hidden="true" />
            <div>
              <span className="font-medium">{t("migrationWorkspace.prepare.dataReadyTitle")}</span>
            </div>
          </div>
        ) : null}

        <Alert>
          <ShieldCheck className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.prepare.privateBoundaryTitle")}</AlertTitle>
          <AlertDescription>{t("migrationWorkspace.prepare.privateBoundaryDescription")}</AlertDescription>
        </Alert>

        <details className="rounded-lg border bg-muted/10">
          <summary className="cursor-pointer px-4 py-3 text-sm font-medium">
            {t("migrationWorkspace.prepare.technicalDetails")}
          </summary>
          <div className="space-y-5 border-t px-4 py-4">
            <CandidateEvidence attempt={candidateAttempt} locale={intlLocale} />
            <AttemptHistory attempts={attempts} locale={intlLocale} />
            <StagingHistory runs={runs} locale={intlLocale} />

          </div>
        </details>
      </CardContent>

      {retirementRunId && <MigrationStagingRetirementDialog
        key={retirementRunId} target={{ migrationId, stagingRunId: retirementRunId }}
        onClose={() => setRetirementRunId(null)} onChanged={guide.refresh} />}
      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={setStepUpOpen}
        onVerified={resumeStagingAction}
      />
    </Card>
  )
}

function ConversionStep({
  activeAttempt,
  failedAttempt,
  pending,
  canStart,
  onStart,
}: {
  activeAttempt: MigrationConversionAttempt | undefined
  failedAttempt: MigrationConversionAttempt | null
  pending: boolean
  canStart: boolean
  onStart: (retryId?: string) => void
}) {
  const { t } = useI18n()

  if (activeAttempt || pending) {
    return (
      <OperationStatus
        title={t("migrationWorkspace.prepare.preparingTitle")}
        description={t("migrationWorkspace.prepare.preparingDescription")}
      />
    )
  }

  if (failedAttempt) {
    return (
      <Alert variant="destructive">
        <AlertCircle className="h-4 w-4" />
        <AlertTitle>{t("migrationWorkspace.prepare.preparationFailedTitle")}</AlertTitle>
        <AlertDescription className="space-y-3">
          <p>{failedAttempt.failureSummary ?? t("migrationWorkspace.prepare.preparationFailedDescription")}</p>
          <Button
            size="sm"
            variant="outline"
            disabled={!canStart}
            onClick={() => onStart(failedAttempt.conversionAttemptId)}
          >
            <RotateCcw className="mr-2 h-4 w-4" aria-hidden="true" />
            {t("migrationWorkspace.prepare.retryPreparation")}
          </Button>
        </AlertDescription>
      </Alert>
    )
  }

  return (
    <div className="rounded-xl border p-4 sm:p-5">
      <div className="flex items-start gap-3">
        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary/10 text-primary">
          <Database className="h-5 w-5" aria-hidden="true" />
        </span>
        <div className="min-w-0 flex-1">
          <h3 className="font-semibold">{t("migrationWorkspace.prepare.prepareDataTitle")}</h3>
          <p className="mt-1 text-sm leading-6 text-muted-foreground">
            {t("migrationWorkspace.prepare.prepareDataDescription")}
          </p>
          <Button className="mt-4" disabled={!canStart} onClick={() => onStart()}>
            <Database className="mr-2 h-4 w-4" aria-hidden="true" />
            {t("migrationWorkspace.prepare.prepareDataAction")}
          </Button>
        </div>
      </div>
    </div>
  )
}

function PrivateTestStep({
  activeRun,
  failedRun,
  removedRun,
  canStart,
  pending,
  onStart,
}: {
  activeRun: MigrationStagingRun | undefined
  failedRun: MigrationStagingRun | null
  removedRun: MigrationStagingRun | null
  canStart: boolean
  pending: boolean
  onStart: (retryId?: string) => void
}) {
  const { t } = useI18n()

  if (activeRun || pending) {
    return (
      <OperationStatus
        title={t("migrationWorkspace.prepare.testingTitle")}
        description={t("migrationWorkspace.prepare.testingDescription")}
      />
    )
  }

  if (removedRun) {
    return (
      <Alert>
        <Trash2 className="h-4 w-4" />
        <AlertTitle>{t("migrationWorkspace.prepare.privateTestRemovedTitle")}</AlertTitle>
        <AlertDescription className="space-y-3">
          <p>{t("migrationWorkspace.prepare.privateTestRemovedDescription")}</p>
          <Button
            size="sm"
            variant="outline"
            disabled={!canStart}
            onClick={() => onStart(removedRun.stagingRunId)}
          >
            <RotateCcw className="mr-2 h-4 w-4" aria-hidden="true" />
            {t("migrationWorkspace.guide.action.recreatePrivateTest")}
          </Button>
        </AlertDescription>
      </Alert>
    )
  }

  if (failedRun) {
    return (
      <Alert variant="destructive">
        <AlertCircle className="h-4 w-4" />
        <AlertTitle>{t("migrationWorkspace.prepare.privateTestFailedTitle")}</AlertTitle>
        <AlertDescription className="space-y-3">
          <p>{failedRun.failureSummary ?? t("migrationWorkspace.prepare.privateTestFailedDescription")}</p>
          <Button
            size="sm"
            variant="outline"
            disabled={!canStart}
            onClick={() => onStart(failedRun.stagingRunId)}
          >
            <RotateCcw className="mr-2 h-4 w-4" aria-hidden="true" />
            {t("migrationWorkspace.prepare.retryPrivateTest")}
          </Button>
        </AlertDescription>
      </Alert>
    )
  }

  return (
    <div className="rounded-xl border p-4 sm:p-5">
      <div className="flex items-start gap-3">
        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary/10 text-primary">
          <Server className="h-5 w-5" aria-hidden="true" />
        </span>
        <div className="min-w-0 flex-1">
          <h3 className="font-semibold">{t("migrationWorkspace.prepare.privateTestTitle")}</h3>
          <p className="mt-1 text-sm leading-6 text-muted-foreground">
            {t("migrationWorkspace.prepare.privateTestDescription")}
          </p>
          <Button className="mt-4" disabled={!canStart} onClick={() => onStart()}>
            <Server className="mr-2 h-4 w-4" aria-hidden="true" />
            {t("migrationWorkspace.prepare.privateTestAction")}
          </Button>
          {!canStart ? (
            <p className="mt-2 text-xs text-muted-foreground">
              {t("migrationWorkspace.prepare.privateTestWaiting")}
            </p>
          ) : null}
        </div>
      </div>
    </div>
  )
}

function PrivateTestSuccess({ run }: { run: MigrationStagingRun }) {
  const { t } = useI18n()
  const allRuntimeChecksPassed = run.databaseImportSucceeded &&
    run.synapseHealthPassed &&
    run.elementContainerStarted &&
    run.elementHealthPassed &&
    run.elementSynapseConnectivityPassed &&
    run.elementNetworkAttached
  const privateProofPassed = run.privateOnly && !run.publicRoutesCreated

  return (
    <div className="rounded-xl border border-emerald-500/30 bg-emerald-500/5 p-4 sm:p-5">
      <div className="flex items-start gap-3">
        <CheckCircle2 className="mt-0.5 h-6 w-6 shrink-0 text-emerald-500" aria-hidden="true" />
        <div className="min-w-0 flex-1">
          <h3 className="text-base font-semibold">{t("migrationWorkspace.prepare.privateTestPassedTitle")}</h3>
          <p className="mt-1 text-sm leading-6 text-muted-foreground">
            {t("migrationWorkspace.prepare.privateTestPassedDescription")}
          </p>
          <div className="mt-4 grid gap-3 sm:grid-cols-3">
            <CountFact label={t("migrationWorkspace.staging.users")} value={run.usersCount} />
            <CountFact label={t("migrationWorkspace.staging.rooms")} value={run.roomsCount} />
            <CountFact label={t("migrationWorkspace.staging.events")} value={run.eventsCount} />
          </div>
          <div className="mt-4 grid gap-2 text-sm md:grid-cols-3">
            <CheckLine passed={run.databaseImportSucceeded} text={t("migrationWorkspace.prepare.databaseCheck")} />
            <CheckLine passed={allRuntimeChecksPassed} text={t("migrationWorkspace.prepare.runtimeCheck")} />
            <CheckLine passed={privateProofPassed} text={t("migrationWorkspace.prepare.privateCheck")} />
          </div>
          {run.destroyedAtUtc ? (
            <div className="mt-4 flex items-start gap-2 rounded-lg border border-emerald-500/25 bg-emerald-500/5 p-3 text-sm">
              <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-500" aria-hidden="true" />
              <span>{t("migrationWorkspace.prepare.cleanupCompleted")}</span>
            </div>
          ) : run.failureCode?.startsWith("staging_cleanup_") ? (
            <Alert variant="destructive" className="mt-4">
              <AlertCircle className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.prepare.cleanupFailedTitle")}</AlertTitle>
              <AlertDescription>
                {run.failureSummary ?? t("migrationWorkspace.prepare.cleanupFailedDescription")}
              </AlertDescription>
            </Alert>
          ) : (
            <p className="mt-4 text-sm leading-6 text-muted-foreground">
              {t("migrationWorkspace.prepare.cleanupDeferred")}
            </p>
          )}
        </div>
      </div>
    </div>
  )
}

function OperationStatus({ title, description }: { title: string; description?: string }) {
  return (
    <div className="flex items-start gap-3 rounded-xl border bg-primary/[0.03] p-4 sm:p-5">
      <Loader2 className="mt-0.5 h-5 w-5 shrink-0 animate-spin text-primary" aria-hidden="true" />
      <div>
        <div className="font-semibold">{title}</div>
        {description ? <p className="mt-1 text-sm text-muted-foreground">{description}</p> : null}
      </div>
    </div>
  )
}

function CandidateEvidence({
  attempt,
  locale,
}: {
  attempt: MigrationConversionAttempt | undefined
  locale: string
}) {
  const { t } = useI18n()
  if (!attempt) return null

  return (
    <div>
      <h3 className="font-medium">{t("migrationWorkspace.prepare.candidateEvidence")}</h3>
      <div className="mt-3 grid gap-3 md:grid-cols-2">
        <Fact label={t("migrationWorkspace.conversion.candidateId")} value={attempt.candidateArtifactId} mono />
        <Fact label={t("migrationWorkspace.conversion.candidateKind")} value={attempt.candidateArtifactKind} />
        <Fact label={t("migrationWorkspace.conversion.sourceHash")} value={attempt.candidateSourcePackageSha256} mono />
        <Fact label={t("migrationWorkspace.conversion.artifactHash")} value={attempt.candidateArtifactSha256} mono />
        <Fact label={t("migrationWorkspace.conversion.manifestHash")} value={attempt.candidateManifestSha256} mono />
        <Fact label={t("migrationWorkspace.conversion.checksumsHash")} value={attempt.candidateChecksumsSha256} mono />
        <Fact label={t("migrationWorkspace.conversion.verifiedAt")} value={formatDate(attempt.candidateVerifiedAtUtc, locale)} />
        <Fact label={t("migrationWorkspace.conversion.retention")} value={attempt.candidateRetentionState} />
      </div>
    </div>
  )
}

function AttemptHistory({ attempts, locale }: { attempts: MigrationConversionAttempt[]; locale: string }) {
  const { t } = useI18n()
  return (
    <div>
      <h3 className="font-medium">{t("migrationWorkspace.prepare.conversionHistory")}</h3>
      {attempts.length === 0 ? (
        <p className="mt-2 text-sm text-muted-foreground">{t("migrationWorkspace.conversion.empty")}</p>
      ) : (
        <div className="mt-3 space-y-2">
          {attempts.map((attempt) => (
            <div key={attempt.conversionAttemptId} className="rounded-lg border p-3 text-sm">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-mono text-xs">{attempt.conversionAttemptId}</span>
                <Badge variant={attempt.status === "failed" ? "destructive" : "outline"}>{attempt.status}</Badge>
              </div>
              <p className="mt-2 text-muted-foreground">{attempt.currentStep}</p>
              <p className="mt-1 text-xs text-muted-foreground">
                {formatDate(attempt.startedAtUtc, locale)} · {formatDate(attempt.completedAtUtc, locale)}
              </p>
              {attempt.sourceStackId ? (
                <p className="mt-2 break-all font-mono text-xs text-muted-foreground">
                  {t("migrationWorkspace.prepare.sourceStackId")}: {attempt.sourceStackId}
                </p>
              ) : null}
              {attempt.failureSummary ? <p className="mt-2 text-destructive">{attempt.failureSummary}</p> : null}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function StagingHistory({ runs, locale }: { runs: MigrationStagingRun[]; locale: string }) {
  const { t } = useI18n()
  return (
    <div>
      <h3 className="font-medium">{t("migrationWorkspace.prepare.privateTestHistory")}</h3>
      {runs.length === 0 ? (
        <p className="mt-2 text-sm text-muted-foreground">{t("migrationWorkspace.staging.empty")}</p>
      ) : (
        <div className="mt-3 space-y-2">
          {runs.map((run) => (
            <div key={run.stagingRunId} className="rounded-lg border p-3 text-sm">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-mono text-xs">{run.stagingRunId}</span>
                <Badge variant={run.status.startsWith("failed") ? "destructive" : "outline"}>{run.status}</Badge>
              </div>
              <p className="mt-2 text-muted-foreground">{run.currentStep}</p>
              <div className="mt-3 grid gap-2 md:grid-cols-2">
                <Fact label={t("migrationWorkspace.staging.candidateId")} value={run.candidateArtifactId} mono />
                <Fact label={t("migrationWorkspace.staging.matrixServerName")} value={run.matrixServerName} />
                <Fact label={t("migrationWorkspace.staging.startedAt")} value={formatDate(run.startedAtUtc, locale)} />
                <Fact label={t("migrationWorkspace.staging.completedAt")} value={formatDate(run.completedAtUtc, locale)} />
                <Fact label={t("migrationWorkspace.staging.synapseImage")} value={run.synapseImageReference} mono />
                <Fact label={t("migrationWorkspace.staging.elementImage")} value={run.elementImageReference} mono />
              </div>
              {run.failureSummary ? <p className="mt-2 text-destructive">{run.failureSummary}</p> : null}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function CheckLine({ passed, text }: { passed: boolean; text: string }) {
  return (
    <div className="flex items-center gap-2 rounded-lg border bg-background/70 p-3">
      {passed ? (
        <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-500" aria-hidden="true" />
      ) : (
        <AlertCircle className="h-4 w-4 shrink-0 text-amber-500" aria-hidden="true" />
      )}
      <span>{text}</span>
    </div>
  )
}

function CountFact({ label, value }: { label: string; value: number | null }) {
  return (
    <div className="rounded-lg border bg-background/70 p-3">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-1 text-xl font-semibold">{value ?? "—"}</div>
    </div>
  )
}

function Fact({
  label,
  value,
  mono = false,
}: {
  label: string
  value: string | null | undefined
  mono?: boolean
}) {
  return (
    <div className="rounded-lg border p-3">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className={mono ? "mt-1 break-all font-mono text-xs" : "mt-1 break-all text-sm"}>
        {value || "—"}
      </div>
    </div>
  )
}

function formatDate(value: string | null | undefined, locale: string) {
  return formatMigrationDateTime(value, locale)
}
