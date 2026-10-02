import { useMemo, useState } from "react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Link } from "react-router-dom"
import {
  AlertCircle,
  ArrowRight,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  CircleAlert,
  Clock3,
  ExternalLink,
  FileCheck2,
  FlaskConical,
  LoaderCircle,
  MonitorUp,
  RefreshCw,
  Settings2,
  Users,
  ShieldCheck,
  Trash2,
} from "lucide-react"

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import { cn } from "@/lib/utils"
import {
  useDestroyRestoreStagingRun,
  useRunRestoredServerChecks,
} from "@/features/operator/backups/hooks/use-backups"
import type { RestoreWorkspaceResponse } from "@/features/operator/backups/api/types/restore-workspace.types"
import { isHostAgentProblemError } from "@/features/operator/backups/api/transport/host-agent"
import {
  captureRestorePrivateTestBaseline,
  reconcileRestorePrivateTestOutcome,
} from "@/features/operator/backups/api/workspace/restore-private-test-outcome"
import {
  reconcileRestorePrivateTestRetirementOutcome,
} from "@/features/operator/backups/api/workspace/restore-private-test-retirement-outcome"
import { RestoreTargetAndCreatePanel } from "./restore-target-and-create-panel"
import {
  useCompleteRestoreHandover,
  useRunRestoreWorkspacePrivateTest,
} from "@/features/operator/backups/hooks/use-restore-workspace"
import {
  buildVisibleSteps,
  initialOpenStep,
  type VisibleStep,
} from "./workspace-ui"
import { formatDate } from "@/features/operator/backups/shared/components/backup-formatting"
import {
  getRestoreWorkspaceProblem,
  getRestoreWorkspaceProblemMessage,
  getRestoreWorkspaceTechnicalDetail,
  RestoreWorkspaceTechnicalDetails,
} from "./restore-workspace-problems"

type Props = {
  workspace: RestoreWorkspaceResponse
  onViewEvidence: () => void
  onViewLogs: () => void
  onWorkspaceChanged: () => Promise<unknown>
}

function isRestoreProgressionClosed(workspace: RestoreWorkspaceResponse) {
  return ["cancelled", "abandoned", "superseded"].includes(
    workspace.attempt.status.trim().toLowerCase(),
  )
}

export function StandardRestoreAccordion({
  workspace,
  onViewEvidence,
  onViewLogs,
  onWorkspaceChanged,
}: Props) {
  const { t } = useI18n()
  const steps = useMemo(
    () => buildVisibleSteps(workspace.standardStages, t),
    [t, workspace.standardStages],
  )
  const [openStep, setOpenStep] = useState(() => initialOpenStep(steps))
  const progressionClosed = isRestoreProgressionClosed(workspace)

  return (
    <div className="space-y-3">
      {steps.map((step, index) => (
        <RestoreStageCard
          key={step.key}
          index={index}
          isOpen={openStep === step.key}
          step={step}
          workspace={workspace}
          progressionClosed={progressionClosed}
          onToggle={() => setOpenStep(openStep === step.key ? "" : step.key)}
          onOpenStep={setOpenStep}
          onViewEvidence={onViewEvidence}
          onViewLogs={onViewLogs}
          onWorkspaceChanged={onWorkspaceChanged}
          onSkipPrivateTest={() => setOpenStep("choose-and-create-restored-server")}
        />
      ))}

      <p className="pt-1 text-xs text-muted-foreground">
        {t("restoreWorkspace.reference")}: <span className="font-mono">{workspace.restoreSessionId}</span>
      </p>
    </div>
  )
}

function RestoreStageCard({
  index,
  isOpen,
  step,
  workspace,
  progressionClosed,
  onToggle,
  onOpenStep,
  onViewEvidence,
  onViewLogs,
  onWorkspaceChanged,
  onSkipPrivateTest,
}: {
  index: number
  isOpen: boolean
  step: VisibleStep
  workspace: RestoreWorkspaceResponse
  progressionClosed: boolean
  onToggle: () => void
  onOpenStep: (stepKey: string) => void
  onViewEvidence: () => void
  onViewLogs: () => void
  onWorkspaceChanged: () => Promise<unknown>
  onSkipPrivateTest: () => void
}) {
  return (
    <Card className={cn("gap-0 border py-0 shadow-sm", stageCardClass(step.state, isOpen))}>
      <button
        type="button"
        onClick={onToggle}
        className="flex w-full items-center gap-4 px-5 py-4 text-left transition-colors hover:bg-muted/35 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        aria-expanded={isOpen}
      >
        <span className={cn("flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-sm font-semibold shadow-sm", stageCircleClass(step.state))}>
          {index + 1}
        </span>
        <span className="min-w-0 flex-1">
          <span className="block text-[0.95rem] font-semibold">{step.title}</span>
          <span className="mt-1 block text-sm leading-5 text-muted-foreground">{step.description}</span>
        </span>
        <StageStateBadge state={step.state} />
        {isOpen ? <ChevronUp className="h-4 w-4 shrink-0 text-muted-foreground" /> : <ChevronDown className="h-4 w-4 shrink-0 text-muted-foreground" />}
      </button>

      {isOpen ? (
        <CardContent className="border-t border-border/70 bg-background/[0.18] px-5 pb-5 pt-5">
          <StepPanel
            step={step}
            workspace={workspace}
            progressionClosed={progressionClosed}
            onViewEvidence={onViewEvidence}
            onViewLogs={onViewLogs}
            onWorkspaceChanged={onWorkspaceChanged}
            onSkipPrivateTest={onSkipPrivateTest}
            onOpenPrivateTest={() => onOpenStep("private-test")}
            onOpenTargetSelection={() => onOpenStep("choose-and-create-restored-server")}
            onOpenVerification={() => onOpenStep("check-restored-server")}
            onOpenHandover={() => onOpenStep("complete-and-hand-over")}
          />
        </CardContent>
      ) : null}
    </Card>
  )
}

function StageStateBadge({ state }: { state: string }) {
  const { t } = useI18n()
  const normalized = state.toLowerCase()
  const label = stageStateLabel(normalized, t)
  const icon = normalized === "completed" ? <CheckCircle2 className="h-3.5 w-3.5" /> : undefined

  return (
    <Badge className={cn("hidden gap-1.5 sm:inline-flex", stageBadgeClass(normalized))} variant="secondary">
      {icon}{label}
    </Badge>
  )
}

