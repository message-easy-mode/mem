import { useEffect, useState } from "react"
import { Link, useNavigate } from "react-router-dom"
import { ArrowLeft, ArrowRight, CheckCircle2, CircleAlert, Loader2, LockKeyhole, PlayCircle, ShieldCheck } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import { ApiProblemAlert } from "@/components/operator/api-problem-alert"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { getNpmAdminCredential, saveNpmAdminCredential } from "../api/npm-admin-credential.api"
import type { NpmAdminCredentialProjection } from "../api/npm-admin-credential.api"
import { acceptSetupReview, getSetupReview } from "../api/setup-review.api"
import type { SetupReviewResponse } from "../api/setup-review.types"
import { getPlatformStatus } from "@/features/setup/start/api/platform-status-api"
import { getStartupResumePath } from "@/features/setup/start/api/platform-status"

export function SetupReviewPage() {
  const { t } = useI18n()
  const navigate = useNavigate()
  const [review, setReview] = useState<SetupReviewResponse | null>(null)
  const [npmCredential, setNpmCredential] = useState<NpmAdminCredentialProjection | null>(null)
  const [loading, setLoading] = useState(true)
  const [accepting, setAccepting] = useState(false)
  const [savingNpmCredential, setSavingNpmCredential] = useState(false)
  const [editingNpmCredential, setEditingNpmCredential] = useState(false)
  const [npmEmail, setNpmEmail] = useState("")
  const [npmPassword, setNpmPassword] = useState("")
  const [error, setError] = useState<unknown>(null)
  const [reviewDestination, setReviewDestination] = useState<{
    mode: "resume" | "complete"
    path: string
    label: string
  } | null>(null)

  useEffect(() => {
    let cancelled = false

    void getSetupReview()
      .then(async (reviewValue) => {
        if (cancelled) return

        setReview(reviewValue)

        if (!reviewValue.installationId) {
          try {
            const platformStatus = await getPlatformStatus()
            const resumePath = getStartupResumePath(platformStatus)

            if (!cancelled && resumePath) {
              const resumeLabel =
                platformStatus.activeInstallationStage === "handoff"
                  ? t("setup.start.resume.finish")
                  : platformStatus.activeInstallationStage === "verification"
                    ? t("setup.start.resume.openVerification")
                    : platformStatus.activeInstallationStage === "failure-review"
                      ? t("setup.start.resume.reviewFailure")
                      : t("setup.start.resume.install")
              setReviewDestination({
                mode: "resume",
                path: resumePath,
                label: resumeLabel,
              })
            } else if (!cancelled && platformStatus.startupTarget === "dashboard") {
              setReviewDestination({
                mode: "complete",
                path: "/dashboard",
                label: t("setup.review.openDashboard"),
              })
            }
          } catch {
            // Review remains a safe informational state even if the startup
            // projection cannot be loaded. The setup-start link remains available.
          }

          return
        }

        const credentialValue = await getNpmAdminCredential()
        if (cancelled) return

        setNpmCredential(credentialValue)
        setNpmEmail(
          credentialValue.administratorEmail
            ?? reviewValue.npmAdministrator.administratorEmail
            ?? reviewValue.domain.acmeEmail
            ?? "",
        )
      })
      .catch((err) => {
        if (!cancelled) setError(err)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => { cancelled = true }
  }, [t])

  async function saveNpmCredential() {
    if (!npmEmail.trim() || !npmPassword) return

    setSavingNpmCredential(true)
    setError(null)
    try {
      const stored = await saveNpmAdminCredential({
        email: npmEmail.trim(),
        password: npmPassword,
      })
      // Once the server has accepted and protected the credential, discard the
      // plaintext browser copy before performing any secondary refresh work.
      setNpmPassword("")
      setNpmCredential(stored)
      setNpmEmail(stored.administratorEmail ?? npmEmail.trim())
      setEditingNpmCredential(false)

      const refreshed = await getSetupReview()
      setReview(refreshed)
      setNpmEmail(stored.administratorEmail ?? refreshed.domain.acmeEmail ?? "")
    } catch (err) {
      setError(err)
    } finally {
      setSavingNpmCredential(false)
    }
  }

  async function acceptReview() {
    if (review?.reviewAccepted) {
      navigate("/setup/install")
      return
    }

    setAccepting(true)
    setError(null)
    try {
      const accepted = await acceptSetupReview()
      setReview(accepted)
      navigate("/setup/install")
    } catch (err) {
      setError(err)
    } finally {
      setAccepting(false)
    }
  }

  const npmAdministrator = npmCredential ?? review?.npmAdministrator ?? null

  if (loading) {
    return <div className="p-8 text-sm text-muted-foreground">{t("setup.review.loading")}</div>
  }

  if (review && !review.installationId && review.errorCode === "SetupAuthorityMissing") {
    return (
      <InactiveReviewState
        message={review.message}
        destination={reviewDestination}
      />
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <PageBreadcrumbs items={[{ label: t("navigation.setupSection"), to: "/setup/start" }, { label: t("navigation.setup.review") }]} />
          <h1 className="text-2xl font-semibold tracking-tight">{t("setup.review.title")}</h1>
          <p className="max-w-3xl text-sm text-muted-foreground">
            {t("setup.review.description")}
          </p>
        </div>
        <Button asChild variant="outline">
          <Link to="/setup/domain"><ArrowLeft className="mr-2 h-4 w-4" />{t("setup.review.backDomain")}</Link>
        </Button>
      </div>

      {error ? (
        <ApiProblemAlert
          error={error}
          title={t("setup.review.failedTitle")}
          fallbackDescription={t("setup.review.failedDescription")}
        />
      ) : null}

      {review ? (
        <>
          <Card className={review.reviewAccepted ? "border-emerald-500/30 bg-emerald-500/5" : "border-sky-500/30 bg-sky-500/5"}>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                {review.reviewAccepted ? <CheckCircle2 className="h-5 w-5 text-emerald-300" /> : <ShieldCheck className="h-5 w-5 text-sky-300" />}
                {review.reviewAccepted ? t("setup.review.frozenTitle") : t("setup.review.planTitle")}
              </CardTitle>
              <CardDescription>{review.message}</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="rounded-xl border border-border bg-background/50 p-4 text-sm text-muted-foreground">
                <div className="flex items-start gap-3">
                  <LockKeyhole className="mt-0.5 h-4 w-4 shrink-0" />
                  <div>
                    <div className="font-medium text-foreground">{t("setup.review.noMutationTitle")}</div>
                    <div className="mt-1">{t("setup.review.noMutationDescription")}</div>
                  </div>
                </div>
              </div>
              {review.blockers.length > 0 ? (
                <div className="rounded-xl border border-destructive/40 bg-destructive/5 p-4">
                  <div className="font-medium">{t("setup.review.blockers")}</div>
                  <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-muted-foreground">
                    {review.blockers.map((item) => <li key={item}>{item}</li>)}
                  </ul>
                </div>
              ) : null}
            </CardContent>
          </Card>

          <Card className={npmAdministrator?.credentialStored ? "border-emerald-500/30" : "border-amber-500/30"}>
            <CardHeader>
              <CardTitle>{t("setup.review.npmTitle")}</CardTitle>
              <CardDescription>
                {t("setup.review.npmDescription")}
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              {npmAdministrator?.credentialStored && !editingNpmCredential ? (
                <>
                  <div className="grid gap-3 text-sm sm:grid-cols-2">
                    <ReviewValue label={t("setup.review.adminEmail")} value={npmAdministrator.administratorEmail ?? t("setup.review.stored")} />
                    <ReviewValue label={t("setup.review.adminCredential")} value={t("setup.review.storedProtected")} />
                    <ReviewValue
                      label={t("setup.review.verification")}
                      value={npmAdministrator.verifiedAtUtc ? t("setup.review.verifiedAt", { timestamp: npmAdministrator.verifiedAtUtc }) : t("setup.review.verifyDuringInit")}
                    />
                  </div>
                  {!review.reviewAccepted ? (
                    <Button type="button" variant="outline" onClick={() => setEditingNpmCredential(true)}>
                      {t("setup.review.changeNpm")}
                    </Button>
                  ) : null}
                </>
              ) : (
                <div className="grid gap-4 sm:max-w-xl">
                  <div className="space-y-2">
                    <Label htmlFor="npmAdminEmail">{t("setup.review.adminEmail")}</Label>
                    <Input
                      id="npmAdminEmail"
                      type="email"
                      autoComplete="email"
                      value={npmEmail}
                      disabled={savingNpmCredential}
                      onChange={(event) => setNpmEmail(event.target.value)}
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="npmAdminPassword">{t("setup.review.adminPassword")}</Label>
                    <Input
                      id="npmAdminPassword"
                      type="password"
                      autoComplete="new-password"
                      value={npmPassword}
                      disabled={savingNpmCredential}
                      onChange={(event) => setNpmPassword(event.target.value)}
                    />
                    <p className="text-xs text-muted-foreground">
                      {t("setup.review.passwordProtection")}
                    </p>
                  </div>
                  <div className="flex flex-col gap-2 sm:flex-row">
                    {npmAdministrator?.credentialStored ? (
                      <Button type="button" variant="outline" disabled={savingNpmCredential} onClick={() => { setEditingNpmCredential(false); setNpmPassword("") }}>
                        {t("setup.common.cancel")}
                      </Button>
                    ) : null}
                    <Button
                      type="button"
                      disabled={savingNpmCredential || !npmEmail.trim() || !npmPassword}
                      onClick={saveNpmCredential}
                    >
                      {savingNpmCredential ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <LockKeyhole className="mr-2 h-4 w-4" />}
                      {t("setup.review.saveCredential")}
                    </Button>
                  </div>
                </div>
              )}
            </CardContent>
          </Card>

          <div className="grid gap-4 lg:grid-cols-2">
            <Card>
              <CardHeader><CardTitle>{t("setup.review.serverTitle")}</CardTitle><CardDescription>{t("setup.review.serverDescription")}</CardDescription></CardHeader>
              <CardContent className="space-y-2 text-sm">
                <div>{t("setup.review.preflightCounts", { passed: review.preflight.passed, warnings: review.preflight.warnings, failed: review.preflight.failed, unavailable: review.preflight.unavailable })}</div>
                <div className="text-muted-foreground">{t("setup.review.blockingFindings", { count: review.preflight.blockingIssueCount })}</div>
                {review.preflight.runId ? <div className="break-all text-xs text-muted-foreground">{t("setup.review.runId", { id: review.preflight.runId })}</div> : null}
              </CardContent>
            </Card>

            <Card>
              <CardHeader><CardTitle>{t("setup.review.publicTitle")}</CardTitle><CardDescription>{t("setup.review.publicDescription")}</CardDescription></CardHeader>
              <CardContent className="grid gap-3 text-sm sm:grid-cols-2">
                <ReviewValue label={t("setup.review.baseDomain")} value={review.domain.baseDomain} />
                <ReviewValue label={t("setup.review.wildcardCertificate")} value={review.domain.wildcardCertificate} />
                <ReviewValue label={t("setup.review.dnsProvider")} value={review.domain.dnsProvider} />
                <ReviewValue label={t("setup.review.acmeAccount")} value={review.domain.acmeEmail} />
                <ReviewValue label={t("setup.review.certificateRequest")} value={review.domain.certificateEnvironment} />
                <ReviewValue label={t("setup.review.dnsCredential")} value={review.domain.providerCredentialStored ? t("setup.review.storedProtected") : t("setup.review.missing")} />
              </CardContent>
            </Card>

            <Card>
              <CardHeader><CardTitle>{t("setup.review.servicesTitle")}</CardTitle><CardDescription>{t("setup.review.servicesDescription")}</CardDescription></CardHeader>
              <CardContent className="grid gap-3 text-sm sm:grid-cols-2">
                <ReviewValue label={t("setup.review.dockerNetwork")} value={review.platform.networkName} />
                <ReviewValue label={t("setup.review.postgres")} value={review.platform.postgresContainerName} />
                <ReviewValue label={t("setup.review.postgresVolume")} value={review.platform.postgresVolumeName} />
                <ReviewValue label={t("setup.review.npmIngress")} value={review.platform.npmContainerName} />
                <ReviewValue label={t("setup.review.publicPorts")} value={`${review.platform.npmHttpPort}, ${review.platform.npmHttpsPort}`} />
                <ReviewValue label={t("setup.review.npmAdminPort")} value={String(review.platform.npmAdminPort)} />
                <ReviewValue label={t("setup.review.coturn")} value={review.platform.coturnContainerName} />
                <ReviewValue label={t("setup.review.coturnPublicHost")} value={review.platform.coturnPublicHost} />
                <ReviewValue label={t("setup.review.coturnTurnPort")} value={`${review.platform.coturnTurnPort}/tcp+udp`} />
                <ReviewValue label={t("setup.review.coturnRelayRange")} value={review.platform.coturnRelayPortRange} />
              </CardContent>
            </Card>

            <Card>
              <CardHeader><CardTitle>{t("setup.review.willNotChange")}</CardTitle><CardDescription>{t("setup.review.willNotChangeDescription")}</CardDescription></CardHeader>
              <CardContent>
                <ul className="list-disc space-y-2 pl-5 text-sm text-muted-foreground">
                  {review.willNotChange.map((item) => <li key={item}>{item}</li>)}
                </ul>
              </CardContent>
            </Card>
          </div>

          <Card>
            <CardHeader><CardTitle>{t("setup.review.actionsAfterStart")}</CardTitle></CardHeader>
            <CardContent>
              <ol className="list-decimal space-y-2 pl-5 text-sm text-muted-foreground">
                {review.plannedActions.map((item) => <li key={item}>{item}</li>)}
              </ol>
            </CardContent>
          </Card>

          <div className="flex flex-col gap-2 sm:flex-row sm:justify-end">
            <Button asChild variant="outline"><Link to="/setup/domain">{t("setup.review.editDomain")}</Link></Button>
            <Button onClick={acceptReview} disabled={accepting || (!review.canAccept && !review.reviewAccepted)}>
              {accepting ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : review.reviewAccepted ? (
                <PlayCircle className="mr-2 h-4 w-4" />
              ) : (
                <ShieldCheck className="mr-2 h-4 w-4" />
              )}
              {review.reviewAccepted ? t("setup.review.continueInstall") : t("setup.review.accept")}
            </Button>
          </div>

          <details className="rounded-xl border border-border bg-card p-4">
            <summary className="cursor-pointer text-sm font-medium text-muted-foreground">{t("common.technicalDetails")}</summary>
            <div className="mt-4 space-y-2 text-xs text-muted-foreground">
              <div>{t("setup.review.technical.installation", { value: review.installationId ?? t("setup.common.unavailable") })}</div>
              <div>{t("setup.review.technical.status", { value: review.installationStatus })}</div>
              <div>{t("setup.review.technical.fingerprint", { value: review.planSha256 ?? t("setup.review.notAcceptedYet") })}</div>
              <div>{t("setup.review.technical.reviewedAt", { value: review.reviewedAtUtc ?? t("setup.review.notAcceptedYet") })}</div>
            </div>
          </details>
        </>
      ) : null}
    </div>
  )
}

function ReviewValue({ label, value }: { label: string; value: string }) {
  const { t } = useI18n()
  return <div className="rounded-lg border border-border bg-background/40 p-3"><div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div><div className="mt-1 break-words font-medium">{value || t("setup.review.notConfigured")}</div></div>
}
function InactiveReviewState({
  message,
  destination,
}: {
  message: string
  destination: { mode: "resume" | "complete"; path: string; label: string } | null
}) {
  const { t } = useI18n()
  const resuming = destination?.mode === "resume"
  const completed = destination?.mode === "complete"
  return (
    <div className="space-y-6">
      <div className="space-y-2">
        <PageBreadcrumbs items={[{ label: t("navigation.setupSection"), to: "/setup/start" }, { label: t("navigation.setup.review") }]} />
        <h1 className="text-2xl font-semibold tracking-tight">
          {resuming ? t("setup.review.inactive.resumeTitle") : completed ? t("setup.review.inactive.completeTitle") : t("setup.review.inactive.notReadyTitle")}
        </h1>
        <p className="max-w-3xl text-sm text-muted-foreground">
          {resuming
            ? t("setup.review.inactive.resumeDescription")
            : completed
              ? t("setup.review.completeDescription")
              : t("setup.review.inactive.notReadyDescription")}
        </p>
      </div>

      <Card className="border-sky-500/30 bg-sky-500/5">
        <CardHeader>
          <div className="flex items-start gap-3">
            <div className="mt-0.5 rounded-full border border-sky-500/30 bg-sky-500/10 p-2 text-sky-300">
              <CircleAlert className="h-5 w-5" />
            </div>
            <div>
              <CardTitle>
                {resuming ? t("setup.review.inactive.underwayTitle") : completed ? t("setup.review.historicalTitle") : t("setup.review.inactive.noActiveTitle")}
              </CardTitle>
              <CardDescription>
                {resuming
                  ? t("setup.review.inactive.underwayDescription")
                  : completed
                    ? t("setup.review.historicalDescription")
                    : t("setup.review.inactive.noActiveDescription")}
              </CardDescription>
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {!destination ? (
            <p className="text-sm text-muted-foreground">{message}</p>
          ) : null}
          <div className="rounded-xl border border-border bg-background/50 p-4 text-sm text-muted-foreground">
            {resuming
              ? t("setup.review.inactive.resumeBoundary")
              : completed
                ? t("setup.review.inactive.completeBoundary")
                : t("setup.review.inactive.notReadyBoundary")}
          </div>
          <div className="flex flex-wrap gap-2">
            {destination ? (
              <Button asChild>
                <Link to={destination.path}>
                  {destination.label}
                  <ArrowRight className="ml-2 h-4 w-4" />
                </Link>
              </Button>
            ) : null}
            <Button asChild variant={destination ? "outline" : "default"}>
              <Link to="/setup/start">{t("setup.review.returnStart")}</Link>
            </Button>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}

