import { CheckCircle2, LoaderCircle, ShieldCheck, TriangleAlert } from "lucide-react"
import { useMemo, useState, type ReactNode } from "react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import {
  confirmCatalogCutover,
  createCatalogCutoverPreview,
  createCatalogProductionCandidate,
  executeCatalogCutover,
  getCatalogPreCutoverReadiness,
  getCatalogPostCutoverProjection,
  type CatalogCutoverConfirmationResponse,
  type CatalogCutoverExecutionResponse,
  type CatalogCutoverPreviewResponse,
  type CatalogPreCutoverReadinessResponse,
  type CatalogProductionCandidateResponse,
  type CatalogPostCutoverProjectionResponse,
} from "@/features/operator/backups/api"

const acknowledgementKeys = [
  "acknowledgePreviewReviewed",
  "acknowledgeCandidateIsPrivateAndHealthy",
  "acknowledgePublicRouteExposureRisk",
  "acknowledgeNoAutomaticRollback",
  "acknowledgeFinalBackupRequired",
  "acknowledgeExecutionStillLocked",
] as const

type AckKey = (typeof acknowledgementKeys)[number]

const acknowledgementLabels: Record<AckKey, TranslationKey> = {
  acknowledgePreviewReviewed: "backupCatalog.cutover.ack.acknowledgePreviewReviewed",
  acknowledgeCandidateIsPrivateAndHealthy: "backupCatalog.cutover.ack.acknowledgeCandidateIsPrivateAndHealthy",
  acknowledgePublicRouteExposureRisk: "backupCatalog.cutover.ack.acknowledgePublicRouteExposureRisk",
  acknowledgeNoAutomaticRollback: "backupCatalog.cutover.ack.acknowledgeNoAutomaticRollback",
  acknowledgeFinalBackupRequired: "backupCatalog.cutover.ack.acknowledgeFinalBackupRequired",
  acknowledgeExecutionStillLocked: "backupCatalog.cutover.ack.acknowledgeExecutionStillLocked",
}

const executionAcknowledgementKeys = [
  "acknowledgeFinalApproval",
  "acknowledgeFinalBackupWillBeCaptured",
  "acknowledgeOldRuntimeWillBeStopped",
  "acknowledgePublicRouteMutation",
  "acknowledgeManualRollback",
] as const

type ExecutionAckKey = (typeof executionAcknowledgementKeys)[number]

const executionAcknowledgementLabels: Record<ExecutionAckKey, TranslationKey> = {
  acknowledgeFinalApproval: "backupCatalog.cutover.executeAck.acknowledgeFinalApproval",
  acknowledgeFinalBackupWillBeCaptured: "backupCatalog.cutover.executeAck.acknowledgeFinalBackupWillBeCaptured",
  acknowledgeOldRuntimeWillBeStopped: "backupCatalog.cutover.executeAck.acknowledgeOldRuntimeWillBeStopped",
  acknowledgePublicRouteMutation: "backupCatalog.cutover.executeAck.acknowledgePublicRouteMutation",
  acknowledgeManualRollback: "backupCatalog.cutover.executeAck.acknowledgeManualRollback",
}

type Props = { catalogEntryId: string; payloadAvailable: boolean }