function stageCardClass(state: string, isOpen: boolean) {
  const normalized = state.toLowerCase()
  if (normalized === "failed") return cn("border-destructive/40 bg-destructive/[0.04]", isOpen && "ring-1 ring-destructive/20")
  if (normalized === "completed") return cn("border-primary/25 bg-gradient-to-r from-primary/[0.055] via-card to-card", isOpen && "ring-1 ring-primary/20")
  if (["running", "ready", "optional"].includes(normalized)) return cn("border-blue-500/30 bg-gradient-to-r from-blue-500/[0.06] via-card to-card", isOpen && "ring-1 ring-blue-500/20")
  return cn("border-border/80 bg-card", isOpen && "ring-1 ring-foreground/10")
}

function stageCircleClass(state: string) {
  const normalized = state.toLowerCase()
  if (normalized === "failed") return "bg-destructive/15 text-destructive"
  if (normalized === "completed") return "bg-primary text-primary-foreground"
  if (["running", "ready", "optional"].includes(normalized)) return "bg-blue-500 text-white"
  return "bg-muted text-muted-foreground"
}

function stageBadgeClass(state: string) {
  if (state === "failed") return "bg-destructive/12 text-destructive hover:bg-destructive/12"
  if (state === "completed") return "bg-primary/12 text-primary hover:bg-primary/12"
  if (["running", "ready", "optional"].includes(state)) return "bg-blue-500/12 text-blue-500 hover:bg-blue-500/12"
  return "bg-muted text-muted-foreground hover:bg-muted"
}

function stageStateLabel(
  state: string,
  t: ReturnType<typeof useI18n>["t"],
) {
  if (state === "not-started") return t("restoreWorkspace.status.locked")
  if (state === "ready") return t("restoreWorkspace.status.available")
  if (state === "running") return t("restoreWorkspace.status.running")
  if (state === "completed") return t("restoreWorkspace.status.completed")
  if (state === "failed") return t("restoreWorkspace.status.failed")
  if (state === "optional") return t("restoreWorkspace.status.optional")
  return state.replaceAll("-", " ")
}

function StepPanel({
  step,
  workspace,
  progressionClosed,
  onViewEvidence,
  onViewLogs,
  onWorkspaceChanged,
  onSkipPrivateTest,
  onOpenPrivateTest,
  onOpenTargetSelection,
  onOpenVerification,
  onOpenHandover,
}: {
  step: VisibleStep
  workspace: RestoreWorkspaceResponse
  progressionClosed: boolean
  onViewEvidence: () => void
  onViewLogs: () => void
  onWorkspaceChanged: () => Promise<unknown>
  onSkipPrivateTest: () => void
  onOpenPrivateTest: () => void
  onOpenTargetSelection: () => void
  onOpenVerification: () => void
  onOpenHandover: () => void
}) {
  if (
    progressionClosed &&
    [
      "choose-and-create-restored-server",
      "check-restored-server",
      "complete-and-hand-over",
    ].includes(step.key)
  ) {
    return <GenericRestoreStepPanel step={step} />
  }

  if (step.key === "backup-ready") {
    return (
      <BackupReadyPanel
        step={step}
        progressionClosed={progressionClosed}
        onViewEvidence={onViewEvidence}
        onOpenPrivateTest={onOpenPrivateTest}
        onOpenTargetSelection={onOpenTargetSelection}
      />
    )
  }
  if (step.key === "private-test") {
    return (
      <PrivateRestoreTestPanel
        workspace={workspace}
        step={step}
        progressionClosed={progressionClosed}
        onViewEvidence={onViewEvidence}
        onViewLogs={onViewLogs}
        onWorkspaceChanged={onWorkspaceChanged}
        onSkipForNow={onSkipPrivateTest}
        onOpenTargetSelection={onOpenTargetSelection}
      />
    )
  }
  if (step.key === "choose-and-create-restored-server") {
    return (
      <RestoreTargetAndCreatePanel
        workspace={workspace}
        step={step}
        onWorkspaceChanged={onWorkspaceChanged}
        onContinueToVerification={onOpenVerification}
      />
    )
  }
  if (step.key === "check-restored-server") {
    return (
      <RestoredServerVerificationPanel
        workspace={workspace}
        step={step}
        onViewLogs={onViewLogs}
        onWorkspaceChanged={onWorkspaceChanged}
        onContinueToHandover={onOpenHandover}
      />
    )
  }
  if (step.key === "complete-and-hand-over") {
    return (
      <RestoreCompletionPanel
        workspace={workspace}
        step={step}
        onViewEvidence={onViewEvidence}
        onViewLogs={onViewLogs}
        onWorkspaceChanged={onWorkspaceChanged}
      />
    )
  }
  return <GenericRestoreStepPanel step={step} />
}

function BackupReadyPanel({
  step,
  progressionClosed,
  onViewEvidence,
  onOpenPrivateTest,
  onOpenTargetSelection,
}: {
  step: VisibleStep
  progressionClosed: boolean
  onViewEvidence: () => void
  onOpenPrivateTest: () => void
  onOpenTargetSelection: () => void
}) {
  const { t } = useI18n()
  const checks = [
    t("restoreWorkspace.backupReady.check.manifest"),
    t("restoreWorkspace.backupReady.check.files"),
    t("restoreWorkspace.backupReady.check.checksums"),
    t("restoreWorkspace.backupReady.check.structure"),
  ]

  return (
    <div className="grid gap-5 lg:grid-cols-2">
      <section className="flex gap-4 rounded-xl border border-primary/25 bg-gradient-to-br from-primary/[0.075] via-card to-card p-5">
        <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full border-2 border-primary text-primary">
          <CheckCircle2 className="h-6 w-6" />
        </div>
        <div>
          <div className="text-base font-semibold">{t("restoreWorkspace.backupReady.title")}</div>
          <p className="mt-2 text-sm leading-6 text-muted-foreground">{t("restoreWorkspace.backupReady.description")}</p>
          {step.summary ? <p className="mt-2 text-sm leading-6 text-muted-foreground">{step.summary}</p> : null}
          <div className="mt-5 flex flex-wrap gap-2">
            <Button variant="outline" size="sm" onClick={onViewEvidence}>
              <FileCheck2 className="mr-2 h-4 w-4" />{t("restoreWorkspace.backupReady.viewValidation")}
            </Button>
            {!progressionClosed ? (
              <>
                <Button size="sm" onClick={onOpenPrivateTest}>
                  {t("restoreWorkspace.backupReady.continuePrivateTest")} <ArrowRight className="ml-2 h-4 w-4" />
                </Button>
                <Button variant="outline" size="sm" onClick={onOpenTargetSelection}>
                  {t("restoreWorkspace.backupReady.skipTest")} <ArrowRight className="ml-2 h-4 w-4" />
                </Button>
              </>
            ) : null}
          </div>
        </div>
      </section>
      <section className="rounded-xl border border-border/80 bg-card p-5">
        <div className="text-base font-semibold">{t("restoreWorkspace.backupReady.checked")}</div>
        <ul className="mt-4 space-y-3 text-sm text-muted-foreground">
          {checks.map((check) => <li key={check} className="flex items-center gap-2"><CheckCircle2 className="h-4 w-4 shrink-0 text-primary" />{check}</li>)}
        </ul>
      </section>
    </div>
  )
}

