import { MigrationStagingRetirementDialog } from "./migration-staging-retirement-dialog"
import { formatMigrationDateTime } from "./migration-time"
import { useState } from "react"
import type { LucideIcon } from "lucide-react"
import {
  AlertCircle,
  CheckCircle2,
  Database,
  Loader2,
  Monitor,
  Network,
  RefreshCw,
  RotateCcw,
  Server,
  ShieldCheck,
  Trash2,
  Users,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type { MigrationStagingRun } from "@/features/operator/migrations/api/migration-staging"
import { isMigrationStagingStepUpRequired } from "@/features/operator/migrations/api/migration-staging"
import type { MigrationSessionDetail } from "@/features/operator/migrations/api/migration-sessions"
import {
  useMigrationStagingRuns,
  useStartMigrationStaging,
} from "@/features/operator/migrations/hooks/use-migration-staging"


type PendingAction =
  | { kind: "start"; retryOfStagingRunId?: string }
  | null

export function MigrationStagingWorkspace({ detail }: { detail: MigrationSessionDetail }) {
  const { intlLocale, t } = useI18n()
  const migrationId = detail.session.migrationId
  const stagingAvailable = detail.session.phase === "staging"
  const runsQuery = useMigrationStagingRuns(migrationId, stagingAvailable)
  const startMutation = useStartMigrationStaging(migrationId)
  const [retirementRunId, setRetirementRunId] = useState<string | null>(null)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [pendingAction, setPendingAction] = useState<PendingAction>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  const runs = runsQuery.data ?? []
  const latestRun = runs[0]
  const activeRun = runs.find((run) =>
    ["pending", "running", "retiring", "retirement-needs-attention"].includes(run.status) || (run.retirementReviewAvailable && !run.destroyedAtUtc),
  )
  const retrySource = !activeRun && latestRun &&
    ["failed", "failed-cleaned", "destroyed"].includes(latestRun.status)
      ? latestRun
      : null
  const actionPending = startMutation.isPending

  const runStart = async (retryOfStagingRunId?: string) => {
    try {
      setActionError(null)
      await startMutation.mutateAsync(retryOfStagingRunId)
      setPendingAction(null)
    } catch (caught) {
      if (isMigrationStagingStepUpRequired(caught)) {
        setPendingAction({ kind: "start", retryOfStagingRunId })
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  const resumePendingAction = () => {
    const action = pendingAction
    setStepUpOpen(false)
    if (action?.kind === "start") {
      void runStart(action.retryOfStagingRunId)
    }
  }

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2">
              <Server className="h-5 w-5" aria-hidden="true" />
              {t("migrationWorkspace.staging.title")}
            </CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("migrationWorkspace.staging.description")}
            </p>
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={() => void runsQuery.refetch()}
            disabled={!stagingAvailable || runsQuery.isFetching}
          >
            <RefreshCw
              className={runsQuery.isFetching ? "mr-2 h-4 w-4 animate-spin" : "mr-2 h-4 w-4"}
              aria-hidden="true"
            />
            {t("migrationWorkspace.refresh")}
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-5">
        {!stagingAvailable ? (
          <Alert>
            <AlertCircle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.staging.notReadyTitle")}</AlertTitle>
            <AlertDescription>{t("migrationWorkspace.staging.notReadyDescription")}</AlertDescription>
          </Alert>
        ) : null}

        {runsQuery.isError ? (
          <Alert variant="destructive">
            <AlertCircle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.staging.loadErrorTitle")}</AlertTitle>
            <AlertDescription>{t("migrationWorkspace.staging.loadErrorDescription")}</AlertDescription>
          </Alert>
        ) : null}

        {actionError ? (
          <Alert variant="destructive">
            <AlertCircle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.staging.actionErrorTitle")}</AlertTitle>
            <AlertDescription>{actionError}</AlertDescription>
          </Alert>
        ) : null}

        {stagingAvailable ? (
          <div className="flex flex-wrap items-center gap-3">
            <Button
              onClick={() => void runStart(retrySource?.stagingRunId)}
              disabled={Boolean(activeRun) || actionPending}
            >
              {actionPending ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" />
              ) : retrySource ? (
                <RotateCcw className="mr-2 h-4 w-4" aria-hidden="true" />
              ) : (
                <Server className="mr-2 h-4 w-4" aria-hidden="true" />
              )}
              {retrySource
                ? t("migrationWorkspace.staging.retry")
                : t("migrationWorkspace.staging.start")}
            </Button>
            <p className="text-sm text-muted-foreground">
              {activeRun
                ? t("migrationWorkspace.staging.activeDescription")
                : retrySource
                  ? t("migrationWorkspace.staging.retryDescription")
                  : t("migrationWorkspace.staging.startDescription")}
            </p>
          </div>
        ) : null}

        {runsQuery.isLoading && stagingAvailable ? (
          <div className="flex items-center gap-2 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
            {t("migrationWorkspace.staging.loading")}
          </div>
        ) : runs.length === 0 && stagingAvailable ? (
          <div className="rounded-lg border border-dashed p-4 text-sm text-muted-foreground">
            {t("migrationWorkspace.staging.empty")}
          </div>
        ) : (
          <div className="space-y-4">
            {runs.map((run) => (
              <StagingRunCard
                key={run.stagingRunId}
                run={run}
                locale={intlLocale}
                reviewDisabled={runsQuery.isError}
                onReview={setRetirementRunId}
              />
            ))}
          </div>
        )}

        {runs.some((run) => run.status === "pending" || run.status === "running") ? (
          <p className="text-sm text-muted-foreground">
            {t("migrationWorkspace.staging.refreshSafe")}
          </p>
        ) : null}

        <Alert>
          <ShieldCheck className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.staging.boundaryTitle")}</AlertTitle>
          <AlertDescription>{t("migrationWorkspace.staging.boundaryDescription")}</AlertDescription>
        </Alert>
      </CardContent>

      {retirementRunId && <MigrationStagingRetirementDialog key={retirementRunId}
        target={{ migrationId, stagingRunId: retirementRunId }} onClose={() => setRetirementRunId(null)} />}
      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={setStepUpOpen}
        onVerified={resumePendingAction}
      />
    </Card>
  )
}

function StagingRunCard({
  run,
  locale,
  reviewDisabled,
  onReview,
}: {
  run: MigrationStagingRun
  locale: string
  reviewDisabled: boolean
  onReview: (stagingRunId: string) => void
}) {
  const { t } = useI18n()
  const privateProofPassed = run.privateOnly && !run.publicRoutesCreated
  const elementRuntimePassed = run.elementContainerStarted &&
    run.elementHealthPassed &&
    run.elementSynapseConnectivityPassed &&
    run.elementNetworkAttached

  return (
    <div className="rounded-lg border p-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-mono text-sm">{run.stagingRunId}</span>
            <Badge variant={run.status.startsWith("failed") ? "destructive" : "outline"}>
              {stagingStatusLabel(t, run.status)}
            </Badge>
          </div>
          <p className="mt-2 text-sm text-muted-foreground">
            {t("migrationWorkspace.staging.currentStep")}: {run.currentStep}
          </p>
        </div>
        {run.retirementReviewAvailable ? (
          <Button
            size="sm"
            variant="outline"
            disabled={reviewDisabled}
            onClick={() => onReview(run.stagingRunId)}
          >
            <Trash2 className="mr-2 h-4 w-4" aria-hidden="true" />
            {t("migrationRetirement.review")}
          </Button>
        ) : null}
      </div>

      <div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Fact label={t("migrationWorkspace.staging.candidateId")} value={run.candidateArtifactId} mono />
        <Fact label={t("migrationWorkspace.staging.matrixServerName")} value={run.matrixServerName} />
        <Fact label={t("migrationWorkspace.staging.startedAt")} value={formatDate(run.startedAtUtc, locale)} />
        <Fact label={t("migrationWorkspace.staging.completedAt")} value={formatDate(run.completedAtUtc, locale)} />
      </div>

      {run.retryOfStagingRunId ? (
        <p className="mt-3 text-xs text-muted-foreground">
          {t("migrationWorkspace.staging.retryOf")}: <span className="font-mono">{run.retryOfStagingRunId}</span>
        </p>
      ) : null}

      <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-5">
        <Evidence
          icon={Network}
          passed={privateProofPassed}
          title={t("migrationWorkspace.staging.privateProofTitle")}
          description={privateProofPassed
            ? t("migrationWorkspace.staging.privateProofPassed")
            : t("migrationWorkspace.staging.privateProofFailed")}
        />
        <Evidence
          icon={Database}
          passed={run.databaseImportSucceeded}
          title={t("migrationWorkspace.staging.databaseTitle")}
          description={run.databaseImportSucceeded
            ? t("migrationWorkspace.staging.databasePassed")
            : t("migrationWorkspace.staging.databasePending")}
        />
        <Evidence
          icon={Server}
          passed={run.synapseHealthPassed}
          title={t("migrationWorkspace.staging.synapseTitle")}
          description={run.synapseHealthPassed
            ? t("migrationWorkspace.staging.synapsePassed")
            : t("migrationWorkspace.staging.synapsePending")}
        />
        <Evidence
          icon={ShieldCheck}
          passed={run.elementConfigPresent && Boolean(run.elementConfigSha256)}
          title={t("migrationWorkspace.staging.elementTitle")}
          description={run.elementConfigPresent && run.elementConfigSha256
            ? t("migrationWorkspace.staging.elementPassed")
            : t("migrationWorkspace.staging.elementPending")}
        />
        <Evidence
          icon={Monitor}
          passed={elementRuntimePassed}
          title={t("migrationWorkspace.staging.elementRuntimeTitle")}
          description={elementRuntimePassed
            ? t("migrationWorkspace.staging.elementRuntimePassed")
            : t("migrationWorkspace.staging.elementRuntimePending")}
        />
      </div>

      <div className="mt-4 grid gap-3 sm:grid-cols-3">
        <Fact label={t("migrationWorkspace.staging.elementConfigHash")} value={run.elementConfigSha256} mono />
        <Fact label={t("migrationWorkspace.staging.synapseImage")} value={run.synapseImageReference} mono />
        <Fact label={t("migrationWorkspace.staging.elementImage")} value={run.elementImageReference} mono />
      </div>

      <div className="mt-4 grid gap-3 sm:grid-cols-3">
        <Count icon={Users} label={t("migrationWorkspace.staging.users")} value={run.usersCount} />
        <Count icon={Server} label={t("migrationWorkspace.staging.rooms")} value={run.roomsCount} />
        <Count icon={Database} label={t("migrationWorkspace.staging.events")} value={run.eventsCount} />
      </div>

      {run.failureSummary ? (
        <Alert variant="destructive" className="mt-4">
          <AlertCircle className="h-4 w-4" />
          <AlertTitle>{run.failureCode ?? t("migrationWorkspace.staging.failed")}</AlertTitle>
          <AlertDescription>{run.failureSummary}</AlertDescription>
        </Alert>
      ) : null}

      {run.destroyedAtUtc ? (
        <p className="mt-4 text-sm text-muted-foreground">
          {t("migrationWorkspace.staging.destroyedAt")}: {formatDate(run.destroyedAtUtc, locale)}
        </p>
      ) : null}
    </div>
  )
}

function Evidence({
  icon: Icon,
  passed,
  title,
  description,
}: {
  icon: LucideIcon
  passed: boolean
  title: string
  description: string
}) {
  return (
    <div className="rounded-lg border p-3">
      <div className="flex items-center gap-2">
        {passed ? (
          <CheckCircle2 className="h-4 w-4 text-emerald-500" aria-hidden="true" />
        ) : (
          <Icon className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
        )}
        <span className="text-sm font-medium">{title}</span>
      </div>
      <p className="mt-2 text-xs text-muted-foreground">{description}</p>
    </div>
  )
}

function Count({ icon: Icon, label, value }: { icon: LucideIcon; label: string; value: number | null }) {
  return (
    <div className="flex items-center gap-3 rounded-lg border p-3">
      <Icon className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
      <div>
        <div className="text-xs text-muted-foreground">{label}</div>
        <div className="font-medium">{value ?? "—"}</div>
      </div>
    </div>
  )
}

function Fact({ label, value, mono = false }: { label: string; value: string | null; mono?: boolean }) {
  return (
    <div>
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className={mono ? "mt-1 break-all font-mono text-xs" : "mt-1 text-sm"}>{value || "—"}</div>
    </div>
  )
}

function formatDate(value: string | null, locale: string) {
  return formatMigrationDateTime(value, locale)
}

function stagingStatusLabel(t: ReturnType<typeof useI18n>["t"], status: string) {
  switch (status) {
    case "running": return t("migrationWorkspace.staging.status.running")
    case "verified": return t("migrationWorkspace.staging.status.verified")
    case "failed": return t("migrationWorkspace.staging.status.failed")
    case "failed-cleaned": return t("migrationWorkspace.staging.status.failedCleaned")
    case "retiring": return t("migrationRetirement.inProgress")
    case "retirement-needs-attention": return t("migrationRetirement.needsAttention")
    case "destroyed": return t("migrationWorkspace.staging.status.destroyed")
    default: return status
  }
}
