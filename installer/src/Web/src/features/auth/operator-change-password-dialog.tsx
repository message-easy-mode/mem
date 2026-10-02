import { useEffect, useState, type FormEvent } from "react"
import { createPortal } from "react-dom"
import { KeyRound } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { PasswordInput } from "@/features/auth/password-input"
import { changeOperatorPassword } from "@/features/auth/control-plane-auth"

type OperatorChangePasswordDialogProps = {
  open: boolean
  onOpenChange: (open: boolean) => void
  onChanged: () => void
}

/**
 * Accepts only the replacement password after a separate password+TOTP
 * step-up. Submitted secret values are cleared from component state before the
 * network result is rendered and are never persisted in browser storage.
 */
export function OperatorChangePasswordDialog({
  open,
  onOpenChange,
  onChanged,
}: OperatorChangePasswordDialogProps) {
  const { t } = useI18n()
  const [newPassword, setNewPassword] = useState("")
  const [confirmation, setConfirmation] = useState("")
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  useEffect(() => {
    if (open) {
      return
    }

    setNewPassword("")
    setConfirmation("")
    setError(null)
    setIsSubmitting(false)
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
    return () => window.removeEventListener("keydown", onKeyDown)
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

    const submittedPassword = newPassword
    const submittedConfirmation = confirmation

    setNewPassword("")
    setConfirmation("")
    setError(null)

    if (!submittedPassword) {
      setError(t("passwordChange.required"))
      return
    }

    if (submittedPassword !== submittedConfirmation) {
      setError(t("passwordChange.mismatch"))
      return
    }

    setIsSubmitting(true)

    void changeOperatorPassword(submittedPassword, submittedConfirmation)
      .then((result) => {
        switch (result.status) {
          case "changed":
            // Keep the modal covering the authenticated page until the caller
            // performs the hard login navigation. Closing first produces a
            // visible dashboard/page flash after the server has already
            // revoked this browser's authority.
            onChanged()
            return
          case "step_up_required":
            setError(t("passwordChange.stepUpExpired"))
            return
          case "confirmation_mismatch":
            setError(t("passwordChange.mismatch"))
            return
          case "must_differ":
            setError(t("passwordChange.mustDiffer"))
            return
          case "not_accepted":
            setError(t("passwordChange.notAccepted"))
            return
          default:
            setError(t("passwordChange.unavailable"))
        }
      })
      .catch(() => {
        setError(t("passwordChange.connectionFailed"))
      })
      .finally(() => {
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
          aria-labelledby="operator-change-password-title"
          className="w-full max-w-md shadow-xl"
        >
          <CardHeader>
            <div className="space-y-3">
              <div className="flex h-11 w-11 items-center justify-center rounded-2xl border border-border bg-muted">
                <KeyRound className="h-5 w-5" />
              </div>
              <div>
                <CardTitle id="operator-change-password-title" className="text-xl">
                  {t("passwordChange.title")}
                </CardTitle>
                <CardDescription className="mt-2">
                  {t("passwordChange.description")}
                </CardDescription>
              </div>
            </div>
          </CardHeader>

          <CardContent>
            <form className="space-y-4" onSubmit={handleSubmit}>
              {error ? (
                <Alert variant="destructive">
                  <AlertTitle>{t("passwordChange.errorTitle")}</AlertTitle>
                  <AlertDescription>{error}</AlertDescription>
                </Alert>
              ) : null}

              <div className="space-y-2">
                <label htmlFor="operator-new-password" className="text-sm font-medium">
                  {t("passwordChange.newPasswordLabel")}
                </label>
                <PasswordInput
                  id="operator-new-password"
                  autoComplete="new-password"
                  autoFocus
                  value={newPassword}
                  onChange={(event) => setNewPassword(event.target.value)}
                />
              </div>

              <div className="space-y-2">
                <label htmlFor="operator-confirm-password" className="text-sm font-medium">
                  {t("passwordChange.confirmPasswordLabel")}
                </label>
                <PasswordInput
                  id="operator-confirm-password"
                  autoComplete="new-password"
                  value={confirmation}
                  onChange={(event) => setConfirmation(event.target.value)}
                />
              </div>

              <p className="text-xs text-muted-foreground">
                {t("passwordChange.policyHelp")}
              </p>

              <Alert>
                <AlertTitle>{t("passwordChange.sessionTitle")}</AlertTitle>
                <AlertDescription>{t("passwordChange.sessionDescription")}</AlertDescription>
              </Alert>

              <div className="flex justify-end gap-2 pt-1">
                <Button
                  type="button"
                  variant="outline"
                  disabled={isSubmitting}
                  onClick={close}
                >
                  {t("passwordChange.cancel")}
                </Button>
                <Button type="submit" disabled={isSubmitting}>
                  {isSubmitting ? t("passwordChange.changing") : t("passwordChange.change")}
                </Button>
              </div>
            </form>
          </CardContent>
        </Card>
      </div>
    </div>,
    document.body,
  )
}