function PrivateRestoreTestPanel({
  workspace,
  step,
  progressionClosed,
  onViewEvidence,
  onViewLogs,
  onWorkspaceChanged,
  onSkipForNow,
  onOpenTargetSelection,
}: {
  workspace: RestoreWorkspaceResponse
  step: VisibleStep
  progressionClosed: boolean
  onViewEvidence: () => void
  onViewLogs: () => void
  onWorkspaceChanged: () => Promise<unknown>
  onSkipForNow: () => void
  onOpenTargetSelection: () => void
}) {
  const { language, t } = useI18n()
  const runWorkspacePrivateTest = useRunRestoreWorkspacePrivateTest()
  const destroyPrivateStaging = useDestroyRestoreStagingRun()
  const privateTestProblem = getRestoreWorkspaceProblem(
    runWorkspacePrivateTest.error,
  )
  const localisedPrivateTestProblem = getRestoreWorkspaceProblemMessage(
    privateTestProblem,
  )
  const privateTestTechnicalDetail = getRestoreWorkspaceTechnicalDetail(
    runWorkspacePrivateTest.error,
    privateTestProblem,
  )
  const [retireTargetId, setRetireTargetId] = useState<string | null>(null)
  const [privateTestOutcomeState, setPrivateTestOutcomeState] = useState<
    "idle" | "checking" | "unconfirmed"
  >("idle")
  const [retirementOutcomeState, setRetirementOutcomeState] = useState<
    "idle" | "checking" | "unconfirmed"
  >("idle")
  const privateTestOutcomeIsChecking = privateTestOutcomeState === "checking"
  const retirementOutcomeIsChecking = retirementOutcomeState === "checking"

  // This stage action is the server-projected catalog-backed capability.
  const privateTestStage = step.sourceStages.find((stage) => stage.code === "private-test")
  const privateTestEvidence = privateTestStage?.privateTestEvidence ?? null
  const privateTestCompletedAt =
    privateTestEvidence?.completedAtUtc ??
    privateTestStage?.operationSummary?.completedAtUtc ??
    null
  const privateTestAction = privateTestStage?.primaryAction ?? null
  const privateTestIsRunning =
    step.state === "running" ||
    privateTestStage?.operationSummary?.status.toLowerCase() === "running"

  const canRun =
    !progressionClosed &&
    privateTestAction?.code === "run-private-test" &&
    privateTestAction.enabled &&
    !privateTestIsRunning &&
    !runWorkspacePrivateTest.isPending &&
    !privateTestOutcomeIsChecking

  const canRetire = Boolean(
    privateTestEvidence?.destroyAvailable &&
      privateTestEvidence.stagingId &&
      !destroyPrivateStaging.isPending &&
      !retirementOutcomeIsChecking,
  )

  const unavailableReason =
    !canRun && !privateTestEvidence && !privateTestIsRunning
      ? step.blockers.at(0) ??
        t("restoreWorkspace.privateTest.unavailable")
      : null

  const runPrivateTest = async () => {
    if (!canRun) return

    const baseline = captureRestorePrivateTestBaseline(workspace)
    setPrivateTestOutcomeState("idle")

    try {
      await runWorkspacePrivateTest.mutateAsync(workspace.restoreSessionId)
      await onWorkspaceChanged()
    } catch (error) {
      if (isHostAgentProblemError(error)) {
        // A real HTTP problem response is authoritative and remains rendered
        // through the normal safe server-problem path below.
        return
      }

      // Browser transport loss does not prove that the server rejected this
      // accepted host mutation. Reconcile briefly against the authoritative
      // workspace and let normal workspace polling follow a running operation.
      setPrivateTestOutcomeState("checking")
      const outcome = await reconcileRestorePrivateTestOutcome(
        workspace.restoreSessionId,
        baseline,
      )

      if (outcome) {
        runWorkspacePrivateTest.reset()
        setPrivateTestOutcomeState("idle")
        await onWorkspaceChanged()
        return
      }

      runWorkspacePrivateTest.reset()
      setPrivateTestOutcomeState("unconfirmed")
      await onWorkspaceChanged()
    }
  }

  const retirePrivateTest = async () => {
    if (!retireTargetId) return

    const stagingId = retireTargetId
    setRetirementOutcomeState("idle")

    try {
      await destroyPrivateStaging.mutateAsync(stagingId)
      setRetireTargetId(null)
      await onWorkspaceChanged()
    } catch (error) {
      if (isHostAgentProblemError(error)) {
        // A real HostAgent HTTP problem is authoritative. Keep the confirmation
        // dialog open and render the safe backend error below.
        return
      }

      // Retirement is a server-owned destructive mutation after acceptance.
      // Browser transport loss is not proof that cleanup failed. Reconcile
      // against the canonical Restore Workspace before making a claim.
      setRetirementOutcomeState("checking")
      const outcome = await reconcileRestorePrivateTestRetirementOutcome(
        workspace.restoreSessionId,
        stagingId,
      )

      if (outcome) {
        destroyPrivateStaging.reset()
        setRetirementOutcomeState("idle")
        setRetireTargetId(null)
        await onWorkspaceChanged()
        return
      }

      destroyPrivateStaging.reset()
      setRetirementOutcomeState("unconfirmed")
      await onWorkspaceChanged()
    }
  }

  if (progressionClosed && !privateTestEvidence) {
    return <GenericRestoreStepPanel step={step} />
  }

  if (privateTestEvidence) {
    const runtimeStatus = privateTestEvidence.stagingRuntimeStatus.toLowerCase()
    const runtimeDestroyed =
      privateTestEvidence.stagingRuntimeDestroyed === true ||
      runtimeStatus === "destroyed"
    const retirementNeedsAttention =
      runtimeStatus === "retirement-needs-attention"

    return (
      <>
        <div
          className={cn(
            "rounded-xl border p-5 shadow-sm",
            runtimeDestroyed
              ? "border-border/80 bg-card"
              : retirementNeedsAttention
                ? "border-destructive/35 bg-destructive/[0.035]"
                : "border-primary/35 bg-gradient-to-br from-primary/[0.075] via-card to-card",
          )}
        >
          <div className="flex items-start gap-3">
            {runtimeDestroyed ? (
              <Clock3 className="mt-0.5 h-6 w-6 shrink-0 text-muted-foreground" />
            ) : retirementNeedsAttention ? (
              <CircleAlert className="mt-0.5 h-6 w-6 shrink-0 text-destructive" />
            ) : (
              <CheckCircle2 className="mt-0.5 h-6 w-6 shrink-0 text-primary" />
            )}
            <div className="min-w-0 flex-1">
              <div className="font-medium">
                {runtimeDestroyed
                  ? t("restoreWorkspace.privateTest.retired")
                  : retirementNeedsAttention
                    ? t("restoreWorkspace.privateTest.retirementNeedsAttention")
                    : t("restoreWorkspace.privateTest.completed")}
              </div>
              <p className="mt-1 text-sm text-muted-foreground">{step.summary}</p>

              <dl className="mt-4 grid gap-3 text-sm sm:grid-cols-2">
                <SafeFact
                  label={t("restoreWorkspace.privateTest.matrixIdentity")}
                  value={privateTestEvidence.matrixServerName ?? t("restoreWorkspace.notRecorded")}
                />
                <SafeFact
                  label={t("restoreWorkspace.privateTest.runtime")}
                  value={
                    runtimeDestroyed
                      ? t("restoreWorkspace.status.retired")
                      : retirementNeedsAttention
                        ? t("restoreWorkspace.status.retirementNeedsAttention")
                        : t("restoreWorkspace.status.retainedPrivately")
                  }
                />
                <SafeFact
                  label={t("restoreWorkspace.privateTest.completedAt")}
                  value={
                    privateTestCompletedAt
                      ? formatDate(privateTestCompletedAt, language)
                      : t("restoreWorkspace.notRecorded")
                  }
                />
                <SafeFact
                  label={t("restoreWorkspace.privateTest.stagingReference")}
                  value={privateTestEvidence.stagingId ?? t("restoreWorkspace.notRecorded")}
                />
              </dl>

              <ul className="mt-4 space-y-2 text-sm text-muted-foreground">
                <li className="flex items-center gap-2">
                  <CheckCircle2 className="h-4 w-4 shrink-0 text-primary" />
                  {privateTestEvidence.databaseImportSucceeded
                    ? t("restoreWorkspace.privateTest.databaseRestored")
                    : t("restoreWorkspace.privateTest.databaseNotRestored")}
                </li>
                <li className="flex items-center gap-2">
                  <CheckCircle2 className="h-4 w-4 shrink-0 text-primary" />
                  {privateTestEvidence.synapseHealthPassed
                    ? t("restoreWorkspace.privateTest.synapsePassed")
                    : t("restoreWorkspace.privateTest.synapseNotPassed")}
                </li>
                <li className="flex items-center gap-2">
                  <ShieldCheck className="h-4 w-4 shrink-0 text-primary" />
                  {t("restoreWorkspace.privateTest.privateOnly")}
                </li>
              </ul>

              {retirementNeedsAttention ? (
                <Alert variant="destructive" className="mt-5">
                  <CircleAlert className="h-4 w-4" />
                  <AlertTitle>{t("restoreWorkspace.privateTest.retirementNeedsAttention")}</AlertTitle>
                  <AlertDescription>
                    {t("restoreWorkspace.privateTest.retirementNeedsAttentionDescription")}
                  </AlertDescription>
                </Alert>
              ) : null}

              <div className="mt-5 flex flex-wrap gap-2 border-t border-border/70 pt-4">
                {!progressionClosed ? (
                  <Button onClick={onOpenTargetSelection}>
                    {t("restoreWorkspace.privateTest.continueChoose")} <ArrowRight className="ml-2 h-4 w-4" />
                  </Button>
                ) : null}
                <Button variant="outline" onClick={onViewEvidence}>
                  <FileCheck2 className="mr-2 h-4 w-4" />
                  {t("restoreWorkspace.privateTest.reviewEvidence")}
                </Button>
                <Button variant="outline" onClick={onViewLogs}>
                  {t("restoreWorkspace.viewLogs")}
                </Button>
                {canRetire ? (
                  <Button
                    variant="outline"
                    onClick={() => setRetireTargetId(privateTestEvidence.stagingId)}
                    disabled={destroyPrivateStaging.isPending}
                  >
                    <Trash2 className="mr-2 h-4 w-4" />
                    {retirementNeedsAttention
                      ? t("restoreWorkspace.privateTest.retryRetire")
                      : t("restoreWorkspace.privateTest.retire")}
                  </Button>
                ) : null}
              </div>
            </div>
          </div>
        </div>

        <ConfirmationDialog
          open={retireTargetId !== null}
          onOpenChange={(open) => {
            if (!open) {
              setRetireTargetId(null)
              setRetirementOutcomeState("idle")
              destroyPrivateStaging.reset()
            }
          }}
          title={t("restoreWorkspace.privateTest.retireTitle")}
          description={t("restoreWorkspace.privateTest.retireDescription")}
          confirmLabel={t("restoreWorkspace.privateTest.retire")}
          confirmingLabel={t("restoreWorkspace.privateTest.retiring")}
          confirmVariant="destructive"
          onConfirm={retirePrivateTest}
          isConfirming={
            destroyPrivateStaging.isPending || retirementOutcomeIsChecking
          }
        >
          <Alert>
            <Trash2 className="h-4 w-4" />
            <AlertTitle>{t("restoreWorkspace.privateTest.removeTitle")}</AlertTitle>
            <AlertDescription>
              {t("restoreWorkspace.privateTest.removeDescription")}
            </AlertDescription>
          </Alert>
          {retirementOutcomeState === "checking" ? (
            <Alert>
              <LoaderCircle className="h-4 w-4 animate-spin" />
              <AlertTitle>{t("restoreWorkspace.privateTest.retirementChecking")}</AlertTitle>
              <AlertDescription>
                {t("restoreWorkspace.privateTest.retirementCheckingDescription")}
              </AlertDescription>
            </Alert>
          ) : null}
          {retirementOutcomeState === "unconfirmed" ? (
            <Alert>
              <CircleAlert className="h-4 w-4" />
              <AlertTitle>{t("restoreWorkspace.privateTest.retirementUnconfirmed")}</AlertTitle>
              <AlertDescription>
                {t("restoreWorkspace.privateTest.retirementUnconfirmedDescription")}
              </AlertDescription>
            </Alert>
          ) : null}
          {destroyPrivateStaging.error ? (
            <Alert variant="destructive">
              <AlertTitle>{t("restoreWorkspace.privateTest.retireError")}</AlertTitle>
              <AlertDescription>{destroyPrivateStaging.error.message}</AlertDescription>
            </Alert>
          ) : null}
        </ConfirmationDialog>
      </>
    )
  }

  return (
    <div className="space-y-4">
      {step.blockers.length > 0 ? (
        <Alert variant="destructive">
          <AlertCircle className="h-4 w-4" />
          <AlertTitle>{t("restoreWorkspace.privateTest.blocked")}</AlertTitle>
          <AlertDescription>
            <ul className="mt-2 list-disc space-y-1 pl-4">
              {step.blockers.map((blocker) => <li key={blocker}>{blocker}</li>)}
            </ul>
          </AlertDescription>
        </Alert>
      ) : null}

      {privateTestOutcomeState === "checking" ? (
        <Alert>
          <LoaderCircle className="h-4 w-4 animate-spin" />
          <AlertTitle>{t("restoreWorkspace.privateTest.checkingOutcome")}</AlertTitle>
          <AlertDescription>
            {t("restoreWorkspace.privateTest.checkingOutcomeDescription")}
          </AlertDescription>
        </Alert>
      ) : null}

      {privateTestOutcomeState === "unconfirmed" ? (
        <Alert>
          <CircleAlert className="h-4 w-4" />
          <AlertTitle>{t("restoreWorkspace.privateTest.outcomeUnconfirmed")}</AlertTitle>
          <AlertDescription>
            {t("restoreWorkspace.privateTest.outcomeUnconfirmedDescription")}
          </AlertDescription>
        </Alert>
      ) : null}

      {runWorkspacePrivateTest.isError ? (
        <Alert variant="destructive">
          <AlertCircle className="h-4 w-4" />
          <AlertTitle>{t("restoreWorkspace.privateTest.startError")}</AlertTitle>
          <AlertDescription>
            <p>
              {localisedPrivateTestProblem
                ? t(
                    localisedPrivateTestProblem.key,
                    localisedPrivateTestProblem.values,
                  )
                : t("restoreWorkspace.privateTest.startErrorDescription")}
            </p>
            <RestoreWorkspaceTechnicalDetails detail={privateTestTechnicalDetail} />
          </AlertDescription>
        </Alert>
      ) : null}

      {privateTestIsRunning ? (
        <Alert>
          <LoaderCircle className="h-4 w-4 animate-spin" />
          <AlertTitle>{t("restoreWorkspace.privateTest.running")}</AlertTitle>
          <AlertDescription>
            {t("restoreWorkspace.privateTest.runningDescription")}
          </AlertDescription>
        </Alert>
      ) : null}

      <div className="grid gap-5 min-[1500px]:grid-cols-[minmax(0,1.1fr)_minmax(0,0.9fr)]">
        <section className="min-w-0 rounded-xl border border-primary/20 bg-gradient-to-br from-primary/[0.075] via-card to-card p-5">
          <div className="flex items-start gap-4">
            <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-primary/12 text-primary">
              <FlaskConical className="h-6 w-6" />
            </div>
            <div className="min-w-0">
              <h3 className="text-base font-semibold">{t("restoreWorkspace.privateTest.whatDoes")}</h3>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">
                {t("restoreWorkspace.privateTest.whatDoesDescription")}
              </p>
            </div>
          </div>
          <ul className="mt-5 space-y-3 text-sm text-muted-foreground">
            <li className="flex gap-2">
              <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
              {t("restoreWorkspace.privateTest.noPublic")}
            </li>
            <li className="flex gap-2">
              <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
              {t("restoreWorkspace.privateTest.noExistingImpact")}
            </li>
            <li className="flex gap-2">
              <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
              {t("restoreWorkspace.privateTest.reviewAfter")}
            </li>
          </ul>
        </section>

        <section className="min-w-0 rounded-xl border border-border/80 bg-card p-5">
          <h3 className="text-base font-semibold">{t("restoreWorkspace.privateTest.whatYouGet")}</h3>
          <ul className="mt-4 space-y-2.5 text-sm text-muted-foreground">
            <li className="flex gap-2">
              <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
              {t("restoreWorkspace.privateTest.databaseImport")}
            </li>
            <li className="flex gap-2">
              <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
              {t("restoreWorkspace.privateTest.synapseStartup")}
            </li>
            <li className="flex gap-2">
              <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
              {t("restoreWorkspace.privateTest.configuration")}
            </li>
            <li className="flex gap-2">
              <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
              {t("restoreWorkspace.privateTest.durableEvidence")}
            </li>
          </ul>
          <div className="mt-5 grid gap-2 border-t border-border/70 pt-4">
            <Button className="w-full" onClick={() => void runPrivateTest()} disabled={!canRun}>
              {runWorkspacePrivateTest.isPending || privateTestOutcomeIsChecking ? (
                <LoaderCircle className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <FlaskConical className="mr-2 h-4 w-4" />
              )}
              {step.state === "failed" ? t("restoreWorkspace.privateTest.retry") : t("restoreWorkspace.privateTest.run")}
              <ArrowRight className="ml-2 h-4 w-4" />
            </Button>
            <Button
              variant="outline"
              className="w-full"
              onClick={onSkipForNow}
              disabled={
                runWorkspacePrivateTest.isPending || privateTestOutcomeIsChecking
              }
            >
              {t("restoreWorkspace.privateTest.skip")}
            </Button>
          </div>
          {unavailableReason ? (
            <p className="mt-3 text-xs text-muted-foreground">{unavailableReason}</p>
          ) : null}
        </section>
      </div>
    </div>
  )
}

