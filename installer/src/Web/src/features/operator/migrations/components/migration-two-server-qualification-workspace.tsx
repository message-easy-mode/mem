import { formatMigrationDateTime } from "./migration-time"
import {
  AlertTriangle,
  CheckCircle2,
  Container,
  Download,
  Loader2,
  LockKeyhole,
  RefreshCw,
  Route,
  Server,
  ShieldCheck,
  UploadCloud,
} from "lucide-react"
import { useState } from "react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { isSimplifiedMigrationAssurance, type MigrationAssuranceSummary } from "@/features/operator/migrations/api/migration-assurance"
import {
  downloadMigrationTwoServerQualificationClosureReport,
  isMigrationTwoServerQualificationStepUpRequired,
  type MigrationTwoServerHostIdentity,
  type MigrationTwoServerQualification,
  type MigrationTwoServerQualificationClosureEnvelope,
  type MigrationTwoServerQualificationClosureRequest,
  type MigrationTwoServerQualificationClosureRoute,
  type MigrationTwoServerQualificationClosureState,
  type MigrationTwoServerSourceContainerEvidence,
  type MigrationTwoServerSourceEvidenceEnvelope,
} from "@/features/operator/migrations/api/migration-two-server-qualification"
import {
  useCloseMigrationTwoServerQualification,
  useImportMigrationTwoServerSourceEvidence,
  useMigrationTwoServerQualificationClosureState,
  useMigrationTwoServerQualificationState,
} from "@/features/operator/migrations/hooks/use-migration-two-server-qualification"

