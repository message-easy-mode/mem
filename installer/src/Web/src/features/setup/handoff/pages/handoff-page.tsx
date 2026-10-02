import { useEffect, useState } from "react"
import { Link, useNavigate, useParams } from "react-router-dom"
import {
  AlertTriangle,
  ArrowLeft,
  CheckCircle2,
  ClipboardList,
  FileDown,
  LifeBuoy,
  Loader2,
  LockKeyhole,
  Server,
  ShieldCheck,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  downloadInstallationSupportReport,
  saveSetupSupportReportDownload,
} from "@/features/setup/support-report/api/setup-support-report.api"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"

type SetupHandoffResponse = {
  installationId: string
  status: "ready" | "not_ready" | string
  message: string
  operatorDashboardPath: string
  baseDomain: string | null
  certificateCommonName: string | null
  certificateId: string | null
  isStagingCertificate: boolean
  certificateExpiresAtUtc: string | null
  npmCertificateId: number | null
  postgresContainerName: string
  npmContainerName: string
  coturnContainerName: string
  handoffRequired: boolean
  handoffCompleted: boolean
  warnings: string[]
}

export function HandoffPage() {
  const navigate = useNavigate()
  const { installationId } = useParams()
  const { t } = useI18n()
  const id = installationId ?? "current"

  const [handoff, setHandoff] = useState<SetupHandoffResponse | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [completionError, setCompletionError] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [isCompleting, setIsCompleting] = useState(false)
  const [isDownloadingReport, setIsDownloadingReport] = useState(false)
  const [reportError, setReportError] = useState<string | null>(null)

  const verifyHref = installationId
    ? `/setup/verify/${installationId}`
    : "/setup/install"

  const installHref = installationId
    ? `/setup/install/${installationId}`
    : "/setup/install"

  useEffect(() => {
    let cancelled = false

    async function loadHandoff() {
      setIsLoading(true)
      setError(null)

      try {
        const response = await fetch(`/api/setup/install-runs/${id}/handoff`, {
          method: "GET",
          credentials: "include",
          headers: {
            Accept: "application/json",
          },
        })

        if (!response.ok) {
          const text = await response.text().catch(() => "")
          throw new Error(
            `Failed to load handoff details for ${id}. Status ${response.status}${
              text ? `: ${text}` : ""
            }`,
          )
        }

        const data = (await response.json()) as SetupHandoffResponse

        if (!cancelled) {
          setHandoff(data)
        }
      } catch (err) {
        if (!cancelled) {
          setError(getErrorMessage(err, t("setup.finish.unknownError")))
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false)
        }
      }
    }

    void loadHandoff()

    return () => {
      cancelled = true
    }
  }, [id, t])

  async function downloadReport() {
    if (!installationId || isDownloadingReport) {
      return
    }

    setIsDownloadingReport(true)
    setReportError(null)
    try {
      const download = await downloadInstallationSupportReport(installationId, {
        includeDockerEvidence: true,
        format: "json",
      })
      saveSetupSupportReportDownload(download)
    } catch (err) {
      setReportError(getErrorMessage(err, t("setup.finish.reportFailed")))
    } finally {
      setIsDownloadingReport(false)
    }
  }

  async function completeHandoff() {
    if (!installationId || !handoff?.handoffRequired || handoff.handoffCompleted) {
      return
    }

    setIsCompleting(true)
    setCompletionError(null)

    try {
      const response = await fetch(
        `/api/setup/install-runs/${installationId}/handoff/complete`,
        {
          method: "POST",
          credentials: "include",
          headers: {
            Accept: "application/json",
          },
        },
      )

      if (!response.ok) {
        const text = await response.text().catch(() => "")
        throw new Error(
          `Failed to finish setup handoff. Status ${response.status}${
            text ? `: ${text}` : ""
          }`,
        )
      }

      navigate(handoff.operatorDashboardPath || "/dashboard", { replace: true })
    } catch (err) {
      setCompletionError(getErrorMessage(err, t("setup.finish.handoffFailedTitle")))
    } finally {
      setIsCompleting(false)
    }
  }

  if (isLoading) {
    return (
      <div className="flex min-h-[320px] items-center justify-center">
        <div className="flex items-center gap-2 text-sm text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" />
          {t("setup.finish.loading")}
        </div>
      </div>
    )
  }

  if (error) {
    return (
      <HandoffFrame installHref={installHref} verifyHref={verifyHref}>
        <Card className="border-destructive/30 bg-destructive/10">
          <CardHeader>
            <CardTitle>{t("setup.finish.loadFailedTitle")}</CardTitle>
            <CardDescription>{error}</CardDescription>
          </CardHeader>
        </Card>
      </HandoffFrame>
    )
  }

  if (!handoff) {
    return (
      <HandoffFrame installHref={installHref} verifyHref={verifyHref}>
        <Card className="border-amber-500/30 bg-amber-500/10">
          <CardHeader>
            <CardTitle>{t("setup.finish.unavailableTitle")}</CardTitle>
            <CardDescription>{t("setup.finish.unavailableDescription")}</CardDescription>
          </CardHeader>
          <CardContent>
            <Button asChild variant="outline">
              <Link to={verifyHref}>{t("setup.finish.viewVerification")}</Link>
            </Button>
          </CardContent>
        </Card>
      </HandoffFrame>
    )
  }

  const ready = handoff.status === "ready"
  const pendingHandoff = handoff.handoffRequired && !handoff.handoffCompleted

  return (
    <div className="mx-auto grid w-full max-w-6xl gap-6 px-6 py-8">
      <div className="space-y-2">
        <PageBreadcrumbs
          items={[
            { label: t("navigation.setup.start"), to: "/setup/start" },
            { label: t("navigation.setup.installMem"), to: installHref },
            { label: t("navigation.setup.verify"), to: verifyHref },
            { label: t("navigation.setup.finish") },
          ]}
        />

        <Button asChild variant="ghost" size="sm" className="-ml-3">
          <Link to={verifyHref}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            {t("setup.finish.backVerification")}
          </Link>
        </Button>

        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight">
              {pendingHandoff ? t("setup.finish.pendingTitle") : t("setup.finish.title")}
            </h1>
            <p className="max-w-3xl text-sm text-muted-foreground">
              {pendingHandoff
                ? t("setup.finish.pendingDescription")
                : t("setup.finish.description")}
            </p>
          </div>

          <Badge
            variant="outline"
            className={
              ready
                ? "border-primary/30 bg-primary/10"
                : "border-amber-500/30 bg-amber-500/10"
            }
          >
            {pendingHandoff
              ? t("setup.finish.badgeFinish")
              : ready
                ? t("setup.finish.badgeReady")
                : t("setup.finish.badgeReview")}
          </Badge>
        </div>
      </div>

      <Card className="border-primary/20 bg-primary/5">
        <CardHeader>
          <div className="flex items-start gap-3">
            <div className="rounded-full border border-primary/30 bg-primary/10 p-2">
              <CheckCircle2 className="h-5 w-5 text-primary" />
            </div>
            <div>
              <CardTitle>
                {pendingHandoff ? t("setup.finish.readyTitle") : t("setup.finish.privateTitle")}
              </CardTitle>
              <CardDescription>
                {pendingHandoff
                  ? t("setup.finish.pendingDescription")
                  : t("setup.finish.privateDescription")}
              </CardDescription>
            </div>
          </div>
        </CardHeader>

        <CardContent className="flex flex-wrap gap-2">
          {handoff.handoffRequired && !handoff.handoffCompleted ? (
            <Button
              type="button"
              disabled={!ready || isCompleting}
              onClick={() => void completeHandoff()}
            >
              {isCompleting
                ? t("setup.finish.finishing")
                : t("setup.finish.finishOpenDashboard")}
            </Button>
          ) : (
            <Button asChild disabled={!ready}>
              <Link to={handoff.operatorDashboardPath || "/dashboard"}>
                {t("setup.finish.openDashboard")}
              </Link>
            </Button>
          )}

          <Button asChild variant="outline">
            <Link to="/diagnostics">{t("setup.finish.runDiagnostics")}</Link>
          </Button>

          <Button asChild variant="outline">
            <Link to={verifyHref}>{t("setup.finish.viewVerification")}</Link>
          </Button>
        </CardContent>
      </Card>

      {completionError ? (
        <Alert variant="destructive">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("setup.finish.handoffFailedTitle")}</AlertTitle>
          <AlertDescription>{completionError}</AlertDescription>
        </Alert>
      ) : null}

      {handoff.warnings.length > 0 ? (
        <Card className="border-amber-500/30 bg-amber-500/10">
          <CardHeader>
            <div className="flex items-start gap-3">
              <div className="rounded-full border border-amber-500/30 bg-amber-500/10 p-2">
                <AlertTriangle className="h-5 w-5" />
              </div>
              <div>
                <CardTitle className="text-base">{t("setup.finish.itemsToReview")}</CardTitle>
                <CardDescription>{t("setup.finish.itemsDescription")}</CardDescription>
              </div>
            </div>
          </CardHeader>
          <CardContent>
            <ul className="list-disc space-y-2 pl-5 text-sm text-muted-foreground">
              {handoff.warnings.map((warning) => (
                <li key={warning}>{warning}</li>
              ))}
            </ul>
          </CardContent>
        </Card>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-3">
        <HandoffInfoCard
          icon={ClipboardList}
          title={t("setup.finish.recommendedNext")}
          items={[
            t("setup.finish.next.owner"),
            t("setup.finish.next.dashboard"),
            t("setup.finish.next.stack"),
            t("setup.finish.next.diagnostics"),
          ]}
        />

        <HandoffInfoCard
          icon={ShieldCheck}
          title={t("setup.finish.securityBoundary")}
          items={[
            t("setup.finish.security.private"),
            t("setup.finish.security.noPublish"),
            t("setup.finish.security.portal"),
            t("setup.finish.security.retired"),
          ]}
        />

        <HandoffInfoCard
          icon={LifeBuoy}
          title={t("setup.finish.recoveryNotes")}
          items={[
            t("setup.finish.recovery.codes"),
            t("setup.finish.recovery.address"),
            t("setup.finish.recovery.backups"),
            t("setup.finish.recovery.legacy"),
          ]}
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <div className="flex items-center gap-2">
              <Server className="h-5 w-5" />
              <CardTitle className="text-base">{t("setup.finish.requiredDependencies")}</CardTitle>
            </div>
            <CardDescription>{t("setup.finish.requiredDependenciesDescription")}</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-2 text-sm">
            <ServiceStatus
              label={t("setup.finish.postgres")}
              value={t("setup.finish.serviceInstalled", { name: handoff.postgresContainerName })}
            />
            <ServiceStatus
              label={t("setup.finish.npm")}
              value={t("setup.finish.serviceInstalled", { name: handoff.npmContainerName })}
            />
            <ServiceStatus
              label={t("setup.finish.coturn")}
              value={t("setup.finish.serviceInstalled", { name: handoff.coturnContainerName })}
            />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <div className="flex items-center gap-2">
              <LockKeyhole className="h-5 w-5" />
              <CardTitle className="text-base">{t("setup.finish.publicCertificate")}</CardTitle>
            </div>
            <CardDescription>{t("setup.finish.publicCertificateDescription")}</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-2 text-sm">
            <ServiceStatus
              label={t("setup.finish.baseDomain")}
              value={handoff.baseDomain ?? t("setup.finish.notConfigured")}
            />
            <ServiceStatus
              label={t("setup.finish.certificate")}
              value={handoff.certificateCommonName ?? t("setup.finish.notConfigured")}
            />
            <ServiceStatus
              label={t("setup.finish.certificateType")}
              value={!handoff.certificateId || !handoff.certificateCommonName
                ? t("setup.finish.notConfigured")
                : handoff.isStagingCertificate
                  ? t("setup.finish.certStaging")
                  : t("setup.finish.certProduction")}
            />
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">{t("setup.finish.supportReport")}</CardTitle>
          <CardDescription>{t("setup.finish.supportReportDescription")}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          {reportError ? (
            <Alert variant="destructive">
              <AlertTriangle className="h-4 w-4" />
              <AlertTitle>{t("setup.finish.reportFailed")}</AlertTitle>
              <AlertDescription>{reportError}</AlertDescription>
            </Alert>
          ) : null}
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              variant="outline"
              disabled={!installationId || isDownloadingReport}
              onClick={() => void downloadReport()}
            >
              {isDownloadingReport ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <FileDown className="mr-2 h-4 w-4" />
              )}
              {isDownloadingReport
                ? t("setup.common.preparingReport")
                : t("setup.common.downloadSupportReport")}
            </Button>
            <Button asChild variant="outline">
              <Link to={verifyHref}>{t("setup.finish.viewVerification")}</Link>
            </Button>
          </div>
          <p className="text-xs text-muted-foreground">
            {t("setup.finish.reportOmissions")}
          </p>
        </CardContent>
      </Card>
    </div>
  )
}

