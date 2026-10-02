import { useEffect, useState } from "react"
import { Link, useNavigate, useSearchParams } from "react-router-dom"
import {
  ArrowLeft,
  ArrowRight,
  CheckCircle2,
  Loader2,
  Pencil,
  ShieldCheck,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { LastResultCard } from "@/features/shared/domains/components/last-result-card"
import type { LastResult } from "@/features/shared/domains/components/types"
import { getErrorMessage } from "@/features/shared/domains/components/ingress-tls-shared"

import {
  getSetupDomainPlan,
  validateSetupDomainPlan,
} from "../api/setup-domain-plan.api"
import type { SetupDomainPlanResponse } from "../api/setup-domain-plan.types"

type DomainFormState = {
  baseDomain: string
  acmeEmail: string
  desecToken: string
  useStaging: boolean
}

export function SetupDomainPage() {
  const { t } = useI18n()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const readOnlyPreview = searchParams.get("setupPreview") === "1"
  const [plan, setPlan] = useState<SetupDomainPlanResponse | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [isValidating, setIsValidating] = useState(false)
  const [isEditing, setIsEditing] = useState(false)
  const [lastResult, setLastResult] = useState<LastResult>(null)
  const [form, setForm] = useState<DomainFormState>({
    baseDomain: "",
    acmeEmail: "",
    desecToken: "",
    useStaging: false,
  })

  useEffect(() => {
    let active = true

    void getSetupDomainPlan()
      .then((current) => {
        if (!active) {
          return
        }

        setPlan(current)
        if (current.zone || current.acmeEmail) {
          setForm((existing) => ({
            ...existing,
            baseDomain: current.zone,
            acmeEmail: current.acmeEmail,
            useStaging: current.useStaging,
          }))
        }
      })
      .catch((error) => {
        if (!active) {
          return
        }

        setLastResult({
          succeeded: false,
          status: "Failed",
          message: t("setup.domain.loadFailed"),
          errorCode: "DomainPlanLoadFailed",
          errorDetail: getErrorMessage(error),
          evidence: [],
        })
      })
      .finally(() => {
        if (active) {
          setIsLoading(false)
        }
      })

    return () => {
      active = false
    }
  }, [t])

  const baseDomain = normaliseBaseDomain(form.baseDomain)
  const wildcardDomain = baseDomain ? `*.${baseDomain}` : "*.example.com"
  const canSubmit =
    baseDomain.length > 0 &&
    form.acmeEmail.trim().length > 0 &&
    form.desecToken.trim().length > 0 &&
    !isValidating

  function updateField<TKey extends keyof DomainFormState>(
    key: TKey,
    value: DomainFormState[TKey],
  ) {
    setForm((current) => ({ ...current, [key]: value }))
  }

  async function handleValidatePlan() {
    if (!canSubmit) {
      return
    }

    setIsValidating(true)
    setLastResult(null)

    try {
      const result = await validateSetupDomainPlan({
        baseDomain,
        acmeEmail: form.acmeEmail.trim(),
        dnsProvider: "desec",
        providerToken: form.desecToken.trim(),
        useStaging: form.useStaging,
      })

      setLastResult(result)
      if (!result.succeeded) {
        return
      }

      setPlan(result)
      setForm((current) => ({ ...current, desecToken: "" }))
      setIsEditing(false)
    } catch (error) {
      setLastResult({
        succeeded: false,
        status: "Failed",
        message: t("setup.domain.requestFailed"),
        errorCode: "DomainPlanValidationRequestFailed",
        errorDetail: getErrorMessage(error),
        evidence: [],
      })
    } finally {
      setIsValidating(false)
    }
  }

  const showReady = Boolean(plan?.configured) && !isEditing

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <h1 className="text-2xl font-semibold tracking-tight">{t("setup.domain.title")}</h1>
          <p className="max-w-3xl text-sm text-muted-foreground">
            {t("setup.domain.description")}
          </p>
        </div>

        <Button asChild variant="outline">
          <Link to="/setup/check-server">
            <ArrowLeft className="mr-2 h-4 w-4" />
            {t("setup.domain.backChecks")}
          </Link>
        </Button>
      </div>

      <Card className="border-sky-500/30 bg-sky-500/5">
        <CardContent className="space-y-2 p-4 text-sm text-muted-foreground">
          <div className="font-medium text-foreground">{t("setup.domain.validationOnly")}</div>
          <p>
            {t("setup.domain.validationOnlyDescription")}
          </p>
          <p>
            {t("setup.domain.mutationBoundary")}
          </p>
        </CardContent>
      </Card>

      {isLoading ? (
        <Card>
          <CardContent className="flex items-center gap-2 p-6 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" />
            {t("setup.domain.loading")}
          </CardContent>
        </Card>
      ) : readOnlyPreview ? (
        <Card className="border-amber-500/30 bg-amber-500/5">
          <CardHeader>
            <CardTitle>{t("setup.domain.previewLockedTitle")}</CardTitle>
            <CardDescription>{t("setup.domain.previewLockedDescription")}</CardDescription>
          </CardHeader>
          <CardContent className="text-sm text-muted-foreground">
            {t("setup.domain.previewLockedEvidence")}
          </CardContent>
        </Card>
      ) : showReady && plan ? (
        <DomainPlanReadyCard
          plan={plan}
          onEdit={() => setIsEditing(true)}
          onContinue={() => navigate("/setup/review")}
        />
      ) : (
        <DomainPlanCard
          form={form}
          wildcardDomain={wildcardDomain}
          canSubmit={canSubmit}
          isPending={isValidating}
          isEditingExisting={Boolean(plan?.configured)}
          onChange={updateField}
          onSubmit={handleValidatePlan}
        />
      )}

      {lastResult ? <LastResultCard result={lastResult} /> : null}

      <details className="rounded-2xl border border-border bg-card p-4">
        <summary className="cursor-pointer text-sm font-medium text-muted-foreground">
          {t("common.technicalDetails")}
        </summary>
        <div className="mt-4 space-y-3 text-sm text-muted-foreground">
          <p>
            {t("setup.domain.technical1")}
          </p>
          <p>
            {t("setup.domain.technical2")}
          </p>
        </div>
      </details>
    </div>
  )
}

