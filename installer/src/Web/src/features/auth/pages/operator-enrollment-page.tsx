import { useEffect, useState, type FormEvent } from "react"
import { useNavigate } from "react-router-dom"
import { KeyRound, ShieldCheck } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { LanguageSelect } from "@/components/layout/language-select"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { PasswordInput } from "@/features/auth/password-input"
import {
  cancelOperatorEnrollment,
  completeOperatorEnrollment,
  getOperatorEnrollmentState,
  OperatorEnrollmentProblemError,
  prepareOperatorEnrollment,
  verifyOperatorEnrollmentCode,
  verifyOperatorEnrollmentTotp,
  type OperatorEnrollmentAuthenticator,
} from "@/features/auth/operator-enrollment-auth"
import { TotpEnrollmentQrCode } from "@/features/auth/totp-enrollment-qr-code"

type EnrollmentStage = "checking" | "code" | "password" | "totp" | "ready" | "recovery"

function errorMessage(
  error: unknown,
  translate: (key: TranslationKey) => string,
) {
  const code = error instanceof OperatorEnrollmentProblemError
    ? error.code
    : null

  const keyByCode = {
    enrollment_code_required: "operatorEnrollment.error.codeRequired",
    enrollment_code_invalid: "operatorEnrollment.error.codeInvalid",
    enrollment_grant_unavailable: "operatorEnrollment.error.sessionExpired",
    password_required: "operatorEnrollment.error.passwordRequired",
    password_not_accepted: "operatorEnrollment.error.passwordRejected",
    invalid_totp: "operatorEnrollment.error.invalidTotp",
    totp_required: "operatorEnrollment.error.totpRequired",
    enrollment_authenticator_unavailable: "operatorEnrollment.error.authenticatorUnavailable",
    enrollment_completion_unavailable: "operatorEnrollment.error.completionUnavailable",
  } as const

  return code && code in keyByCode
    ? translate(keyByCode[code as keyof typeof keyByCode])
    : translate("operatorEnrollment.error.default")
}

