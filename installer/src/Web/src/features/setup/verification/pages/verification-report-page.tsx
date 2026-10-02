import { useMemo } from "react"
import {
  ArrowLeft,
  ArrowRight,
  CheckCircle2,
  CircleAlert,
  CircleHelp,
  Clock3,
  RefreshCw,
} from "lucide-react"
import { useNavigate, useParams } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import type {
  VerificationCheckResult,
  VerificationEvidence,
  VerificationStatus,
} from "@/features/setup/verification/api/verification.types"
import { useVerificationReport } from "@/features/setup/verification/hooks/use-verification-report"
import { InstallCompletedOperationChecklist } from "@/features/setup/install-run/components/install-completed-operation-checklist"
import { InstallStepList } from "@/features/setup/install-run/components/install-step-list"
import { useInstallationSteps } from "@/features/setup/install-run/hooks/use-installation-run"
import { toWorkflowStep } from "@/features/setup/install-run/hooks/use-install-workflow"

export function VerificationReportPage() {
  const navigate = useNavigate()
  const { installationId } = useParams()
  const { t, intlLocale } = useI18n()

  const reportQuery = useVerificationReport(installationId)
  const report = reportQuery.data
  const installationStepsQuery = useInstallationSteps(installationId, false)
  const installationSteps = useMemo(
    () => (installationStepsQuery.data ?? []).map(toWorkflowStep),
    [installationStepsQuery.data],
  )
  const handoffCompleted =
    installationSteps.length > 0 &&
    installationSteps.every((step) => step.status === "Succeeded")

  const installHref = installationId
    ? `/setup/install/${installationId}`
    : "/setup/install"

  const handoffHref = installationId
    ? `/setup/handoff/${installationId}`
    : "/setup/install"

  return (
    <div className="mx-auto grid w-full max-w-6xl gap-6 px-6 py-8">
      <div>
        <PageBreadcrumbs
          items={[
            { label: t("navigation.setup.start"), to: "/setup/start" },
            { label: t("navigation.setup.installMem"), to: installHref },
            { label: t("navigation.setup.verify") },
          ]}
        />

        <Button
          type="button"
          variant="ghost"
          className="mb-4 px-0"
          onClick={() => navigate(installHref)}
        >
          <ArrowLeft className="mr-2 h-4 w-4" />
          {t("setup.verify.backActivity")}
        </Button>

        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight">
              {t("setup.verify.title")}
            </h1>
            <p className="mt-2 text-muted-foreground">
              {t("setup.verify.description")}
            </p>
          </div>

          <Button
            type="button"
            variant="outline"
            onClick={() => void reportQuery.refetch()}
            disabled={!installationId || reportQuery.isFetching}
          >
            <RefreshCw className="mr-2 h-4 w-4" />
            {t("setup.verify.refresh")}
          </Button>
        </div>
      </div>

      {!installationId ? (
        <Alert variant="destructive">
          <CircleAlert className="h-4 w-4" />
          <AlertTitle>{t("setup.verify.unavailableTitle")}</AlertTitle>
          <AlertDescription>{t("setup.verify.noId")}</AlertDescription>
        </Alert>
      ) : null}

      {reportQuery.isLoading ? (
        <Card>
          <CardContent className="py-8 text-sm text-muted-foreground">
            {t("setup.verify.loading")}
          </CardContent>
        </Card>
      ) : null}

      {reportQuery.error ? (
        <Alert variant="destructive">
          <CircleAlert className="h-4 w-4" />
          <AlertTitle>{t("setup.verify.failedTitle")}</AlertTitle>
          <AlertDescription>
            {getErrorMessage(reportQuery.error)}
          </AlertDescription>
        </Alert>
      ) : null}

      {report ? (
        <>
          <Card>
            <CardHeader>
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div>
                  <CardTitle>{t("setup.verify.platformTitle")}</CardTitle>
                  <CardDescription>
                    {t("setup.verify.platformDescription")}
                  </CardDescription>
                </div>

                <StatusBadge status={report.status} />
              </div>
            </CardHeader>

            <CardContent className="space-y-4">
              <Alert
                variant={report.status === "Failed" ? "destructive" : "default"}
              >
                <StatusIcon status={report.status} />
                <AlertTitle>{t(verificationStatusTranslationKey(report.status))}</AlertTitle>
                <AlertDescription>{report.message}</AlertDescription>
              </Alert>

              <div className="grid gap-3 md:grid-cols-4">
                <SummaryTile
                  label={t("setup.verify.succeeded")}
                  value={countByStatus(report.checks, "Succeeded")}
                />
                <SummaryTile
                  label={t("setup.verify.warnings")}
                  value={countByStatus(report.checks, "Warning")}
                />
                <SummaryTile
                  label={t("setup.verify.pending")}
                  value={countByStatus(report.checks, "Pending")}
                />
                <SummaryTile
                  label={t("setup.verify.failed")}
                  value={countByStatus(report.checks, "Failed")}
                />
              </div>

              <div className="text-xs text-muted-foreground">
                {t("setup.verify.lastChecked", {
                  value: formatDateTime(report.checkedAtUtc, intlLocale),
                })}
              </div>
            </CardContent>
          </Card>

          <div className="grid gap-4">
            {report.checks.map((check) => (
              <VerificationCheckCard key={check.key} check={check} />
            ))}
          </div>
        </>
      ) : null}

      {installationId ? (
        <Card>
          <CardHeader>
            <CardTitle>{t("setup.verify.installationHistoryTitle")}</CardTitle>
            <CardDescription>
              {t("setup.verify.installationHistoryDescription")}
            </CardDescription>
          </CardHeader>
          <CardContent>
            {installationStepsQuery.isLoading ? (
              <div className="text-sm text-muted-foreground">
                {t("setup.verify.installationHistoryLoading")}
              </div>
            ) : installationStepsQuery.error ? (
              <Alert variant="destructive">
                <CircleAlert className="h-4 w-4" />
                <AlertTitle>{t("setup.verify.installationHistoryFailed")}</AlertTitle>
                <AlertDescription>
                  {getErrorMessage(installationStepsQuery.error)}
                </AlertDescription>
              </Alert>
            ) : (
              <div className="space-y-4">
                <InstallCompletedOperationChecklist steps={installationSteps} />

                <details className="rounded-xl border border-border bg-background/40">
                  <summary className="cursor-pointer px-4 py-3 text-sm font-medium">
                    {t("setup.activity.installationDetails")}
                  </summary>
                  <div className="border-t border-border px-4 py-4">
                    <InstallStepList steps={installationSteps} />
                  </div>
                </details>
              </div>
            )}
          </CardContent>
        </Card>
      ) : null}

      <div className="flex flex-wrap items-center justify-between gap-3">
        <Button
          type="button"
          variant="outline"
          onClick={() => navigate(installHref)}
        >
          <ArrowLeft className="mr-2 h-4 w-4" />
          {t("setup.verify.back")}
        </Button>

        {installationStepsQuery.isSuccess ? (
          <Button
            type="button"
            disabled={!installationId}
            onClick={() => navigate(handoffCompleted ? "/dashboard" : handoffHref)}
          >
            {t(
              handoffCompleted
                ? "setup.verify.openDashboard"
                : "setup.verify.continueFinish",
            )}
            <ArrowRight className="ml-2 h-4 w-4" />
          </Button>
        ) : null}
      </div>
    </div>
  )
}

