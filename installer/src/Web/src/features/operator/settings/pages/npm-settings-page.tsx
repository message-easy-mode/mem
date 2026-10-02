import { useEffect, useRef, useState, type FormEvent } from "react"
import {
  AlertTriangle,
  CheckCircle2,
  ExternalLink,
  Eye,
  EyeOff,
  KeyRound,
  RefreshCw,
  ServerCog,
} from "lucide-react"

import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { useOperatorSession } from "@/features/auth/operator-session-context"

import {
  isStepUpRequiredNpmSettingsProblem,
  NpmSettingsProblemError,
  type UpdateOperatorNpmCredentialInput,
} from "../api/npm-settings.api"
import { SettingsSectionNavigation } from "../components/settings-section-navigation"
import {
  useOperatorNpmSettings,
  useRevealOperatorNpmCredential,
  useUpdateOperatorNpmCredential,
} from "../hooks/use-npm-settings"

type PendingStepUpAction =
  | { kind: "reveal" }
  | { kind: "update"; input: UpdateOperatorNpmCredentialInput }
  | null

function problemMessage(error: unknown, t: ReturnType<typeof useI18n>["t"]): string {
  if (!(error instanceof NpmSettingsProblemError)) {
    return t("settings.npm.problem.default")
  }

  const keyByCode: Record<string, Parameters<typeof t>[0]> = {
    npm_installed_state_unavailable: "settings.npm.problem.notInstalled",
    npm_credential_unavailable: "settings.npm.problem.credentialUnavailable",
    npm_credentials_rejected: "settings.npm.problem.credentialsRejected",
    npm_credential_verification_unavailable: "settings.npm.problem.verificationUnavailable",
    NpmAdminEmailInvalid: "settings.npm.problem.emailInvalid",
    NpmAdminPasswordMissing: "settings.npm.problem.passwordMissing",
    NpmAdminPasswordTooLong: "settings.npm.problem.passwordTooLong",
  }

  return error.code && keyByCode[error.code]
    ? t(keyByCode[error.code])
    : t("settings.npm.problem.default")
}

