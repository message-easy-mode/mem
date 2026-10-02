import { useEffect, useState } from "react"
import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { ArrowLeft, CheckCircle2, Loader2, LockKeyhole, PlayCircle } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import { ApiProblemAlert } from "@/components/operator/api-problem-alert"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { getJson, postJson } from "@/lib/api"
import { getSetupReview } from "@/features/setup/review/api/setup-review.api"
import type { SetupReviewResponse } from "@/features/setup/review/api/setup-review.types"

import type { RunInstallationResponse } from "../../install-run/api/installation-run.types"

type InstallPlanResponse = {
  id: string
  status: string
  configJson: string | null
  frozenConfigJson: string | null
  lastError: string | null
}

async function getCurrentInstallPlan() {
  return getJson<InstallPlanResponse | null>("/api/setup/install-plans/current")
}

async function runInstallPlan(id: string) {
  return postJson<void, RunInstallationResponse>(
    `/api/setup/install-runs/${id}/run`,
  )
}

export function SetupInstallPage() {
  const { t } = useI18n()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const readOnlyPreview = searchParams.get("setupPreview") === "1"

  const [isStarting, setIsStarting] = useState(false)
  const [loading, setLoading] = useState(true)
  const [plan, setPlan] = useState<InstallPlanResponse | null>(null)
  const [review, setReview] = useState<SetupReviewResponse | null>(null)
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    let cancelled = false

    async function load() {
      try {
        const [current, currentReview] = await Promise.all([
          getCurrentInstallPlan(),
          getSetupReview(),
        ])

        if (!cancelled) {
          setPlan(current)
          setReview(currentReview)
        }
      } catch (err) {
        if (!cancelled) {
          setError(err)
        }
      } finally {
        if (!cancelled) {
          setLoading(false)
        }
      }
    }

    void load()

    return () => {
      cancelled = true
    }
  }, [])

  async function handleStartInstall() {
    if (!plan || !review?.reviewAccepted || review.installationId !== plan.id) {
      return
    }

    setIsStarting(true)
    setError(null)

    try {
      const result = await runInstallPlan(plan.id)

      if (!result.accepted) {
        throw new Error(result.message)
      }

      navigate(`/setup/install/${plan.id}`)
    } catch (err) {
      setError(err)
    } finally {
      setIsStarting(false)
    }
  }

  const reviewReady = Boolean(
    plan &&
      review?.reviewAccepted &&
      review.installationId === plan.id &&
      plan.frozenConfigJson,
  )

  if (loading) {
    return <div className="p-8 text-sm text-muted-foreground">{t("setup.install.loading")}</div>
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <PageBreadcrumbs
            items={[
              { label: t("navigation.setupSection"), to: "/setup/start" },
              { label: t("navigation.setup.review"), to: "/setup/review" },
              { label: t("setup.install.title") },
            ]}
          />

          <h1 className="text-2xl font-semibold tracking-tight">{t("setup.install.title")}</h1>

          <p className="max-w-3xl text-sm text-muted-foreground">
            {t("setup.install.description")}
          </p>
        </div>

        <Button asChild variant="outline">
          <Link to="/setup/review">
            <ArrowLeft className="mr-2 h-4 w-4" />
            {t("setup.install.backReview")}
          </Link>
        </Button>
      </div>

      {readOnlyPreview ? (
        <Card className="border-amber-500/30 bg-amber-500/5">
          <CardHeader>
            <CardTitle>{t("setup.install.previewLockedTitle")}</CardTitle>
            <CardDescription>{t("setup.install.previewLockedDescription")}</CardDescription>
          </CardHeader>
          <CardContent className="text-sm text-muted-foreground">
            {t("setup.install.previewLockedEvidence")}
          </CardContent>
        </Card>
      ) : reviewReady && review ? (
        <Card className="border-emerald-500/30 bg-emerald-500/5">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <CheckCircle2 className="h-5 w-5 text-emerald-300" />
              {t("setup.install.readyTitle")}
            </CardTitle>
            <CardDescription>
              {t("setup.install.readyDescription")}
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-5">
            <div className="grid gap-3 text-sm sm:grid-cols-2 md:grid-cols-3 xl:grid-cols-6">
              <InstallValue label={t("setup.install.domain")} value={review.domain.baseDomain} />
              <InstallValue label={t("setup.install.certificate")} value={review.domain.certificateEnvironment} />
              <InstallValue label={t("setup.install.postgres")} value={review.platform.postgresContainerName} />
              <InstallValue label={t("setup.install.npmIngress")} value={review.platform.npmContainerName} />
              <InstallValue label={t("setup.install.sharedTurn")} value={review.platform.coturnContainerName} />
              <InstallValue
                label={t("setup.install.supportTools")}
                value={review.platform.enabledSupportTools.length > 0
                  ? review.platform.enabledSupportTools.join(", ")
                  : t("setup.install.noneSelected")}
              />
            </div>

            <div className="rounded-xl border border-border bg-background/50 p-4 text-sm text-muted-foreground">
              <div className="flex items-start gap-3">
                <LockKeyhole className="mt-0.5 h-4 w-4 shrink-0" />
                <div>
                  <div className="font-medium text-foreground">{t("setup.install.authority")}</div>
                  <div className="mt-1">
                    {t("setup.install.effects")}
                  </div>
                </div>
              </div>
            </div>

            <details className="rounded-xl border border-border bg-background/40 p-4">
              <summary className="cursor-pointer text-sm font-medium text-muted-foreground">{t("setup.install.showReference")}</summary>
              <div className="mt-3 space-y-1 text-xs text-muted-foreground">
                <div className="break-all">{t("setup.install.technical.installation", { value: plan?.id ?? t("setup.common.unavailable") })}</div>
                <div className="break-all">{t("setup.install.technical.fingerprint", { value: review.planSha256 ?? t("setup.common.unavailable") })}</div>
                <div>{t("setup.install.technical.reviewedAt", { value: review.reviewedAtUtc ?? t("setup.common.unavailable") })}</div>
              </div>
            </details>
          </CardContent>
        </Card>
      ) : (
        <Card className="border-amber-500/30 bg-amber-500/5">
          <CardHeader>
            <CardTitle>{t("setup.install.reviewRequiredTitle")}</CardTitle>
            <CardDescription>
              {t("setup.install.reviewRequiredDescription")}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <Button asChild>
              <Link to="/setup/review">{t("setup.install.returnReview")}</Link>
            </Button>
          </CardContent>
        </Card>
      )}

      {error ? (
        <ApiProblemAlert
          error={error}
          title={t("setup.install.failedTitle")}
          fallbackDescription={t("setup.install.failedDescription")}
        />
      ) : null}

      {!readOnlyPreview ? (
        <div className="flex flex-col gap-2 sm:flex-row sm:justify-end">
          <Button asChild variant="outline">
            <Link to="/setup/review">{t("setup.install.backReview")}</Link>
          </Button>

          <Button onClick={handleStartInstall} disabled={isStarting || !reviewReady}>
            {isStarting ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <PlayCircle className="mr-2 h-4 w-4" />
            )}
            {isStarting ? t("setup.install.starting") : t("setup.install.start")}
          </Button>
        </div>
      ) : null}
    </div>
  )
}

function InstallValue({ label, value }: { label: string; value: string }) {
  const { t } = useI18n()
  return (
    <div className="rounded-lg border border-border bg-background/40 p-3">
      <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-1 break-words font-medium">{value || t("setup.review.notConfigured")}</div>
    </div>
  )
}