function VerificationCheckCard({ check }: { check: VerificationCheckResult }) {
  const { t } = useI18n()

  return (
    <Card className={cardClassForStatus(check.status)}>
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="flex items-start gap-3">
            <div className="mt-0.5 flex h-10 w-10 shrink-0 items-center justify-center rounded-full border border-border bg-background">
              <StatusIcon status={check.status} />
            </div>

            <div>
              <CardTitle className="text-base">{check.title}</CardTitle>
              <CardDescription>{check.description}</CardDescription>
            </div>
          </div>

          <StatusBadge status={check.status} />
        </div>
      </CardHeader>

      <CardContent className="space-y-4">
        <p className="text-sm">{check.message}</p>

        {check.evidence.length > 0 ? (
          <details className="rounded-xl border border-border bg-background/40 p-4">
            <summary className="cursor-pointer text-sm font-medium">
              {t("setup.verify.evidence", { count: check.evidence.length })}
            </summary>

            <div className="mt-3 grid gap-2">
              {check.evidence.map((item, index) => (
                <EvidenceRow key={`${item.key}-${index}`} item={item} />
              ))}
            </div>
          </details>
        ) : null}
      </CardContent>
    </Card>
  )
}

function EvidenceRow({ item }: { item: VerificationEvidence }) {
  const { t } = useI18n()

  return (
    <div className="grid gap-2 rounded-lg border border-border/70 bg-muted/30 p-3 text-xs md:grid-cols-[minmax(0,0.4fr)_minmax(0,1fr)_auto]">
      <div className="font-medium">{item.key}</div>
      <div className="break-all text-muted-foreground">
        {item.sensitive ? t("setup.verify.sensitive") : item.value}
      </div>
      {item.status ? <Badge variant="outline">{item.status}</Badge> : null}
    </div>
  )
}

