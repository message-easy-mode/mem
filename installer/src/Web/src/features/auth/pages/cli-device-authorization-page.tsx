import { useEffect, useState, type FormEvent } from "react"
import { KeyRound } from "lucide-react"

import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import { Input } from "@/components/ui/input"
import {
  approveCliDeviceAuthorization,
  denyCliDeviceAuthorization,
  reviewCliDeviceAuthorization,
  type CliDeviceAuthorizationReviewResult,
} from "@/features/auth/cli-device-authorization"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"

type PendingReview = Extract<
  CliDeviceAuthorizationReviewResult,
  { status: "authorization_pending" }
>

type Outcome = "approved" | "denied" | null

type ErrorState = {
  title: "review" | "approval" | "denial"
  message: string
}

/**
 * Same-origin browser approval surface for a CLI device code. The short code
 * lives in React state only: this route does not read it from a query string,
 * persist it, or expose a device credential to the browser.
 */
export function CliDeviceAuthorizationPage() {
  const { language, t } = useI18n()
  const [userCode, setUserCode] = useState("")
  const [reviewedUserCode, setReviewedUserCode] = useState<string | null>(null)
  const [review, setReview] = useState<PendingReview | null>(null)
  const [outcome, setOutcome] = useState<Outcome>(null)
  const [error, setError] = useState<ErrorState | null>(null)
  const [isReviewing, setIsReviewing] = useState(false)
  const [isDeciding, setIsDeciding] = useState(false)
  const [isApprovalConfirmationOpen, setIsApprovalConfirmationOpen] = useState(false)
  const [isStepUpOpen, setIsStepUpOpen] = useState(false)
  const [awaitingStepUp, setAwaitingStepUp] = useState(false)
  const [pendingApprovalCode, setPendingApprovalCode] = useState<string | null>(null)

  useEffect(() => {
    if (
      !awaitingStepUp ||
      !pendingApprovalCode ||
      isApprovalConfirmationOpen ||
      isStepUpOpen
    ) {
      return
    }

    // The exact reviewed code remains only in component memory. Let the
    // confirmation dialog close before the independent viewport verifier
    // opens, so its focus trap has been released.
    const timer = window.setTimeout(() => {
      setIsStepUpOpen(true)
    }, 0)

    return () => {
      window.clearTimeout(timer)
    }
  }, [awaitingStepUp, isApprovalConfirmationOpen, isStepUpOpen, pendingApprovalCode])

  function resetReviewState() {
    setReviewedUserCode(null)
    setReview(null)
    setOutcome(null)
    setError(null)
    setIsApprovalConfirmationOpen(false)
    setAwaitingStepUp(false)
    setPendingApprovalCode(null)
    setIsStepUpOpen(false)
  }

  function explainReviewStatus(status: CliDeviceAuthorizationReviewResult["status"]) {
    if (status === "authorization_expired") {
      return t("cliDeviceAuthorization.expired")
    }

    if (
      status === "authorization_approved" ||
      status === "authorization_denied" ||
      status === "authorization_consumed"
    ) {
      return t("cliDeviceAuthorization.completed")
    }

    return t("cliDeviceAuthorization.unavailable")
  }

  function handleCodeChanged(value: string) {
    setUserCode(value.toUpperCase())
    resetReviewState()
  }

  function handleReview(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const submittedCode = userCode.trim()
    resetReviewState()

    if (!submittedCode) {
      setError({
        title: "review",
        message: t("cliDeviceAuthorization.codeRequired"),
      })
      return
    }

    setIsReviewing(true)

    void reviewCliDeviceAuthorization(submittedCode).then((result) => {
      if (result.status === "authorization_pending") {
        setReviewedUserCode(submittedCode)
        setReview(result)
        return
      }

      setError({
        title: "review",
        message: explainReviewStatus(result.status),
      })
    }).finally(() => {
      setIsReviewing(false)
    })
  }

  function applyDecisionFailure(
    status: "authorization_expired" | "authorization_approved" | "authorization_denied" |
      "authorization_consumed" | "authorization_unavailable" | "unavailable",
    title: "approval" | "denial",
  ) {
    const message = status === "unavailable"
      ? t("cliDeviceAuthorization.connectionFailed")
      : explainReviewStatus(status)

    setError({ title, message })
  }

  function completeApproval(code: string) {
    setIsDeciding(true)
    setError(null)

    void approveCliDeviceAuthorization(code).then((result) => {
      if (result.status === "step_up_required") {
        setIsApprovalConfirmationOpen(false)
        setPendingApprovalCode(code)
        setAwaitingStepUp(true)
        return
      }

      if (result.status === "authorization_approved") {
        setIsApprovalConfirmationOpen(false)
        setAwaitingStepUp(false)
        setPendingApprovalCode(null)
        setReview(null)
        setReviewedUserCode(null)
        setUserCode("")
        setOutcome("approved")
        return
      }

      applyDecisionFailure(result.status, "approval")
    }).finally(() => {
      setIsDeciding(false)
    })
  }

  function beginApproval() {
    if (!review || !reviewedUserCode) {
      return
    }

    setError(null)
    setIsApprovalConfirmationOpen(true)
  }

  function approveConfirmedDevice() {
    if (reviewedUserCode) {
      completeApproval(reviewedUserCode)
    }
  }

  function denyReviewedDevice() {
    if (!reviewedUserCode) {
      return
    }

    const code = reviewedUserCode
    setIsDeciding(true)
    setError(null)

    void denyCliDeviceAuthorization(code).then((result) => {
      if (result.status === "authorization_denied") {
        setReview(null)
        setReviewedUserCode(null)
        setUserCode("")
        setOutcome("denied")
        return
      }

      applyDecisionFailure(result.status, "denial")
    }).finally(() => {
      setIsDeciding(false)
    })
  }

  function changeApprovalConfirmationOpen(open: boolean) {
    setIsApprovalConfirmationOpen(open)

    if (!open && !isDeciding) {
      setError(null)
    }
  }

  function changeStepUpOpen(open: boolean) {
    setIsStepUpOpen(open)

    if (!open) {
      // OperatorStepUpDialog closes itself before invoking onVerified. Keep the
      // exact in-memory code long enough for that synchronous continuation,
      // but do not reopen the verifier after a user cancellation.
      setAwaitingStepUp(false)
      window.setTimeout(() => {
        setPendingApprovalCode(null)
      }, 0)
    }
  }

  const hasCompletedOutcome = outcome !== null

  function resumeApprovalAfterStepUp() {
    const code = pendingApprovalCode
    setAwaitingStepUp(false)
    setPendingApprovalCode(null)
    setIsStepUpOpen(false)

    if (code) {
      completeApproval(code)
    }
  }

  return (
    <main className="min-h-full bg-background px-4 py-8 text-foreground sm:px-6 sm:py-12">
      <div className="mx-auto w-full max-w-xl space-y-5">
        <Card>
          <CardHeader className="space-y-3">
            <div className="flex h-12 w-12 items-center justify-center rounded-2xl border border-border bg-muted">
              <KeyRound className="h-6 w-6" />
            </div>
            <div>
              <CardTitle className="text-2xl">{t("cliDeviceAuthorization.title")}</CardTitle>
              <CardDescription className="mt-2">
                {t("cliDeviceAuthorization.description")}
              </CardDescription>
            </div>
          </CardHeader>

          <CardContent className="space-y-5">
            {outcome === "approved" ? (
              <Alert>
                <AlertTitle>{t("cliDeviceAuthorization.approvedTitle")}</AlertTitle>
                <AlertDescription>{t("cliDeviceAuthorization.approvedDescription")}</AlertDescription>
              </Alert>
            ) : null}

            {outcome === "denied" ? (
              <Alert>
                <AlertTitle>{t("cliDeviceAuthorization.deniedTitle")}</AlertTitle>
                <AlertDescription>{t("cliDeviceAuthorization.deniedDescription")}</AlertDescription>
              </Alert>
            ) : null}

            {error ? (
              <Alert variant="destructive">
                <AlertTitle>
                  {error.title === "review"
                    ? t("cliDeviceAuthorization.reviewErrorTitle")
                    : error.title === "approval"
                      ? t("cliDeviceAuthorization.approvalErrorTitle")
                      : t("cliDeviceAuthorization.denialErrorTitle")}
                </AlertTitle>
                <AlertDescription>{error.message}</AlertDescription>
              </Alert>
            ) : null}

            {hasCompletedOutcome ? (
              <Button type="button" variant="outline" onClick={resetReviewState}>
                {t("cliDeviceAuthorization.authorizeAnother")}
              </Button>
            ) : null}

            {!hasCompletedOutcome ? (
              <form className="space-y-3" onSubmit={handleReview}>
                <div className="space-y-2">
                  <label htmlFor="cli-device-code" className="text-sm font-medium">
                    {t("cliDeviceAuthorization.codeLabel")}
                  </label>
                  <Input
                    id="cli-device-code"
                    value={userCode}
                    onChange={(event) => handleCodeChanged(event.target.value)}
                    placeholder={t("cliDeviceAuthorization.codePlaceholder")}
                    autoComplete="off"
                    autoCapitalize="characters"
                    autoCorrect="off"
                    spellCheck={false}
                    disabled={isReviewing || isDeciding || isStepUpOpen}
                  />
                </div>
                <Button type="submit" disabled={isReviewing || isDeciding || isStepUpOpen}>
                  {isReviewing
                    ? t("cliDeviceAuthorization.reviewing")
                    : t("cliDeviceAuthorization.review")}
                </Button>
              </form>
            ) : null}

            {!hasCompletedOutcome && review ? (
              <div className="space-y-4 rounded-lg border border-border p-4">
                <div>
                  <h2 className="text-base font-semibold">
                    {t("cliDeviceAuthorization.reviewTitle")}
                  </h2>
                  <p className="mt-1 text-sm leading-relaxed text-muted-foreground">
                    {t("cliDeviceAuthorization.reviewDescription")}
                  </p>
                </div>

                <dl className="grid gap-3 text-sm sm:grid-cols-2">
                  <div className="rounded-md bg-muted/50 p-3">
                    <dt className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                      {t("cliDeviceAuthorization.deviceLabel")}
                    </dt>
                    <dd className="mt-1 break-words font-medium">{review.deviceLabel}</dd>
                  </div>
                  <div className="rounded-md bg-muted/50 p-3">
                    <dt className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                      {t("cliDeviceAuthorization.expiresAt")}
                    </dt>
                    <dd className="mt-1 font-medium">
                      {formatDateTime(review.expiresAtUtc, language)}
                    </dd>
                  </div>
                </dl>

                <Alert>
                  <AlertTitle>{t("cliDeviceAuthorization.noCredentialNoticeTitle")}</AlertTitle>
                  <AlertDescription>{t("cliDeviceAuthorization.noCredentialNotice")}</AlertDescription>
                </Alert>

                <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
                  <Button
                    type="button"
                    variant="outline"
                    disabled={isDeciding || isStepUpOpen}
                    onClick={denyReviewedDevice}
                  >
                    {isDeciding
                      ? t("cliDeviceAuthorization.denying")
                      : t("cliDeviceAuthorization.deny")}
                  </Button>
                  <Button
                    type="button"
                    disabled={isDeciding || isStepUpOpen}
                    onClick={beginApproval}
                  >
                    {isDeciding
                      ? t("cliDeviceAuthorization.approving")
                      : t("cliDeviceAuthorization.approve")}
                  </Button>
                </div>
              </div>
            ) : null}
          </CardContent>
        </Card>
      </div>

      <ConfirmationDialog
        open={isApprovalConfirmationOpen}
        onOpenChange={changeApprovalConfirmationOpen}
        title={t("cliDeviceAuthorization.approveConfirmTitle")}
        description={t("cliDeviceAuthorization.approveConfirmDescription")}
        confirmLabel={t("cliDeviceAuthorization.approveConfirmAction")}
        confirmingLabel={t("cliDeviceAuthorization.approving")}
        cancelLabel={t("stepUp.cancel")}
        onConfirm={approveConfirmedDevice}
        isConfirming={isDeciding}
      >
        {review ? (
          <div className="rounded-md border p-3 text-sm">
            <div className="font-medium break-words">{review.deviceLabel}</div>
            <div className="mt-1 text-muted-foreground">
              {t("cliDeviceAuthorization.expiresAt")}: {formatDateTime(review.expiresAtUtc, language)}
            </div>
          </div>
        ) : null}
      </ConfirmationDialog>

      <OperatorStepUpDialog
        open={isStepUpOpen}
        onOpenChange={changeStepUpOpen}
        onVerified={resumeApprovalAfterStepUp}
      />
    </main>
  )
}
