import { formatMigrationDateTime } from "./migration-time"
import {
  Archive,
  CheckCircle2,
  DatabaseBackup,
  Download,
  Loader2,
  RefreshCw,
  Server,
  ShieldAlert,
  ShieldCheck,
  TriangleAlert,
} from "lucide-react"
import { useMemo, useState } from "react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import {
  downloadMigrationAcceptanceCompletionReport,
  isMigrationAcceptanceStepUpRequired,
  type AcceptMigrationRequest,
  type MigrationAcceptanceState,
} from "@/features/operator/migrations/api/migration-acceptance"
import { isSimplifiedMigrationAssurance } from "@/features/operator/migrations/api/migration-assurance"
import type { MigrationProductionAdoptionPlan } from "@/features/operator/migrations/api/migration-production-adoption"
import {
  useAcceptMigration,
  useMigrationAcceptanceState,
  useRetryMigrationBaselineBackup,
} from "@/features/operator/migrations/hooks/use-migration-acceptance"
import { useMigrationProductionAdoptionState } from "@/features/operator/migrations/hooks/use-migration-production-adoption"

const acknowledgementKeys = [
  "acknowledgeFreshPublicVerification",
  "acknowledgeTargetWriteDivergence",
  "acknowledgeRollbackBoundaryChanges",
  "acknowledgeLegacySourceResourcesRetained",
  "acknowledgeNoAutomaticLegacyDeletion",
] as const

type AcknowledgementKey = typeof acknowledgementKeys[number]
type Acknowledgements = Record<AcknowledgementKey, boolean>

const initialAcknowledgements: Acknowledgements = {
  acknowledgeFreshPublicVerification: false,
  acknowledgeTargetWriteDivergence: false,
  acknowledgeRollbackBoundaryChanges: false,
  acknowledgeLegacySourceResourcesRetained: false,
  acknowledgeNoAutomaticLegacyDeletion: false,
}

