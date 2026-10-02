import { useEffect, useState, type FormEvent } from "react"
import { Navigate, useLocation, useNavigate } from "react-router-dom"
import { ShieldCheck } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { LanguageSelect } from "@/components/layout/language-select"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { PasswordInput } from "@/features/auth/password-input"
import {
  getControlPlaneSession,
  loginOperator,
  verifyOperatorRecoveryCode,
  verifyOperatorTotp,
} from "@/features/auth/control-plane-auth"

type LoginAccessState = "checking" | "ready" | "redirect_bootstrap" | "redirect_dashboard"
type LoginStage = "password" | "totp" | "recovery_code"

export function LoginPage() {
  const { t } = useI18n()
  const navigate = useNavigate()
  const location = useLocation()

  const [accessState, setAccessState] = useState<LoginAccessState>("checking")
  const [stage, setStage] = useState<LoginStage>("password")
  const [username, setUsername] = useState("")
  const [password, setPassword] = useState("")
  const [totp, setTotp] = useState("")
  const [recoveryCode, setRecoveryCode] = useState("")
  const [recoveryCodeError, setRecoveryCodeError] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  useEffect(() => {
    let cancelled = false

    async function resolveLoginAccess() {
      try {
        const session = await getControlPlaneSession()

        if (cancelled) {
          return
        }

        if (session.requiresFirstOwnerBootstrap) {
          setAccessState("redirect_bootstrap")
          return
        }

        setAccessState(session.authenticated ? "redirect_dashboard" : "ready")
      } catch {
        if (!cancelled) {
          setAccessState("ready")
        }
      }
    }

    void resolveLoginAccess()

    return () => {
      cancelled = true
    }
  }, [])

  const from =
    typeof location.state === "object" &&
    location.state !== null &&
    "from" in location.state &&
    typeof location.state.from === "string"
      ? location.state.from
      : "/"

  if (accessState === "checking") {
    return (
      <main className="flex min-h-screen items-center justify-center bg-background text-foreground">
        <div className="text-sm text-muted-foreground">Checking operator sign-in...</div>
      </main>
    )
  }

  if (accessState === "redirect_bootstrap") {
    return <Navigate to="/bootstrap" replace state={{ from }} />
  }

  if (accessState === "redirect_dashboard") {
    return <Navigate to={from} replace />
  }

  function handlePassword(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setIsSubmitting(true)
    setRecoveryCodeError(null)
    setError(null)

    void loginOperator(username.trim(), password).then((result) => {
      if (result.status === "mfa_required") {
        setPassword("")
        setTotp("")
        setRecoveryCode("")
        setRecoveryCodeError(null)
        setStage("totp")
        return
      }

      if (result.status === "authenticated") {
        navigate(from, { replace: true })
        return
      }

      if (result.status === "rate_limited") {
        setError(t("login.rateLimited"))
        return
      }

      setError(t("login.invalidCredentials"))
    }).catch(() => {
      setError(t("login.connectionFailed"))
    }).finally(() => {
      setIsSubmitting(false)
    })
  }

  function handleTotp(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const submittedCode = totp.trim()

    setIsSubmitting(true)
    setRecoveryCodeError(null)
    setError(null)
    setTotp("")

    void verifyOperatorTotp(submittedCode).then((result) => {
      if (result.status === "authenticated") {
        navigate(from, { replace: true })
        return
      }

      if (result.status === "rate_limited") {
        setError(t("login.rateLimited"))
        return
      }

      setError(t("login.invalidTotp"))
    }).catch(() => {
      setError(t("login.connectionFailed"))
    }).finally(() => {
      setIsSubmitting(false)
    })
  }

  function handleRecoveryCode(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const submittedCode = recoveryCode.trim()

    setIsSubmitting(true)
    setRecoveryCodeError(null)
    setError(null)
    setRecoveryCode("")

    void verifyOperatorRecoveryCode(submittedCode).then((result) => {
      if (result.status === "authenticated") {
        navigate(from, { replace: true })
        return
      }

      if (result.status === "rate_limited") {
        setError(t("login.rateLimited"))
        return
      }

      // Keep recovery-code failures deliberately generic. This covers a
      // mistyped code, a replayed code, and a code replaced by regeneration
      // without revealing which case occurred.
      setRecoveryCodeError(t("login.invalidRecoveryCode"))
    }).catch(() => {
      setError(t("login.connectionFailed"))
    }).finally(() => {
      setIsSubmitting(false)
    })
  }

  function switchToTotp() {
    setRecoveryCodeError(null)
    setError(null)
    setRecoveryCode("")
    setStage("totp")
  }

  function switchToRecoveryCode() {
    setRecoveryCodeError(null)
    setError(null)
    setTotp("")
    setStage("recovery_code")
  }

  const description = stage === "password"
    ? t("login.passwordDescription")
    : stage === "totp"
      ? t("login.totpDescription")
      : t("login.recoveryCodeDescription")

  return (
    <main className="min-h-screen bg-background text-foreground">
      <div className="mx-auto flex min-h-screen w-full max-w-md flex-col justify-center px-6 py-12">
        <div className="mb-4 flex justify-end">
          <LanguageSelect id="login-language" />
        </div>

        <Card className="w-full">
          <CardHeader className="space-y-3">
            <div className="flex h-12 w-12 items-center justify-center rounded-2xl border border-border bg-muted">
              <ShieldCheck className="h-6 w-6" />
            </div>
            <div>
              <CardTitle className="text-2xl">{t("login.title")}</CardTitle>
              <CardDescription className="mt-2">{description}</CardDescription>
            </div>
          </CardHeader>

          <CardContent>
            {error ? (
              <Alert variant="destructive" className="mb-4">
                <AlertTitle>{t("login.errorTitle")}</AlertTitle>
                <AlertDescription>{error}</AlertDescription>
              </Alert>
            ) : null}

            {stage === "password" ? (
              <form className="space-y-4" onSubmit={handlePassword}>
                <div className="space-y-2">
                  <label htmlFor="login-username" className="text-sm font-medium">
                    {t("login.usernameLabel")}
                  </label>
                  <Input
                    id="login-username"
                    autoComplete="username"
                    autoCapitalize="none"
                    spellCheck={false}
                    autoFocus
                    value={username}
                    onChange={(event) => setUsername(event.target.value)}
                  />
                </div>
                <div className="space-y-2">
                  <label htmlFor="login-password" className="text-sm font-medium">
                    {t("login.passwordLabel")}
                  </label>
                  <PasswordInput
                    id="login-password"
                    autoComplete="current-password"
                    value={password}
                    onChange={(event) => setPassword(event.target.value)}
                  />
                </div>
                <Button type="submit" className="w-full" disabled={isSubmitting}>
                  {isSubmitting ? t("login.signingIn") : t("login.signIn")}
                </Button>
              </form>
            ) : stage === "totp" ? (
              <form className="space-y-4" onSubmit={handleTotp}>
                <div className="space-y-2">
                  <label htmlFor="login-totp" className="text-sm font-medium">
                    {t("login.totpLabel")}
                  </label>
                  <Input
                    id="login-totp"
                    inputMode="numeric"
                    autoComplete="one-time-code"
                    autoCorrect="off"
                    autoCapitalize="none"
                    spellCheck={false}
                    autoFocus
                    value={totp}
                    onChange={(event) => setTotp(event.target.value)}
                    placeholder="123456"
                  />
                </div>
                <Button type="submit" className="w-full" disabled={isSubmitting}>
                  {isSubmitting ? t("login.verifying") : t("login.verify")}
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  className="w-full"
                  disabled={isSubmitting}
                  onClick={switchToRecoveryCode}
                >
                  {t("login.useRecoveryCode")}
                </Button>
              </form>
            ) : (
              <form className="space-y-4" onSubmit={handleRecoveryCode}>
                <div className="space-y-2">
                  <label htmlFor="login-recovery-code" className="text-sm font-medium">
                    {t("login.recoveryCodeLabel")}
                  </label>
                  <Input
                    id="login-recovery-code"
                    name="mem-recovery-code"
                    type="password"
                    autoComplete="off"
                    autoCorrect="off"
                    autoCapitalize="none"
                    spellCheck={false}
                    autoFocus
                    aria-invalid={recoveryCodeError ? "true" : undefined}
                    aria-describedby={recoveryCodeError ? "login-recovery-code-error" : undefined}
                    value={recoveryCode}
                    onChange={(event) => {
                      setRecoveryCode(event.target.value)
                      setRecoveryCodeError(null)
                    }}
                  />
                  {recoveryCodeError ? (
                    <p
                      id="login-recovery-code-error"
                      role="alert"
                      className="text-sm text-destructive"
                    >
                      {recoveryCodeError}
                    </p>
                  ) : null}
                </div>
                <Button type="submit" className="w-full" disabled={isSubmitting}>
                  {isSubmitting ? t("login.usingRecoveryCode") : t("login.submitRecoveryCode")}
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  className="w-full"
                  disabled={isSubmitting}
                  onClick={switchToTotp}
                >
                  {t("login.useAuthenticatorCode")}
                </Button>
              </form>
            )}
          </CardContent>
        </Card>
      </div>
    </main>
  )
}