function getRestoredServerCheckDefinitions(
  t: ReturnType<typeof useI18n>["t"],
) {
  return [
    {
      code: "host-agent.npm.ready",
      title: t("restoreWorkspace.verify.title.gateway"),
      description: t("restoreWorkspace.verify.description.gateway"),
    },
    {
      code: "host-agent.matrix.public-route.configured",
      title: t("restoreWorkspace.verify.title.matrixRoute"),
      description: t("restoreWorkspace.verify.description.matrixRoute"),
    },
    {
      code: "host-agent.element.public-route.configured",
      title: t("restoreWorkspace.verify.title.elementRoute"),
      description: t("restoreWorkspace.verify.description.elementRoute"),
    },
    {
      code: "host-agent.matrix.internal-http.reachable",
      title: t("restoreWorkspace.verify.title.matrixInternal"),
      description: t("restoreWorkspace.verify.description.matrixInternal"),
    },
    {
      code: "host-agent.element.internal-http.reachable",
      title: t("restoreWorkspace.verify.title.elementInternal"),
      description: t("restoreWorkspace.verify.description.elementInternal"),
    },
    {
      code: "host-agent.matrix.public-https.reachable",
      title: t("restoreWorkspace.verify.title.matrixPublic"),
      description: t("restoreWorkspace.verify.description.matrixPublic"),
    },
    {
      code: "host-agent.element.public-https.reachable",
      title: t("restoreWorkspace.verify.title.elementPublic"),
      description: t("restoreWorkspace.verify.description.elementPublic"),
    },
  ] as const
}