export function MigrationCutoverCompatibilityPanel({ catalogEntryId, payloadAvailable }: Props) {
  const { t } = useI18n()
  const [candidate, setCandidate] = useState<CatalogProductionCandidateResponse | null>(null)
  const [preview, setPreview] = useState<CatalogCutoverPreviewResponse | null>(null)
  const [confirmation, setConfirmation] = useState<CatalogCutoverConfirmationResponse | null>(null)
  const [readiness, setReadiness] = useState<CatalogPreCutoverReadinessResponse | null>(null)
  const [execution, setExecution] = useState<CatalogCutoverExecutionResponse | null>(null)
  const [postCutover, setPostCutover] = useState<CatalogPostCutoverProjectionResponse | null>(null)
  const [acks, setAcks] = useState<Record<AckKey, boolean>>(() => Object.fromEntries(acknowledgementKeys.map((key) => [key, false])) as Record<AckKey, boolean>)
  const [executionAcks, setExecutionAcks] = useState<Record<ExecutionAckKey, boolean>>(() => Object.fromEntries(executionAcknowledgementKeys.map((key) => [key, false])) as Record<ExecutionAckKey, boolean>)
  const [oldRuntimeStackSlug, setOldRuntimeStackSlug] = useState("")
  const [pending, setPending] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [candidateStepUpOpen, setCandidateStepUpOpen] = useState(false)
  const [executionStepUpOpen, setExecutionStepUpOpen] = useState(false)

  const allAcknowledged = useMemo(() => acknowledgementKeys.every((key) => acks[key]), [acks])
  const allExecutionAcknowledged = useMemo(() => executionAcknowledgementKeys.every((key) => executionAcks[key]), [executionAcks])
  const candidateId = candidate?.candidate.candidateId
  const previewId = preview?.preview.previewId
  const confirmationId = confirmation?.confirmation.confirmationId
  const executionFinished = execution?.execution.status === "completed" || execution?.execution.status === "already_completed"

  async function run<T>(name: string, action: () => Promise<T>, success: (value: T) => void) {
    setPending(name)
    setError(null)
    try {
      success(await action())
    } catch (value) {
      setError(value instanceof Error ? value.message : t("backupCatalog.cutover.errorGeneric"))
    } finally {
      setPending(null)
    }
  }

  function createCandidateAfterStepUp() {
    void run("candidate", () => createCatalogProductionCandidate(catalogEntryId), (value) => {
      setCandidate(value)
      setPreview(null)
      setConfirmation(null)
      setReadiness(null)
      setExecution(null)
      setPostCutover(null)
    })
  }

  function executeAfterStepUp() {
    if (!candidateId || !confirmationId || !oldRuntimeStackSlug.trim()) return
    void run("execution", () => executeCatalogCutover(catalogEntryId, {
      confirmationId,
      candidateId,
      oldRuntimeStackSlug: oldRuntimeStackSlug.trim(),
      execute: true,
      ...executionAcks,
    }), (value) => { setExecution(value); void run("post-cutover", () => getCatalogPostCutoverProjection(catalogEntryId), setPostCutover) })
  }

  return (
    <Card className="border-amber-500/35 bg-amber-500/[0.025]">
      <CardHeader>
        <CardTitle className="flex items-center gap-2"><ShieldCheck className="h-5 w-5 text-amber-400" />{t("backupCatalog.cutover.title")}</CardTitle>
        <p className="text-sm text-muted-foreground">{t("backupCatalog.cutover.description")}</p>
      </CardHeader>
      <CardContent className="space-y-5">
        <Alert><TriangleAlert className="h-4 w-4" /><AlertTitle>{t("backupCatalog.cutover.controlledTitle")}</AlertTitle><AlertDescription>{t("backupCatalog.cutover.controlledDescription")}</AlertDescription></Alert>
        {error ? <Alert variant="destructive"><AlertTitle>{t("backupCatalog.cutover.errorTitle")}</AlertTitle><AlertDescription>{error}</AlertDescription></Alert> : null}

        <Step number="1" title={t("backupCatalog.cutover.candidateTitle")} detail={t("backupCatalog.cutover.candidateDescription")}>
          <Button disabled={!payloadAvailable || pending !== null || executionFinished} onClick={() => setCandidateStepUpOpen(true)}>
            {pending === "candidate" ? <LoaderCircle className="mr-2 h-4 w-4 animate-spin" /> : null}
            {candidate ? t("backupCatalog.cutover.resumeCandidate") : t("backupCatalog.cutover.createCandidate")}
          </Button>
          {candidate ? <Evidence rows={[[t("backupCatalog.cutover.candidateId"), candidate.candidate.candidateId], [t("backupCatalog.cutover.status"), candidate.candidate.status], [t("backupCatalog.cutover.private"), candidate.candidate.safety.privateOnly ? t("common.yes") : t("common.no")]]} /> : null}
        </Step>

        <Step number="2" title={t("backupCatalog.cutover.previewTitle")} detail={t("backupCatalog.cutover.previewDescription")}>
          <Button variant="outline" disabled={!candidateId || pending !== null || executionFinished} onClick={() => candidateId && void run("preview", () => createCatalogCutoverPreview(catalogEntryId, candidateId), (value) => { setPreview(value); setConfirmation(null); setReadiness(null); setExecution(null) })}>
            {pending === "preview" ? <LoaderCircle className="mr-2 h-4 w-4 animate-spin" /> : null}{t("backupCatalog.cutover.createPreview")}
          </Button>
          {preview ? <Evidence rows={[[t("backupCatalog.cutover.previewId"), preview.preview.previewId], [t("backupCatalog.cutover.status"), preview.preview.status], [t("backupCatalog.cutover.blockers"), String(preview.preview.blockers.length)]]} /> : null}
        </Step>

        <Step number="3" title={t("backupCatalog.cutover.confirmTitle")} detail={t("backupCatalog.cutover.confirmDescription")}>
          <div className="space-y-2">
            {acknowledgementKeys.map((key) => <label key={key} className="flex items-start gap-2 text-sm"><input type="checkbox" className="mt-1" checked={acks[key]} disabled={executionFinished} onChange={(event) => setAcks((current) => ({ ...current, [key]: event.target.checked }))} /><span>{t(acknowledgementLabels[key])}</span></label>)}
          </div>
          <Button variant="outline" disabled={!candidateId || !previewId || !allAcknowledged || pending !== null || executionFinished} onClick={() => candidateId && previewId && void run("confirmation", () => confirmCatalogCutover(catalogEntryId, { previewId, candidateId, ...acks }), (value) => { setConfirmation(value); setReadiness(null); setExecution(null) })}>
            {pending === "confirmation" ? <LoaderCircle className="mr-2 h-4 w-4 animate-spin" /> : null}{t("backupCatalog.cutover.saveConfirmation")}
          </Button>
          {confirmation ? <Evidence rows={[[t("backupCatalog.cutover.confirmationId"), confirmation.confirmation.confirmationId], [t("backupCatalog.cutover.status"), confirmation.confirmation.status], [t("backupCatalog.cutover.executionAvailable"), confirmation.confirmation.executionAvailable ? t("common.yes") : t("common.no")]]} /> : null}
        </Step>

        <Step number="4" title={t("backupCatalog.cutover.readinessTitle")} detail={t("backupCatalog.cutover.readinessDescription")}>
          <Button disabled={!candidateId || !previewId || !confirmationId || pending !== null || executionFinished} onClick={() => candidateId && previewId && confirmationId && void run("readiness", () => getCatalogPreCutoverReadiness(catalogEntryId, candidateId, previewId, confirmationId), (value) => { setReadiness(value); setOldRuntimeStackSlug(value.catalog.sourceStackSlug ?? "") })}>
            {pending === "readiness" ? <LoaderCircle className="mr-2 h-4 w-4 animate-spin" /> : null}{t("backupCatalog.cutover.checkReadiness")}
          </Button>
          {readiness ? <div className="space-y-3"><Alert variant={readiness.executionReady ? "default" : "destructive"}>{readiness.executionReady ? <CheckCircle2 className="h-4 w-4" /> : <TriangleAlert className="h-4 w-4" />}<AlertTitle>{readiness.executionReady ? t("backupCatalog.cutover.ready") : t("backupCatalog.cutover.blocked")}</AlertTitle><AlertDescription>{readiness.detail}</AlertDescription></Alert><Evidence rows={[[t("backupCatalog.cutover.candidateId"), candidateId ?? ""], [t("backupCatalog.cutover.previewId"), previewId ?? ""], [t("backupCatalog.cutover.confirmationId"), confirmationId ?? ""]]} />{readiness.blockers.length ? <MessageList title={t("backupCatalog.cutover.blockers")} values={readiness.blockers} /> : null}{readiness.warnings.length ? <MessageList title={t("backupCatalog.cutover.warnings")} values={readiness.warnings} /> : null}</div> : null}
        </Step>

        <Step number="5" title={t("backupCatalog.cutover.executionTitle")} detail={t("backupCatalog.cutover.executionDescription")}>
          <Alert variant="destructive"><TriangleAlert className="h-4 w-4" /><AlertTitle>{t("backupCatalog.cutover.executionDangerTitle")}</AlertTitle><AlertDescription>{t("backupCatalog.cutover.executionDangerDescription")}</AlertDescription></Alert>
          <label className="block space-y-2 text-sm"><span className="font-medium">{t("backupCatalog.cutover.oldRuntimeStackSlug")}</span><Input value={oldRuntimeStackSlug} disabled={!readiness?.executionReady || pending !== null || executionFinished} onChange={(event) => setOldRuntimeStackSlug(event.target.value)} autoComplete="off" /></label>
          <div className="space-y-2">
            {executionAcknowledgementKeys.map((key) => <label key={key} className="flex items-start gap-2 text-sm"><input type="checkbox" className="mt-1" checked={executionAcks[key]} disabled={!readiness?.executionReady || executionFinished} onChange={(event) => setExecutionAcks((current) => ({ ...current, [key]: event.target.checked }))} /><span>{t(executionAcknowledgementLabels[key])}</span></label>)}
          </div>
          <Button variant="destructive" disabled={!readiness?.executionReady || !candidateId || !confirmationId || !oldRuntimeStackSlug.trim() || !allExecutionAcknowledged || pending !== null || executionFinished} onClick={() => setExecutionStepUpOpen(true)}>
            {pending === "execution" ? <LoaderCircle className="mr-2 h-4 w-4 animate-spin" /> : null}{t("backupCatalog.cutover.execute")}
          </Button>
          {execution ? <ExecutionEvidence execution={execution} /> : null}
        </Step>

        <Step number="6" title={t("backupCatalog.cutover.postCutoverTitle")} detail={t("backupCatalog.cutover.postCutoverDescription")}>
          <Button variant="outline" disabled={!executionFinished || pending !== null} onClick={() => void run("post-cutover", () => getCatalogPostCutoverProjection(catalogEntryId), setPostCutover)}>
            {pending === "post-cutover" ? <LoaderCircle className="mr-2 h-4 w-4 animate-spin" /> : null}{t("backupCatalog.cutover.refreshPostCutover")}
          </Button>
          {postCutover ? <PostCutoverEvidence projection={postCutover} /> : null}
        </Step>
      </CardContent>
      <OperatorStepUpDialog open={candidateStepUpOpen} onOpenChange={setCandidateStepUpOpen} onVerified={createCandidateAfterStepUp} />
      <OperatorStepUpDialog open={executionStepUpOpen} onOpenChange={setExecutionStepUpOpen} onVerified={executeAfterStepUp} />
    </Card>
  )
}