export function MigrationTwoServerQualificationWorkspace({ migrationId }: { migrationId: string }) {
  const { intlLocale, t } = useI18n()
  const stateQuery = useMigrationTwoServerQualificationState(migrationId)
  const closureQuery = useMigrationTwoServerQualificationClosureState(
    migrationId,
    stateQuery.data?.qualified === true,
  )
  const importMutation = useImportMigrationTwoServerSourceEvidence(migrationId)
  const closeMutation = useCloseMigrationTwoServerQualification(migrationId)
  const [sourceFile, setSourceFile] = useState<File | null>(null)
  const [sourceEnvelope, setSourceEnvelope] = useState<MigrationTwoServerSourceEvidenceEnvelope | null>(null)
  const [reviewError, setReviewError] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [closureNote, setClosureNote] = useState("")
  const [closureAcknowledgements, setClosureAcknowledgements] = useState({
    qualificationEvidenceReviewed: false,
    distinctHostEvidenceReviewed: false,
    normalLifecycleReviewed: false,
    sourceRetentionReviewed: false,
    releaseHandoffAcknowledged: false,
  })
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [stepUpAction, setStepUpAction] = useState<"import" | "close" | "download" | null>(null)
  const [reportPending, setReportPending] = useState(false)

  const reviewFile = async (file: File | null) => {
    setSourceFile(file)
    setSourceEnvelope(null)
    setReviewError(null)
    setActionError(null)
    if (!file) return

    try {
      const parsed = JSON.parse(await file.text()) as unknown
      if (!isSourceEvidenceEnvelope(parsed)) {
        throw new Error(t("migrationWorkspace.twoServerQualification.fileInvalid"))
      }
      setSourceEnvelope(parsed)
    } catch (caught) {
      setReviewError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  const submit = async () => {
    if (!sourceEnvelope) {
      setActionError(t("migrationWorkspace.twoServerQualification.fileRequired"))
      return
    }

    setActionError(null)
    try {
      await importMutation.mutateAsync(sourceEnvelope)
      setStepUpAction(null)
    } catch (caught) {
      if (isMigrationTwoServerQualificationStepUpRequired(caught)) {
        setStepUpAction("import")
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  const closureRequest = (): MigrationTwoServerQualificationClosureRequest => ({
    ...closureAcknowledgements,
    note: closureNote.trim() || null,
  })

  const closeQualification = async () => {
    setActionError(null)
    try {
      await closeMutation.mutateAsync(closureRequest())
      setStepUpAction(null)
    } catch (caught) {
      if (isMigrationTwoServerQualificationStepUpRequired(caught)) {
        setStepUpAction("close")
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  const downloadClosureReport = async () => {
    setActionError(null)
    setReportPending(true)
    try {
      const report = await downloadMigrationTwoServerQualificationClosureReport(migrationId)
      const objectUrl = URL.createObjectURL(report.blob)
      const link = document.createElement("a")
      link.href = objectUrl
      link.download = report.fileName
      document.body.appendChild(link)
      link.click()
      link.remove()
      URL.revokeObjectURL(objectUrl)
      setStepUpAction(null)
    } catch (caught) {
      if (isMigrationTwoServerQualificationStepUpRequired(caught)) {
        setStepUpAction("download")
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    } finally {
      setReportPending(false)
    }
  }

  const finishStepUp = () => {
    setStepUpOpen(false)
    if (stepUpAction === "import") void submit()
    if (stepUpAction === "close") void closeQualification()
    if (stepUpAction === "download") void downloadClosureReport()
  }

  if (stateQuery.isLoading) {
    return (
      <Card aria-label={t("migrationWorkspace.twoServerQualification.loading")}>
        <CardContent className="flex items-center gap-2 py-6 text-sm text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
          {t("migrationWorkspace.twoServerQualification.loading")}
        </CardContent>
      </Card>
    )
  }

  if (stateQuery.isError || !stateQuery.data) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>{t("migrationWorkspace.twoServerQualification.title")}</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.twoServerQualification.loadFailed")}</AlertTitle>
            <AlertDescription>
              {stateQuery.error instanceof Error
                ? stateQuery.error.message
                : t("migrationWorkspace.twoServerQualification.loadFailedDescription")}
            </AlertDescription>
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
  return (
    <Card className="border-violet-500/30 bg-violet-500/[0.02]">
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2">
              <Server className="h-5 w-5 text-violet-400" aria-hidden="true" />
              {t("migrationWorkspace.twoServerQualification.title")}
            </CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("migrationWorkspace.twoServerQualification.description")}
            </p>
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={() => void stateQuery.refetch()}
            disabled={stateQuery.isFetching || importMutation.isPending || closeMutation.isPending || reportPending}
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
          <AlertTitle>{t("migrationWorkspace.twoServerQualification.boundaryTitle")}</AlertTitle>
          <AlertDescription>
            {t("migrationWorkspace.twoServerQualification.boundaryDescription")}
          </AlertDescription>
        </Alert>

        {actionError ? (
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.twoServerQualification.actionFailed")}</AlertTitle>
            <AlertDescription>{actionError}</AlertDescription>
          </Alert>
        ) : null}

        {state.status === "not-applicable-simplified-assurance" && isSimplifiedMigrationAssurance(state.assurance) ? (
          <SimplifiedAssuranceBoundary assurance={state.assurance} detail={state.detail} />
        ) : state.qualified && state.qualification ? (
          <>
            <QualifiedEvidence qualification={state.qualification} locale={intlLocale} />
            <QualificationClosure
              state={closureQuery.data ?? null}
              loading={closureQuery.isLoading}
              fetching={closureQuery.isFetching}
              error={closureQuery.isError
                ? closureQuery.error instanceof Error
                  ? closureQuery.error.message
                  : t("migrationWorkspace.twoServerQualification.closureLoadFailedDescription")
                : null}
              acknowledgements={closureAcknowledgements}
              setAcknowledgement={(key, checked) => setClosureAcknowledgements((current) => ({
                ...current,
                [key]: checked,
              }))}
              note={closureNote}
              setNote={setClosureNote}
              closePending={closeMutation.isPending}
              reportPending={reportPending}
              onRefresh={() => void closureQuery.refetch()}
              onClose={() => void closeQualification()}
              onDownload={() => void downloadClosureReport()}
              locale={intlLocale}
            />
          </>
        ) : (
          <>
            <Alert variant={state.qualificationEligible ? "default" : "destructive"}>
              {state.qualificationEligible
                ? <CheckCircle2 className="h-4 w-4" />
                : <AlertTriangle className="h-4 w-4" />}
              <AlertTitle>
                {state.qualificationEligible
                  ? t("migrationWorkspace.twoServerQualification.ready")
                  : t("migrationWorkspace.twoServerQualification.blocked")}
              </AlertTitle>
              <AlertDescription>{state.detail}</AlertDescription>
            </Alert>

            {state.blockers.length > 0 ? (
              <div className="rounded-lg border border-destructive/30 p-4">
                <div className="font-medium">{t("migrationWorkspace.twoServerQualification.blockers")}</div>
                <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-muted-foreground">
                  {state.blockers.map((blocker) => <li key={blocker}>{blocker}</li>)}
                </ul>
              </div>
            ) : null}

            {state.qualificationEligible ? (
              <div className="space-y-4 rounded-lg border p-4">
                <div>
                  <div className="font-medium">{t("migrationWorkspace.twoServerQualification.importTitle")}</div>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {t("migrationWorkspace.twoServerQualification.importDescription")}
                  </p>
                </div>
                <Input
                  type="file"
                  accept="application/json,.json"
                  aria-label={t("migrationWorkspace.twoServerQualification.chooseFile")}
                  disabled={importMutation.isPending}
                  onChange={(event) => void reviewFile(event.target.files?.[0] ?? null)}
                />
                {reviewError ? (
                  <Alert variant="destructive">
                    <AlertTriangle className="h-4 w-4" />
                    <AlertTitle>{t("migrationWorkspace.twoServerQualification.fileInvalidTitle")}</AlertTitle>
                    <AlertDescription>{reviewError}</AlertDescription>
                  </Alert>
                ) : null}
                {sourceEnvelope ? (
                  <SourceEvidenceReview envelope={sourceEnvelope} fileName={sourceFile?.name ?? "—"} locale={intlLocale} />
                ) : null}
                <Button
                  disabled={!sourceEnvelope || importMutation.isPending}
                  onClick={() => void submit()}
                >
                  {importMutation.isPending
                    ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" />
                    : <UploadCloud className="mr-2 h-4 w-4" aria-hidden="true" />}
                  {t("migrationWorkspace.twoServerQualification.importAction")}
                </Button>
              </div>
            ) : null}
          </>
        )}

        <p className="text-xs text-muted-foreground">
          {t("migrationWorkspace.twoServerQualification.refreshSafe")}
        </p>

        <OperatorStepUpDialog
          open={stepUpOpen}
          onOpenChange={setStepUpOpen}
          onVerified={finishStepUp}
        />
      </CardContent>
    </Card>
  )
}

function SimplifiedAssuranceBoundary({
  assurance,
  detail,
}: {
  assurance: MigrationAssuranceSummary
  detail: string
}) {
  const { t } = useI18n()
  if (!assurance) return null
  return (
    <div className="space-y-4 rounded-lg border border-amber-500/30 p-4">
      <Alert>
        <ShieldCheck className="h-4 w-4" />
        <AlertTitle>{t("migrationWorkspace.twoServerQualification.simplifiedComplete")}</AlertTitle>
        <AlertDescription>{detail}</AlertDescription>
      </Alert>
      <EvidenceGrid rows={[
        [t("migrationWorkspace.twoServerQualification.assuranceType"), assurance.authorityType],
        [t("migrationWorkspace.twoServerQualification.productionAuthority"), assurance.productionAuthorityId ?? "—"],
        [t("migrationWorkspace.twoServerQualification.packageRevision"), assurance.packageRevisionId],
        [t("migrationWorkspace.twoServerQualification.sourceFreezeEvidence"), assurance.formalSourceFreezeEvidenceCollected ? t("common.yes") : t("common.no")],
        [t("migrationWorkspace.twoServerQualification.finalRecapture"), assurance.finalRecapturePerformed ? t("common.yes") : t("common.no")],
        [t("migrationWorkspace.twoServerQualification.postCaptureWritesExcluded"), assurance.postCaptureWritesIndependentlyExcluded ? t("common.yes") : t("common.no")],
        [t("migrationWorkspace.twoServerQualification.rollbackAssurance"), assurance.rollbackAssurance],
      ]} />
      <p className="text-sm text-muted-foreground">
        {t("migrationWorkspace.twoServerQualification.simplifiedReportHandoff")}
      </p>
    </div>
  )
}

function SourceEvidenceReview({
  envelope,
  fileName,
  locale,
}: {
  envelope: MigrationTwoServerSourceEvidenceEnvelope
  fileName: string
  locale: string
}) {
  const { t } = useI18n()
  const payload = envelope.payload
  return (
    <div className="space-y-4 rounded-lg border border-violet-500/30 p-4">
      <div className="flex items-center gap-2 font-medium">
        <CheckCircle2 className="h-4 w-4 shrink-0" aria-hidden="true" />
        {t("migrationWorkspace.twoServerQualification.reviewTitle")}
      </div>
      <Alert>
        <ShieldCheck className="h-4 w-4" />
        <AlertTitle>{t("migrationWorkspace.twoServerQualification.reviewUntrustedTitle")}</AlertTitle>
        <AlertDescription>{t("migrationWorkspace.twoServerQualification.reviewUntrustedDescription")}</AlertDescription>
      </Alert>
      <EvidenceGrid rows={[
        [t("migrationWorkspace.twoServerQualification.fileName"), fileName],
        [t("migrationWorkspace.twoServerQualification.schema"), envelope.schemaVersion],
        [t("migrationWorkspace.twoServerQualification.sourceEvidenceHash"), envelope.payloadSha256],
        [t("migrationWorkspace.twoServerQualification.sourceAttempt"), payload.qualificationAttemptId],
        [t("migrationWorkspace.twoServerQualification.generated"), formatDate(payload.generatedAtUtc, locale)],
        [t("migrationWorkspace.twoServerQualification.sourceMigration"), payload.migrationId],
        [t("migrationWorkspace.twoServerQualification.packageRevision"), payload.packageRevisionId],
        [t("migrationWorkspace.twoServerQualification.packageHash"), payload.encryptedPackageSha256],
        [t("migrationWorkspace.twoServerQualification.packageSize"), formatBytes(payload.encryptedPackageBytes)],
        [t("migrationWorkspace.twoServerQualification.sourceStack"), payload.sourceStackSlug],
        [t("migrationWorkspace.twoServerQualification.matrixServer"), payload.matrixServerName],
        [t("migrationWorkspace.twoServerQualification.freezeAttempt"), payload.freezeAttemptId],
      ]} />
      <HostIdentity title={t("migrationWorkspace.twoServerQualification.sourceHost")} host={payload.sourceHost} />
      <div className="grid gap-2 sm:grid-cols-3">
        <Check label={t("migrationWorkspace.twoServerQualification.sourceFrozen")} passed={payload.sourceFrozen} />
        <Check label={t("migrationWorkspace.twoServerQualification.noSourceRouteMutation")} passed={!payload.publicRoutingMutationOccurred} />
        <Check label={t("migrationWorkspace.twoServerQualification.noDevelopmentControlPlane")} passed={!payload.developmentExternalControlPlane} />
      </div>
    </div>
  )
}

function QualifiedEvidence({ qualification, locale }: { qualification: MigrationTwoServerQualification; locale: string }) {
  const { t } = useI18n()
  return (
    <div className="space-y-5">
      <Alert>
        <CheckCircle2 className="h-4 w-4" />
        <AlertTitle>{t("migrationWorkspace.twoServerQualification.qualifiedTitle")}</AlertTitle>
        <AlertDescription>{t("migrationWorkspace.twoServerQualification.qualifiedDescription")}</AlertDescription>
      </Alert>

      <EvidenceGrid rows={[
        [t("migrationWorkspace.twoServerQualification.qualificationId"), qualification.qualificationId],
        [t("migrationWorkspace.twoServerQualification.qualifiedAt"), formatDate(qualification.qualifiedAtUtc, locale)],
        [t("migrationWorkspace.twoServerQualification.sourceEvidenceHash"), qualification.sourceEvidenceSha256],
        [t("migrationWorkspace.twoServerQualification.qualificationEvidenceHash"), qualification.qualificationEvidenceSha256],
        [t("migrationWorkspace.twoServerQualification.adoptionPlan"), qualification.adoptionPlanId],
        [t("migrationWorkspace.twoServerQualification.runtimeStack"), qualification.runtimeStackId],
        [t("migrationWorkspace.twoServerQualification.productionVerification"), qualification.productionVerificationId],
        [t("migrationWorkspace.twoServerQualification.acceptance"), qualification.acceptanceId],
        [t("migrationWorkspace.twoServerQualification.baselineHandoff"), qualification.baselineBackupHandoffId],
        [t("migrationWorkspace.twoServerQualification.baselineCatalog"), qualification.baselineCatalogEntryId],
      ]} />

      <div className="grid gap-4 xl:grid-cols-2">
        <HostIdentity title={t("migrationWorkspace.twoServerQualification.sourceHost")} host={qualification.sourceHost} />
        <HostIdentity title={t("migrationWorkspace.twoServerQualification.targetHost")} host={qualification.targetHost} />
      </div>

      <div className="grid gap-2 sm:grid-cols-2">
        <Check label={t("migrationWorkspace.twoServerQualification.distinctMachine")} passed={qualification.distinctMachineIdentity} />
        <Check label={t("migrationWorkspace.twoServerQualification.distinctDocker")} passed={qualification.distinctDockerEngineIdentity} />
      </div>

      <SourceContainers containers={qualification.sourceContainers} />

      <Button variant="outline" asChild>
        <a href={`/backups/catalog/${encodeURIComponent(qualification.baselineCatalogEntryId)}`}>
          {t("migrationWorkspace.twoServerQualification.openBaseline")}
        </a>
      </Button>
    </div>
  )
}

type QualificationClosureAcknowledgements = Omit<MigrationTwoServerQualificationClosureRequest, "note">
type QualificationClosureAcknowledgementKey = keyof QualificationClosureAcknowledgements

function QualificationClosure({
  state,
  loading,
  fetching,
  error,
  acknowledgements,
  setAcknowledgement,
  note,
  setNote,
  closePending,
  reportPending,
  onRefresh,
  onClose,
  onDownload,
  locale,
}: {
  state: MigrationTwoServerQualificationClosureState | null
  loading: boolean
  fetching: boolean
  error: string | null
  acknowledgements: QualificationClosureAcknowledgements
  setAcknowledgement: (key: QualificationClosureAcknowledgementKey, checked: boolean) => void
  note: string
  setNote: (value: string) => void
  closePending: boolean
  reportPending: boolean
  onRefresh: () => void
  onClose: () => void
  onDownload: () => void
  locale: string
}) {
  const { t } = useI18n()
  const acknowledgementLabels = {
    qualificationEvidenceReviewed: "migrationWorkspace.twoServerQualification.closureAckQualification",
    distinctHostEvidenceReviewed: "migrationWorkspace.twoServerQualification.closureAckDistinctHosts",
    normalLifecycleReviewed: "migrationWorkspace.twoServerQualification.closureAckLifecycle",
    sourceRetentionReviewed: "migrationWorkspace.twoServerQualification.closureAckRetention",
    releaseHandoffAcknowledged: "migrationWorkspace.twoServerQualification.closureAckHandoff",
  } as const
  const allAcknowledged = Object.values(acknowledgements).every(Boolean)

  if (loading) {
    return (
      <div className="flex items-center gap-2 rounded-lg border p-4 text-sm text-muted-foreground">
        <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
        {t("migrationWorkspace.twoServerQualification.closureLoading")}
      </div>
    )
  }

  if (error || !state) {
    return (
      <Alert variant="destructive">
        <AlertTriangle className="h-4 w-4" />
        <AlertTitle>{t("migrationWorkspace.twoServerQualification.closureLoadFailed")}</AlertTitle>
        <AlertDescription className="space-y-3">
          <p>{error ?? t("migrationWorkspace.twoServerQualification.closureLoadFailedDescription")}</p>
          <Button variant="outline" size="sm" onClick={onRefresh}>
            <RefreshCw className="mr-2 h-4 w-4" aria-hidden="true" />
            {t("migrationWorkspace.refresh")}
          </Button>
        </AlertDescription>
      </Alert>
    )
  }

  if (state.closed && state.closure) {
    return (
      <ClosedQualificationClosure
        closure={state.closure}
        reportPending={reportPending}
        onDownload={onDownload}
        locale={locale}
      />
    )
  }

  return (
    <div className="space-y-4 rounded-lg border border-emerald-500/30 p-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <div className="flex items-center gap-2 font-medium">
            <LockKeyhole className="h-4 w-4" aria-hidden="true" />
            {t("migrationWorkspace.twoServerQualification.closureTitle")}
          </div>
          <p className="mt-1 text-sm text-muted-foreground">
            {t("migrationWorkspace.twoServerQualification.closureDescription")}
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={onRefresh} disabled={fetching || closePending}>
          <RefreshCw className={fetching ? "mr-2 h-4 w-4 animate-spin" : "mr-2 h-4 w-4"} aria-hidden="true" />
          {t("migrationWorkspace.refresh")}
        </Button>
      </div>

      <Alert variant={state.closureEligible ? "default" : "destructive"}>
        {state.closureEligible
          ? <CheckCircle2 className="h-4 w-4" />
          : <AlertTriangle className="h-4 w-4" />}
        <AlertTitle>
          {state.closureEligible
            ? t("migrationWorkspace.twoServerQualification.closureReady")
            : t("migrationWorkspace.twoServerQualification.closureBlocked")}
        </AlertTitle>
        <AlertDescription>{state.detail}</AlertDescription>
      </Alert>

      {state.blockers.length > 0 ? (
        <div className="rounded-lg border border-destructive/30 p-4">
          <div className="font-medium">{t("migrationWorkspace.twoServerQualification.closureBlockers")}</div>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-muted-foreground">
            {state.blockers.map((blocker) => <li key={blocker}>{blocker}</li>)}
          </ul>
        </div>
      ) : null}

      {state.closureEligible ? (
        <div className="space-y-4">
          <Alert>
            <ShieldCheck className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.twoServerQualification.closureReviewTitle")}</AlertTitle>
            <AlertDescription>{t("migrationWorkspace.twoServerQualification.closureReviewDescription")}</AlertDescription>
          </Alert>

          <div className="space-y-3">
            {(Object.keys(acknowledgementLabels) as QualificationClosureAcknowledgementKey[]).map((key) => (
              <label key={key} className="flex items-start gap-2 rounded-md border p-3 text-sm">
                <input
                  type="checkbox"
                  className="mt-1"
                  checked={acknowledgements[key]}
                  disabled={closePending}
                  onChange={(event) => setAcknowledgement(key, event.target.checked)}
                />
                <span>{t(acknowledgementLabels[key])}</span>
              </label>
            ))}
          </div>

          <div className="space-y-2">
            <label className="text-sm font-medium" htmlFor="qualification-closure-note">
              {t("migrationWorkspace.twoServerQualification.closureNote")}
            </label>
            <Input
              id="qualification-closure-note"
              value={note}
              maxLength={1000}
              disabled={closePending}
              placeholder={t("migrationWorkspace.twoServerQualification.closureNotePlaceholder")}
              onChange={(event) => setNote(event.target.value)}
            />
          </div>

          <Button disabled={!allAcknowledged || closePending} onClick={onClose}>
            {closePending
              ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" />
              : <LockKeyhole className="mr-2 h-4 w-4" aria-hidden="true" />}
            {t("migrationWorkspace.twoServerQualification.closureAction")}
          </Button>
        </div>
      ) : null}
    </div>
  )
}

function ClosedQualificationClosure({
  closure,
  reportPending,
  onDownload,
  locale,
}: {
  closure: MigrationTwoServerQualificationClosureEnvelope
  reportPending: boolean
  onDownload: () => void
  locale: string
}) {
  const { t } = useI18n()
  const payload = closure.payload
  return (
    <div className="space-y-5 rounded-lg border border-emerald-500/40 bg-emerald-500/[0.03] p-4">
      <Alert>
        <CheckCircle2 className="h-4 w-4" />
        <AlertTitle>{t("migrationWorkspace.twoServerQualification.closureCompleteTitle")}</AlertTitle>
        <AlertDescription>{t("migrationWorkspace.twoServerQualification.closureCompleteDescription")}</AlertDescription>
      </Alert>

      <EvidenceGrid rows={[
        [t("migrationWorkspace.twoServerQualification.closureId"), payload.closureId],
        [t("migrationWorkspace.twoServerQualification.closureClosedAt"), formatDate(payload.closedAtUtc, locale)],
        [t("migrationWorkspace.twoServerQualification.closureSchema"), closure.schemaVersion],
        [t("migrationWorkspace.twoServerQualification.closureEvidenceHash"), closure.payloadSha256],
        [t("migrationWorkspace.twoServerQualification.closureMemVersion"), payload.memVersion],
        [t("migrationWorkspace.twoServerQualification.qualificationId"), payload.qualificationId],
        [t("migrationWorkspace.twoServerQualification.runtimeStack"), payload.runtimeStackId],
        [t("migrationWorkspace.twoServerQualification.closureRuntimeSlug"), payload.runtimeStackSlug],
        [t("migrationWorkspace.twoServerQualification.productionVerification"), payload.productionVerificationId],
        [t("migrationWorkspace.twoServerQualification.acceptance"), payload.acceptanceId],
        [t("migrationWorkspace.twoServerQualification.baselineHandoff"), payload.baselineBackupHandoffId],
        [t("migrationWorkspace.twoServerQualification.baselineCatalog"), payload.baselineCatalogEntryId],
        [t("migrationWorkspace.twoServerQualification.closureBaselineBackup"), payload.baselineBackupId],
        [t("migrationWorkspace.twoServerQualification.closureBaselineState"), payload.baselineCatalogPayloadState],
        [t("migrationWorkspace.twoServerQualification.closureBaselineIntegrity"), payload.baselineCatalogIntegrityStatus],
        [t("migrationWorkspace.twoServerQualification.closureBaselineCompleted"), formatDate(payload.baselineCompletedAtUtc, locale)],
        [t("migrationWorkspace.twoServerQualification.closureBaselineBytes"), formatNullableBytes(payload.baselineBackupBytes)],
        [t("migrationWorkspace.twoServerQualification.closureBaselineFiles"), formatNullableCount(payload.baselineBackupFiles)],
        [t("migrationWorkspace.twoServerQualification.closureBaselineWarnings"), formatNullableCount(payload.baselineBackupWarnings)],
      ]} />

      <div className="grid gap-4 xl:grid-cols-2">
        <HostIdentity title={t("migrationWorkspace.twoServerQualification.sourceHost")} host={payload.sourceHost} />
        <HostIdentity title={t("migrationWorkspace.twoServerQualification.targetHost")} host={payload.targetHost} />
      </div>

      <div className="grid gap-2 sm:grid-cols-2">
        <Check label={t("migrationWorkspace.twoServerQualification.distinctMachine")} passed={payload.distinctMachineIdentity} />
        <Check label={t("migrationWorkspace.twoServerQualification.distinctDocker")} passed={payload.distinctDockerEngineIdentity} />
      </div>

      <PublicRoutes routes={payload.publicRoutes} locale={locale} />
      <SourceContainers containers={payload.sourceContainers} />

      {payload.note ? (
        <div className="rounded-md border p-3 text-sm">
          <div className="font-medium">{t("migrationWorkspace.twoServerQualification.closureNote")}</div>
          <p className="mt-1 whitespace-pre-wrap text-muted-foreground">{payload.note}</p>
        </div>
      ) : null}

      <Alert>
        <ShieldCheck className="h-4 w-4" />
        <AlertTitle>{t("migrationWorkspace.twoServerQualification.closureRetentionTitle")}</AlertTitle>
        <AlertDescription>{t("migrationWorkspace.twoServerQualification.closureRetentionDescription")}</AlertDescription>
      </Alert>

      <div className="flex flex-wrap gap-2">
        <Button variant="outline" asChild>
          <a href={`/stacks/${encodeURIComponent(payload.runtimeStackSlug)}`}>
            {t("migrationWorkspace.twoServerQualification.closureOpenStack")}
          </a>
        </Button>
        <Button variant="outline" asChild>
          <a href={`/backups/catalog/${encodeURIComponent(payload.baselineCatalogEntryId)}`}>
            {t("migrationWorkspace.twoServerQualification.openBaseline")}
          </a>
        </Button>
        <Button onClick={onDownload} disabled={reportPending}>
          {reportPending
            ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" />
            : <Download className="mr-2 h-4 w-4" aria-hidden="true" />}
          {t("migrationWorkspace.twoServerQualification.closureDownload")}
        </Button>
      </div>
    </div>
  )
}

function PublicRoutes({ routes, locale }: { routes: MigrationTwoServerQualificationClosureRoute[]; locale: string }) {
  const { t } = useI18n()
  return (
    <div className="space-y-3 rounded-lg border p-4">
      <div className="flex items-center gap-2 font-medium">
        <Route className="h-4 w-4" aria-hidden="true" />
        {t("migrationWorkspace.twoServerQualification.closureRoutes")}
      </div>
      <div className="grid gap-3 xl:grid-cols-2">
        {routes.map((route) => (
          <div key={`${route.serviceKey}-${route.providerRouteId}`} className="rounded-md border p-3">
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div>
                <div className="font-medium">{route.serviceKey}</div>
                <div className="mt-1 text-sm text-muted-foreground">{route.publicHost}</div>
              </div>
              <Badge variant={route.status.toLowerCase() === "verified" ? "secondary" : "destructive"}>
                {route.status}
              </Badge>
            </div>
            <div className="mt-3 grid gap-2 sm:grid-cols-2">
              <Fact label={t("migrationWorkspace.twoServerQualification.closureRouteId")} value={route.providerRouteId} mono />
              <Fact label={t("migrationWorkspace.twoServerQualification.closureRouteProvider")} value={route.provider} />
              <Fact label={t("migrationWorkspace.twoServerQualification.closureRouteTarget")} value={`${route.forwardScheme}://${route.forwardHost}:${route.forwardPort}`} mono />
              <Fact label={t("migrationWorkspace.twoServerQualification.closureRouteVerified")} value={route.lastVerifiedAtUtc ? formatDate(route.lastVerifiedAtUtc, locale) : "—"} />
            </div>
            <div className="mt-3 grid gap-2 sm:grid-cols-3">
              <Check label={t("migrationWorkspace.twoServerQualification.closureRoutePublic")} passed={route.isPublic} />
              <Check label={t("migrationWorkspace.twoServerQualification.closureRouteTls")} passed={route.sslExpected && route.sslConfigured} />
              <Check label={t("migrationWorkspace.twoServerQualification.closureRouteForceTls")} passed={route.forceSsl} />
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

function HostIdentity({ title, host }: { title: string; host: MigrationTwoServerHostIdentity }) {
  const { t } = useI18n()
  return (
    <div className="space-y-3 rounded-lg border p-4">
      <div className="flex items-center gap-2 font-medium">
        <Server className="h-4 w-4" aria-hidden="true" />
        {title}
      </div>
      <EvidenceGrid rows={[
        [t("migrationWorkspace.twoServerQualification.machineName"), host.machineName],
        [t("migrationWorkspace.twoServerQualification.operatingSystem"), host.operatingSystem],
        [t("migrationWorkspace.twoServerQualification.architecture"), host.architecture],
        [t("migrationWorkspace.twoServerQualification.machineHash"), host.machineIdSha256],
        [t("migrationWorkspace.twoServerQualification.dockerName"), host.dockerName],
        [t("migrationWorkspace.twoServerQualification.dockerVersion"), host.dockerServerVersion],
        [t("migrationWorkspace.twoServerQualification.dockerHash"), host.dockerEngineIdSha256],
      ]} />
    </div>
  )
}

function SourceContainers({ containers }: { containers: MigrationTwoServerSourceContainerEvidence[] }) {
  const { t } = useI18n()
  return (
    <div className="space-y-3 rounded-lg border p-4">
      <div className="flex items-center gap-2 font-medium">
        <Container className="h-4 w-4" aria-hidden="true" />
        {t("migrationWorkspace.twoServerQualification.sourceContainers")}
      </div>
      <div className="space-y-3">
        {containers.map((container) => (
          <div key={container.containerId} className="rounded-md border p-3 text-sm">
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div>
                <div className="font-medium">{container.containerName}</div>
                <div className="mt-1 text-xs text-muted-foreground">{container.role}</div>
              </div>
              <Badge variant={container.frozenStatePreserved && container.identityMatched ? "secondary" : "destructive"}>
                {container.frozenStatePreserved && container.identityMatched
                  ? t("migrationWorkspace.twoServerQualification.frozenVerified")
                  : t("migrationWorkspace.twoServerQualification.containerProblem")}
              </Badge>
            </div>
            <div className="mt-3 grid gap-2 sm:grid-cols-2">
              <Fact label={t("migrationWorkspace.twoServerQualification.containerState")} value={container.currentState} />
              <Fact label={t("migrationWorkspace.twoServerQualification.restartPolicy")} value={container.currentRestartPolicy} />
              <Fact label={t("migrationWorkspace.twoServerQualification.containerId")} value={container.containerId} mono />
              <Fact label={t("migrationWorkspace.twoServerQualification.imageId")} value={container.imageId} mono />
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

function EvidenceGrid({ rows }: { rows: Array<[string, string]> }) {
  return (
    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
      {rows.map(([label, value]) => <Fact key={label} label={label} value={value} mono={looksLikeIdentity(label, value)} />)}
    </div>
  )
}

function Fact({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="min-w-0 rounded-md border bg-muted/20 p-3">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className={mono ? "mt-1 break-all font-mono text-xs" : "mt-1 break-words text-sm"}>{value}</div>
    </div>
  )
}

function Check({ label, passed }: { label: string; passed: boolean }) {
  const { t } = useI18n()
  return (
    <div className="flex items-center gap-2 rounded-md border p-3 text-sm">
      {passed
        ? <CheckCircle2 className="h-4 w-4 text-emerald-500" aria-hidden="true" />
        : <AlertTriangle className="h-4 w-4 text-destructive" aria-hidden="true" />}
      <span>{label}</span>
      <Badge className="ml-auto" variant={passed ? "secondary" : "destructive"}>
        {passed ? t("common.yes") : t("common.no")}
      </Badge>
    </div>
  )
}

function isSourceEvidenceEnvelope(value: unknown): value is MigrationTwoServerSourceEvidenceEnvelope {
  if (!isRecord(value) || value.schemaVersion !== "mem.migration.two-server-source-evidence.v1") return false
  if (typeof value.payloadSha256 !== "string" || !isRecord(value.payload)) return false
  const payload = value.payload
  return typeof payload.qualificationAttemptId === "string" &&
    typeof payload.migrationId === "string" &&
    typeof payload.packageRevisionId === "string" &&
    typeof payload.encryptedPackageSha256 === "string" &&
    typeof payload.sourceStackSlug === "string" &&
    typeof payload.matrixServerName === "string" &&
    isRecord(payload.sourceHost) &&
    typeof payload.sourceHost.machineIdSha256 === "string" &&
    typeof payload.sourceHost.dockerEngineIdSha256 === "string" &&
    Array.isArray(payload.containers)
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null
}

function looksLikeIdentity(label: string, value: string): boolean {
  return value.length >= 32 || label.toLowerCase().includes("id") || label.toLowerCase().includes("sha")
}

function formatDate(value: string, locale: string): string {
  return formatMigrationDateTime(value, locale)
}

function formatNullableBytes(value: number | null): string {
  return value === null ? "—" : formatBytes(value)
}

function formatNullableCount(value: number | null): string {
  return value === null ? "—" : new Intl.NumberFormat().format(value)
}

function formatBytes(value: number): string {
  if (!Number.isFinite(value) || value < 0) return String(value)
  if (value < 1024) return `${value} B`
  const units = ["KiB", "MiB", "GiB", "TiB"]
  let amount = value / 1024
  let unit = units[0]
  for (let index = 1; index < units.length && amount >= 1024; index++) {
    amount /= 1024
    unit = units[index]
  }
  return `${amount.toFixed(amount >= 10 ? 1 : 2)} ${unit}`
}