function RestoredServerVerificationPanel({
  workspace,
  step,
  onViewLogs,
  onWorkspaceChanged,
  onContinueToHandover,
}: {
  workspace: RestoreWorkspaceResponse
  step: VisibleStep
  onViewLogs: () => void
  onWorkspaceChanged: () => Promise<unknown>
  onContinueToHandover: () => void
}) {
  const { language, t } = useI18n()
  const restoredServerCheckDefinitions = getRestoredServerCheckDefinitions(t)
  const runChecks = useRunRestoredServerChecks()
  const targetStackSlug = workspace.target.stackSlug?.trim() ?? ""
  const verification = workspace.verification
  const canRun = Boolean(targetStackSlug) &&
    !runChecks.isPending &&
    !["blocked", "cancelled", "not-started"].includes(step.state)

  const latestChecks = new Map(
    verification.checks.map((check) => [check.code, check]),
  )

  const resultRows = [
    ...restoredServerCheckDefinitions.map((definition) => ({
      code: definition.code,
      title: definition.title,
      description: definition.description,
      result: latestChecks.get(definition.code),
    })),
    ...verification.checks
      .filter((check) => !restoredServerCheckDefinitions.some(
        (definition) => definition.code === check.code,
      ))
      .map((check) => ({
        code: check.code,
        title: check.title,
        description: t("restoreWorkspace.verify.additional"),
        result: check,
      })),
  ]

  const runVerification = async () => {
    if (!canRun) return
    await runChecks.mutateAsync(targetStackSlug)
    await onWorkspaceChanged()
  }

  if (["blocked", "cancelled", "not-started"].includes(step.state)) {
    return <GenericRestoreStepPanel step={step} />
  }

  const elementUrl = workspace.target.elementHost
    ? `https://${workspace.target.elementHost}`
    : null

  return (
    <div className="space-y-4">
      {runChecks.isError ? (
        <Alert variant="destructive">
          <AlertCircle className="h-4 w-4" />
          <AlertTitle>{t("restoreWorkspace.verify.error")}</AlertTitle>
          <AlertDescription>
            {t("restoreWorkspace.verify.errorDescription")}
          </AlertDescription>
        </Alert>
      ) : null}

      <div className="grid min-w-0 gap-5 min-[1500px]:grid-cols-2 min-[1800px]:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_18rem]">
        <section className="min-w-0 rounded-xl border border-border/80 bg-card p-5 shadow-sm">
          <div className="flex items-start gap-3">
            {runChecks.isPending ? (
              <LoaderCircle className="mt-0.5 h-5 w-5 shrink-0 animate-spin text-primary" />
            ) : (
              <ShieldCheck className="mt-0.5 h-5 w-5 shrink-0 text-primary" />
            )}
            <div>
              <h3 className="font-semibold">{t("restoreWorkspace.verify.checkingTitle")}</h3>
              <p className="mt-1 text-sm text-muted-foreground">
                {t("restoreWorkspace.verify.checkingDescription")}
              </p>
            </div>
          </div>

          <ul className="mt-5 space-y-4">
            {restoredServerCheckDefinitions.map((definition) => {
              const result = latestChecks.get(definition.code)
              const pending = runChecks.isPending
              const passed = result?.status === "passed"
              const failed = result?.status === "failed"

              return (
                <li key={definition.code} className="flex gap-3">
                  {pending ? (
                    <LoaderCircle className="mt-0.5 h-4 w-4 shrink-0 animate-spin text-blue-600 dark:text-blue-400" />
                  ) : failed ? (
                    <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-destructive" />
                  ) : passed ? (
                    <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
                  ) : (
                    <Clock3 className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" />
                  )}
                  <div className="min-w-0">
                    <p className={failed ? "font-medium text-destructive" : "font-medium"}>
                      {definition.title}
                    </p>
                    <p className="mt-0.5 text-xs text-muted-foreground">
                      {definition.description}
                    </p>
                  </div>
                </li>
              )
            })}
          </ul>
        </section>

        <section className="min-w-0 rounded-xl border border-border/80 bg-card p-5 shadow-sm">
          <div className="flex items-start gap-3">
            {verification.hasRun && verification.allPassed ? (
              <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-primary" />
            ) : verification.hasRun && verification.allPassed === false ? (
              <AlertCircle className="mt-0.5 h-5 w-5 shrink-0 text-destructive" />
            ) : (
              <Clock3 className="mt-0.5 h-5 w-5 shrink-0 text-blue-600 dark:text-blue-400" />
            )}
            <div>
              <h3 className="font-semibold">{t("restoreWorkspace.verify.results")}</h3>
              <p className="mt-1 text-sm text-muted-foreground">
                {runChecks.isPending
                  ? t("restoreWorkspace.verify.running")
                  : verification.summary}
              </p>
            </div>
          </div>

          {verification.hasRun || runChecks.isPending ? (
            <ul className="mt-5 space-y-3 text-sm">
              {resultRows.map((row) => {
                const passed = row.result?.status === "passed"
                const failed = row.result?.status === "failed"
                const label = runChecks.isPending
                  ? t("restoreWorkspace.verify.runningLabel")
                  : passed
                    ? t("restoreWorkspace.verify.passed")
                    : failed
                      ? t("restoreWorkspace.verify.needsAttention")
                      : t("restoreWorkspace.notRecorded")

                return (
                  <li key={row.code} className="flex items-start justify-between gap-3">
                    <span className="min-w-0 text-muted-foreground">{row.title}</span>
                    <span className={cn(
                      "shrink-0 font-medium",
                      passed && "text-primary",
                      failed && "text-destructive",
                      !passed && !failed && "text-muted-foreground",
                    )}>
                      {label}
                    </span>
                  </li>
                )
              })}
            </ul>
          ) : (
            <div className="mt-5 rounded-md border border-dashed border-border/70 p-3 text-sm text-muted-foreground">
              {t("restoreWorkspace.verify.noFreshChecks")}
            </div>
          )}

          {verification.hasRun ? (
            <dl className="mt-5 grid gap-3 border-t border-border/70 pt-4 text-sm sm:grid-cols-2">
              <SafeFact label={t("restoreWorkspace.verify.checked")} value={verification.checkedAtUtc ? formatDate(verification.checkedAtUtc, language) : t("restoreWorkspace.notRecorded")} />
              <SafeFact
                label={t("restoreWorkspace.verify.outcome")}
                value={verification.allPassed ? t("restoreWorkspace.verify.allPassed") : t("restoreWorkspace.verify.reviewFailed")}
              />
            </dl>
          ) : null}

          <div className="mt-5 flex flex-wrap gap-2 border-t border-border/70 pt-4">
            {verification.hasRun && verification.allPassed ? (
              <Button onClick={onContinueToHandover}>
                {t("restoreWorkspace.verify.continue")} <ArrowRight className="ml-2 h-4 w-4" />
              </Button>
            ) : null}
            <Button
              variant={verification.hasRun && verification.allPassed ? "outline" : "default"}
              onClick={() => void runVerification()}
              disabled={!canRun}
            >
              {runChecks.isPending ? (
                <LoaderCircle className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <RefreshCw className="mr-2 h-4 w-4" />
              )}
              {verification.hasRun ? t("restoreWorkspace.verify.runAgain") : t("restoreWorkspace.verify.run")}
            </Button>
          </div>
        </section>

        <aside className="min-w-0 rounded-xl border border-blue-500/30 bg-gradient-to-br from-blue-500/[0.085] via-card to-card p-5 shadow-sm min-[1500px]:col-span-2 min-[1800px]:col-span-1">
          <div className="flex items-center gap-2 font-medium text-blue-700 dark:text-blue-300">
            <ShieldCheck className="h-4 w-4" />
            {t("restoreWorkspace.verify.expect")}
          </div>
          <ul className="mt-4 space-y-3 text-sm text-muted-foreground">
            <li>{t("restoreWorkspace.verify.expect.credentials")}</li>
            <li>{t("restoreWorkspace.verify.expect.signIn")}</li>
            <li>{t("restoreWorkspace.verify.expect.upload")}</li>
          </ul>

          <div className="mt-5 space-y-2 border-t border-blue-500/20 pt-4">
            {elementUrl ? (
              <Button variant="outline" size="sm" className="w-full justify-start" asChild>
                <a href={elementUrl} target="_blank" rel="noreferrer">
                  {t("restoreWorkspace.openElement")} <ExternalLink className="ml-2 h-4 w-4" />
                </a>
              </Button>
            ) : null}
            <Button variant="outline" size="sm" className="w-full justify-start" onClick={onViewLogs}>
              {t("restoreWorkspace.viewLogs")}
            </Button>
          </div>
        </aside>
      </div>

      <p className="text-xs text-muted-foreground">
        {t("restoreWorkspace.verify.operatorAcceptance")}
      </p>
    </div>
  )
}

