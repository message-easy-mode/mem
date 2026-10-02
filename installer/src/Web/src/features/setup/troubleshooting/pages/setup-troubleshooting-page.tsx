import { useEffect, useMemo, useState } from "react"
import {
  Check,
  Clipboard,
  FileDown,
  LifeBuoy,
  Loader2,
  TerminalSquare,
} from "lucide-react"
import { Link, useSearchParams } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { SetupStepLayout } from "@/features/setup/components/setup-step-layout"
import { getPlatformStatus } from "@/features/setup/start/api/platform-status-api"
import {
  downloadInstallationSupportReport,
  saveSetupSupportReportDownload,
} from "@/features/setup/support-report/api/setup-support-report.api"

import { buildSetupHostSupportCommands } from "../setup-host-support-command"

type RuntimeProjection = {
  runtimeMode: string
}

export function SetupTroubleshootingPage() {
  const { t } = useI18n()
  const [searchParams] = useSearchParams()
  const requestedInstallationId = searchParams.get("installationId")?.trim() || null
  const [runtimeMode, setRuntimeMode] = useState<string | null>(null)
  const [activeInstallationId, setActiveInstallationId] = useState<string | null>(
    requestedInstallationId,
  )
  const [isLoading, setIsLoading] = useState(true)
  const [loadWarning, setLoadWarning] = useState<string | null>(null)
  const [isDownloading, setIsDownloading] = useState(false)
  const [downloadError, setDownloadError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false

    async function loadContext() {
      setIsLoading(true)
      setLoadWarning(null)

      const [runtimeResult, platformResult] = await Promise.allSettled([
        fetch("/health/runtime", {
          method: "GET",
          credentials: "include",
          headers: { Accept: "application/json" },
        }).then(async (response) => {
          if (!response.ok) {
            throw new Error(`Runtime preflight returned HTTP ${response.status}.`)
          }

          return (await response.json()) as RuntimeProjection
        }),
        getPlatformStatus(),
      ])

      if (cancelled) return

      if (runtimeResult.status === "fulfilled") {
        setRuntimeMode(runtimeResult.value.runtimeMode)
      }

      if (!requestedInstallationId && platformResult.status === "fulfilled") {
        setActiveInstallationId(
          platformResult.value.activeInstallationId?.trim() || null,
        )
      }

      if (
        runtimeResult.status === "rejected" ||
        platformResult.status === "rejected"
      ) {
        setLoadWarning(t("setup.troubleshooting.contextUnavailableDescription"))
      }

      setIsLoading(false)
    }

    void loadContext()

    return () => {
      cancelled = true
    }
  }, [requestedInstallationId, t])

  const commands = useMemo(
    () => buildSetupHostSupportCommands(runtimeMode, activeInstallationId),
    [runtimeMode, activeInstallationId],
  )

  const installationHref = activeInstallationId
    ? `/setup/install/${activeInstallationId}`
    : "/setup/start"

  async function downloadReport() {
    if (!activeInstallationId || isDownloading) return

    setIsDownloading(true)
    setDownloadError(null)

    try {
      const result = await downloadInstallationSupportReport(activeInstallationId, {
        includeDockerEvidence: true,
        format: "json",
      })
      saveSetupSupportReportDownload(result)
    } catch (error) {
      setDownloadError(getErrorMessage(error, t("setup.troubleshooting.downloadFailed")))
    } finally {
      setIsDownloading(false)
    }
  }

  return (
    <SetupStepLayout
      title={t("setup.troubleshooting.title")}
      description={t("setup.troubleshooting.description")}
    >
      <div className="space-y-6">
        {loadWarning ? (
          <Alert>
            <LifeBuoy className="h-4 w-4" />
            <AlertTitle>{t("setup.troubleshooting.contextUnavailableTitle")}</AlertTitle>
            <AlertDescription>{loadWarning}</AlertDescription>
          </Alert>
        ) : null}

        <Card className="border-sky-500/30 bg-sky-500/5">
          <CardHeader>
            <div className="flex items-start gap-3">
              <div className="rounded-full border border-sky-500/30 bg-sky-500/10 p-2">
                <LifeBuoy className="h-5 w-5 text-sky-300" />
              </div>
              <div>
                <CardTitle>{t("setup.troubleshooting.reportTitle")}</CardTitle>
                <CardDescription>{t("setup.troubleshooting.reportDescription")}</CardDescription>
              </div>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-3 text-sm sm:grid-cols-2">
              <InfoValue
                label={t("setup.troubleshooting.runtime")}
                value={isLoading ? t("setup.troubleshooting.loading") : runtimeMode ?? t("setup.common.unavailable")}
              />
              <InfoValue
                label={t("setup.troubleshooting.installation")}
                value={
                  isLoading
                    ? t("setup.troubleshooting.loading")
                    : activeInstallationId ?? t("setup.troubleshooting.noActiveInstallation")
                }
                breakAll
              />
            </div>

            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                disabled={!activeInstallationId || isDownloading}
                onClick={() => void downloadReport()}
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

              <Button asChild variant="outline">
                <Link to={installationHref}>
                  {activeInstallationId
                    ? t("setup.troubleshooting.returnActivity")
                    : t("setup.troubleshooting.returnStart")}
                </Link>
              </Button>

              <Button asChild variant="outline">
                <Link to="/docs/installation/troubleshooting">
                  {t("setup.troubleshooting.openDocs")}
                </Link>
              </Button>
            </div>

            {downloadError ? (
              <p className="text-sm text-destructive">{downloadError}</p>
            ) : null}
          </CardContent>
        </Card>

        <div className="grid gap-4 lg:grid-cols-2">
          <CommandCard
            title={t("setup.troubleshooting.hostCommandTitle")}
            description={t("setup.troubleshooting.hostCommandDescription")}
            command={commands.reportCommand}
          />
          <CommandCard
            title={t("setup.troubleshooting.rawLogsTitle")}
            description={t("setup.troubleshooting.rawLogsDescription")}
            command={commands.rawLogsCommand}
          />
        </div>

        <Card>
          <CardHeader>
            <CardTitle>{t("setup.troubleshooting.recoveryTitle")}</CardTitle>
            <CardDescription>{t("setup.troubleshooting.recoveryDescription")}</CardDescription>
          </CardHeader>
          <CardContent>
            <ul className="list-disc space-y-2 pl-5 text-sm text-muted-foreground">
              <li>{t("setup.troubleshooting.ruleVolumes")}</li>
              <li>{t("setup.troubleshooting.ruleRetry")}</li>
              <li>{t("setup.troubleshooting.ruleCompleted")}</li>
              <li>{t("setup.troubleshooting.ruleReport")}</li>
            </ul>
          </CardContent>
        </Card>
      </div>
    </SetupStepLayout>
  )
}

