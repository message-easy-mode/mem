import { useEffect, useState, type FormEvent } from "react"
import { createPortal } from "react-dom"
import { ShieldCheck } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { PasswordInput } from "@/features/auth/password-input"
import { verifyOperatorStepUp } from "@/features/auth/control-plane-auth"

type OperatorStepUpDialogProps = {
  open: boolean
  onOpenChange: (open: boolean) => void
  /**
   * Optional bounded continuation for a high-risk action that was already
   * explicitly confirmed. It runs only after the server issues a new
   * session-bound step-up grant.
   */
  onVerified?: () => void
}

/**
 * Reusable re-authentication surface for a later high-risk action. It keeps
 * password and TOTP values in component state only, clears them before every
 * network result is rendered, and deliberately does not offer recovery-code
 * completion: recovery codes are not an equivalent step-up factor.
 */
export function OperatorStepUpDialog({
  open,
  onOpenChange,
  onVerified,
}: OperatorStepUpDialogProps) {
  const { t } = useI18n()
  const [password, setPassword] = useState("")
  const [totp, setTotp] = useState("")
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [verified, setVerified] = useState(false)

  useEffect(() => {
    if (open) {
      return
    }

    setPassword("")
    setTotp("")
    setError(null)
    setIsSubmitting(false)
    setVerified(false)
  }, [open])

  useEffect(() => {
    if (!open) {
      return
    }

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape" && !isSubmitting) {
        onOpenChange(false)
      }
    }

    window.addEventListener("keydown", onKeyDown)

    return () => {
      window.removeEventListener("keydown", onKeyDown)
    }
  }, [isSubmitting, onOpenChange, open])

  if (!open) {
    return null
  }

  function close() {
    if (!isSubmitting) {
      onOpenChange(false)
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const submittedPassword = password
    const submittedTotp = totp.trim()

    setPassword("")
    setTotp("")
    setError(null)
    setIsSubmitting(true)

    void verifyOperatorStepUp(submittedPassword, submittedTotp).then((result) => {
      if (result.status === "verified") {
        if (onVerified) {
          // A caller can resume only the exact action it already confirmed.
          // Close this sensitive credential surface before that retry starts.
          onOpenChange(false)
          onVerified()
          return
        }

        setVerified(true)
        return
      }

      setError(
        result.status === "unavailable"
          ? t("stepUp.unavailable")
          : t("stepUp.invalid"),
      )
    }).catch(() => {
      setError(t("stepUp.connectionFailed"))
    }).finally(() => {
      setIsSubmitting(false)
    })
  }

  return createPortal(
    <div className="fixed inset-0 z-[60] overflow-y-auto overscroll-contain px-4 py-6 sm:px-6 sm:py-8">
      <button
        type="button"
        aria-hidden="true"
        tabIndex={-1}
        className="fixed inset-0 bg-background/85 backdrop-blur-sm"
        disabled={isSubmitting}
        onClick={close}
      />

      <div className="relative z-10 flex min-h-full items-start justify-center [@media(min-height:42rem)]:items-center">
        <Card
          role="dialog"
          aria-modal="true"
          aria-labelledby="operator-step-up-title"
          className="w-full max-w-md shadow-xl"
        >
          <CardHeader>
            <div className="space-y-3">
              <div className="flex h-11 w-11 items-center justify-center rounded-2xl border border-border bg-muted">
                <ShieldCheck className="h-5 w-5" />
              </div>
              <div>
                <CardTitle id="operator-step-up-title" className="text-xl">
                  {t("stepUp.title")}
                </CardTitle>
                <CardDescription className="mt-2">
                  {t("stepUp.description")}
                </CardDescription>
              </div>
            </div>
          </CardHeader>

          <CardContent>
            {verified ? (
              <Alert>
                <AlertTitle>{t("stepUp.verifiedTitle")}</AlertTitle>
                <AlertDescription>{t("stepUp.verifiedDescription")}</AlertDescription>
              </Alert>
            ) : (
              <form className="space-y-4" onSubmit={handleSubmit}>
                {error ? (
                  <Alert variant="destructive">
                    <AlertTitle>{t("stepUp.errorTitle")}</AlertTitle>
                    <AlertDescription>{error}</AlertDescription>
                  </Alert>
                ) : null}

                <div className="space-y-2">
                  <label htmlFor="step-up-password" className="text-sm font-medium">
                    {t("stepUp.passwordLabel")}
                  </label>
                  <PasswordInput
                    id="step-up-password"
                    autoComplete="current-password"
                    autoFocus
                    value={password}
                    onChange={(event) => setPassword(event.target.value)}
                  />
                </div>

                <div className="space-y-2">
                  <label htmlFor="step-up-totp" className="text-sm font-medium">
                    {t("stepUp.totpLabel")}
                  </label>
                  <Input
                    id="step-up-totp"
                    inputMode="numeric"
                    autoComplete="one-time-code"
                    autoCorrect="off"
                    autoCapitalize="none"
                    spellCheck={false}
                    value={totp}
                    onChange={(event) => setTotp(event.target.value)}
                    placeholder="123456"
                  />
                </div>

                <p className="text-xs text-muted-foreground">
                  {t("stepUp.recoveryCodeNotAccepted")}
                </p>

                <div className="flex justify-end gap-2 pt-1">
                  <Button
                    type="button"
                    variant="outline"
                    disabled={isSubmitting}
                    onClick={close}
                  >
                    {t("stepUp.cancel")}
                  </Button>
                  <Button type="submit" disabled={isSubmitting}>
                    {isSubmitting ? t("stepUp.verifying") : t("stepUp.verify")}
                  </Button>
                </div>
              </form>
            )}

            {verified ? (
              <div className="mt-4 flex justify-end">
                <Button type="button" onClick={close}>
                  {t("stepUp.close")}
                </Button>
              </div>
            ) : null}
          </CardContent>
        </Card>
      </div>
    </div>,
    document.body,
  )
}
