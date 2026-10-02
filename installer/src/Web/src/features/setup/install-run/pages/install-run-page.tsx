import {
  AlertTriangle,
  CheckCircle2,
  FileDown,
  LifeBuoy,
  Loader2,
} from "lucide-react"
import { useState } from "react"
import { Link, useNavigate, useParams } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"

import { SetupStepLayout } from "@/features/setup/components/setup-step-layout"
import { cn } from "@/lib/utils"
import { useInstallWorkflow } from "../hooks/use-install-workflow"
import { InstallStatusPill } from "../components/install-status-pill"
import { InstallActionsBar } from "../components/install-actions-bar"
import { InstallBanner } from "../components/install-banner"
import { InstallStepList } from "../components/install-step-list"
import { InstallCurrentStepCard } from "../components/install-current-step-card"
import { InstallOperationProgressCard } from "../components/install-operation-progress-card"
import { InstallCompletedOperationChecklist } from "../components/install-completed-operation-checklist"
import { InstallFailureGuidancePanel } from "../components/install-failure-guidance-panel"
import { InstallNpmCredentialPanel } from "../components/install-npm-credential-panel"
import { getInstallFailureGuidance } from "../api/install-failure-guidance"
import type { WorkflowStep } from "../api/install.types"
import {
  downloadInstallationSupportReport,
  saveSetupSupportReportDownload,
} from "@/features/setup/support-report/api/setup-support-report.api"