function DomainPlanCard({
  form,
  wildcardDomain,
  canSubmit,
  isPending,
  isEditingExisting,
  onChange,
  onSubmit,
}: {
  form: DomainFormState
  wildcardDomain: string
  canSubmit: boolean
  isPending: boolean
  isEditingExisting: boolean
  onChange: <TKey extends keyof DomainFormState>(
    key: TKey,
    value: DomainFormState[TKey],
  ) => void
  onSubmit: () => void
}) {
  const { t } = useI18n()
  return (
    <Card>
      <CardHeader className="text-center">
        <CardTitle>
          {isEditingExisting ? t("setup.domain.updateTitle") : t("setup.domain.chooseTitle")}
        </CardTitle>
<CardDescription>{t("setup.domain.formDescription")}</CardDescription>
      </CardHeader>

      <CardContent className="mx-auto max-w-xl space-y-6">
        <div className="rounded-2xl border border-border bg-muted/20 p-4 text-sm">
          <div className="font-medium text-foreground">{t("setup.domain.thisStepWill")}</div>
          <ul className="mt-2 space-y-1 text-muted-foreground">
            <li>✓ {t("setup.domain.will.validate")}</li>
            <li>✓ {t("setup.domain.will.readOnly")}</li>
            <li>✓ {t("setup.domain.will.store")}</li>
            <li>✓ {t("setup.domain.will.prepare")}</li>
            <li>— {t("setup.domain.will.noMutation")}</li>
          </ul>
        </div>

        <div className="space-y-2">
          <Label htmlFor="baseDomain">{t("setup.domain.baseDomain")}</Label>
          <Input
            id="baseDomain"
            value={form.baseDomain}
            placeholder="example.com"
            autoComplete="off"
            disabled={isPending}
            onChange={(event) => onChange("baseDomain", event.target.value)}
          />
          <p className="text-xs text-muted-foreground">
            {t("setup.domain.plannedWildcard", { domain: wildcardDomain })}
          </p>
        </div>

        <div className="space-y-2">
          <Label htmlFor="dnsProvider">{t("setup.domain.dnsProvider")}</Label>
          <Input id="dnsProvider" value="deSEC" disabled />
          <p className="text-xs text-muted-foreground">
            {t("setup.domain.desecSupported")}
          </p>
        </div>

        <div className="space-y-2">
          <Label htmlFor="acmeEmail">{t("setup.domain.acmeEmail")}</Label>
          <Input
            id="acmeEmail"
            value={form.acmeEmail}
            placeholder="you@example.com"
            autoComplete="email"
            disabled={isPending}
            onChange={(event) => onChange("acmeEmail", event.target.value)}
          />
        </div>

        <div className="space-y-2">
          <Label htmlFor="desecToken">{t("setup.domain.desecToken")}</Label>
          <Input
            id="desecToken"
            type="password"
            value={form.desecToken}
            placeholder={isEditingExisting ? t("setup.domain.tokenUpdatePlaceholder") : t("setup.domain.tokenPlaceholder")}
            autoComplete="off"
            disabled={isPending}
            onChange={(event) => onChange("desecToken", event.target.value)}
          />
          <p className="text-xs text-muted-foreground">
            {t("setup.domain.tokenHelp")}
          </p>
        </div>

        <label className="flex items-center gap-2 rounded-xl border border-border bg-muted/20 p-3 text-sm">
          <input
            type="checkbox"
            checked={form.useStaging}
            disabled={isPending}
            onChange={(event) => onChange("useStaging", event.target.checked)}
          />
          <span>
            <span className="block font-medium text-foreground">{t("setup.domain.staging")}</span>
            <span className="mt-0.5 block text-xs text-muted-foreground">
              {t("setup.domain.stagingHelp")}
            </span>
          </span>
        </label>

        <Button className="w-full" disabled={!canSubmit} onClick={onSubmit}>
          {isPending ? (
            <>
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              {t("setup.domain.validating")}
            </>
          ) : (
            <>
              <ShieldCheck className="mr-2 h-4 w-4" />
              {isEditingExisting ? t("setup.domain.validateUpdated") : t("setup.domain.validatePlan")}
            </>
          )}
        </Button>
      </CardContent>
    </Card>
  )
}