export function OperatorEnrollmentPage() {
  const { t } = useI18n()
  const navigate = useNavigate()

  const [stage, setStage] = useState<EnrollmentStage>("checking")
  const [username, setUsername] = useState<string | null>(null)
  const [enrollmentCode, setEnrollmentCode] = useState("")
  const [password, setPassword] = useState("")
  const [passwordConfirmation, setPasswordConfirmation] = useState("")
  const [totpCode, setTotpCode] = useState("")
  const [authenticator, setAuthenticator] = useState<OperatorEnrollmentAuthenticator | null>(null)
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([])
  const [hasStoredRecoveryCodes, setHasStoredRecoveryCodes] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  useEffect(() => {
    let cancelled = false

    async function resolveExistingEnrollment() {
      try {
        const state = await getOperatorEnrollmentState()

        if (cancelled) {
          return
        }

        if (!state.active || !state.stage) {
          setStage("code")
          return
        }

        setUsername(state.username)

        if (state.stage === "password") {
          setStage("password")
          return
        }

        if (state.stage === "recovery") {
          setStage("ready")
          return
        }

        const prepared = await prepareOperatorEnrollment(null)

        if (!cancelled) {
          setAuthenticator(prepared)
          setUsername(prepared.username)
          setStage("totp")
        }
      } catch (caught) {
        if (!cancelled) {
          setError(errorMessage(caught, t))
          setStage("code")
        }
      }
    }

    void resolveExistingEnrollment()

    return () => {
      cancelled = true
    }
  }, [t])

  function handleCode(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    setIsSubmitting(true)

    void verifyOperatorEnrollmentCode(enrollmentCode.trim())
      .then((state) => {
        setUsername(state.username)
        setEnrollmentCode("")
        setStage("password")
      })
      .catch((caught) => {
        setError(errorMessage(caught, t))
      })
      .finally(() => {
        setIsSubmitting(false)
      })
  }

  function handlePassword(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)

    if (password !== passwordConfirmation) {
      setError(t("operatorEnrollment.error.passwordMismatch"))
      return
    }

    setIsSubmitting(true)

    void prepareOperatorEnrollment(password)
      .then((prepared) => {
        setAuthenticator(prepared)
        setUsername(prepared.username)
        setPassword("")
        setPasswordConfirmation("")
        setStage("totp")
      })
      .catch((caught) => {
        setError(errorMessage(caught, t))
      })
      .finally(() => {
        setIsSubmitting(false)
      })
  }

  function handleVerifyTotp(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    setIsSubmitting(true)

    void verifyOperatorEnrollmentTotp(totpCode.trim())
      .then(() => completeOperatorEnrollment())
      .then((completed) => {
        setUsername(completed.username)
        setRecoveryCodes(completed.recoveryCodes)
        setHasStoredRecoveryCodes(false)
        setTotpCode("")
        setStage("recovery")
      })
      .catch((caught) => {
        setError(errorMessage(caught, t))
      })
      .finally(() => {
        setIsSubmitting(false)
      })
  }

  function handleGenerateRecoveryCodes() {
    setError(null)
    setIsSubmitting(true)

    void completeOperatorEnrollment()
      .then((completed) => {
        setUsername(completed.username)
        setRecoveryCodes(completed.recoveryCodes)
        setHasStoredRecoveryCodes(false)
        setStage("recovery")
      })
      .catch((caught) => {
        setError(errorMessage(caught, t))
      })
      .finally(() => {
        setIsSubmitting(false)
      })
  }

  function handleCancel() {
    setIsSubmitting(true)

    void cancelOperatorEnrollment()
      .finally(() => {
        navigate("/login", { replace: true })
      })
  }

  if (stage === "checking") {
    return (
      <main className="flex min-h-screen items-center justify-center bg-background text-foreground">
        <div className="text-sm text-muted-foreground">{t("operatorEnrollment.checking")}</div>
      </main>
    )
  }

  return (
    <main className="min-h-screen bg-background text-foreground">
      <div className="mx-auto flex min-h-screen w-full max-w-lg flex-col justify-center px-6 py-12">
        <div className="mb-4 flex justify-end">
          <LanguageSelect id="operator-enrollment-language" />
        </div>

        <Card className="w-full">
          <CardHeader className="space-y-3">
            <div className="flex h-12 w-12 items-center justify-center rounded-2xl border border-border bg-muted">
              <ShieldCheck className="h-6 w-6" />
            </div>
            <div>
              <h1 className="text-2xl font-semibold tracking-tight">
                {t("operatorEnrollment.title")}
              </h1>
              <CardDescription className="mt-2">
                {stage === "code"
                  ? t("operatorEnrollment.codeDescription")
                  : t("operatorEnrollment.description", {
                      username: username ?? t("operatorEnrollment.operatorFallback"),
                    })}
              </CardDescription>
            </div>
          </CardHeader>

          <CardContent>
            {error ? (
              <Alert variant="destructive" className="mb-4">
                <AlertTitle>{t("operatorEnrollment.errorTitle")}</AlertTitle>
                <AlertDescription>{error}</AlertDescription>
              </Alert>
            ) : null}

            {stage === "code" ? (
              <form className="space-y-4" onSubmit={handleCode}>
                <div className="space-y-2">
                  <label htmlFor="operator-enrollment-code" className="text-sm font-medium">
                    {t("operatorEnrollment.codeLabel")}
                  </label>
                  <Input
                    id="operator-enrollment-code"
                    type="password"
                    autoComplete="one-time-code"
                    autoCorrect="off"
                    autoCapitalize="none"
                    spellCheck={false}
                    data-lpignore="true"
                    data-1p-ignore="true"
                    autoFocus
                    value={enrollmentCode}
                    onChange={(event) => setEnrollmentCode(event.target.value)}
                    placeholder="mem_enrol_..."
                    disabled={isSubmitting}
                  />
                </div>

                <Alert>
                  <KeyRound className="h-4 w-4" />
                  <AlertTitle>{t("operatorEnrollment.codeSafetyTitle")}</AlertTitle>
                  <AlertDescription>{t("operatorEnrollment.codeSafetyDescription")}</AlertDescription>
                </Alert>

                <Button
                  type="submit"
                  className="w-full"
                  disabled={isSubmitting || enrollmentCode.trim().length === 0}
                >
                  {isSubmitting ? t("operatorEnrollment.codeSubmitting") : t("operatorEnrollment.codeSubmit")}
                </Button>
              </form>
            ) : null}

            {stage === "password" ? (
              <form className="space-y-4" onSubmit={handlePassword}>
                <div className="space-y-2">
                  <label htmlFor="operator-enrollment-password" className="text-sm font-medium">
                    {t("operatorEnrollment.passwordLabel")}
                  </label>
                  <PasswordInput
                    id="operator-enrollment-password"
                    autoComplete="new-password"
                    autoFocus
                    value={password}
                    onChange={(event) => setPassword(event.target.value)}
                    disabled={isSubmitting}
                  />
                  <p className="text-xs text-muted-foreground">
                    {t("operatorEnrollment.passwordDescription")}
                  </p>
                </div>

                <div className="space-y-2">
                  <label htmlFor="operator-enrollment-password-confirmation" className="text-sm font-medium">
                    {t("operatorEnrollment.passwordConfirmationLabel")}
                  </label>
                  <PasswordInput
                    id="operator-enrollment-password-confirmation"
                    autoComplete="new-password"
                    value={passwordConfirmation}
                    onChange={(event) => setPasswordConfirmation(event.target.value)}
                    disabled={isSubmitting}
                  />
                </div>

                <div className="flex flex-col gap-2 sm:flex-row">
                  <Button
                    type="submit"
                    className="flex-1"
                    disabled={isSubmitting || password.length === 0 || passwordConfirmation.length === 0}
                  >
                    {isSubmitting ? t("operatorEnrollment.passwordSubmitting") : t("operatorEnrollment.passwordSubmit")}
                  </Button>
                  <Button type="button" variant="outline" onClick={handleCancel} disabled={isSubmitting}>
                    {t("operatorEnrollment.cancel")}
                  </Button>
                </div>
              </form>
            ) : null}

            {stage === "totp" && authenticator ? (
              <form className="space-y-4" onSubmit={handleVerifyTotp}>
                <div className="rounded-lg border border-border bg-muted/40 p-4">
                  <div className="text-sm font-medium">{t("operatorEnrollment.qrTitle")}</div>
                  <p className="mt-1 text-xs text-muted-foreground">
                    {t("operatorEnrollment.qrDescription")}
                  </p>
                  <div className="mt-3 flex justify-center rounded-lg bg-white p-3">
                    <TotpEnrollmentQrCode
                      authenticatorUri={authenticator.authenticatorUri}
                      title={t("operatorEnrollment.qrTitle")}
                    />
                  </div>
                </div>

                <div className="rounded-lg border border-border bg-muted/40 p-4">
                  <div className="text-sm font-medium">{t("operatorEnrollment.manualEntryTitle")}</div>
                  <dl className="mt-3 space-y-2 text-sm">
                    <div className="grid gap-1 sm:grid-cols-[9rem_1fr]">
                      <dt className="text-muted-foreground">{t("operatorEnrollment.authenticatorIssuer")}</dt>
                      <dd>MEM Control Plane</dd>
                    </div>
                    <div className="grid gap-1 sm:grid-cols-[9rem_1fr]">
                      <dt className="text-muted-foreground">{t("operatorEnrollment.authenticatorAccount")}</dt>
                      <dd>{authenticator.username}</dd>
                    </div>
                    <div className="grid gap-1 sm:grid-cols-[9rem_1fr]">
                      <dt className="text-muted-foreground">{t("operatorEnrollment.authenticatorKey")}</dt>
                      <dd>
                        <code className="break-all rounded bg-background px-1 py-0.5">
                          {authenticator.manualEntryKey}
                        </code>
                      </dd>
                    </div>
                  </dl>
                </div>

                <div className="space-y-2">
                  <label htmlFor="operator-enrollment-totp" className="text-sm font-medium">
                    {t("operatorEnrollment.totpLabel")}
                  </label>
                  <Input
                    id="operator-enrollment-totp"
                    inputMode="numeric"
                    autoComplete="one-time-code"
                    autoCorrect="off"
                    autoCapitalize="none"
                    spellCheck={false}
                    autoFocus
                    value={totpCode}
                    onChange={(event) => setTotpCode(event.target.value)}
                    placeholder="123456"
                    disabled={isSubmitting}
                  />
                </div>

                <div className="flex flex-col gap-2 sm:flex-row">
                  <Button type="submit" className="flex-1" disabled={isSubmitting}>
                    {isSubmitting ? t("operatorEnrollment.totpSubmitting") : t("operatorEnrollment.totpSubmit")}
                  </Button>
                  <Button type="button" variant="outline" onClick={handleCancel} disabled={isSubmitting}>
                    {t("operatorEnrollment.cancel")}
                  </Button>
                </div>
              </form>
            ) : null}

            {stage === "ready" ? (
              <div className="space-y-4">
                <Alert>
                  <AlertTitle>{t("operatorEnrollment.readyTitle")}</AlertTitle>
                  <AlertDescription>{t("operatorEnrollment.readyDescription")}</AlertDescription>
                </Alert>

                <div className="flex flex-col gap-2 sm:flex-row">
                  <Button
                    type="button"
                    className="flex-1"
                    onClick={handleGenerateRecoveryCodes}
                    disabled={isSubmitting}
                  >
                    {isSubmitting ? t("operatorEnrollment.recoveryGenerating") : t("operatorEnrollment.recoveryGenerate")}
                  </Button>
                  <Button type="button" variant="outline" onClick={handleCancel} disabled={isSubmitting}>
                    {t("operatorEnrollment.cancel")}
                  </Button>
                </div>
              </div>
            ) : null}

            {stage === "recovery" ? (
              <div className="space-y-4">
                <Alert>
                  <AlertTitle>{t("operatorEnrollment.recoveryAlertTitle")}</AlertTitle>
                  <AlertDescription>{t("operatorEnrollment.recoveryAlertDescription")}</AlertDescription>
                </Alert>

                <pre
                  className="max-h-56 overflow-auto rounded-lg border border-border bg-muted p-4 text-sm"
                  aria-label={t("operatorEnrollment.recoveryCodesLabel")}
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
                  <span>{t("operatorEnrollment.recoveryAcknowledgement")}</span>
                </label>

                <Button
                  type="button"
                  className="w-full"
                  onClick={() => navigate("/dashboard", { replace: true })}
                  disabled={!hasStoredRecoveryCodes}
                >
                  {t("operatorEnrollment.finish")}
                </Button>
              </div>
            ) : null}
          </CardContent>
        </Card>
      </div>
    </main>
  )
}