export function InstallRunPage() {
  const navigate = useNavigate()
  const { installationId } = useParams()
  const { t, intlLocale } = useI18n()
  const [technicalOpen, setTechnicalOpen] = useState(false)
  const install = useInstallWorkflow(installationId)

  const workflow = install.workflow
  const steps = workflow?.steps ?? []
  const currentStep =
    steps.find((step) => step.status === "Running") ??
    steps.find((step) => step.status === "WaitingForUser") ??
    steps.find((step) => step.status === "Failed") ??
    null

  const status = getWorkflowStatus(workflow)
  const failureGuidance = getInstallFailureGuidance(
    currentStep,
    install.installation?.configJson,
  )
  const actionHref = getRequiredActionHref(
    installationId,
    currentStep?.title,
    workflow?.lastError,
  )
  const npmCredentialAttention = getNpmCredentialAttention(currentStep)
  const handoffCompleted =
    steps.length > 0 && steps.every((step) => step.status === "Succeeded")

  return (
    <SetupStepLayout
      title={t("setup.activity.title")}
      description={t("setup.activity.description")}
      showStepNav={false}
    >
      <div className="space-y-6">
        <Card className="border-primary/20 bg-primary/5">
          <CardHeader className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
            <div>
              <CardTitle>{t(getActivityTitleKey(status))}</CardTitle>
              <p className="mt-2 max-w-3xl text-sm text-muted-foreground">
                {t(
                  status === "Succeeded" && handoffCompleted
                    ? "setup.activity.activityDescription.completedHandoff"
                    : getActivityDescriptionKey(status),
                )}
              </p>
            </div>

            <InstallStatusPill status={status} />
          </CardHeader>

          <CardContent className="space-y-6">
            {status !== "Succeeded" ? <InstallSummaryCards steps={steps} /> : null}

            {status === "Running" && currentStep && (
              <InstallOperationProgressCard
                step={currentStep}
                setupStartedAtUtc={workflow?.startedAtUtc}
              />
            )}

            {status === "Failed" && failureGuidance && (
              <InstallFailureGuidancePanel guidance={failureGuidance} />
            )}

            {status === "WaitingForUser" && npmCredentialAttention ? (
              <InstallNpmCredentialPanel
                key={`${currentStep?.order ?? 0}-${currentStep?.attemptCount ?? 0}`}
                errorCode={npmCredentialAttention}
                onContinue={install.startOrContinue}
                isContinuePending={install.isContinuePending}
              />
            ) : status === "WaitingForUser" ? (
              <RequiredActionPanel
                actionHref={actionHref}
                currentStepTitle={currentStep?.title}
                message={currentStep?.message ?? workflow?.lastError}
              />
            ) : null}

            {status === "Succeeded" && (
              <CompletedInstallPanel
                installationId={installationId}
                handoffCompleted={handoffCompleted}
                steps={steps}
                onViewDetails={() => setTechnicalOpen(true)}
              />
            )}

            {status === "WaitingForUser" && (
              <div className="flex flex-wrap gap-3">
                <Button variant="outline" onClick={() => navigate("/diagnostics")}>
                  {t("setup.activity.runDiagnostics")}
                </Button>
                <SetupSupportReportButton installationId={installationId} />
                <TroubleshootingButton installationId={installationId} />
              </div>
            )}

            {status !== "Succeeded" && status !== "WaitingForUser" && (
              <div className="flex flex-wrap gap-3">
                <InstallActionsBar
                  workflow={workflow}
                  onContinue={install.startOrContinue}
                  onRefresh={install.refresh}
                  isContinuePending={install.isContinuePending}
                />

                <Button variant="outline" onClick={() => navigate("/diagnostics")}>
                  {t("setup.activity.runDiagnostics")}
                </Button>

                {status === "Failed" && (
                  <>
                    <SetupSupportReportButton installationId={installationId} />
                    <TroubleshootingButton installationId={installationId} />
                  </>
                )}
              </div>
            )}
          </CardContent>
        </Card>

        <details
          id="installation-operation-history"
          open={technicalOpen}
          onToggle={(event) => setTechnicalOpen(event.currentTarget.open)}
          className="rounded-2xl border border-border bg-card/95 shadow-sm"
        >
          <summary className="cursor-pointer px-6 py-4 text-base font-semibold tracking-tight">
            {t(
              status === "Succeeded"
                ? "setup.activity.installationDetails"
                : "setup.activity.technicalProgress",
            )}
          </summary>

          <div className="space-y-6 border-t border-border px-6 py-6">
            <InstallBanner
              workflow={workflow}
              lastRunMessage={install.lastRunMessage}
              isContinuePending={install.isContinuePending}
            />

            {workflow && (
              <div className="rounded-lg border border-border/80 bg-background/30 p-4 text-sm">
                <div className="grid gap-2 md:grid-cols-2">
                  <InfoRow
                    label={t("setup.activity.started")}
                    value={formatDateTime(workflow.startedAtUtc, intlLocale)}
                  />
                  <InfoRow
                    label={t("setup.activity.completed")}
                    value={formatDateTime(workflow.completedAtUtc, intlLocale)}
                  />
                  <InfoRow
                    label={t("setup.activity.lastError")}
                    value={workflow.lastError ?? "-"}
                  />
                  <InfoRow
                    label={t("setup.activity.technicalReference")}
                    value={workflow.installationId}
                    breakAll
                  />
                </div>
              </div>
            )}

            {status !== "Succeeded" && (
              <InstallCurrentStepCard
                step={currentStep}
                failedPhase={failureGuidance?.failedPhase}
              />
            )}

            <InstallStepList steps={steps} />

            <div className="flex flex-wrap gap-3">
              <InstallActionsBar
                workflow={workflow}
                onContinue={install.startOrContinue}
                onRefresh={install.refresh}
                isContinuePending={install.isContinuePending}
              />
            </div>
          </div>
        </details>
      </div>
    </SetupStepLayout>
  )
}

function InstallSummaryCards({
  steps,
}: {
  steps: Array<{ status: string }>
}) {
  const { t } = useI18n()
  const completed = steps.filter((step) => step.status === "Succeeded").length
  const running = steps.filter((step) => step.status === "Running").length
  const waiting = steps.filter((step) => step.status === "WaitingForUser").length
  const failed = steps.filter((step) => step.status === "Failed").length

  return (
    <div className="grid gap-3 md:grid-cols-4">
      <SummaryCard label={t("setup.activity.summaryCompleted")} value={completed} />
      <SummaryCard label={t("setup.activity.summaryRunning")} value={running} />
      <SummaryCard label={t("setup.activity.summaryAttention")} value={waiting} />
      <SummaryCard label={t("setup.activity.summaryFailed")} value={failed} />
    </div>
  )
}

function SummaryCard({
  label,
  value,
}: {
  label: string
  value: number
}) {
  return (
    <div className="rounded-lg border border-border/80 bg-background/30 p-3">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className="mt-1 text-2xl font-semibold">{value}</div>
    </div>
  )
}