function ExecutionEvidence({ execution }: { execution: CatalogCutoverExecutionResponse }) {
  const { t } = useI18n()
  const result = execution.execution
  const completed = result.status === "completed" || result.status === "already_completed"
  return <div className="space-y-3"><Alert variant={completed ? "default" : "destructive"}>{completed ? <CheckCircle2 className="h-4 w-4" /> : <TriangleAlert className="h-4 w-4" />}<AlertTitle>{completed ? t("backupCatalog.cutover.executionCompleted") : t("backupCatalog.cutover.executionNotCompleted")}</AlertTitle><AlertDescription>{result.detail ?? execution.detail}</AlertDescription></Alert><Evidence rows={[[t("backupCatalog.cutover.executionId"), result.executionId], [t("backupCatalog.cutover.status"), result.status], [t("backupCatalog.cutover.finalBackupCaptured"), result.finalBackup.captured ? t("common.yes") : t("common.no")], [t("backupCatalog.cutover.oldRuntimeRetained"), result.oldRuntime.retainedForRollback ? t("common.yes") : t("common.no")], [t("backupCatalog.cutover.routesSucceeded"), result.routes.allRequiredRoutesSucceeded ? t("common.yes") : t("common.no")], [t("backupCatalog.cutover.publicVerificationPassed"), result.publicVerification.passed ? t("common.yes") : t("common.no")], [t("backupCatalog.cutover.rollbackCompleted"), result.rollback.completed ? t("common.yes") : t("common.no")]]} />{result.blockers.length ? <MessageList title={t("backupCatalog.cutover.blockers")} values={result.blockers} /> : null}{result.warnings.length ? <MessageList title={t("backupCatalog.cutover.warnings")} values={result.warnings} /> : null}{result.errors.length ? <MessageList title={t("backupCatalog.cutover.errors")} values={result.errors} /> : null}</div>
}

