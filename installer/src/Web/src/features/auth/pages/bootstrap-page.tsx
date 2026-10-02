import { useEffect, useState, type FormEvent } from "react"
import { Navigate, useNavigate } from "react-router-dom"
import { KeyRound, ShieldCheck } from "lucide-react"
import { TotpEnrollmentQrCode } from "@/features/auth/totp-enrollment-qr-code"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { LanguageSelect } from "@/components/layout/language-select"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { PasswordInput } from "@/features/auth/password-input"
import {
  cancelBootstrap,
  completeBootstrap,
  prepareFirstOwner,
  verifyBootstrapCode,
  verifyBootstrapTotp,
  getControlPlaneSession,
  type FirstOwnerEnrollment,
} from "@/features/auth/control-plane-auth"

type BootstrapStage = "code" | "owner" | "totp" | "recovery"
type BootstrapAccessState = "checking" | "ready" | "redirect_login" | "redirect_dashboard"

function messageFor(error: unknown, t: (key: TranslationKey) => string) {
  const code = error instanceof Error ? error.message : ""

  const keyByCode: Record<string, TranslationKey> = {
    bootstrap_code_not_accepted: "bootstrap.error.codeNotAccepted",
    bootstrap_unavailable: "bootstrap.error.unavailable",
    username_required: "bootstrap.error.usernameRequired",
    username_invalid: "bootstrap.error.usernameInvalid",
    username_unavailable: "bootstrap.error.usernameUnavailable",
    password_required: "bootstrap.error.passwordRequired",
    password_not_accepted: "bootstrap.error.passwordNotAccepted",
    email_invalid: "bootstrap.error.emailInvalid",
    bootstrap_owner_already_prepared: "bootstrap.error.ownerAlreadyPrepared",
    invalid_totp: "bootstrap.error.invalidTotp",
    totp_required: "bootstrap.error.totpRequired",
    bootstrap_totp_verification_required: "bootstrap.error.totpRequired",
  }

  return t(keyByCode[code] ?? "bootstrap.error.unavailable")
}