function SafeFact({ label, value }: { label: string; value: string }) {
  return <div><dt className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</dt><dd className="mt-1">{value}</dd></div>
}

function GenericRestoreStepPanel({ step }: { step: VisibleStep }) {
  const { t } = useI18n()
  const isFailed = step.state === "failed"
  const isBlocked = step.state === "blocked" || step.state === "not-started"
  return (
    <div className={cn("rounded-lg border p-4", isFailed && "border-destructive/40 bg-destructive/5")}>
      <div className="flex items-start gap-3"><StatusIcon severity={step.state} /><div className="min-w-0"><div className="font-medium">{isFailed ? t("restoreWorkspace.generic.needsAttention") : isBlocked ? t("restoreWorkspace.generic.notAvailableYet") : t("restoreWorkspace.generic.details")}</div>{step.summary ? <p className="mt-1 text-sm text-muted-foreground">{step.summary}</p> : <p className="mt-1 text-sm text-muted-foreground">{step.description}</p>}{step.blockers.length > 0 ? <ul className="mt-3 list-disc space-y-1 pl-5 text-sm text-muted-foreground">{step.blockers.map((blocker) => <li key={blocker}>{blocker}</li>)}</ul> : null}<p className="mt-3 text-xs text-muted-foreground">{t("restoreWorkspace.generic.mappingNote")}</p></div></div>
    </div>
  )
}

