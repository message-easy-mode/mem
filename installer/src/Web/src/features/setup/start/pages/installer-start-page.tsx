import { useState } from "react"
import { Link, useNavigate } from "react-router-dom"
import {
  AlertTriangle,
  CheckCircle2,
  Loader2,
  Play,
  RotateCw,
  ShieldAlert,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { ApiProblemAlert } from "@/components/operator/api-problem-alert"
import { Card, CardContent } from "@/components/ui/card"

import { usePlatformStatus } from "../hooks/use-platform-status"
import { beginPlatformSetup } from "../api/platform-status-api"
import {
  getSetupStartMode,
  getStartupResumePath,
  type PlatformStatusResponse,
  type SetupStartMode,
} from "../api/platform-status"

export function InstallerStartPage() {
  const { t } = useI18n()
  const platformStatus = usePlatformStatus()

  if (platformStatus.isLoading) {
    return (
      <StartPageFrame>
        <Card>
          <CardContent className="flex items-center gap-3 p-6 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" />
            {t("setup.start.checking")}
          </CardContent>
        </Card>
      </StartPageFrame>
    )
  }

  if (platformStatus.error) {
    return (
      <StartPageFrame>
        <Alert variant="destructive">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("setup.start.checkFailedTitle")}</AlertTitle>
          <AlertDescription>{String(platformStatus.error)}</AlertDescription>
        </Alert>
      </StartPageFrame>
    )
  }

  const status = platformStatus.data

  if (!status) {
    return (
      <StartPageFrame>
        <Card>
          <CardContent className="p-6 text-sm text-muted-foreground">
            {t("setup.start.noState")}
          </CardContent>
        </Card>
      </StartPageFrame>
    )
  }

  return (
    <StartPageFrame>
      <SetupStartCard status={status} />
    </StartPageFrame>
  )
}

function StartPageFrame({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-[calc(100vh-12rem)] items-center justify-center">
      <div className="w-full max-w-2xl">{children}</div>
    </div>
  )
}

function SetupStartCard({ status }: { status: PlatformStatusResponse }) {
  const resumePath = getStartupResumePath(status)
  if (resumePath) {
    return <ResumableInstallStart status={status} resumePath={resumePath} />
  }

  const mode = getSetupStartMode(status)

  if (mode === "already-installed") {
    return <AlreadyInstalledStart status={status} />
  }

  if (mode === "migration-required" || mode === "upgrade") {
    return <MigrationRequiredStart status={status} />
  }

  if (mode === "repair") {
    return <RepairStart status={status} />
  }

  return <FreshInstallStart status={status} mode={mode} />
}

function FreshInstallStart({
  status,
  mode,
}: {
  status: PlatformStatusResponse
  mode: SetupStartMode
}) {
  const { t } = useI18n()
  const navigate = useNavigate()
  const [isBeginning, setIsBeginning] = useState(false)
  const [beginError, setBeginError] = useState<unknown>(null)

  async function handleBeginSetup() {
    setIsBeginning(true)
    setBeginError(null)

    try {
      await beginPlatformSetup()
      navigate("/preflight")
    } catch (error) {
      setBeginError(error)
    } finally {
      setIsBeginning(false)
    }
  }

  const continuingSetup =
    status.recommendedAction === "continue-setup" &&
    Boolean(status.activeInstallationId?.trim())

  return (
    <Card className="border-border bg-card/80">
      <CardContent className="space-y-8 p-8 text-center">
        <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full border border-primary/30 bg-primary/10 text-primary">
          <Play className="h-5 w-5" />
        </div>

        <div className="space-y-3">
          <h1 className="text-xl font-semibold tracking-tight">
            {continuingSetup ? t("setup.start.inProgress.title") : t("setup.start.fresh.title")}
          </h1>

          <p className="mx-auto max-w-md text-sm leading-relaxed text-muted-foreground">
            {continuingSetup
              ? t("setup.start.inProgress.description")
              : t("setup.start.fresh.description")}
          </p>
        </div>

        <RequirementsList
          items={[
            t("setup.start.requirement.privateConnection"),
            t("setup.start.requirement.server"),
            t("setup.start.requirement.dns"),
            t("setup.start.requirement.time"),
          ]}
        />

        {mode === "unknown" ? (
          <Alert>
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("setup.start.unknown.title")}</AlertTitle>
            <AlertDescription>
              {t("setup.start.unknown.description")}
            </AlertDescription>
          </Alert>
        ) : null}

        {!status.docker.reachable ? (
          <Alert variant="destructive" className="text-left">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("setup.start.dockerUnavailable.title")}</AlertTitle>
            <AlertDescription>
              {status.docker.message ?? t("setup.start.dockerUnavailable.description")}
            </AlertDescription>
          </Alert>
        ) : null}

        {beginError ? (
          <ApiProblemAlert
            error={beginError}
            title={t("setup.start.beginFailedTitle")}
            fallbackDescription={t("setup.start.beginFailedDescription")}
          />
        ) : null}

        <div>
          {mode === "unknown" ? (
            <Button asChild size="lg">
              <Link to="/preflight">{t("setup.start.runChecks")}</Link>
            </Button>
          ) : (
            <Button size="lg" onClick={handleBeginSetup} disabled={isBeginning}>
              {isBeginning ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
              {status.recommendedAction === "continue-setup"
                ? t("setup.start.continue")
                : t("setup.start.startCheck")}
            </Button>
          )}
        </div>
      </CardContent>
    </Card>
  )
}

