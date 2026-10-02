import { formatMigrationDateTime, migrationUtcIso } from "./migration-time"
import { finishMigration as finishMigrationRequest, retryMigrationBaselineBackup, getMigrationAcceptanceState } from "../api/migration-acceptance"
import { useMigrationGuidedState, useGuidedMigrationCommand, useGuidedMigrationEvidence } from "./migration-guided-state-context"
import { isMigrationOutcomeUncertain } from "../api/migration-guided-state"
import {
  Archive,
  CheckCircle2,
  DatabaseBackup,
  Download,
  Loader2,
  RadioTower,
  RefreshCw,
  Server,
  ShieldCheck,
  TriangleAlert,
} from "lucide-react"
import { useRef, useState } from "react"

import { useI18n } from "@/app/i18n/i18n-context"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import {
  downloadMigrationAcceptanceCompletionReport,
  isMigrationAcceptanceStepUpRequired,
  type FinishMigrationRequest,
  type MigrationAcceptanceState,
} from "@/features/operator/migrations/api/migration-acceptance"

type PendingAction = "finish" | "retry-baseline" | "download-report" | null

type Props = {
  migrationId: string
  onChanged: () => Promise<unknown>
}

export function MigrationFinishWorkspace({
  migrationId,
  onChanged,
}: Props) {
  const { intlLocale, t } = useI18n()
  const guide = useMigrationGuidedState()
  const guided = guide.workspace.guided
  const stage = guide.workspace.stages.find((item) => item.code === "finish-migration")!
  const stateQuery = useGuidedMigrationEvidence("baseline-backup", `acceptance:${guided.operationRevisions["finish-migration"]}`, () => getMigrationAcceptanceState(migrationId))
  const finishMutation = useGuidedMigrationCommand("finish-migration", (request: FinishMigrationRequest) => finishMigrationRequest(migrationId, request))
  const retryBaselineMutation = useGuidedMigrationCommand("baseline-backup", (_input: undefined) => retryMigrationBaselineBackup(migrationId))
  const [retentionDays, setRetentionDays] = useState(14)
  const finishing = useRef(false)
  const [finishPending, setFinishPending] = useState(false)
  const [pendingFinishRequest, setPendingFinishRequest] = useState<FinishMigrationRequest | null>(null)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [pendingAction, setPendingAction] = useState<PendingAction>(null)
  const [reportPending, setReportPending] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

  function confirmedRequest(): FinishMigrationRequest {
    return {
      retentionDays,
      confirmVerifiedServerIsAuthoritative: true,
      confirmRecoveryBoundaryChanges: true,
      confirmRetainOldServerAndNoAutomaticDeletion: true,
    }
  }

  async function finishMigration(request: FinishMigrationRequest) {
    if (finishing.current || !guide.allows("finish-migration")) return
    finishing.current = true
    setFinishPending(true)
    try {
      setActionError(null)
      await finishMutation.mutateAsync(request)
      setPendingFinishRequest(null)
    } catch (caught) {
      if (isMigrationOutcomeUncertain(caught)) return
      if (isMigrationAcceptanceStepUpRequired(caught)) {
        // Freeze the exact inline decisions; verification cannot substitute a
        // newly edited retention choice or authorize any other operation.
        setPendingFinishRequest(request)
        setPendingAction("finish")
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    } finally {
      finishing.current = false
      setFinishPending(false)
    }
    await onChanged().catch(() => undefined)
  }

  async function retryBaseline() {
    if (!guide.allows("retry-baseline-backup")) return
    try {
      setActionError(null)
      await retryBaselineMutation.mutateAsync(undefined)
      await onChanged().catch(() => undefined)
    } catch (caught) {
      if (isMigrationOutcomeUncertain(caught)) return
      if (isMigrationAcceptanceStepUpRequired(caught)) {
        setPendingAction("retry-baseline")
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  async function downloadReport() {
    setActionError(null)
    setReportPending(true)
    try {
      const download = await downloadMigrationAcceptanceCompletionReport(migrationId)
      const url = URL.createObjectURL(download.blob)
      const anchor = document.createElement("a")
      anchor.href = url
      anchor.download = download.fileName
      anchor.click()
      URL.revokeObjectURL(url)
    } catch (caught) {
      if (isMigrationOutcomeUncertain(caught)) return
      if (isMigrationAcceptanceStepUpRequired(caught)) {
        setPendingAction("download-report")
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    } finally {
      setReportPending(false)
    }
  }

  const observedState = stateQuery.data
  const state = observedState?.accepted === guided.accepted &&
    (observedState?.acceptance?.acceptanceId ?? null) === guided.acceptanceId &&
    (observedState?.baselineBackup?.status ?? "not-created") === guided.baselineBackupStatus ? observedState : undefined
  const baseline = state?.baselineBackup
  const complete = guided.accepted && guided.baselineBackupStatus === "created"
  const acceptedPending = guided.accepted && ["pending", "not-created"].includes(guided.baselineBackupStatus)
  const acceptedFailed = guided.accepted && guided.baselineBackupStatus === "failed"
  const acceptanceEligible = stage.primaryAction?.code === "finish-migration" && stage.primaryAction.enabled
  const targetStackSlug = guide.workspace.target.stackSlug
  const catalogEntryId = guided.baselineCatalogEntryId

  return (
    <>
      <Card className={complete ? "border-emerald-500/35 bg-emerald-500/[0.025]" : undefined}>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            {complete
              ? <CheckCircle2 className="h-5 w-5" aria-hidden="true" />
              : <Archive className="h-5 w-5" aria-hidden="true" />}
            {complete
              ? t("migrationWorkspace.finish.completedTitle")
              : t("migrationWorkspace.finish.title")}
          </CardTitle>
          <p className="text-sm text-muted-foreground">
            {complete
              ? t("migrationWorkspace.finish.completedDescription")
              : t("migrationWorkspace.finish.description")}
          </p>
        </CardHeader>
        <CardContent className="space-y-5">
          {stateQuery.isError ? <Alert><AlertTitle>{t("migrationWorkspace.guided.evidenceTitle")}</AlertTitle><AlertDescription>{t("migrationWorkspace.guided.evidenceUnavailable")}</AlertDescription></Alert> : null}
          {actionError ? (
            <Alert variant="destructive">
              <TriangleAlert className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.finish.actionErrorTitle")}</AlertTitle>
              <AlertDescription>{actionError}</AlertDescription>
            </Alert>
          ) : null}

          {!guided.accepted ? (
            <>
              <Alert variant={acceptanceEligible ? "default" : "destructive"}>
                {acceptanceEligible
                  ? <ShieldCheck className="h-4 w-4" />
                  : <TriangleAlert className="h-4 w-4" />}
                <AlertTitle>
                  {acceptanceEligible
                    ? t("migrationWorkspace.finish.readyTitle")
                    : t("migrationWorkspace.finish.blockedTitle")}
                </AlertTitle>
                <AlertDescription>
                  {acceptanceEligible
                    ? t("migrationWorkspace.finish.readyDescription")
                    : t("migrationWorkspace.finish.blockedDescription")}
                </AlertDescription>
              </Alert>

              {acceptanceEligible ? (
                <>
                  <div className="rounded-xl border p-4 sm:p-5">
                    <label
                      className="text-sm font-medium"
                      htmlFor="migration-finish-retention-days"
                    >
                      {t("migrationWorkspace.finish.retentionLabel")}
                    </label>
                    <select
                      id="migration-finish-retention-days"
                      className="mt-2 block rounded-md border bg-background px-3 py-2 text-sm"
                      value={retentionDays}
                      disabled={finishPending || stepUpOpen || !guide.allows("finish-migration")}
                      onChange={(event) => setRetentionDays(Number(event.target.value))}
                    >
                      {[7, 14, 21, 30].map((days) => (
                        <option key={days} value={days}>
                          {t("migrationWorkspace.finish.retentionDays", { count: days })}
                        </option>
                      ))}
                    </select>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">
                      {t("migrationWorkspace.finish.retentionHelp")}
                    </p>
                  </div>

                  <div id="migration-finish-decisions" className="space-y-2 rounded-xl border bg-muted/10 p-4 text-sm">
                    <p>{t("migrationWorkspace.finish.boundaryAuthoritative")}</p>
                    <p>{t("migrationWorkspace.finish.boundaryWrites")}</p>
                    <p>{t("migrationWorkspace.finish.boundaryRetention")}</p>
                    <p>{t("migrationWorkspace.handoff.finishDecision")}</p>
                  </div>

                  <Button
                    className="w-full"
                    onClick={() => void finishMigration(confirmedRequest())}
                    aria-describedby="migration-finish-decisions"
                    aria-busy={finishPending}
                    disabled={finishPending || stepUpOpen || !guide.allows("finish-migration")}
                  >
                    {finishPending
                      ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" />
                      : <Archive className="mr-2 h-4 w-4" aria-hidden="true" />}
                    {finishPending
                      ? t("migrationWorkspace.finish.finishing")
                      : t("migrationWorkspace.finish.finish")}
                  </Button>
                </>
              ) : (
                <details className="rounded-lg border bg-muted/10">
                  <summary className="cursor-pointer px-4 py-3 text-sm font-medium">
                    {t("migrationWorkspace.finish.blockers")}
                  </summary>
                  <ul className="list-disc space-y-1 border-t px-8 py-4 text-sm text-muted-foreground">
                    {(state?.blockers ?? []).map((blocker) => <li key={blocker}>{blocker}</li>)}
                  </ul>
                </details>
              )}
            </>
          ) : acceptedPending ? (
            <>
              <Alert>
                <Loader2 className="h-4 w-4 animate-spin" />
                <AlertTitle>{t("migrationWorkspace.finish.acceptedPendingTitle")}</AlertTitle>
                <AlertDescription>{t("migrationWorkspace.finish.acceptedPendingDescription")}</AlertDescription>
              </Alert>
              {state ? <AcceptedSummary state={state} locale={intlLocale} /> : null}
              <Button variant="outline" onClick={() => void guide.refresh().catch(() => undefined)}>
                <RefreshCw className="mr-2 h-4 w-4" aria-hidden="true" />
                {t("migrationWorkspace.guided.checkAgain")}
              </Button>
              {state ? <TechnicalFinishDetails state={state} /> : null}
            </>
          ) : acceptedFailed ? (
            <>
              <Alert variant="destructive">
                <TriangleAlert className="h-4 w-4" />
                <AlertTitle>{t("migrationWorkspace.finish.baselineFailedTitle")}</AlertTitle>
                <AlertDescription>
                  {baseline?.failureSummary ?? t("migrationWorkspace.finish.baselineFailedDescription")}
                </AlertDescription>
              </Alert>
              {state ? <AcceptedSummary state={state} locale={intlLocale} /> : null}
              <Button
                variant="outline"
                onClick={() => void retryBaseline()}
                disabled={retryBaselineMutation.isPending || finishPending || stepUpOpen || !guide.allows("retry-baseline-backup")}
              >
                {retryBaselineMutation.isPending
                  ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" />
                  : <RefreshCw className="mr-2 h-4 w-4" aria-hidden="true" />}
                {retryBaselineMutation.isPending
                  ? t("migrationWorkspace.finish.continuingBaseline")
                  : t("migrationWorkspace.finish.retryBaseline")}
              </Button>
              {state ? <TechnicalFinishDetails state={state} /> : null}
            </>
          ) : complete ? (
            <>
              {state ? <CompletionSummary state={state} locale={intlLocale} /> : null}
              <div className="flex flex-wrap gap-2">
                {targetStackSlug ? (
                  <Button asChild className="h-auto min-h-8 whitespace-normal">
                    <a href={`/stacks/${encodeURIComponent(targetStackSlug)}`}>
                      <Server className="mr-2 h-4 w-4" aria-hidden="true" />
                      {t("migrationWorkspace.finish.openServer")}
                    </a>
                  </Button>
                ) : null}
                {catalogEntryId ? (
                  <Button asChild variant="outline" className="h-auto min-h-8 whitespace-normal">
                    <a href={`/backups/catalog/${encodeURIComponent(catalogEntryId)}`}>
                      <DatabaseBackup className="mr-2 h-4 w-4" aria-hidden="true" />
                      {t("migrationWorkspace.finish.openBackup")}
                    </a>
                  </Button>
                ) : null}
              </div>
              <div className="flex flex-wrap gap-2">
                {targetStackSlug ? (
                  <Button asChild variant="ghost" size="sm" className="h-auto min-h-8 whitespace-normal">
                    <a href={`/stacks/${encodeURIComponent(targetStackSlug)}/services`}>
                      <RadioTower className="mr-2 h-4 w-4" aria-hidden="true" />
                      {t("migrationWorkspace.finish.reviewTurn")}
                    </a>
                  </Button>
                ) : null}
                <Button variant="ghost" size="sm" className="h-auto min-h-8 whitespace-normal"
                  onClick={() => void downloadReport()} disabled={reportPending}>
                  {reportPending
                    ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" />
                    : <Download className="mr-2 h-4 w-4" aria-hidden="true" />}
                  {reportPending
                    ? t("migrationWorkspace.finish.downloading")
                    : t("migrationWorkspace.finish.downloadReport")}
                </Button>
              </div>
              {state ? <TechnicalFinishDetails state={state} /> : null}
            </>
          ) : null}
        </CardContent>
      </Card>

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={(open) => {
          setStepUpOpen(open)
          if (!open) {
            setPendingAction(null)
            setPendingFinishRequest(null)
          }
        }}
        onVerified={() => {
          setStepUpOpen(false)
          const action = pendingAction
          setPendingAction(null)
          const request = pendingFinishRequest
          setPendingFinishRequest(null)
          if (action === "finish" && request) void finishMigration(request)
          if (action === "retry-baseline") void retryBaseline()
          if (action === "download-report") void downloadReport()
        }}
      />
    </>
  )
}

function AcceptedSummary({
  state,
  locale,
}: {
  state: MigrationAcceptanceState
  locale: string
}) {
  const { t } = useI18n()
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      <Fact
        label={t("migrationWorkspace.finish.acceptedStatus")}
        value={t("migrationWorkspace.finish.accepted")}
      />
      <Fact
        label={t("migrationWorkspace.finish.retainedUntil")}
        value={formatDate(state.legacyRetention?.retainUntilUtc, locale)}
      />
    </div>
  )
}

function CompletionSummary({ state, locale }: { state: MigrationAcceptanceState; locale: string }) {
  const { t } = useI18n()
  const retention = state.legacyRetention?.retainUntilUtc
  return (
    <div className="space-y-3">
      <div className="grid gap-3 sm:grid-cols-2">
        <Fact label={t("migrationWorkspace.finish.acceptedStatus")} value={t("migrationWorkspace.finish.accepted")} />
        <Fact label={t("migrationWorkspace.finish.baselineStatus")} value={t("migrationWorkspace.finish.baselineCreated")} />
      </div>
      <div className="rounded-lg border p-3 text-sm">
        <p className="font-medium">
          {t("migrationWorkspace.finish.retainedUntil")}: {" "}
          <time dateTime={migrationUtcIso(retention) ?? undefined} title={retention ?? undefined}>
            {formatDate(retention, locale)}
          </time>
        </p>
        <p className="mt-1 text-xs text-muted-foreground">{t("migrationWorkspace.finish.noAutomaticDeletion")}</p>
      </div>
    </div>
  )
}

function TechnicalFinishDetails({
  state,
}: {
  state: MigrationAcceptanceState
}) {
  const { t } = useI18n()
  const acceptance = state.acceptance
  const retention = state.legacyRetention
  const baseline = state.baselineBackup

  return (
    <details className="rounded-lg border bg-muted/10">
      <summary className="cursor-pointer px-4 py-3 text-sm font-medium">
        {t("migrationWorkspace.finish.technicalDetails")}
      </summary>
      <div className="space-y-4 border-t px-4 py-4">
        {state.accepted && baseline?.status === "created" ? (
          <p className="text-sm text-muted-foreground">{t("migrationWorkspace.finish.completeAlertDescription")}</p>
        ) : null}
        <div className="grid gap-3 sm:grid-cols-2">
          <Fact label={t("migrationWorkspace.acceptance.acceptedAt") + " (UTC)"}
            value={migrationUtcIso(acceptance?.acceptedAtUtc) ?? "—"} mono />
          <Fact label={t("migrationWorkspace.finish.retainedUntil") + " (UTC)"}
            value={migrationUtcIso(retention?.retainUntilUtc) ?? "—"} mono />
          <Fact label={t("migrationWorkspace.acceptance.baselineCreatedAt") + " (UTC)"}
            value={migrationUtcIso(baseline?.backupCreatedAtUtc) ?? "—"} mono />
        </div>
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <Fact
            label={t("migrationWorkspace.finish.acceptanceId")}
            value={acceptance?.acceptanceId ?? "—"}
            mono
          />
          <Fact
            label={t("migrationWorkspace.finish.retentionId")}
            value={retention?.retentionRecordId ?? "—"}
            mono
          />
          <Fact
            label={t("migrationWorkspace.finish.baselineHandoffId")}
            value={baseline?.handoffId ?? "—"}
            mono
          />
          <Fact
            label={t("migrationWorkspace.finish.catalogEntryId")}
            value={baseline?.catalogEntryId ?? "—"}
            mono
          />
        </div>
        {state.warnings.length ? (
          <ul className="list-disc space-y-1 pl-5 text-sm text-muted-foreground">
            {state.warnings.map((warning) => <li key={warning}>{warning}</li>)}
          </ul>
        ) : null}
      </div>
    </details>
  )
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
        : "mt-1 break-words text-sm font-medium"}
      >
        {value}
      </div>
    </div>
  )
}

function formatDate(value: string | null | undefined, locale: string) {
  return formatMigrationDateTime(value, locale)
}