export function NpmSettingsPage() {
  const { t, language } = useI18n()
  const { session } = useOperatorSession()
  const isPlatformOwner = session.roles.includes("platform_owner")
  const settings = useOperatorNpmSettings(isPlatformOwner)
  const reveal = useRevealOperatorNpmCredential()
  const update = useUpdateOperatorNpmCredential()

  const [revealedPassword, setRevealedPassword] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)
  const [email, setEmail] = useState("")
  const [password, setPassword] = useState("")
  const pendingStepUpAction = useRef<PendingStepUpAction>(null)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [successMessage, setSuccessMessage] = useState<string | null>(null)
  const [revealErrorMessage, setRevealErrorMessage] = useState<string | null>(null)
  const [updateErrorMessage, setUpdateErrorMessage] = useState<string | null>(null)

  useEffect(() => {
    if (!settings.data?.administratorEmail || editing) {
      return
    }
    setEmail(settings.data.administratorEmail)
  }, [editing, settings.data?.administratorEmail])

  useEffect(() => {
    if (!revealedPassword) {
      return
    }

    const timeout = window.setTimeout(() => setRevealedPassword(null), 30_000)
    return () => window.clearTimeout(timeout)
  }, [revealedPassword])

  if (!isPlatformOwner) {
    return (
      <Card className="mx-auto max-w-2xl">
        <CardHeader>
          <CardTitle>{t("settings.security.accessDeniedTitle")}</CardTitle>
          <CardDescription>{t("settings.security.accessDeniedDescription")}</CardDescription>
        </CardHeader>
      </Card>
    )
  }

  const runReveal = () => {
    setSuccessMessage(null)
    setRevealErrorMessage(null)
    reveal.reset()
    reveal.mutate(undefined, {
      onSuccess: (result) => {
        pendingStepUpAction.current = null
        setRevealedPassword(result.password)
        // The password must not remain in React Query mutation data after it
        // has been copied into the intentionally short-lived reveal state.
        reveal.reset()
      },
      onError: (error) => {
        if (isStepUpRequiredNpmSettingsProblem(error)) {
          pendingStepUpAction.current = { kind: "reveal" }
          setStepUpOpen(true)
          reveal.reset()
          return
        }

        pendingStepUpAction.current = null
        setRevealErrorMessage(problemMessage(error, t))
        reveal.reset()
      },
    })
  }

  const runUpdate = (input: UpdateOperatorNpmCredentialInput) => {
    setSuccessMessage(null)
    setUpdateErrorMessage(null)
    update.reset()
    update.mutate(input, {
      onSuccess: () => {
        pendingStepUpAction.current = null
        setRevealedPassword(null)
        setEditing(false)
        setPassword("")
        setSuccessMessage(t("settings.npm.update.saved"))
        // Clear mutation variables so the verified candidate password is not
        // retained by React Query after the request has completed.
        update.reset()
      },
      onError: (error) => {
        if (isStepUpRequiredNpmSettingsProblem(error)) {
          // Retain the candidate only in the explicit pending step-up ref. The
          // mutation itself is reset so it does not hold a second copy.
          pendingStepUpAction.current = { kind: "update", input }
          setStepUpOpen(true)
          update.reset()
          return
        }

        pendingStepUpAction.current = null
        setPassword("")
        setUpdateErrorMessage(problemMessage(error, t))
        update.reset()
      },
    })
  }

  const submitUpdate = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const input = { email: email.trim(), password }
    setPassword("")
    runUpdate(input)
  }

  const resumeAfterStepUp = () => {
    const pending = pendingStepUpAction.current
    pendingStepUpAction.current = null
    if (!pending) {
      return
    }

    if (pending.kind === "reveal") {
      runReveal()
      return
    }

    runUpdate(pending.input)
  }

  const current = settings.data

  return (
    <div className="space-y-6">
      <div className="space-y-2">
        <div className="text-sm font-medium text-muted-foreground">
          {t("settings.breadcrumb")}
        </div>
        <h1 className="text-3xl font-semibold tracking-tight">
          {t("settings.npm.title")}
        </h1>
        <p className="max-w-3xl text-muted-foreground">
          {t("settings.npm.description")}
        </p>
      </div>

      <SettingsSectionNavigation />

      {settings.isLoading ? (
        <Card>
          <CardHeader>
            <CardTitle>{t("settings.npm.loading")}</CardTitle>
          </CardHeader>
        </Card>
      ) : null}

      {settings.isError ? (
        <Alert variant="destructive">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("settings.npm.loadErrorTitle")}</AlertTitle>
          <AlertDescription>{problemMessage(settings.error, t)}</AlertDescription>
        </Alert>
      ) : null}

      {current ? (
        <>
          {current.warning ? (
            <Alert>
              <AlertTriangle className="h-4 w-4" />
              <AlertTitle>{t("settings.npm.runtime.warningTitle")}</AlertTitle>
              <AlertDescription>{current.warning}</AlertDescription>
            </Alert>
          ) : null}

          <Card>
            <CardHeader>
              <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div>
                  <CardTitle className="flex items-center gap-2">
                    <ServerCog className="h-5 w-5" />
                    {t("settings.npm.runtime.title")}
                  </CardTitle>
                  <CardDescription className="mt-1">
                    {t("settings.npm.runtime.description")}
                  </CardDescription>
                </div>
                <Badge variant={current.running ? "default" : "outline"}>
                  {current.running
                    ? t("settings.npm.runtime.running")
                    : current.runtimeExists
                      ? t("settings.npm.runtime.stopped")
                      : t("settings.npm.runtime.unavailable")}
                </Badge>
              </div>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid gap-3 lg:grid-cols-3">
                <div className="rounded-xl border border-border p-4">
                  <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                    {t("settings.npm.runtime.runningImage")}
                  </div>
                  <div className="mt-2 break-all font-medium">
                    {current.runtimeImage ?? t("settings.npm.runtime.imageUnavailable")}
                  </div>
                </div>
                <div className="rounded-xl border border-border p-4">
                  <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                    {t("settings.npm.runtime.supportedRelease")}
                  </div>
                  <div className="mt-2 font-medium">
                    {t("settings.npm.runtime.supportedVersion", { version: current.approvedRuntimeVersion })}
                  </div>
                  <div className="mt-1 break-all text-xs text-muted-foreground">
                    {current.approvedRuntimeImage}
                  </div>
                </div>
                <div className="rounded-xl border border-border p-4">
                  <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                    {t("settings.npm.runtime.pinStatus")}
                  </div>
                  <div className="mt-2 font-medium">
                    {current.runtimeImageAligned
                      ? t("settings.npm.runtime.aligned")
                      : t("settings.npm.runtime.notAligned")}
                  </div>
                  <div className="mt-1 text-xs text-muted-foreground">
                    {current.runtimeState
                      ? t("settings.npm.runtime.state", { state: current.runtimeState })
                      : t("settings.npm.runtime.stateUnavailable")}
                  </div>
                </div>
              </div>
              <div className="flex justify-end">
                {current.browserUrl ? (
                  <Button asChild variant="outline">
                    <a href={current.browserUrl} target="_blank" rel="noreferrer noopener">
                      <ExternalLink className="mr-2 h-4 w-4" />
                      {t("settings.npm.open")}
                    </a>
                  </Button>
                ) : (
                  <Button type="button" variant="outline" disabled>
                    <ExternalLink className="mr-2 h-4 w-4" />
                    {t("settings.npm.openUnavailable")}
                  </Button>
                )}
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <KeyRound className="h-5 w-5" />
                {t("settings.npm.credential.title")}
              </CardTitle>
              <CardDescription>
                {t("settings.npm.credential.description")}
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-5">
              <div className="grid gap-3 lg:grid-cols-3">
                <div className="rounded-xl border border-border p-4">
                  <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                    {t("settings.npm.credential.email")}
                  </div>
                  <div className="mt-2 break-all font-medium">
                    {current.administratorEmail ?? t("settings.npm.credential.missing")}
                  </div>
                </div>
                <div className="rounded-xl border border-border p-4">
                  <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                    {t("settings.npm.credential.password")}
                  </div>
                  <div className="mt-2 font-medium">
                    {current.credentialStored ? "••••••••••••••••" : t("settings.npm.credential.missing")}
                  </div>
                </div>
                <div className="rounded-xl border border-border p-4">
                  <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                    {t("settings.npm.credential.status")}
                  </div>
                  <div className="mt-2 font-medium">
                    {current.credentialStatus === "Verified"
                      ? t("settings.npm.credential.verified")
                      : current.credentialStatus}
                  </div>
                  <div className="mt-1 text-xs text-muted-foreground">
                    {current.lastVerifiedAtUtc
                      ? t("settings.npm.credential.lastVerified", {
                          timestamp: formatDateTime(current.lastVerifiedAtUtc, language),
                        })
                      : t("settings.npm.credential.notVerified")}
                  </div>
                </div>
              </div>

              {revealedPassword ? (
                <Alert>
                  <Eye className="h-4 w-4" />
                  <AlertTitle>{t("settings.npm.reveal.visibleTitle")}</AlertTitle>
                  <AlertDescription className="mt-3 space-y-3">
                    <Input
                      aria-label={t("settings.npm.reveal.passwordAria")}
                      value={revealedPassword}
                      readOnly
                      autoComplete="off"
                    />
                    <div className="flex items-center justify-between gap-3 text-xs text-muted-foreground">
                      <span>{t("settings.npm.reveal.autoHide")}</span>
                      <Button type="button" size="sm" variant="outline" onClick={() => setRevealedPassword(null)}>
                        <EyeOff className="mr-2 h-4 w-4" />
                        {t("settings.npm.reveal.hide")}
                      </Button>
                    </div>
                  </AlertDescription>
                </Alert>
              ) : null}

              {revealErrorMessage ? (
                <Alert variant="destructive">
                  <AlertTriangle className="h-4 w-4" />
                  <AlertTitle>{t("settings.npm.reveal.errorTitle")}</AlertTitle>
                  <AlertDescription>{revealErrorMessage}</AlertDescription>
                </Alert>
              ) : null}

              {successMessage ? (
                <Alert>
                  <CheckCircle2 className="h-4 w-4" />
                  <AlertTitle>{successMessage}</AlertTitle>
                  <AlertDescription>{t("settings.npm.update.savedDescription")}</AlertDescription>
                </Alert>
              ) : null}

              {updateErrorMessage ? (
                <Alert variant="destructive">
                  <AlertTriangle className="h-4 w-4" />
                  <AlertTitle>{t("settings.npm.update.errorTitle")}</AlertTitle>
                  <AlertDescription>{updateErrorMessage}</AlertDescription>
                </Alert>
              ) : null}

              {editing ? (
                <form className="space-y-4 rounded-xl border border-border p-4" onSubmit={submitUpdate}>
                  <div>
                    <div className="font-medium">{t("settings.npm.update.title")}</div>
                    <p className="mt-1 text-sm text-muted-foreground">
                      {t("settings.npm.update.description")}
                    </p>
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="npm-settings-email">{t("settings.npm.update.email")}</Label>
                    <Input
                      id="npm-settings-email"
                      type="email"
                      autoComplete="username"
                      value={email}
                      onChange={(event) => setEmail(event.target.value)}
                      disabled={update.isPending}
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="npm-settings-password">{t("settings.npm.update.password")}</Label>
                    <Input
                      id="npm-settings-password"
                      type="password"
                      autoComplete="new-password"
                      value={password}
                      onChange={(event) => setPassword(event.target.value)}
                      disabled={update.isPending}
                    />
                  </div>
                  <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
                    <Button
                      type="button"
                      variant="outline"
                      disabled={update.isPending}
                      onClick={() => {
                        setEditing(false)
                        setPassword("")
                        setUpdateErrorMessage(null)
                        pendingStepUpAction.current = null
                      }}
                    >
                      {t("settings.npm.update.cancel")}
                    </Button>
                    <Button type="submit" disabled={update.isPending || !email.trim() || !password}>
                      {update.isPending ? (
                        <>
                          <RefreshCw className="mr-2 h-4 w-4 animate-spin" />
                          {t("settings.npm.update.verifying")}
                        </>
                      ) : t("settings.npm.update.verifyAndSave")}
                    </Button>
                  </div>
                </form>
              ) : (
                <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap">
                  <Button
                    type="button"
                    variant="outline"
                    disabled={!current.credentialStored || reveal.isPending}
                    onClick={runReveal}
                  >
                    {reveal.isPending ? (
                      <RefreshCw className="mr-2 h-4 w-4 animate-spin" />
                    ) : (
                      <Eye className="mr-2 h-4 w-4" />
                    )}
                    {t("settings.npm.reveal.action")}
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => {
                      setSuccessMessage(null)
                      setUpdateErrorMessage(null)
                      update.reset()
                      setEmail(current.administratorEmail ?? "")
                      setPassword("")
                      setEditing(true)
                    }}
                  >
                    <KeyRound className="mr-2 h-4 w-4" />
                    {t("settings.npm.update.action")}
                  </Button>
                </div>
              )}
            </CardContent>
          </Card>
        </>
      ) : null}

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={(open) => {
          setStepUpOpen(open)
          if (!open) {
            // OperatorStepUpDialog closes immediately before invoking onVerified.
            // Clear any pending NPM candidate on the next task so successful
            // verification can consume it synchronously, while cancellation
            // does not retain a password in component memory.
            window.setTimeout(() => {
              pendingStepUpAction.current = null
            }, 0)
          }
        }}
        onVerified={resumeAfterStepUp}
      />
    </div>
  )
}