function HandoffFrame({
  installHref,
  verifyHref,
  children,
}: {
  installHref: string
  verifyHref: string
  children: React.ReactNode
}) {
  const { t } = useI18n()

  return (
    <div className="mx-auto grid w-full max-w-6xl gap-6 px-6 py-8">
      <PageBreadcrumbs
        items={[
          { label: t("navigation.setup.start"), to: "/setup/start" },
          { label: t("navigation.setup.installMem"), to: installHref },
          { label: t("navigation.setup.verify"), to: verifyHref },
          { label: t("navigation.setup.finish") },
        ]}
      />
      <Button asChild variant="ghost" size="sm" className="-ml-3">
        <Link to={verifyHref}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          {t("setup.finish.backVerification")}
        </Link>
      </Button>
      {children}
    </div>
  )
}

function getErrorMessage(error: unknown, fallback: string) {
  if (error instanceof Error) {
    return error.message
  }

  if (typeof error === "string") {
    return error
  }

  return fallback
}

function ServiceStatus({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between gap-3 rounded-lg border border-border bg-background/40 px-3 py-2">
      <span className="text-muted-foreground">{label}</span>
      <span className="text-right font-medium">{value}</span>
    </div>
  )
}

function HandoffInfoCard({
  icon: Icon,
  title,
  items,
}: {
  icon: React.ComponentType<{ className?: string }>
  title: string
  items: string[]
}) {
  return (
    <Card>
      <CardHeader>
        <div className="flex items-center gap-2">
          <Icon className="h-5 w-5" />
          <CardTitle className="text-base">{title}</CardTitle>
        </div>
      </CardHeader>
      <CardContent>
        <ul className="list-disc space-y-2 pl-5 text-sm text-muted-foreground">
          {items.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </ul>
      </CardContent>
    </Card>
  )
}