export function BootstrapPage() {
  const { t } = useI18n()
  const navigate = useNavigate()

  const [accessState, setAccessState] = useState<BootstrapAccessState>("checking")
  const [stage, setStage] = useState<BootstrapStage>("code")
  const [token, setToken] = useState("")
  const [username, setUsername] = useState("")
  const [email, setEmail] = useState("")
  const [password, setPassword] = useState("")
  const [passwordConfirmation, setPasswordConfirmation] = useState("")
  const [totpCode, setTotpCode] = useState("")
  const [enrollment, setEnrollment] = useState<FirstOwnerEnrollment | null>(null)
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([])
  const [hasStoredRecoveryCodes, setHasStoredRecoveryCodes] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false

    async function resolveBootstrapAccess() {
      try {
        const session = await getControlPlaneSession()

        if (cancelled) {
          return
        }

        if (session.authenticated && session.authenticationKind === "operator") {
          setAccessState("redirect_dashboard")
          return
        }

        setAccessState(
          session.requiresFirstOwnerBootstrap
            ? "ready"
            : "redirect_login",
        )
      } catch {
        if (!cancelled) {
          // The bootstrap screen may still submit its one-time code. Keep an
          // unavailable session probe from silently sending an operator to an
          // unrelated normal-login screen.
          setAccessState("ready")
        }
      }
    }

    void resolveBootstrapAccess()

    return () => {
      cancelled = true
    }
  }, [])

  async function withSubmit(action: () => Promise<void>) {
    setIsSubmitting(true)
    setError(null)

    try {
      await action()
    } catch (caught) {
      setError(messageFor(caught, t))
    } finally {
      setIsSubmitting(false)
    }
  }

  function handleVerifyCode(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    void withSubmit(async () => {
      if (!token.trim()) {
        throw new Error("bootstrap_code_not_accepted")
      }

      await verifyBootstrapCode(token.trim())
      setToken("")
      setStage("owner")
    })
  }

  function handlePrepareOwner(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    void withSubmit(async () => {
      if (password !== passwordConfirmation) {
        setError(t("bootstrap.error.passwordConfirmation"))
        return
      }

      const result = await prepareFirstOwner({
        username: username.trim(),
        email: email.trim(),
        password,
      })

      setPassword("")
      setPasswordConfirmation("")
      setEnrollment(result)
      setStage("totp")
    })
  }

  function handleVerifyTotp(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    void withSubmit(async () => {
      await verifyBootstrapTotp(totpCode.trim())
      setTotpCode("")
      const completed = await completeBootstrap()
      setRecoveryCodes(completed.recoveryCodes)
      setStage("recovery")
    })
  }

  function handleFinish() {
    if (!hasStoredRecoveryCodes) {
      setError(t("bootstrap.error.recoveryAcknowledgement"))
      return
    }

    navigate("/", { replace: true })
  }

  function handleCancel() {
    void withSubmit(async () => {
      await cancelBootstrap()
      setStage("code")
      setEnrollment(null)
      setRecoveryCodes([])
      setHasStoredRecoveryCodes(false)
      setTotpCode("")
    })
  }

  if (accessState === "checking") {
    return (
      <main className="flex min-h-screen items-center justify-center bg-background text-foreground">
        <div className="text-sm text-muted-foreground">Checking first-owner setup...</div>
      </main>
    )
  }

  if (accessState === "redirect_dashboard") {
    return <Navigate to="/" replace />
  }

  if (accessState === "redirect_login") {
    return <Navigate to="/login" replace />
  }

  return (
    <main className="min-h-screen bg-background text-foreground">
      <div className="mx-auto flex min-h-screen w-full max-w-xl flex-col justify-center px-6 py-12">
        <div className="mb-4 flex justify-end">
          <LanguageSelect id="bootstrap-language" />
        </div>

        <Card className="w-full">
          <CardHeader className="space-y-3">
            <div className="flex h-12 w-12 items-center justify-center rounded-2xl border border-border bg-muted">
              {stage === "recovery" ? (
                <KeyRound className="h-6 w-6" />
              ) : (
                <ShieldCheck className="h-6 w-6" />
              )}
            </div>

            <div>
              <CardTitle className="text-2xl">{t("bootstrap.title")}</CardTitle>
              <CardDescription className="mt-2">
                {stage === "code"
                  ? t("bootstrap.codeDescription")
                  : stage === "owner"
                    ? t("bootstrap.ownerDescription")
                    : stage === "totp"
                      ? t("bootstrap.totpDescription")
                      : t("bootstrap.recoveryDescription")}
              </CardDescription>
            </div>
          </CardHeader>

          <CardContent className="space-y-5">
            {error ? (
              <Alert variant="destructive">
                <AlertTitle>{t("bootstrap.errorTitle")}</AlertTitle>
                <AlertDescription>{error}</AlertDescription>
              </Alert>
            ) : null}

            {stage === "code" ? (
              <form className="space-y-4" onSubmit={handleVerifyCode}>
                <div className="space-y-2">
                  <label htmlFor="bootstrap-token" className="text-sm font-medium">
                    {t("bootstrap.codeLabel")}
                  </label>
                  <Input
                    id="bootstrap-token"
                    type="password"
                    autoComplete="one-time-code"
                    autoCorrect="off"
                    autoCapitalize="none"
                    spellCheck={false}
                    autoFocus
                    value={token}
                    onChange={(event) => setToken(event.target.value)}
                    placeholder="mem_..."
                    data-lpignore="true"
                    data-1p-ignore="true"
                  />
                  <p className="text-xs text-muted-foreground">
                    {t("bootstrap.codeHelp")}
                  </p>
                </div>
                <Button type="submit" className="w-full" disabled={isSubmitting}>
                  {isSubmitting ? t("bootstrap.verifyingCode") : t("bootstrap.verifyCode")}
                </Button>
              </form>
            ) : null}

            {stage === "owner" ? (
              <form className="space-y-4" onSubmit={handlePrepareOwner}>
                <div className="space-y-2">
                  <label htmlFor="bootstrap-username" className="text-sm font-medium">
                    {t("bootstrap.usernameLabel")}
                  </label>
                  <Input
                    id="bootstrap-username"
                    autoComplete="username"
                    autoCapitalize="none"
                    spellCheck={false}
                    value={username}
                    onChange={(event) => setUsername(event.target.value)}
                  />
                </div>

                <div className="space-y-2">
                  <label htmlFor="bootstrap-email" className="text-sm font-medium">
                    {t("bootstrap.emailLabel")}
                  </label>
                  <Input
                    id="bootstrap-email"
                    type="email"
                    autoComplete="email"
                    value={email}
                    onChange={(event) => setEmail(event.target.value)}
                  />
                  <p className="text-xs text-muted-foreground">
                    {t("bootstrap.emailHelp")}
                  </p>
                </div>

                <div className="space-y-2">
                  <label htmlFor="bootstrap-password" className="text-sm font-medium">
                    {t("bootstrap.passwordLabel")}
                  </label>
                  <PasswordInput
                    id="bootstrap-password"
                    autoComplete="new-password"
                    value={password}
                    onChange={(event) => setPassword(event.target.value)}
                  />
                  <p className="text-xs text-muted-foreground">
                    {t("bootstrap.passwordHelp")}
                  </p>
                </div>

                <div className="space-y-2">
                  <label htmlFor="bootstrap-password-confirmation" className="text-sm font-medium">
                    {t("bootstrap.passwordConfirmationLabel")}
                  </label>
                  <PasswordInput
                    id="bootstrap-password-confirmation"
                    autoComplete="new-password"
                    value={passwordConfirmation}
                    onChange={(event) => setPasswordConfirmation(event.target.value)}
                  />
                </div>

                <div className="flex flex-col gap-2 sm:flex-row">
                  <Button type="submit" className="flex-1" disabled={isSubmitting}>
                    {isSubmitting ? t("bootstrap.preparingOwner") : t("bootstrap.prepareOwner")}
                  </Button>
                  <Button type="button" variant="outline" onClick={handleCancel} disabled={isSubmitting}>
                    {t("bootstrap.cancel")}
                  </Button>
                </div>
              </form>
            ) : null}

            {stage === "totp" && enrollment ? (
              <form className="space-y-4" onSubmit={handleVerifyTotp}>
                <div className="rounded-lg border border-border bg-muted/40 p-4">
                  <div className="text-sm font-medium">{t("bootstrap.qrTitle")}</div>
                  <p className="mt-1 text-xs text-muted-foreground">
                    {t("bootstrap.qrDescription")}
                  </p>
                  <div className="mt-3 flex justify-center rounded-lg bg-white p-3">
                    <TotpEnrollmentQrCode
                      authenticatorUri={enrollment.authenticatorUri}
                      title={t("bootstrap.qrTitle")}
                    />
                  </div>
                  <p className="mt-3 text-xs text-muted-foreground">
                    {t("bootstrap.qrSecurityNote")}
                  </p>
                </div>

                <div className="rounded-lg border border-border bg-muted/40 p-4">
                  <div className="text-sm font-medium">{t("bootstrap.manualEntryTitle")}</div>
                  <p className="mt-1 text-xs text-muted-foreground">
                    {t("bootstrap.manualEntryDescription")}
                  </p>
                  <dl className="mt-3 space-y-2 text-sm">
                    <div className="grid gap-1 sm:grid-cols-[9rem_1fr]">
                      <dt className="text-muted-foreground">{t("bootstrap.authenticatorIssuer")}</dt>
                      <dd>MEM Control Plane</dd>
                    </div>
                    <div className="grid gap-1 sm:grid-cols-[9rem_1fr]">
                      <dt className="text-muted-foreground">{t("bootstrap.authenticatorAccount")}</dt>
                      <dd>{enrollment.username}</dd>
                    </div>
                    <div className="grid gap-1 sm:grid-cols-[9rem_1fr]">
                      <dt className="text-muted-foreground">{t("bootstrap.authenticatorKey")}</dt>
                      <dd>
                        <code className="break-all rounded bg-background px-1 py-0.5">
                          {enrollment.manualEntryKey}
                        </code>
                      </dd>
                    </div>
                  </dl>
                </div>

                <div className="space-y-2">
                  <label htmlFor="bootstrap-totp" className="text-sm font-medium">
                    {t("bootstrap.totpLabel")}
                  </label>
                  <Input
                    id="bootstrap-totp"
                    inputMode="numeric"
                    autoComplete="one-time-code"
                    autoCorrect="off"
                    autoCapitalize="none"
                    spellCheck={false}
                    autoFocus
                    value={totpCode}
                    onChange={(event) => setTotpCode(event.target.value)}
                    placeholder="123456"
                  />
                </div>

                <div className="flex flex-col gap-2 sm:flex-row">
                  <Button type="submit" className="flex-1" disabled={isSubmitting}>
                    {isSubmitting ? t("bootstrap.verifyingTotp") : t("bootstrap.verifyTotp")}
                  </Button>
                  <Button type="button" variant="outline" onClick={handleCancel} disabled={isSubmitting}>
                    {t("bootstrap.cancel")}
                  </Button>
                </div>
              </form>
            ) : null}

            {stage === "recovery" ? (
              <div className="space-y-4">
                <Alert>
                  <AlertTitle>{t("bootstrap.recoveryAlertTitle")}</AlertTitle>
                  <AlertDescription>{t("bootstrap.recoveryAlertDescription")}</AlertDescription>
                </Alert>

                <pre
                  className="max-h-56 overflow-auto rounded-lg border border-border bg-muted p-4 text-sm"
                  aria-label={t("bootstrap.recoveryCodesLabel")}
                >
                  {recoveryCodes.join("\n")}
                </pre>

                <label className="flex items-start gap-3 rounded-lg border border-border p-3 text-sm">
                  <input
                    type="checkbox"
                    checked={hasStoredRecoveryCodes}
                    onChange={(event) => setHasStoredRecoveryCodes(event.target.checked)}
                    className="mt-0.5"
                  />
                  <span>{t("bootstrap.recoveryAcknowledgement")}</span>
                </label>

                <Button type="button" className="w-full" onClick={handleFinish}>
                  {t("bootstrap.finish")}
                </Button>
              </div>
            ) : null}
          </CardContent>
        </Card>
      </div>
    </main>
  )
}