function MigrationRequiredStart({ status }: { status: PlatformStatusResponse }) {
  const { t } = useI18n()
  const detected = status.detectedInstallation
  const version = detected?.version ?? "v0.1.0"
  const targetVersion = detected?.targetVersion ?? "v0.2.0"

  return (
    <Card className="border-border bg-card/80">
      <CardContent className="space-y-8 p-8 text-center">
        <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full border border-amber-500/30 bg-amber-500/10 text-amber-300">
          <RotateCw className="h-5 w-5" />
        </div>

        <div className="space-y-3">
          <h1 className="text-xl font-semibold tracking-tight">{t("setup.start.legacy.title")}</h1>

          <p className="mx-auto max-w-md text-sm leading-relaxed text-muted-foreground">
            {t("setup.start.legacy.description", { version })}
          </p>

          <p className="mx-auto max-w-md text-sm leading-relaxed text-muted-foreground">
            {t("setup.start.legacy.migrate", { targetVersion })}
          </p>
        </div>

        <Alert variant="destructive" className="text-left">
          <ShieldAlert className="h-4 w-4" />
          <AlertTitle>{t("setup.start.legacy.blockedTitle")}</AlertTitle>
          <AlertDescription>
            {t("setup.start.legacy.blockedDescription")}
          </AlertDescription>
        </Alert>

        <div className="flex flex-col justify-center gap-2 sm:flex-row">
          <Button asChild size="lg">
            <Link to="/diagnostics">{t("setup.start.reviewDiagnostics")}</Link>
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}

function AlreadyInstalledStart({ status }: { status: PlatformStatusResponse }) {
  const { t } = useI18n()
  const detected = status.detectedInstallation
  const version = detected?.version ?? t("setup.start.currentVersion")

  return (
    <Card className="border-border bg-card/80">
      <CardContent className="space-y-8 p-8 text-center">
        <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full border border-emerald-500/30 bg-emerald-500/10 text-emerald-300">
          <CheckCircle2 className="h-5 w-5" />
        </div>

        <div className="space-y-3">
          <h1 className="text-xl font-semibold tracking-tight">{t("setup.start.installed.title")}</h1>

          <p className="mx-auto max-w-md text-sm leading-relaxed text-muted-foreground">
            {t("setup.start.installed.description", { version })}
          </p>

          <p className="mx-auto max-w-md text-sm leading-relaxed text-muted-foreground">
            {t("setup.start.installed.next")}
          </p>
        </div>

        {status.warnings.length > 0 ? (
          <div className="space-y-2 text-left">
            {status.warnings.map((warning) => (
              <Alert key={warning.code} variant={warning.blocking ? "destructive" : "default"}>
                <AlertTriangle className="h-4 w-4" />
                <AlertTitle>{warning.title}</AlertTitle>
                <AlertDescription>{warning.message}</AlertDescription>
              </Alert>
            ))}
          </div>
        ) : null}

        <div className="flex flex-col justify-center gap-2 sm:flex-row">
          <Button asChild size="lg">
            <Link to="/dashboard">{t("setup.start.openDashboard")}</Link>
          </Button>

          <Button asChild size="lg" variant="outline">
            <Link to="/preflight">{t("setup.start.runHealthCheck")}</Link>
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}

function RepairStart({ status }: { status: PlatformStatusResponse }) {
  const { t } = useI18n()
  const historicalInstallationMissing =
    status.startupTarget === "dashboard" &&
    status.recommendedAction === "review-diagnostics" &&
    status.warnings.some((warning) => warning.code === "installation-history-missing")

  return (
    <Card className="border-border bg-card/80">
      <CardContent className="space-y-8 p-8 text-center">
        <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full border border-destructive/30 bg-destructive/10 text-destructive">
          <ShieldAlert className="h-5 w-5" />
        </div>

        <div className="space-y-3">
          <h1 className="text-xl font-semibold tracking-tight">
            {historicalInstallationMissing
              ? t("setup.start.repair.establishedTitle")
              : t("setup.start.repair.attentionTitle")}
          </h1>

          <p className="mx-auto max-w-md text-sm leading-relaxed text-muted-foreground">
            {historicalInstallationMissing
              ? t("setup.start.repair.establishedDescription")
              : t("setup.start.repair.attentionDescription")}
          </p>
        </div>

        {status.warnings.length > 0 ? (
          <div className="space-y-2 text-left">
            {status.warnings.map((warning) => (
              <Alert key={warning.code} variant={warning.blocking ? "destructive" : "default"}>
                <AlertTriangle className="h-4 w-4" />
                <AlertTitle>{warning.title}</AlertTitle>
                <AlertDescription>{warning.message}</AlertDescription>
              </Alert>
            ))}
          </div>
        ) : null}

        <div className="flex flex-col justify-center gap-2 sm:flex-row">
          {historicalInstallationMissing ? (
            <Button asChild size="lg">
              <Link to="/dashboard">{t("setup.start.openDashboard")}</Link>
            </Button>
          ) : (
            <Button asChild size="lg">
              <Link to="/preflight">{t("setup.start.repair.action")}</Link>
            </Button>
          )}

          <Button asChild size="lg" variant="outline">
            <Link to="/diagnostics">{t("setup.start.openDiagnostics")}</Link>
          </Button>
        </div>

        <p className="text-xs text-muted-foreground">
          {historicalInstallationMissing
            ? t("setup.start.repair.historicalNote")
            : t("setup.start.repair.note")}
        </p>
      </CardContent>
    </Card>
  )
}

function ResumableInstallStart({
  status,
  resumePath,
}: {
  status: PlatformStatusResponse
  resumePath: string
}) {
  const { t } = useI18n()
  const stage = status.activeInstallationStage
  const title =
    stage === "handoff"
      ? t("setup.start.resume.handoffTitle")
      : stage === "verification"
        ? t("setup.start.resume.verificationTitle")
        : stage === "failure-review"
          ? t("setup.start.resume.failureTitle")
          : t("setup.start.resume.installTitle")
  const actionLabel =
    stage === "handoff"
      ? t("setup.start.resume.finish")
      : stage === "verification"
        ? t("setup.start.resume.openVerification")
        : stage === "failure-review"
          ? t("setup.start.resume.reviewFailure")
          : t("setup.start.resume.install")
  const description =
    stage === "handoff"
      ? t("setup.start.resume.handoffDescription")
      : stage === "verification"
        ? t("setup.start.resume.verificationDescription")
        : t("setup.start.resume.installDescription")

  return (
    <Card className="border-amber-500/30 bg-card/80">
      <CardContent className="space-y-8 p-8 text-center">
        <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full border border-amber-500/30 bg-amber-500/10 text-amber-300">
          <RotateCw className="h-5 w-5" />
        </div>

        <div className="space-y-3">
          <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
          <p className="mx-auto max-w-md text-sm leading-relaxed text-muted-foreground">
            {description}
          </p>
        </div>

        {status.warnings.length > 0 ? (
          <div className="space-y-2 text-left">
            {status.warnings.map((warning) => (
              <Alert key={warning.code} variant={warning.blocking ? "destructive" : "default"}>
                <AlertTriangle className="h-4 w-4" />
                <AlertTitle>{warning.title}</AlertTitle>
                <AlertDescription>{warning.message}</AlertDescription>
              </Alert>
            ))}
          </div>
        ) : null}

        <div className="flex flex-col justify-center gap-2 sm:flex-row">
          <Button asChild size="lg">
            <Link to={resumePath}>{actionLabel}</Link>
          </Button>
          <Button asChild size="lg" variant="outline">
            <Link to="/diagnostics">{t("setup.start.openDiagnostics")}</Link>
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}

function RequirementsList({ items }: { items: string[] }) {
  const { t } = useI18n()
  return (
    <div className="mx-auto max-w-sm rounded-2xl border border-border bg-muted/20 p-4 text-left">
      <div className="text-sm font-medium text-foreground">{t("setup.start.requirements.title")}</div>
      <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-muted-foreground">
        {items.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
    </div>
  )
}