function CommandCard({
  title,
  description,
  command,
}: {
  title: string
  description: string
  command: string | null
}) {
  const { t } = useI18n()
  const [copyState, setCopyState] = useState<"idle" | "copied" | "failed">("idle")

  async function copyCommand() {
    if (!command || !navigator.clipboard?.writeText) {
      setCopyState("failed")
      return
    }

    try {
      await navigator.clipboard.writeText(command)
      setCopyState("copied")
    } catch {
      setCopyState("failed")
    }
  }

  return (
    <Card>
      <CardHeader>
        <div className="flex items-start gap-3">
          <div className="rounded-full border border-border bg-muted/40 p-2">
            <TerminalSquare className="h-5 w-5" />
          </div>
          <div>
            <CardTitle className="text-base">{title}</CardTitle>
            <CardDescription>{description}</CardDescription>
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        {command ? (
          <>
            <pre className="overflow-x-auto whitespace-pre-wrap break-words rounded-lg border border-border bg-background/50 p-3 text-xs">
              <code>{command}</code>
            </pre>
            <div className="flex items-center gap-3">
              <Button type="button" variant="outline" size="sm" onClick={() => void copyCommand()}>
                {copyState === "copied" ? (
                  <Check className="mr-2 h-4 w-4" />
                ) : (
                  <Clipboard className="mr-2 h-4 w-4" />
                )}
                {copyState === "copied"
                  ? t("setup.troubleshooting.copied")
                  : t("setup.troubleshooting.copy")}
              </Button>
              <span className="text-xs text-muted-foreground" role="status" aria-live="polite">
                {copyState === "failed" ? t("setup.troubleshooting.copyFailed") : ""}
              </span>
            </div>
          </>
        ) : (
          <p className="text-sm text-muted-foreground">
            {t("setup.troubleshooting.commandUnavailable")}
          </p>
        )}
      </CardContent>
    </Card>
  )
}

function InfoValue({
  label,
  value,
  breakAll = false,
}: {
  label: string
  value: string
  breakAll?: boolean
}) {
  return (
    <div className="rounded-lg border border-border bg-background/40 p-3">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className={`mt-1 text-sm font-medium ${breakAll ? "break-all" : ""}`}>
        {value}
      </div>
    </div>
  )
}

function getErrorMessage(error: unknown, fallback: string) {
  if (error instanceof Error) return error.message
  if (typeof error === "string") return error
  return fallback
}