export function MigrationAcceptanceWorkspace({ migrationId }: { migrationId: string }) {
  const { intlLocale, t } = useI18n()
  const stateQuery = useMigrationAcceptanceState(migrationId)
  const adoptionQuery = useMigrationProductionAdoptionState(migrationId)
  const acceptMutation = useAcceptMigration(migrationId)
  const retryBaselineMutation = useRetryMigrationBaselineBackup(migrationId)
  const [retentionDays, setRetentionDays] = useState(14)
  const [acknowledgements, setAcknowledgements] = useState<Acknowledgements>(initialAcknowledgements)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [pendingAction, setPendingAction] = useState<"accept" | "retry-baseline" | "download-report" | null>(null)
  const [reportPending, setReportPending] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const allAcknowledged = useMemo(
    () => acknowledgementKeys.every((key) => acknowledgements[key]),
    [acknowledgements],
  )

  const submit = async () => {
    setActionError(null)
    const request: AcceptMigrationRequest = {
      note: null,
      retentionDays,
      ...acknowledgements,
    }
    try {
      await acceptMutation.mutateAsync(request)
    } catch (caught) {
      if (isMigrationAcceptanceStepUpRequired(caught)) {
        setPendingAction("accept")
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  const retryBaseline = async () => {
    setActionError(null)
    try {
      await retryBaselineMutation.mutateAsync()
    } catch (caught) {
      if (isMigrationAcceptanceStepUpRequired(caught)) {
        setPendingAction("retry-baseline")
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  const downloadCompletionReport = async () => {
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

  if (stateQuery.isLoading) {
    return (
      <Card aria-label={t("migrationWorkspace.acceptance.loading")}>
        <CardContent className="flex items-center gap-2 py-6 text-sm text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
          {t("migrationWorkspace.acceptance.loading")}
        </CardContent>
      </Card>
    )
  }

  if (stateQuery.isError || !stateQuery.data) {
    return (
      <Card>
        <CardHeader><CardTitle>{t("migrationWorkspace.acceptance.title")}</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          <Alert variant="destructive">
            <TriangleAlert className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.acceptance.loadFailed")}</AlertTitle>
            <AlertDescription>{stateQuery.error instanceof Error ? stateQuery.error.message : t("migrationWorkspace.acceptance.loadFailedDescription")}</AlertDescription>
          </Alert>
          <Button variant="outline" onClick={() => void stateQuery.refetch()}>
            <RefreshCw className="mr-2 h-4 w-4" aria-hidden="true" />
            {t("migrationWorkspace.refresh")}
          </Button>
        </CardContent>
      </Card>
    )
  }

  const state = stateQuery.data
  const plan = adoptionQuery.data?.plan ?? null
  return (
    <Card>
      <CardHeader>
        <div className="flex items-start justify-between gap-3">
          <div>
            <CardTitle className="flex items-center gap-2">
              <Archive className="h-5 w-5" aria-hidden="true" />
              {t("migrationWorkspace.acceptance.title")}
            </CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">{t("migrationWorkspace.acceptance.description")}</p>
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              void stateQuery.refetch()
              void adoptionQuery.refetch()
            }}
            disabled={stateQuery.isFetching || adoptionQuery.isFetching}
          >
            <RefreshCw className={stateQuery.isFetching || adoptionQuery.isFetching ? "mr-2 h-4 w-4 animate-spin" : "mr-2 h-4 w-4"} aria-hidden="true" />
            {t("migrationWorkspace.refresh")}
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {actionError ? (
          <Alert variant="destructive"><TriangleAlert className="h-4 w-4" /><AlertTitle>{t("migrationWorkspace.acceptance.actionFailed")}</AlertTitle><AlertDescription>{actionError}</AlertDescription></Alert>
        ) : null}

        <AssuranceEvidence assurance={state.assurance} />

        {state.accepted ? (
          <AcceptedEvidence
            state={state}
            plan={plan}
            locale={intlLocale}
            retryPending={retryBaselineMutation.isPending}
            reportPending={reportPending}
            onRetry={() => void retryBaseline()}
            onDownloadReport={() => void downloadCompletionReport()}
          />
        ) : (
          <>
            <Alert variant={state.acceptanceEligible ? "default" : "destructive"}>
              {state.acceptanceEligible ? <CheckCircle2 className="h-4 w-4" /> : <ShieldAlert className="h-4 w-4" />}
              <AlertTitle>{state.acceptanceEligible ? t("migrationWorkspace.acceptance.ready") : t("migrationWorkspace.acceptance.blocked")}</AlertTitle>
              <AlertDescription>{state.detail}</AlertDescription>
            </Alert>

            <VerificationAuthority plan={plan} locale={intlLocale} />

            {state.blockers.length ? <MessageList title={t("migrationWorkspace.acceptance.blockers")} values={state.blockers} /> : null}
            {state.warnings.length ? <MessageList title={t("migrationWorkspace.acceptance.warnings")} values={state.warnings} /> : null}

            <div className="rounded-lg border p-4">
              <label className="text-sm font-medium" htmlFor="migration-retention-days">{t("migrationWorkspace.acceptance.retentionDays")}</label>
              <select
                id="migration-retention-days"
                className="mt-2 block rounded-md border bg-background px-3 py-2 text-sm"
                value={retentionDays}
                disabled={!state.acceptanceEligible || acceptMutation.isPending}
                onChange={(event) => setRetentionDays(Number(event.target.value))}
              >
                {[7, 14, 21, 30].map((days) => <option key={days} value={days}>{t("migrationWorkspace.acceptance.days", { count: days })}</option>)}
              </select>
            </div>

            <Acknowledgements values={acknowledgements} disabled={!state.acceptanceEligible || acceptMutation.isPending} onChange={(key, checked) => setAcknowledgements((current) => ({ ...current, [key]: checked }))} />

            <Button
              variant="destructive"
              disabled={!state.acceptanceEligible || !allAcknowledged || acceptMutation.isPending}
              onClick={() => void submit()}
            >
              {acceptMutation.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" /> : null}
              {t("migrationWorkspace.acceptance.accept")}
            </Button>
          </>
        )}

        <OperatorStepUpDialog
          open={stepUpOpen}
          onOpenChange={setStepUpOpen}
          onVerified={() => {
            setStepUpOpen(false)
            const action = pendingAction
            setPendingAction(null)
            if (action === "retry-baseline") void retryBaseline()
            else if (action === "download-report") void downloadCompletionReport()
            else void submit()
          }}
        />
      </CardContent>
    </Card>
  )
}

function AssuranceEvidence({ assurance }: { assurance: MigrationAcceptanceState["assurance"] }) {
  const { t } = useI18n()
  if (!assurance) return null
  const simplified = isSimplifiedMigrationAssurance(assurance)
  return (
    <div className="space-y-3 rounded-lg border p-4">
      <div className="flex items-center gap-2 font-medium">
        <ShieldCheck className="h-4 w-4" aria-hidden="true" />
        {t("migrationWorkspace.acceptance.assuranceTitle")}
      </div>
      <Alert variant={simplified ? "default" : "default"}>
        <ShieldCheck className="h-4 w-4" />
        <AlertTitle>{simplified
          ? t("migrationWorkspace.acceptance.assuranceSimplified")
          : t("migrationWorkspace.acceptance.assuranceHigh")}</AlertTitle>
        <AlertDescription>{assurance.detail}</AlertDescription>
      </Alert>
      <Evidence rows={[
        [t("migrationWorkspace.acceptance.authorityType"), assurance.authorityType],
        [t("migrationWorkspace.acceptance.productionAuthorityId"), assurance.productionAuthorityId ?? "—"],
        [t("migrationWorkspace.acceptance.packageRevisionId"), assurance.packageRevisionId],
        [t("migrationWorkspace.acceptance.sourceFreezeEvidence"), yesNo(assurance.formalSourceFreezeEvidenceCollected, t("common.yes"), t("common.no"))],
        [t("migrationWorkspace.acceptance.finalRecapture"), yesNo(assurance.finalRecapturePerformed, t("common.yes"), t("common.no"))],
        [t("migrationWorkspace.acceptance.postCaptureWritesExcluded"), yesNo(assurance.postCaptureWritesIndependentlyExcluded, t("common.yes"), t("common.no"))],
        [t("migrationWorkspace.acceptance.rollbackAssurance"), assurance.rollbackAssurance],
      ]} />
    </div>
  )
}

function VerificationAuthority({ plan, locale }: { plan: MigrationProductionAdoptionPlan | null; locale: string }) {
  const { t } = useI18n()
  if (!plan) {
    return (
      <div className="rounded-lg border p-4 text-sm text-muted-foreground">
        {t("migrationWorkspace.acceptance.verificationUnavailable")}
      </div>
    )
  }

  const verification = plan.productionVerification
  const passed = verification.passed
  return (
    <div className="space-y-3 rounded-lg border p-4">
      <div className="flex items-center gap-2 font-medium">
        <ShieldCheck className="h-4 w-4" aria-hidden="true" />
        {t("migrationWorkspace.acceptance.verificationAuthority")}
      </div>
      <Alert variant={passed ? "default" : "destructive"}>
        {passed ? <CheckCircle2 className="h-4 w-4" /> : <ShieldAlert className="h-4 w-4" />}
        <AlertTitle>{passed ? t("migrationWorkspace.acceptance.verificationFresh") : t("migrationWorkspace.acceptance.verificationNotFresh")}</AlertTitle>
        <AlertDescription>{verification.failureSummary ?? t("migrationWorkspace.acceptance.verificationAuthorityDetail")}</AlertDescription>
      </Alert>
      <Evidence rows={[
        [t("migrationWorkspace.acceptance.verificationId"), verification.verificationId ?? "—"],
        [t("migrationWorkspace.acceptance.runtimeStackId"), plan.runtimeStackId],
        [t("migrationWorkspace.acceptance.targetStack"), plan.targetStackSlug],
        [t("migrationWorkspace.acceptance.verificationChecks"), `${verification.checkCount - verification.failedCheckCount}/${verification.checkCount}`],
        [t("migrationWorkspace.acceptance.verificationCompleted"), formatDate(verification.completedAtUtc, locale)],
        [t("migrationWorkspace.acceptance.verificationEvidence"), verification.evidenceSha256 ?? "—"],
        [t("migrationWorkspace.acceptance.readinessReportId"), verification.readinessReportId ?? "—"],
      ]} />
    </div>
  )
}

function Acknowledgements({ values, disabled, onChange }: { values: Acknowledgements; disabled: boolean; onChange: (key: AcknowledgementKey, checked: boolean) => void }) {
  const { t } = useI18n()
  const labels: Record<AcknowledgementKey, string> = {
    acknowledgeFreshPublicVerification: t("migrationWorkspace.acceptance.ackVerification"),
    acknowledgeTargetWriteDivergence: t("migrationWorkspace.acceptance.ackDivergence"),
    acknowledgeRollbackBoundaryChanges: t("migrationWorkspace.acceptance.ackRollback"),
    acknowledgeLegacySourceResourcesRetained: t("migrationWorkspace.acceptance.ackRetention"),
    acknowledgeNoAutomaticLegacyDeletion: t("migrationWorkspace.acceptance.ackNoDeletion"),
  }
  return (
    <div className="space-y-2 rounded-lg border border-destructive/40 p-4">
      {acknowledgementKeys.map((key) => (
        <label key={key} className="flex items-start gap-2 text-sm">
          <input type="checkbox" className="mt-1" checked={values[key]} disabled={disabled} onChange={(event) => onChange(key, event.target.checked)} />
          <span>{labels[key]}</span>
        </label>
      ))}
    </div>
  )
}

function AcceptedEvidence({ state, plan, locale, retryPending, reportPending, onRetry, onDownloadReport }: { state: MigrationAcceptanceState; plan: MigrationProductionAdoptionPlan | null; locale: string; retryPending: boolean; reportPending: boolean; onRetry: () => void; onDownloadReport: () => void }) {
  const { t } = useI18n()
  const acceptance = state.acceptance!
  const retention = state.legacyRetention!
  const baseline = state.baselineBackup
  const targetStackSlug = plan?.targetStackSlug ?? baseline?.targetStackSlug ?? null
  const runtimeStackId = plan?.runtimeStackId ?? baseline?.privateRuntimeId ?? null
  const runtimeLinkIdentity = targetStackSlug ?? runtimeStackId

  return (
    <div className="space-y-4">
      <Alert><CheckCircle2 className="h-4 w-4" /><AlertTitle>{t("migrationWorkspace.acceptance.accepted")}</AlertTitle><AlertDescription>{state.detail}</AlertDescription></Alert>

      <div className="space-y-3 rounded-lg border p-4">
        <div className="flex items-center gap-2 font-medium">
          <Server className="h-4 w-4" aria-hidden="true" />
          {t("migrationWorkspace.acceptance.lifecycleTitle")}
        </div>
        <Evidence rows={[
          [t("migrationWorkspace.acceptance.acceptanceId"), acceptance.acceptanceId],
          [t("migrationWorkspace.acceptance.acceptedAt"), formatDate(acceptance.acceptedAtUtc, locale)],
          [t("migrationWorkspace.acceptance.acceptedBy"), acceptance.acceptedBy],
          [t("migrationWorkspace.acceptance.runtimeStackId"), runtimeStackId ?? "—"],
          [t("migrationWorkspace.acceptance.targetStack"), targetStackSlug ?? "—"],
          [t("migrationWorkspace.acceptance.verification"), acceptance.publicVerification.status],
          [t("migrationWorkspace.acceptance.verificationChecks"), `${acceptance.publicVerification.checkCount - acceptance.publicVerification.failedCheckCount}/${acceptance.publicVerification.checkCount}`],
          [t("migrationWorkspace.acceptance.verificationEvidence"), acceptance.publicVerification.evidenceSha256 ?? "—"],
        ]} />
        {runtimeLinkIdentity ? (
          <Button asChild variant="outline">
            <a href={`/stacks/${encodeURIComponent(runtimeLinkIdentity)}`}>
              {t("migrationWorkspace.acceptance.openRuntimeStack")}
            </a>
          </Button>
        ) : null}
      </div>

      <div className="space-y-3 rounded-lg border p-4">
        <div className="flex items-center gap-2 font-medium">
          <Archive className="h-4 w-4" aria-hidden="true" />
          {t("migrationWorkspace.acceptance.retentionTitle")}
        </div>
        <Evidence rows={[
          [t("migrationWorkspace.acceptance.retentionRecordId"), retention.retentionRecordId],
          [t("migrationWorkspace.acceptance.retentionUntil"), formatDate(retention.retainUntilUtc, locale)],
          [t("migrationWorkspace.acceptance.sourcePackageRetained"), yesNo(retention.sourcePackageRetained, t("common.yes"), t("common.no"))],
          [t("migrationWorkspace.acceptance.candidateRetained"), yesNo(retention.candidateArtifactRetained, t("common.yes"), t("common.no"))],
          [t("migrationWorkspace.acceptance.stagingEvidenceRetained"), yesNo(retention.privateStagingEvidenceRetained, t("common.yes"), t("common.no"))],
          [t("migrationWorkspace.acceptance.sourceResourcesRetained"), yesNo(retention.legacySourceResourcesRetained, t("common.yes"), t("common.no"))],
          [t("migrationWorkspace.acceptance.automaticDeletion"), yesNo(retention.automaticDeletionAllowed, t("common.yes"), t("common.no"))],
        ]} />
        <p className="text-sm text-muted-foreground">{retention.summary}</p>
      </div>

      <div className="space-y-3 rounded-lg border p-4">
        <div className="flex items-center gap-2 font-medium">
          <DatabaseBackup className="h-4 w-4" aria-hidden="true" />
          {t("migrationWorkspace.acceptance.baselineTitle")}
        </div>
        {!baseline || baseline.status === "pending" ? (
          <>
            <Alert>
              <Loader2 className="h-4 w-4 animate-spin" />
              <AlertTitle>{t("migrationWorkspace.acceptance.baselinePending")}</AlertTitle>
              <AlertDescription>{baseline?.detail ?? t("migrationWorkspace.acceptance.baselinePendingDetail")}</AlertDescription>
            </Alert>
            <Button variant="outline" disabled={retryPending} onClick={onRetry}>
              {retryPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <RefreshCw className="mr-2 h-4 w-4" />}
              {baseline ? t("migrationWorkspace.acceptance.continueBaseline") : t("migrationWorkspace.acceptance.createBaseline")}
            </Button>
          </>
        ) : baseline.status === "failed" ? (
          <>
            <Alert variant="destructive">
              <TriangleAlert className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.acceptance.baselineFailed")}</AlertTitle>
              <AlertDescription>{baseline.failureSummary ?? baseline.detail}</AlertDescription>
            </Alert>
            <Evidence rows={[
              [t("migrationWorkspace.acceptance.baselineHandoffId"), baseline.handoffId],
              [t("migrationWorkspace.acceptance.baselineAttempts"), baseline.attemptCount.toString()],
              [t("migrationWorkspace.acceptance.baselineFailureCode"), baseline.failureCode ?? "—"],
            ]} />
            <Button variant="outline" disabled={retryPending} onClick={onRetry}>
              {retryPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <RefreshCw className="mr-2 h-4 w-4" />}
              {t("migrationWorkspace.acceptance.retryBaseline")}
            </Button>
          </>
        ) : (
          <>
            <Alert>
              <CheckCircle2 className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.acceptance.baselineCreated")}</AlertTitle>
              <AlertDescription>{baseline.detail}</AlertDescription>
            </Alert>
            <Evidence rows={[
              [t("migrationWorkspace.acceptance.baselineHandoffId"), baseline.handoffId],
              [t("migrationWorkspace.acceptance.baselineBackupId"), baseline.backupId ?? "—"],
              [t("migrationWorkspace.acceptance.baselineCatalogId"), baseline.catalogEntryId ?? "—"],
              [t("migrationWorkspace.acceptance.baselineCreatedAt"), formatDate(baseline.backupCreatedAtUtc, locale)],
              [t("migrationWorkspace.acceptance.baselineFiles"), baseline.backupTotalFiles?.toString() ?? "—"],
              [t("migrationWorkspace.acceptance.baselineWarnings"), baseline.backupWarningCount?.toString() ?? "—"],
            ]} />
            <div className="flex flex-wrap gap-2">
              {baseline.catalogEntryId ? (
                <Button asChild variant="outline">
                  <a href={`/backups/catalog/${encodeURIComponent(baseline.catalogEntryId)}`}>
                    {t("migrationWorkspace.acceptance.openBaselineBackup")}
                  </a>
                </Button>
              ) : null}
              {runtimeLinkIdentity ? (
                <Button asChild variant="outline">
                  <a href={`/stacks/${encodeURIComponent(runtimeLinkIdentity)}`}>
                    {t("migrationWorkspace.acceptance.openRuntimeStack")}
                  </a>
                </Button>
              ) : null}
              <Button onClick={onDownloadReport} disabled={reportPending}>
                {reportPending
                  ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" />
                  : <Download className="mr-2 h-4 w-4" aria-hidden="true" />}
                {t("migrationWorkspace.acceptance.downloadCompletionReport")}
              </Button>
            </div>
          </>
        )}
      </div>

      {state.warnings.length ? <MessageList title={t("migrationWorkspace.acceptance.warnings")} values={state.warnings} /> : null}
    </div>
  )
}

function Evidence({ rows }: { rows: [string, string][] }) {
  return <dl className="grid gap-2 rounded-md border p-3 text-sm sm:grid-cols-2">{rows.map(([label, value]) => <div key={label} className="min-w-0"><dt className="text-xs text-muted-foreground">{label}</dt><dd className="break-all font-mono text-xs">{value}</dd></div>)}</dl>
}

function MessageList({ title, values }: { title: string; values: string[] }) {
  return <div><p className="text-sm font-medium">{title}</p><ul className="mt-1 list-disc space-y-1 pl-5 text-sm text-muted-foreground">{values.map((value) => <li key={value}>{value}</li>)}</ul></div>
}

function formatDate(value: string | null | undefined, locale: string) {
  return formatMigrationDateTime(value, locale)
}

function yesNo(value: boolean, yes: string, no: string) {
  return value ? yes : no
}