function DomainPlanReadyCard({
  plan,
  onEdit,
  onContinue,
}: {
  plan: SetupDomainPlanResponse
  onEdit: () => void
  onContinue: () => void
}) {
  const { t } = useI18n()
  return (
    <Card className="border-emerald-500/30 bg-emerald-500/5">
      <CardHeader className="text-center">
        <div className="mx-auto mb-2 flex h-12 w-12 items-center justify-center rounded-full border border-emerald-500/30 bg-emerald-500/10 text-emerald-300">
          <CheckCircle2 className="h-5 w-5" />
        </div>
        <CardTitle>{t("setup.domain.readyTitle")}</CardTitle>
        <CardDescription>
          {t("setup.domain.readyDescription2")}
        </CardDescription>
      </CardHeader>

      <CardContent className="mx-auto max-w-xl space-y-6">
        <div className="grid gap-3 rounded-2xl border border-border bg-background/60 p-4 text-sm sm:grid-cols-2">
          <PlanFact label={t("setup.domain.baseDomain")} value={plan.zone} />
          <PlanFact label={t("setup.domain.wildcard")} value={plan.domain} />
          <PlanFact label={t("setup.domain.dnsProvider")} value="deSEC" />
          <PlanFact
            label={t("setup.domain.providerAccess")}
            value={plan.providerAccessConfirmed ? t("setup.domain.readOnlyConfirmed") : t("setup.domain.notConfirmed")}
          />
          <PlanFact
            label={t("setup.domain.dnsCredential")}
            value={plan.providerCredentialStored ? t("setup.domain.storedProtected") : t("setup.domain.reentryRequired")}
          />
          <PlanFact
            label={t("setup.domain.certificateRequest")}
            value={plan.useStaging ? t("setup.domain.letsencryptStaging") : t("setup.domain.letsencryptProduction")}
          />
          <PlanFact
            label={t("setup.domain.autoRenewal")}
            value={plan.useStaging
              ? t("setup.domain.autoRenewalStaging")
              : t("setup.domain.autoRenewalProduction")}
          />
        </div>

        <div className="rounded-xl border border-sky-500/30 bg-sky-500/5 p-4 text-sm text-muted-foreground">
          {t("setup.domain.reviewBoundary")}
        </div>

        <div className="flex flex-col justify-center gap-2 sm:flex-row">
          <Button type="button" variant="outline" onClick={onEdit}>
            <Pencil className="mr-2 h-4 w-4" />
            {t("setup.domain.edit")}
          </Button>
          <Button type="button" onClick={onContinue}>
            {t("setup.domain.continueReview")}
            <ArrowRight className="ml-2 h-4 w-4" />
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}

function PlanFact({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-border bg-muted/20 p-3">
      <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-1 font-medium text-foreground">{value}</div>
    </div>
  )
}

function normaliseBaseDomain(value: string) {
  return value
    .trim()
    .toLowerCase()
    .replace(/^https?:\/\//, "")
    .replace(/^\*\./, "")
    .replace(/^\.+|\.+$/g, "")
    .replace(/\/+$/, "")
}