function StatusBadge({ status }: { status: VerificationStatus }) {
  const { t } = useI18n()
  return (
    <Badge variant={badgeVariantForStatus(status)}>
      {t(verificationStatusTranslationKey(status))}
    </Badge>
  )
}

function StatusIcon({ status }: { status: VerificationStatus }) {
  if (status === "Succeeded") {
    return <CheckCircle2 className="h-5 w-5" />
  }

  if (status === "Failed") {
    return <CircleAlert className="h-5 w-5" />
  }

  if (status === "Pending" || status === "Skipped") {
    return <Clock3 className="h-5 w-5" />
  }

  return <CircleHelp className="h-5 w-5" />
}

function SummaryTile({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-xl border border-border bg-background/40 p-4">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className="mt-2 text-2xl font-semibold">{value}</div>
    </div>
  )
}

function countByStatus(
  checks: VerificationCheckResult[],
  status: VerificationStatus,
) {
  return checks.filter((check) => check.status === status).length
}

function verificationStatusTranslationKey(status: VerificationStatus): TranslationKey {
  switch (status) {
    case "Succeeded":
      return "setup.verify.status.succeeded"
    case "Warning":
      return "setup.verify.status.warning"
    case "Pending":
      return "setup.verify.status.pending"
    case "Failed":
      return "setup.verify.status.failed"
    case "Skipped":
      return "setup.verify.status.skipped"
  }
}

function badgeVariantForStatus(status: VerificationStatus) {
  switch (status) {
    case "Succeeded":
      return "secondary"
    case "Failed":
      return "destructive"
    case "Warning":
      return "outline"
    case "Pending":
    case "Skipped":
    default:
      return "outline"
  }
}

function cardClassForStatus(status: VerificationStatus) {
  switch (status) {
    case "Succeeded":
      return "border-emerald-500/30 bg-emerald-950/20"
    case "Failed":
      return "border-destructive/40"
    case "Warning":
      return "border-yellow-500/30"
    case "Pending":
    case "Skipped":
    default:
      return ""
  }
}

function getErrorMessage(error: unknown): string {
  if (error instanceof Error) return error.message
  if (typeof error === "string") return error

  try {
    return JSON.stringify(error)
  } catch {
    return String(error)
  }
}

function formatDateTime(value: string, intlLocale: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value

  return date.toLocaleString(intlLocale)
}