function StatusIcon({ severity }: { severity: string }) {
  const normalized = severity.trim().toLowerCase()
  if (["error", "failed", "danger"].includes(normalized)) return <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-destructive" aria-hidden="true" />
  if (["warning", "warn", "caution"].includes(normalized)) return <CircleAlert className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" aria-hidden="true" />
  if (["success", "completed", "ok"].includes(normalized)) return <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
  return <Clock3 className="mt-0.5 h-4 w-4 shrink-0 text-blue-600 dark:text-blue-400" aria-hidden="true" />
}


function RestoreCompletionPanel({
  workspace,
  step,
  onViewEvidence,
  onViewLogs,
  onWorkspaceChanged,
}: {
  workspace: RestoreWorkspaceResponse
  step: VisibleStep
  onViewEvidence: () => void
  onViewLogs: () => void
  onWorkspaceChanged: () => Promise<unknown>
}) {
  const { t } = useI18n()
  const completeHandover = useCompleteRestoreHandover()
  const handoverProblem = getRestoreWorkspaceProblem(completeHandover.error)
  const localisedHandoverProblem = getRestoreWorkspaceProblemMessage(
    handoverProblem,
  )
  const handoverTechnicalDetail = getRestoreWorkspaceTechnicalDetail(
    completeHandover.error,
    handoverProblem,
  )
  const isCompleted = workspace.attempt.status.toLowerCase() === "completed" || step.state === "completed"
  const canComplete = step.state === "ready" && !completeHandover.isPending
  const stackHref = workspace.target.stackSlug
    ? `/stacks/${encodeURIComponent(workspace.target.stackSlug)}`
    : null
  const doctorHref = stackHref ? `${stackHref}?doctor=1` : null
  const elementUrl = workspace.target.elementHost
    ? `https://${workspace.target.elementHost}`
    : null

  const complete = async () => {
    if (!canComplete) return

    try {
      await completeHandover.mutateAsync(workspace.restoreSessionId)
      await onWorkspaceChanged()
    } catch {
      // Mutation state renders the safe operator-facing error below.
    }
  }

  if (!isCompleted) {
    return (
      <div className="grid gap-5 min-[1500px]:grid-cols-[minmax(0,1fr)_18rem]">
        <section className="rounded-xl border border-primary/25 bg-gradient-to-br from-primary/[0.06] via-card to-card p-5 shadow-sm">
          <div className="flex items-start gap-3">
            <CheckCircle2 className="mt-0.5 h-6 w-6 shrink-0 text-primary" />
            <div>
              <h3 className="font-medium">{t("restoreWorkspace.handover.ready")}</h3>
              <p className="mt-1 text-sm text-muted-foreground">
                {t("restoreWorkspace.handover.readyDescription")}
              </p>
            </div>
          </div>
          <p className="mt-4 text-sm text-muted-foreground">
            {t("restoreWorkspace.handover.noChange")}
          </p>
          {completeHandover.isError ? (
            <Alert variant="destructive" className="mt-4">
              <AlertCircle className="h-4 w-4" />
              <AlertTitle>{t("restoreWorkspace.handover.error")}</AlertTitle>
              <AlertDescription>
                <p>
                  {localisedHandoverProblem
                    ? t(
                        localisedHandoverProblem.key,
                        localisedHandoverProblem.values,
                      )
                    : t("restoreWorkspace.handover.errorDescription")}
                </p>
                <RestoreWorkspaceTechnicalDetails
                  detail={handoverTechnicalDetail}
                />
              </AlertDescription>
            </Alert>
          ) : null}
          <Button className="mt-5" onClick={() => void complete()} disabled={!canComplete}>
            {completeHandover.isPending ? <LoaderCircle className="mr-2 h-4 w-4 animate-spin" /> : <CheckCircle2 className="mr-2 h-4 w-4" />}
            {t("restoreWorkspace.handover.complete")}
          </Button>
        </section>
        <aside className="rounded-xl border border-blue-500/30 bg-gradient-to-br from-blue-500/[0.075] via-card to-card p-5 shadow-sm">
          <div className="flex items-center gap-2 font-medium text-blue-700 dark:text-blue-300"><ShieldCheck className="h-4 w-4" />{t("restoreWorkspace.handover.before")}</div>
          <p className="mt-3 text-sm text-muted-foreground">
            {t("restoreWorkspace.handover.beforeDescription")}
          </p>
          <div className="mt-4 flex flex-col gap-2">
            {stackHref ? (
              <Button variant="outline" size="sm" asChild>
                <Link to={stackHref}>{t("restoreWorkspace.openRestoredStack")}<ArrowRight className="ml-2 h-4 w-4" /></Link>
              </Button>
            ) : null}
            {elementUrl ? <Button variant="outline" size="sm" asChild><a href={elementUrl} target="_blank" rel="noreferrer">{t("restoreWorkspace.openElement")}<ExternalLink className="ml-2 h-4 w-4" /></a></Button> : null}
            <Button variant="outline" size="sm" onClick={onViewLogs}>{t("restoreWorkspace.viewLogs")}</Button>
          </div>
        </aside>
      </div>
    )
  }

  return (
    <div className="grid gap-5 min-[1500px]:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
      <section className="flex min-h-64 flex-col justify-center rounded-xl border border-primary/30 bg-gradient-to-br from-primary/[0.1] via-card to-card p-6 text-center shadow-sm">
        <div className="mx-auto flex h-16 w-16 items-center justify-center rounded-full border-2 border-primary">
          <CheckCircle2 className="h-9 w-9 text-primary" />
        </div>
        <h3 className="mt-5 text-xl font-semibold">{t("restoreWorkspace.handover.completeTitle")}</h3>
        <p className="mx-auto mt-2 max-w-md text-sm text-muted-foreground">
          {t("restoreWorkspace.handover.completeDescription")}
        </p>
        <div className="mx-auto mt-5 flex w-full max-w-sm flex-col gap-2">
          {stackHref ? (
            <Button asChild>
              <Link to={stackHref}>{t("restoreWorkspace.openRestoredStack")}<ArrowRight className="ml-2 h-4 w-4" /></Link>
            </Button>
          ) : elementUrl ? (
            <Button asChild><a href={elementUrl} target="_blank" rel="noreferrer">{t("restoreWorkspace.openElement")}<ExternalLink className="ml-2 h-4 w-4" /></a></Button>
          ) : null}
          {elementUrl && stackHref ? (
            <Button variant="outline" asChild>
              <a href={elementUrl} target="_blank" rel="noreferrer">{t("restoreWorkspace.openElement")}<ExternalLink className="ml-2 h-4 w-4" /></a>
            </Button>
          ) : null}
          <Button variant="outline" onClick={onViewEvidence}>{t("restoreWorkspace.handover.viewEvidence")}<ArrowRight className="ml-2 h-4 w-4" /></Button>
        </div>
      </section>
      <section className="rounded-xl border border-border/80 bg-card p-5 shadow-sm">
        <h3 className="font-medium">{t("restoreWorkspace.handover.next")}</h3>
        <div className="mt-4 space-y-2">
          {elementUrl ? (
            <CompletionLink icon={Users} title={t("restoreWorkspace.handover.inviteUsers")} detail={t("restoreWorkspace.handover.inviteUsersDetail")} href={elementUrl} />
          ) : null}
          {stackHref ? (
            <CompletionLink icon={Settings2} title={t("restoreWorkspace.handover.reviewStack")} detail={t("restoreWorkspace.handover.reviewStackDetail")} to={stackHref} />
          ) : null}
          {doctorHref ? (
            <CompletionLink icon={MonitorUp} title={t("restoreWorkspace.handover.runDoctor")} detail={t("restoreWorkspace.handover.runDoctorDetail")} to={doctorHref} />
          ) : null}
          <CompletionLink icon={FileCheck2} title={t("restoreWorkspace.handover.reviewEvidence")} detail={t("restoreWorkspace.handover.reviewEvidenceDetail")} onClick={onViewEvidence} />
        </div>
      </section>
    </div>
  )
}

function CompletionLink({
  icon: Icon,
  title,
  detail,
  href,
  to,
  onClick,
}: {
  icon: typeof Users
  title: string
  detail: string
  href?: string | null
  to?: string | null
  onClick?: () => void
}) {
  const body = <><Icon className="h-5 w-5 shrink-0 text-muted-foreground" /><span className="min-w-0 flex-1 text-left"><span className="block font-medium">{title}</span><span className="mt-0.5 block text-sm text-muted-foreground">{detail}</span></span><ArrowRight className="h-4 w-4 text-muted-foreground" /></>
  const className = "flex w-full items-center gap-3 rounded-lg border border-border/70 p-3 text-left transition-colors hover:bg-muted/50"

  if (to) return <Link className={className} to={to}>{body}</Link>
  if (href) return <a className={className} href={href} target="_blank" rel="noreferrer">{body}</a>
  return <button type="button" className={className} onClick={onClick}>{body}</button>
}