function Step({ number, title, detail, children }: { number: string; title: string; detail: string; children: ReactNode }) { return <section className="space-y-3 rounded-lg border bg-background/30 p-4"><div className="flex gap-3"><span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-muted text-sm font-semibold">{number}</span><div><h3 className="font-medium">{title}</h3><p className="text-sm text-muted-foreground">{detail}</p></div></div><div className="space-y-3 pl-10">{children}</div></section> }
function Evidence({ rows }: { rows: [string, string][] }) { return <dl className="grid gap-2 rounded-md border p-3 text-sm sm:grid-cols-2">{rows.map(([label, value]) => <div key={label} className="min-w-0"><dt className="text-xs text-muted-foreground">{label}</dt><dd className="break-all font-mono text-xs">{value}</dd></div>)}</dl> }
function MessageList({ title, values }: { title: string; values: string[] }) { return <div><p className="text-sm font-medium">{title}</p><ul className="mt-1 list-disc space-y-1 pl-5 text-sm text-muted-foreground">{values.map((value) => <li key={value}>{value}</li>)}</ul></div> }


function PostCutoverEvidence({ projection }: { projection: CatalogPostCutoverProjectionResponse }) {
  const { t } = useI18n()
  const completed = projection.cutover.state === "cutover_completed" && projection.cutover.publicVerificationPassed
  return <div className="space-y-3">
    <Alert variant={completed ? "default" : "destructive"}>
      {completed ? <CheckCircle2 className="h-4 w-4" /> : <TriangleAlert className="h-4 w-4" />}
      <AlertTitle>{completed ? t("backupCatalog.cutover.postCutoverVerified") : t("backupCatalog.cutover.postCutoverAttention")}</AlertTitle>
      <AlertDescription>{projection.detail}</AlertDescription>
    </Alert>
    <Evidence rows={[
      [t("backupCatalog.cutover.executionId"), projection.cutover.executionId ?? "—"],
      [t("backupCatalog.cutover.finalBackupCaptured"), projection.cutover.finalBackupCaptured ? t("common.yes") : t("common.no")],
      [t("backupCatalog.cutover.publicVerificationPassed"), projection.cutover.publicVerificationPassed ? t("common.yes") : t("common.no")],
      [t("backupCatalog.cutover.oldRuntimeRetained"), projection.cutover.oldRuntimeRetainedForRollback ? t("common.yes") : t("common.no")],
      [t("backupCatalog.cutover.postCutoverRollbackAvailable"), projection.cutover.oldRuntimeRetainedForRollback && !projection.cutover.rolledBack ? t("common.yes") : t("common.no")],
      [t("backupCatalog.cutover.postCutoverAcceptance"), t("backupCatalog.cutover.postCutoverAcceptancePending")],
    ]} />
    <Alert><ShieldCheck className="h-4 w-4" /><AlertTitle>{t("backupCatalog.cutover.postCutoverBoundaryTitle")}</AlertTitle><AlertDescription>{t("backupCatalog.cutover.postCutoverBoundaryDescription")}</AlertDescription></Alert>
    {projection.warnings.length ? <MessageList title={t("backupCatalog.cutover.warnings")} values={projection.warnings} /> : null}
  </div>
}