function RequiredActionPanel({
  actionHref,
  currentStepTitle,
  message,
}: {
  actionHref: string
  currentStepTitle: string | undefined
  message: string | null | undefined
}) {
  const { t } = useI18n()
  const domainAction = actionHref === "/setup/domain"

  return (
    <Card className="border-amber-500/30 bg-amber-500/10">
      <CardHeader>
        <div className="flex items-start gap-3">
          <div className="mt-0.5 rounded-full border border-amber-500/30 bg-amber-500/10 p-2 text-amber-300">
            <AlertTriangle className="h-5 w-5" />
          </div>

          <div className="space-y-1">
            <CardTitle className="text-base">{t("setup.activity.actionRequired")}</CardTitle>
            <p className="text-sm text-muted-foreground">
              {t("setup.activity.actionDescription")}
            </p>
          </div>
        </div>
      </CardHeader>

      <CardContent className="space-y-4">
        <div className="grid gap-3 md:grid-cols-2">
          <div className="rounded-lg border border-border/80 bg-background/30 p-3">
            <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
              {t("setup.activity.currentCheck")}
            </div>
            <div className="mt-1 text-sm font-medium">
              {currentStepTitle ?? t("setup.activity.platformCheck")}
            </div>
          </div>

          <div className="rounded-lg border border-border/80 bg-background/30 p-3">
            <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
              {t("setup.activity.recommendedAction")}
            </div>
            <div className="mt-1 text-sm font-medium">
              {domainAction
                ? t("setup.activity.openDomain")
                : t("setup.activity.reviewRequiredStep")}
            </div>
          </div>
        </div>

        {message && (
          <div className="rounded-lg border border-amber-500/30 bg-background/30 p-3 text-sm">
            {message}
          </div>
        )}

        <div className="flex flex-wrap gap-3">
          <Button asChild>
            <Link to={actionHref}>
              {domainAction
                ? t("setup.activity.openDomain")
                : t("setup.activity.openRequiredStep")}
            </Link>
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}

function CompletedInstallPanel({
  installationId,
  handoffCompleted,
  steps,
  onViewDetails,
}: {
  installationId: string | undefined
  handoffCompleted: boolean
  steps: WorkflowStep[]
  onViewDetails: () => void
}) {
  const navigate = useNavigate()
  const { t } = useI18n()

  return (
    <Card className="border-emerald-500/30 bg-emerald-500/10">
      <CardHeader>
        <div className="flex items-start gap-3">
          <div className="mt-0.5 rounded-full border border-emerald-500/30 bg-emerald-500/10 p-2 text-emerald-300">
            <CheckCircle2 className="h-5 w-5" />
          </div>

          <div className="space-y-1">
            <CardTitle className="text-base">
              {t(
                handoffCompleted
                  ? "setup.activity.setupComplete"
                  : "setup.activity.readyFinish",
              )}
            </CardTitle>
            <p className="text-sm text-muted-foreground">
              {t(
                handoffCompleted
                  ? "setup.activity.setupCompleteDescription"
                  : "setup.activity.readyFinishDescription",
              )}
            </p>
          </div>
        </div>
      </CardHeader>

      <CardContent className="space-y-5">
        <InstallCompletedOperationChecklist steps={steps} />

        <div className="flex flex-wrap gap-3">
          <Button
            onClick={() =>
              navigate(
                handoffCompleted
                  ? "/dashboard"
                  : `/setup/handoff/${installationId}`,
              )
            }
            disabled={!installationId}
          >
            {t(
              handoffCompleted
                ? "setup.activity.openDashboard"
                : "setup.activity.continueFinish",
            )}
          </Button>

          <Button
            variant="outline"
            onClick={() => navigate(`/setup/verify/${installationId}`)}
            disabled={!installationId}
          >
            {t("setup.common.viewVerificationReport")}
          </Button>

          <Button
            type="button"
            variant="outline"
            onClick={onViewDetails}
          >
            {t("setup.activity.viewInstallationDetails")}
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}

function TroubleshootingButton({
  installationId,
}: {
  installationId: string | undefined
}) {
  const { t } = useI18n()
  const href = installationId
    ? `/setup/troubleshooting?installationId=${encodeURIComponent(installationId)}`
    : "/setup/troubleshooting"

  return (
    <Button asChild variant="outline">
      <Link to={href}>
        <LifeBuoy className="mr-2 h-4 w-4" />
        {t("setup.activity.troubleshoot")}
      </Link>
    </Button>
  )
}

function SetupSupportReportButton({
  installationId,
}: {
  installationId: string | undefined
}) {
  const { t } = useI18n()
  const [isDownloading, setIsDownloading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function download() {
    if (!installationId || isDownloading) return

    setIsDownloading(true)
    setError(null)
    try {
      const result = await downloadInstallationSupportReport(installationId, {
        includeDockerEvidence: true,
        format: "json",
      })
      saveSetupSupportReportDownload(result)
    } catch (err) {
      setError(err instanceof Error ? err.message : t("setup.troubleshooting.downloadFailed"))
    } finally {
      setIsDownloading(false)
    }
  }

  return (
    <div className="flex flex-col gap-1">
      <Button
        type="button"
        variant="outline"
        disabled={!installationId || isDownloading}
        onClick={() => void download()}
      >
        {isDownloading ? (
          <Loader2 className="mr-2 h-4 w-4 animate-spin" />
        ) : (
          <FileDown className="mr-2 h-4 w-4" />
        )}
        {isDownloading
          ? t("setup.common.preparingReport")
          : t("setup.common.downloadSupportReport")}
      </Button>
      {error ? (
        <span className="max-w-sm text-xs text-destructive">{error}</span>
      ) : null}
    </div>
  )
}

function getActivityTitleKey(status: string): TranslationKey {
  switch (status) {
    case "Running":
      return "setup.activity.activityTitle.running"
    case "WaitingForUser":
      return "setup.activity.activityTitle.waiting"
    case "Succeeded":
      return "setup.activity.activityTitle.succeeded"
    case "Failed":
      return "setup.activity.activityTitle.failed"
    default:
      return "setup.activity.activityTitle.unknown"
  }
}

function getActivityDescriptionKey(status: string): TranslationKey {
  switch (status) {
    case "Running":
      return "setup.activity.activityDescription.running"
    case "WaitingForUser":
      return "setup.activity.activityDescription.waiting"
    case "Succeeded":
      return "setup.activity.activityDescription.succeeded"
    case "Failed":
      return "setup.activity.activityDescription.failed"
    default:
      return "setup.activity.activityDescription.unknown"
  }
}

function getNpmCredentialAttention(
  step: { errorMessage?: string | null } | null,
): "NpmAdminCredentialRequired" | "NpmCredentialsRejected" | null {
  const error = step?.errorMessage?.trim() ?? ""

  if (error.startsWith("NpmAdminCredentialRequired:")) {
    return "NpmAdminCredentialRequired"
  }

  if (error.startsWith("NpmCredentialsRejected:")) {
    return "NpmCredentialsRejected"
  }

  return null
}

function getRequiredActionHref(
  installationId: string | undefined,
  currentStepTitle: string | undefined,
  lastError: string | null | undefined,
) {
  const text = `${currentStepTitle ?? ""} ${lastError ?? ""}`.toLowerCase()

  if (
    text.includes("domain") ||
    text.includes("certificate") ||
    text.includes("npm") ||
    text.includes("ingress") ||
    text.includes("proxy host") ||
    text.includes("tls")
  ) {
    return "/setup/domain"
  }

  if (installationId) {
    return `/setup/install/${installationId}`
  }

  return "/setup/install"
}

function InfoRow({
  label,
  value,
  breakAll = false,
}: {
  label: string
  value: string
  breakAll?: boolean
}) {
  return (
    <div>
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className={cn("mt-1 text-sm", breakAll && "break-all")}>
        {value}
      </div>
    </div>
  )
}

function formatDateTime(value: string | null | undefined, intlLocale: string) {
  if (!value) return "-"

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value

  return date.toLocaleString(intlLocale)
}

function getWorkflowStatus(workflow: unknown) {
  if (!workflow || typeof workflow !== "object") {
    return "Unknown"
  }

  const value =
    getStringProperty(workflow, "status") ??
    getStringProperty(workflow, "workflowStatus") ??
    getStringProperty(workflow, "installationStatus") ??
    getStringProperty(workflow, "state")

  return value ?? "Unknown"
}

function getStringProperty(value: object, key: string) {
  const record = value as Record<string, unknown>
  const property = record[key]

  return typeof property === "string" ? property : null
}
