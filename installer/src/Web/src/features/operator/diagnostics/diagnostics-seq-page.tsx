import { useMutation, useQuery } from "@tanstack/react-query"
import {
  CheckCircle2,
  Clipboard,
  ExternalLink,
  HardDrive,
  Info,
  KeyRound,
  RefreshCw,
  Search,
  Server,
  Settings2,
  ShieldCheck,
  XCircle,
} from "lucide-react"
import { useState } from "react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import type { UiLanguage } from "@/app/i18n/messages"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { useOperatorSession } from "@/features/auth/operator-session-context"
import { cn } from "@/lib/utils"
import {
  getDiagnosticsPortainerOverview,
  getDiagnosticsSeqOverview,
  reviewDiagnosticsSeqSetup,
  updateDiagnosticsSeqUiAuthority,
} from "./api/diagnostics.api"
import type {
  DiagnosticsSeqOverviewResponse,
  DiagnosticsSeqSetupReviewResponse,
} from "./api/diagnostics.types"
import { DiagnosticsSeqBootstrapWizard } from "./components/diagnostics-seq-bootstrap-wizard"
import { DiagnosticsSeqConnection } from "./components/diagnostics-seq-connection"
import { DiagnosticsSeqControls } from "./components/diagnostics-seq-controls"
import { formatDiagnosticsLocalDateTime } from "./diagnostics-time"

const officialDocs = {
  overview: "https://datalust.co/docs",
  docker: "https://datalust.co/docs/getting-started?platform=docker",
  queryPrimer: "https://datalust.co/docs/the-seq-query-language",
  querySyntax: "https://datalust.co/docs/query-syntax",
} as const

const starterQueries = [
  { code: "correlation", value: "CorrelationId = 'paste-correlation-id'" },
  { code: "operation", value: "OperationId = 'paste-operation-id'" },
  { code: "event", value: "EventCode = 'diagnostics.pipeline_self_test'" },
  { code: "feature", value: "Feature = 'migration'" },
  { code: "level", value: "@Level in ['Warning', 'Error', 'Fatal']" },
] as const

export function DiagnosticsSeqPage() {
  const { session } = useOperatorSession()
  const { language, t } = useI18n()
  const isOwner = session.roles.includes("platform_owner")
  const canReview = isOwner || session.roles.includes("operator")
  const overview = useQuery({
    queryKey: ["diagnostics", "seq"],
    queryFn: getDiagnosticsSeqOverview,
    enabled: canReview,
    staleTime: 10_000,
    retry: 0,
  })
  const portainer = useQuery({
    queryKey: ["diagnostics", "portainer"],
    queryFn: getDiagnosticsPortainerOverview,
    enabled: isOwner,
    staleTime: 10_000,
    retry: 0,
  })
  const review = useMutation({ mutationFn: reviewDiagnosticsSeqSetup })
  const [copiedQuery, setCopiedQuery] = useState<string | null>(null)

  const copyQuery = async (code: string, value: string) => {
    try {
      await navigator.clipboard.writeText(value)
      setCopiedQuery(code)
      window.setTimeout(() => setCopiedQuery((current) => current === code ? null : current), 1500)
    } catch {
      setCopiedQuery(null)
    }
  }

  if (!canReview) {
    return (
      <div className="space-y-6">
        <SeqPageHeader refreshing={false} onRefresh={() => undefined} hideRefresh />
        <Card>
          <CardHeader>
            <CardTitle>{t("diagnostics.seq.restricted.title")}</CardTitle>
            <CardDescription>{t("diagnostics.seq.restricted.description")}</CardDescription>
          </CardHeader>
          <CardContent>
            <Button asChild variant="outline" size="sm">
              <Link to="/diagnostics">{t("diagnostics.seq.back")}</Link>
            </Button>
          </CardContent>
        </Card>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <SeqPageHeader
        refreshing={overview.isFetching}
        onRefresh={() => void overview.refetch()}
      />

      {overview.isLoading ? (
        <Card>
          <CardContent className="py-8 text-sm text-muted-foreground">
            {t("diagnostics.seq.loading")}
          </CardContent>
        </Card>
      ) : null}

      {overview.error && !overview.data ? (
        <Alert variant="destructive">
          <XCircle className="h-4 w-4" />
          <AlertTitle>{t("diagnostics.seq.unavailable.title")}</AlertTitle>
          <AlertDescription>{t("diagnostics.seq.unavailable.description")}</AlertDescription>
        </Alert>
      ) : null}

      {overview.data ? (
        <>
          {overview.error ? (
            <Alert>
              <RefreshCw className="h-4 w-4" />
              <AlertTitle>{t("diagnostics.stale.title")}</AlertTitle>
              <AlertDescription>{t("diagnostics.stale.description")}</AlertDescription>
            </Alert>
          ) : null}

          <SeqOverview
            data={overview.data}
            language={language}
            isOwner={isOwner}
            canOpenPortainer={Boolean(portainer.data?.capabilities.canOpenContainers)}
            onUpdated={() => overview.refetch()}
          />
          <DiagnosticsSeqConnection data={overview.data} isOwner={isOwner} />
          <SeqProductBoundary />
          <DiagnosticsSeqBootstrapWizard seq={overview.data} isOwner={isOwner} />
          {!overview.data.runtime.managed &&
           (overview.data.runtime.present || overview.data.configured) ? (
            <SeqSetupReview
              data={review.data}
              canReview={overview.data.capabilities.canReviewSetup}
              pending={review.isPending}
              failed={review.isError}
              onReview={() => review.mutate()}
            />
          ) : null}
          <DiagnosticsSeqControls
            data={overview.data}
            setupReview={overview.data.runtime.managed ? undefined : review.data}
          />
          <SeqGettingStarted
            copiedQuery={copiedQuery}
            onCopy={(code, value) => void copyQuery(code, value)}
          />
          <SeqHelp />
        </>
      ) : null}
    </div>
  )
}

function SeqPageHeader({
  refreshing,
  onRefresh,
  hideRefresh = false,
}: {
  refreshing: boolean
  onRefresh: () => void
  hideRefresh?: boolean
}) {
  const { t } = useI18n()

  return (
    <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
      <div>
        <PageBreadcrumbs items={[
          { label: t("diagnostics.title"), to: "/diagnostics" },
          { label: t("diagnostics.seq.title") },
        ]} />
        <div className="flex items-center gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-orange-400/25 bg-slate-950 p-2 shadow-sm">
            <img
              src="/brands/seq-mark.svg"
              alt=""
              aria-hidden="true"
              className="h-6 w-6 object-contain"
            />
          </div>
          <h1 className="text-2xl font-semibold tracking-tight">
            {t("diagnostics.seq.title")}
          </h1>
        </div>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
          {t("diagnostics.seq.description")}
        </p>
      </div>
      {!hideRefresh ? (
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={onRefresh}
          disabled={refreshing}
        >
          <RefreshCw className={refreshing ? "mr-2 h-4 w-4 animate-spin" : "mr-2 h-4 w-4"} />
          {refreshing ? t("diagnostics.refreshing") : t("diagnostics.refresh")}
        </Button>
      ) : null}
    </div>
  )
}

function SeqOverview({
  data,
  language,
  isOwner,
  canOpenPortainer,
  onUpdated,
}: {
  data: DiagnosticsSeqOverviewResponse
  language: UiLanguage
  isOwner: boolean
  canOpenPortainer: boolean
  onUpdated: () => Promise<unknown>
}) {
  const { t } = useI18n()
  const safeUiUrl = safeExternalUrl(data.ui.url)
  const canConfigureUi = isOwner && Boolean(data.ui.configurable)
  const [editingUiAuthority, setEditingUiAuthority] = useState(false)
  const [uiAuthority, setUiAuthority] = useState("")
  const uiAuthorityProblem = seqUiAuthorityProblem(uiAuthority, t)
  const updateUiAuthority = useMutation({
    mutationFn: updateDiagnosticsSeqUiAuthority,
    onSuccess: async () => {
      setEditingUiAuthority(false)
      await onUpdated()
    },
  })

  const beginUiAuthorityEdit = () => {
    updateUiAuthority.reset()
    setUiAuthority(safeUiUrl ?? "")
    setEditingUiAuthority(true)
  }

  const saveUiAuthority = () => {
    if (uiAuthorityProblem || updateUiAuthority.isPending) return
    updateUiAuthority.mutate(uiAuthority.trim() || null)
  }

  const deliveryDisabled = data.runtime.running &&
    !data.delivery.enabled &&
    !data.delivery.desiredEnabled
  const uiRuntimeAvailable =
    data.runtime.managed && data.runtime.running && data.health.reachable

  return (
    <section aria-labelledby="seq-overview-title" className="space-y-4">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h2 id="seq-overview-title" className="text-lg font-semibold">
            {t("diagnostics.seq.overview.title")}
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            {t("diagnostics.seq.overview.description")}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          {data.capabilities.canOpenUi && safeUiUrl ? (
            <Button asChild size="sm">
              <a href={safeUiUrl} target="_blank" rel="noopener noreferrer">
                {t("diagnostics.seq.open")}
                <ExternalLink className="ml-2 h-3.5 w-3.5" />
              </a>
            </Button>
          ) : canConfigureUi ? (
            <Button type="button" variant="outline" size="sm" disabled>
              {t("diagnostics.seq.open")}
              <ExternalLink className="ml-2 h-3.5 w-3.5" />
            </Button>
          ) : null}
          {canConfigureUi ? (
            <Button type="button" variant="outline" size="sm" onClick={beginUiAuthorityEdit}>
              <Settings2 className="mr-2 h-3.5 w-3.5" />
              {data.ui.configured
                ? t("diagnostics.seq.uiAuthority.change")
                : t("diagnostics.seq.uiAuthority.configure")}
            </Button>
          ) : null}
          {canOpenPortainer && data.runtime.managed && data.runtime.present ? (
            <Button asChild variant="outline" size="sm">
              <a
                href="/api/operator/diagnostics/portainer/seq/container"
                target="_blank"
                rel="noopener noreferrer"
              >
                {t("diagnostics.portainer.seq.open")}
                <ExternalLink className="ml-2 h-3.5 w-3.5" />
              </a>
            </Button>
          ) : null}
        </div>
      </div>

      {editingUiAuthority ? (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">{t("diagnostics.seq.uiAuthority.title")}</CardTitle>
            <CardDescription>{t("diagnostics.seq.uiAuthority.description")}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="seq-ui-authority">{t("diagnostics.seq.uiAuthority.label")}</Label>
              <Input
                id="seq-ui-authority"
                type="url"
                value={uiAuthority}
                placeholder={t("diagnostics.seq.uiAuthority.placeholder")}
                onChange={(event) => setUiAuthority(event.target.value)}
              />
              <p className="text-xs text-muted-foreground">
                {t("diagnostics.seq.uiAuthority.help")}
              </p>
              {uiAuthorityProblem ? (
                <p className="text-sm text-destructive">{uiAuthorityProblem}</p>
              ) : null}
            </div>

            {updateUiAuthority.isError ? (
              <Alert variant="destructive">
                <XCircle className="h-4 w-4" />
                <AlertTitle>{t("diagnostics.seq.uiAuthority.saveFailedTitle")}</AlertTitle>
                <AlertDescription>{t("diagnostics.seq.uiAuthority.saveFailedDescription")}</AlertDescription>
              </Alert>
            ) : null}

            <div className="flex flex-wrap justify-end gap-2">
              <Button
                type="button"
                variant="outline"
                onClick={() => {
                  updateUiAuthority.reset()
                  setEditingUiAuthority(false)
                }}
              >
                {t("common.cancel")}
              </Button>
              <Button
                type="button"
                disabled={Boolean(uiAuthorityProblem) || updateUiAuthority.isPending}
                onClick={saveUiAuthority}
              >
                {updateUiAuthority.isPending
                  ? t("diagnostics.seq.uiAuthority.saving")
                  : t("diagnostics.seq.uiAuthority.save")}
              </Button>
            </div>
          </CardContent>
        </Card>
      ) : null}

      <div className="grid gap-4 md:grid-cols-2 2xl:grid-cols-4" data-seq-overview-grid>
        <SeqStateCard
          icon={Search}
          title={t("diagnostics.seq.overview.delivery")}
          status={deliveryStatus(data, t)}
          tone={data.delivery.activationState === "unavailable"
            ? "degraded"
            : data.delivery.restartRequired
              ? "degraded"
              : data.delivery.enabled
                ? data.delivery.configurationState === "configured" ? "ready" : "degraded"
                : "neutral"}
          description={data.delivery.activationState === "unavailable"
            ? deliveryActivationUnavailableDescription(data, t)
            : data.delivery.restartRequired
              ? data.delivery.desiredEnabled
                ? t("diagnostics.seq.delivery.enablePending")
                : t("diagnostics.seq.delivery.disablePending")
              : data.delivery.enabled
                ? t("diagnostics.seq.overview.deliveryEnabled")
                : t("diagnostics.seq.overview.deliveryDisabled")}
        />
        <SeqStateCard
          icon={Server}
          title={t("diagnostics.seq.overview.runtime")}
          status={runtimeStatus(data.runtime.state, t)}
          tone={runtimeTone(data.runtime.state)}
          description={runtimeDescription(data.runtime.state, t)}
        />
        <SeqStateCard
          icon={ShieldCheck}
          title={t("diagnostics.seq.overview.health")}
          status={healthStatus(data.health.status, t)}
          tone={data.health.status === "ready"
            ? "ready"
            : isNeutralSeqHealthStatus(data.health.status) ? "neutral" : "degraded"}
          description={data.health.reachable
            ? t("diagnostics.seq.overview.healthReachable")
            : t("diagnostics.seq.overview.healthNotReachable")}
        />
        <SeqStateCard
          icon={ExternalLink}
          title={t("diagnostics.seq.overview.ui")}
          status={data.ui.available
            ? t("diagnostics.seq.status.available")
            : data.ui.configured
              ? t("diagnostics.seq.status.configured")
              : t("diagnostics.seq.status.notConfigured")}
          tone={data.ui.available ? "ready" : "neutral"}
          description={data.ui.configured
            ? uiRuntimeAvailable
              ? t("diagnostics.seq.overview.uiConfigured")
              : t("diagnostics.seq.overview.uiConfiguredUnavailable")
            : t("diagnostics.seq.overview.uiMissing")}
        />
      </div>

      {deliveryDisabled ? (
        <Alert>
          <Info className="h-4 w-4" />
          <AlertTitle>
            {data.capabilities.canEnableDelivery
              ? t("diagnostics.seq.connection.deliveryDisabledTitle")
              : t("diagnostics.seq.connection.notConnectedTitle")}
          </AlertTitle>
          <AlertDescription>
            {data.capabilities.canEnableDelivery
              ? t("diagnostics.seq.connection.deliveryDisabledDescription")
              : t("diagnostics.seq.connection.notConnectedDescription")}
          </AlertDescription>
        </Alert>
      ) : null}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">{t("diagnostics.seq.overview.facts")}</CardTitle>
        </CardHeader>
        <CardContent className="grid gap-4 text-sm md:grid-cols-2 xl:grid-cols-4">
          <SeqFact
            label={t("diagnostics.seq.fact.expectedVersion")}
            value={data.runtime.expectedVersion}
          />
          <SeqFact
            label={t("diagnostics.seq.fact.approvedRuntime")}
            value={yesNo(data.runtime.usesApprovedRuntime, t)}
          />
          <SeqFact
            label={t("diagnostics.seq.fact.publicIngress")}
            value={data.runtime.publishesPublicIngress
              ? t("diagnostics.seq.value.enabled")
              : t("diagnostics.seq.value.none")}
          />
          <SeqFact
            label={t("diagnostics.seq.fact.dataRetention")}
            value={t("diagnostics.seq.value.preservedOnRemove")}
          />
          <SeqFact
            label={t("diagnostics.seq.fact.lastChecked")}
            value={formatDate(data.health.lastCheckedAtUtc, language)}
          />
          <SeqFact
            label={t("diagnostics.seq.fact.lastSuccess")}
            value={formatDate(data.health.lastSucceededAtUtc, language)}
          />
          <SeqFact
            label={t("diagnostics.seq.fact.restartRequired")}
            value={yesNo(data.delivery.requiresApiRestartToChange, t)}
          />
          <SeqFact
            label={t("diagnostics.seq.fact.runtimeOwnership")}
            value={runtimeOwnership(data.runtime, t)}
          />
        </CardContent>
      </Card>

      {data.warnings.length > 0 ? (
        <Alert>
          <Info className="h-4 w-4" />
          <AlertTitle>{t("diagnostics.seq.overview.attentionTitle")}</AlertTitle>
          <AlertDescription>
            <ul className="mt-2 space-y-1">
              {data.warnings.map((code) => (
                <li key={code}>{warningMessage(code, t)}</li>
              ))}
            </ul>
          </AlertDescription>
        </Alert>
      ) : null}
    </section>
  )
}

function SeqProductBoundary() {
  const { t } = useI18n()

  return (
    <section aria-labelledby="seq-boundary-title">
      <Card>
        <CardHeader>
          <CardTitle id="seq-boundary-title" className="text-base">
            {t("diagnostics.seq.boundary.title")}
          </CardTitle>
          <CardDescription>{t("diagnostics.seq.boundary.description")}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 md:grid-cols-2">
          <BoundaryColumn
            title={t("diagnostics.seq.boundary.memTitle")}
            items={[
              t("diagnostics.seq.boundary.memIncident"),
              t("diagnostics.seq.boundary.memSafeStore"),
              t("diagnostics.seq.boundary.memRecorder"),
              t("diagnostics.seq.boundary.memReports"),
            ]}
          />
          <BoundaryColumn
            title={t("diagnostics.seq.boundary.seqTitle")}
            items={[
              t("diagnostics.seq.boundary.seqSearch"),
              t("diagnostics.seq.boundary.seqFilter"),
              t("diagnostics.seq.boundary.seqHistory"),
              t("diagnostics.seq.boundary.seqOptional"),
            ]}
          />
        </CardContent>
      </Card>
    </section>
  )
}

function BoundaryColumn({ title, items }: { title: string; items: string[] }) {
  return (
    <div className="rounded-lg border p-4">
      <h3 className="font-medium">{title}</h3>
      <ul className="mt-3 space-y-2 text-sm text-muted-foreground">
        {items.map((item) => (
          <li key={item} className="flex gap-2">
            <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" />
            <span>{item}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}

function SeqSetupReview({
  data,
  canReview,
  pending,
  failed,
  onReview,
}: {
  data: DiagnosticsSeqSetupReviewResponse | undefined
  canReview: boolean
  pending: boolean
  failed: boolean
  onReview: () => void
}) {
  const { language, t } = useI18n()

  return (
    <section aria-labelledby="seq-setup-title">
      <Card>
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <CardTitle id="seq-setup-title" className="flex items-center gap-2 text-base">
                <Settings2 className="h-4 w-4" />
                {t("diagnostics.seq.setup.title")}
              </CardTitle>
              <CardDescription className="mt-1">
                {t("diagnostics.seq.setup.description")}
              </CardDescription>
            </div>
            {canReview ? (
              <Button type="button" variant="outline" size="sm" onClick={onReview} disabled={pending}>
                <RefreshCw className={pending ? "mr-2 h-4 w-4 animate-spin" : "mr-2 h-4 w-4"} />
                {pending
                  ? t("diagnostics.seq.setup.reviewing")
                  : t("diagnostics.seq.setup.review")}
              </Button>
            ) : null}
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {!canReview ? (
            <Alert>
              <Info className="h-4 w-4" />
              <AlertTitle>{t("diagnostics.seq.setup.ownerOnlyTitle")}</AlertTitle>
              <AlertDescription>{t("diagnostics.seq.setup.ownerOnlyDescription")}</AlertDescription>
            </Alert>
          ) : null}

          {failed ? (
            <Alert variant="destructive">
              <XCircle className="h-4 w-4" />
              <AlertTitle>{t("diagnostics.seq.setup.failedTitle")}</AlertTitle>
              <AlertDescription>{t("diagnostics.seq.setup.failedDescription")}</AlertDescription>
            </Alert>
          ) : null}

          {!data ? (
            <p className="text-sm text-muted-foreground">
              {t("diagnostics.seq.setup.notReviewed")}
            </p>
          ) : (
            <div className="space-y-4">
              <Alert variant={data.readyForDeployment ? "default" : "destructive"}>
                {data.readyForDeployment
                  ? <CheckCircle2 className="h-4 w-4" />
                  : <XCircle className="h-4 w-4" />}
                <AlertTitle>
                  {data.readyForDeployment
                    ? t("diagnostics.seq.setup.readyTitle")
                    : t("diagnostics.seq.setup.notReadyTitle")}
                </AlertTitle>
                <AlertDescription>
                  {data.readyForDeployment
                    ? t("diagnostics.seq.setup.readyDescription")
                    : t("diagnostics.seq.setup.notReadyDescription")}
                </AlertDescription>
              </Alert>

              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                <ReviewFact
                  icon={Server}
                  title={t("diagnostics.seq.setup.image")}
                  status={data.image.local && data.image.immutableIdentityAvailable}
                  detail={`${data.image.approvedReference} · ${data.image.expectedVersion}`}
                />
                <ReviewFact
                  icon={HardDrive}
                  title={t("diagnostics.seq.setup.storage")}
                  status={data.storage.state === "ready" || data.storage.state === "ready-to-create"}
                  detail={storageStateLabel(data.storage.state, t)}
                />
                <ReviewFact
                  icon={KeyRound}
                  title={t("diagnostics.seq.setup.secrets")}
                  status={data.secrets.administratorPasswordHashAvailable &&
                    (!data.secrets.ingestionApiKeyRequired || data.secrets.ingestionApiKeyAvailable)}
                  detail={secretReviewDetail(data, t)}
                />
                <ReviewFact
                  icon={ExternalLink}
                  title={t("diagnostics.seq.setup.uiAuthority")}
                  status={data.uiAuthority.configured}
                  detail={data.uiAuthority.configured
                    ? t("diagnostics.seq.setup.configured")
                    : t("diagnostics.seq.setup.notConfigured")}
                />
              </div>

              <div className="grid gap-4 md:grid-cols-2">
                <div className="rounded-lg border p-4 text-sm">
                  <h3 className="font-medium">{t("diagnostics.seq.setup.reviewFacts")}</h3>
                  <dl className="mt-3 space-y-2">
                    <ReviewLine label={t("diagnostics.seq.setup.reviewId")} value={data.reviewId} mono />
                    <ReviewLine
                      label={t("diagnostics.seq.setup.expires")}
                      value={formatDate(data.expiresAtUtc, language)}
                    />
                    <ReviewLine
                      label={t("diagnostics.seq.setup.deliveryReady")}
                      value={yesNo(data.readyForDelivery, t)}
                    />
                    <ReviewLine
                      label={t("diagnostics.seq.setup.eula")}
                      value={yesNo(data.eulaAccepted, t)}
                    />
                    <ReviewLine
                      label={t("diagnostics.seq.setup.publicIngress")}
                      value={data.publishesPublicIngress
                        ? t("diagnostics.seq.value.enabled")
                        : t("diagnostics.seq.value.none")}
                    />
                  </dl>
                </div>
                <div className="rounded-lg border p-4 text-sm">
                  <h3 className="font-medium">{t("diagnostics.seq.setup.plannedEffects")}</h3>
                  <ul className="mt-3 space-y-2 text-muted-foreground">
                    {data.actionCodes.map((code) => (
                      <li key={code} className="flex gap-2">
                        <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" />
                        <span>{actionLabel(code, t)}</span>
                      </li>
                    ))}
                  </ul>
                </div>
              </div>

              {data.warnings.length > 0 ? (
                <Alert>
                  <Info className="h-4 w-4" />
                  <AlertTitle>{t("diagnostics.seq.setup.attentionTitle")}</AlertTitle>
                  <AlertDescription>
                    <ul className="mt-2 space-y-1">
                      {data.warnings.map((code) => (
                        <li key={code}>{warningMessage(code, t)}</li>
                      ))}
                    </ul>
                  </AlertDescription>
                </Alert>
              ) : null}
            </div>
          )}
        </CardContent>
      </Card>
    </section>
  )
}

function SeqGettingStarted({
  copiedQuery,
  onCopy,
}: {
  copiedQuery: string | null
  onCopy: (code: string, value: string) => void
}) {
  const { t } = useI18n()

  return (
    <section aria-labelledby="seq-getting-started-title">
      <Card>
        <CardHeader>
          <CardTitle id="seq-getting-started-title" className="text-base">
            {t("diagnostics.seq.gettingStarted.title")}
          </CardTitle>
          <CardDescription>{t("diagnostics.seq.gettingStarted.description")}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          {starterQueries.map((query) => (
            <div
              key={query.code}
              className="flex flex-col gap-2 rounded-lg border p-3 sm:flex-row sm:items-center sm:justify-between"
            >
              <div className="min-w-0">
                <p className="text-sm font-medium">
                  {queryLabel(query.code, t)}
                </p>
                <code className="mt-1 block overflow-x-auto text-xs text-muted-foreground">
                  {query.value}
                </code>
              </div>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => onCopy(query.code, query.value)}
              >
                <Clipboard className="mr-2 h-3.5 w-3.5" />
                {copiedQuery === query.code
                  ? t("diagnostics.seq.query.copied")
                  : t("diagnostics.seq.query.copy")}
              </Button>
            </div>
          ))}
          <p className="text-xs text-muted-foreground">
            {t("diagnostics.seq.gettingStarted.note")}
          </p>
        </CardContent>
      </Card>
    </section>
  )
}

function SeqHelp() {
  const { t } = useI18n()

  return (
    <section aria-labelledby="seq-help-title">
      <Card>
        <CardHeader>
          <CardTitle id="seq-help-title" className="text-base">
            {t("diagnostics.seq.help.title")}
          </CardTitle>
          <CardDescription>{t("diagnostics.seq.help.description")}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <ExternalHelpLink href={officialDocs.overview} label={t("diagnostics.seq.help.overview")} />
          <ExternalHelpLink href={officialDocs.docker} label={t("diagnostics.seq.help.docker")} />
          <ExternalHelpLink href={officialDocs.queryPrimer} label={t("diagnostics.seq.help.queryPrimer")} />
          <ExternalHelpLink href={officialDocs.querySyntax} label={t("diagnostics.seq.help.querySyntax")} />
        </CardContent>
      </Card>
    </section>
  )
}

function ExternalHelpLink({ href, label }: { href: string; label: string }) {
  return (
    <Button asChild variant="outline" className="h-auto justify-between py-3">
      <a href={href} target="_blank" rel="noopener noreferrer">
        <span>{label}</span>
        <ExternalLink className="ml-2 h-3.5 w-3.5" />
      </a>
    </Button>
  )
}

function SeqStateCard({
  icon: Icon,
  title,
  status,
  tone,
  description,
}: {
  icon: typeof Search
  title: string
  status: string
  tone: "ready" | "degraded" | "neutral"
  description: string
}) {
  return (
    <div className="rounded-xl border bg-card p-4" data-seq-state-card>
      <div className="flex flex-wrap items-start gap-2">
        <div className="flex min-w-[8rem] flex-1 items-center gap-2 text-sm text-muted-foreground">
          <Icon className="h-4 w-4 shrink-0" />
          <span>{title}</span>
        </div>
        <Badge variant="outline" className={cn("ml-auto", tone === "ready"
          ? "border-emerald-500/30 bg-emerald-500/10"
          : tone === "degraded"
            ? "border-amber-500/30 bg-amber-500/10"
            : "")}
        >
          {status}
        </Badge>
      </div>
      <p className="mt-3 text-sm text-muted-foreground">{description}</p>
    </div>
  )
}

function SeqFact({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-1 break-words font-medium">{value}</dd>
    </div>
  )
}

function ReviewFact({
  icon: Icon,
  title,
  status,
  detail,
}: {
  icon: typeof Server
  title: string
  status: boolean
  detail: string
}) {
  return (
    <div className="rounded-lg border p-4">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2 text-sm font-medium">
          <Icon className="h-4 w-4" />
          {title}
        </div>
        {status
          ? <CheckCircle2 className="h-4 w-4 text-emerald-500" />
          : <XCircle className="h-4 w-4 text-amber-500" />}
      </div>
      <p className="mt-2 break-words text-xs text-muted-foreground">{detail}</p>
    </div>
  )
}

function ReviewLine({
  label,
  value,
  mono = false,
}: {
  label: string
  value: string
  mono?: boolean
}) {
  return (
    <div className="flex flex-col gap-1 sm:flex-row sm:justify-between sm:gap-4">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className={mono ? "break-all font-mono text-xs" : "font-medium"}>{value}</dd>
    </div>
  )
}

type Translate = ReturnType<typeof useI18n>["t"]

function deliveryActivationUnavailableDescription(
  data: DiagnosticsSeqOverviewResponse,
  t: Translate,
) {
  return data.warnings.includes("diagnostics.seq_sink_configuration_failed")
    ? t("diagnostics.seq.warning.diagnostics.seq_sink_configuration_failed")
    : t("diagnostics.seq.warning.seq_delivery_startup_prerequisites_unavailable")
}

function deliveryStatus(data: DiagnosticsSeqOverviewResponse, t: Translate) {
  if (data.delivery.activationState === "unavailable") {
    return t("diagnostics.seq.delivery.activation.unavailable")
  }
  if (data.delivery.restartRequired) {
    return data.delivery.desiredEnabled
      ? t("diagnostics.seq.status.enabled")
      : t("diagnostics.seq.status.disabled")
  }
  if (!data.delivery.enabled) return t("diagnostics.seq.status.disabled")
  if (data.delivery.configurationState === "configured") {
    return t("diagnostics.seq.status.configured")
  }
  return t("diagnostics.seq.status.degraded")
}

function runtimeStatus(state: string, t: Translate) {
  switch (knownRuntimeState(state)) {
    case "not-configured": return t("diagnostics.seq.runtimeStatus.not-configured")
    case "absent": return t("diagnostics.seq.runtimeStatus.absent")
    case "running": return t("diagnostics.seq.runtimeStatus.running")
    case "stopped": return t("diagnostics.seq.runtimeStatus.stopped")
    case "unmanaged-conflict": return t("diagnostics.seq.runtimeStatus.unmanaged-conflict")
    case "control-plane-mismatch": return t("diagnostics.seq.runtimeStatus.control-plane-mismatch")
    case "identity-mismatch": return t("diagnostics.seq.runtimeStatus.identity-mismatch")
    case "record-only": return t("diagnostics.seq.runtimeStatus.record-only")
    case "unavailable": return t("diagnostics.seq.runtimeStatus.unavailable")
    default: return t("diagnostics.seq.runtimeStatus.unknown")
  }
}

function runtimeDescription(state: string, t: Translate) {
  switch (knownRuntimeState(state)) {
    case "not-configured": return t("diagnostics.seq.runtime.not-configured")
    case "absent": return t("diagnostics.seq.runtime.absent")
    case "running": return t("diagnostics.seq.runtime.running")
    case "stopped": return t("diagnostics.seq.runtime.stopped")
    case "unmanaged-conflict": return t("diagnostics.seq.runtime.unmanaged-conflict")
    case "control-plane-mismatch": return t("diagnostics.seq.runtime.control-plane-mismatch")
    case "identity-mismatch": return t("diagnostics.seq.runtime.identity-mismatch")
    case "record-only": return t("diagnostics.seq.runtime.record-only")
    case "unavailable": return t("diagnostics.seq.runtime.unavailable")
    default: return t("diagnostics.seq.runtime.unknown")
  }
}


function isNeutralSeqHealthStatus(status: string) {
  return status === "optional-disabled" ||
    status === "stopped-intentionally" ||
    status === "runtime-absent"
}

function healthStatus(status: string, t: Translate) {
  switch (status) {
    case "ready": return t("diagnostics.seq.healthStatus.ready")
    case "optional-disabled": return t("diagnostics.seq.healthStatus.optional-disabled")
    case "configured-unprobed": return t("diagnostics.seq.healthStatus.configured-unprobed")
    case "configuration-error": return t("diagnostics.seq.healthStatus.configuration-error")
    case "unavailable": return t("diagnostics.seq.healthStatus.unavailable")
    case "stopped-intentionally": return t("diagnostics.seq.healthStatus.stopped-intentionally")
    case "runtime-absent": return t("diagnostics.seq.healthStatus.runtime-absent")
    default: return t("diagnostics.seq.healthStatus.unknown")
  }
}

function runtimeTone(state: string): "ready" | "degraded" | "neutral" {
  if (state === "running") return "ready"
  if (state === "not-configured" || state === "absent" || state === "stopped") return "neutral"
  return "degraded"
}

function knownRuntimeState(state: string) {
  return [
    "not-configured",
    "absent",
    "running",
    "stopped",
    "unmanaged-conflict",
    "control-plane-mismatch",
    "identity-mismatch",
    "record-only",
    "unavailable",
  ].includes(state) ? state : "unknown"
}

function storageStateLabel(state: string, t: Translate) {
  switch (state) {
    case "ready": return t("diagnostics.seq.setup.storageState.ready")
    case "ready-to-create": return t("diagnostics.seq.setup.storageState.ready-to-create")
    case "not-configured": return t("diagnostics.seq.setup.storageState.not-configured")
    case "invalid": return t("diagnostics.seq.setup.storageState.invalid")
    default: return t("diagnostics.seq.setup.storageState.unknown")
  }
}

function actionLabel(code: string, t: Translate) {
  switch (code) {
    case "create_mem_managed_container": return t("diagnostics.seq.setup.action.create_mem_managed_container")
    case "attach_mem_gateway_network": return t("diagnostics.seq.setup.action.attach_mem_gateway_network")
    case "preserve_seq_data_directory": return t("diagnostics.seq.setup.action.preserve_seq_data_directory")
    case "do_not_create_public_ingress": return t("diagnostics.seq.setup.action.do_not_create_public_ingress")
    case "leave_event_delivery_unchanged": return t("diagnostics.seq.setup.action.leave_event_delivery_unchanged")
    default: return t("diagnostics.seq.setup.action.unknown")
  }
}

function queryLabel(code: typeof starterQueries[number]["code"], t: Translate) {
  switch (code) {
    case "correlation": return t("diagnostics.seq.query.correlation")
    case "operation": return t("diagnostics.seq.query.operation")
    case "event": return t("diagnostics.seq.query.event")
    case "feature": return t("diagnostics.seq.query.feature")
    case "level": return t("diagnostics.seq.query.level")
  }
}

function runtimeOwnership(
  runtime: DiagnosticsSeqOverviewResponse["runtime"],
  t: Translate,
) {
  if (runtime.state === "control-plane-mismatch") {
    return t("diagnostics.seq.runtimeStatus.control-plane-mismatch")
  }
  if (runtime.managed) return t("diagnostics.seq.value.memManaged")
  if (!runtime.present) return t("diagnostics.seq.value.noRuntime")
  return t("diagnostics.seq.value.unproven")
}

function secretReviewDetail(
  data: DiagnosticsSeqSetupReviewResponse,
  t: Translate,
) {
  if (!data.secrets.administratorPasswordHashAvailable) {
    return t("diagnostics.seq.setup.adminSecretMissing")
  }
  if (data.secrets.ingestionApiKeyRequired && !data.secrets.ingestionApiKeyAvailable) {
    return t("diagnostics.seq.setup.ingestionSecretMissing")
  }
  return t("diagnostics.seq.setup.secretsAvailable")
}

function warningMessage(code: string, t: Translate) {
  switch (code) {
    case "seq_configuration_invalid":
    case "diagnostics.seq_configuration_invalid":
      return t("diagnostics.seq.warning.seq_configuration_invalid")
    case "seq_approved_image_missing": return t("diagnostics.seq.warning.seq_approved_image_missing")
    case "seq_approved_image_invalid": return t("diagnostics.seq.warning.seq_approved_image_invalid")
    case "seq_approved_image_inspection_failed": return t("diagnostics.seq.warning.seq_approved_image_inspection_failed")
    case "diagnostics.seq_api_key_unavailable": return t("diagnostics.seq.warning.apiKeyUnavailable")
    case "diagnostics.seq_admin_password_hash_unavailable": return t("diagnostics.seq.warning.adminPasswordHashUnavailable")
    case "seq_ui_authority_not_configured": return t("diagnostics.seq.warning.seq_ui_authority_not_configured")
    case "seq_management_disabled": return t("diagnostics.seq.warning.seq_management_disabled")
    case "seq_eula_not_accepted": return t("diagnostics.seq.warning.seq_eula_not_accepted")
    case "seq_data_path_invalid": return t("diagnostics.seq.warning.seq_data_path_invalid")
    case "seq_unmanaged_container": return t("diagnostics.seq.warning.seq_unmanaged_container")
    case "seq_control_plane_ownership_mismatch": return t("diagnostics.seq.warning.seq_control_plane_ownership_mismatch")
    case "seq_container_identity_mismatch": return t("diagnostics.seq.warning.seq_container_identity_mismatch")
    case "seq_runtime_record_conflict": return t("diagnostics.seq.warning.seq_runtime_record_conflict")
    case "seq_runtime_status_unavailable": return t("diagnostics.seq.warning.seq_runtime_status_unavailable")
    case "seq_delivery_state_invalid": return t("diagnostics.seq.warning.seq_delivery_state_invalid")
    case "seq_delivery_state_read_failed": return t("diagnostics.seq.warning.seq_delivery_state_read_failed")
    case "seq_delivery_state_path_invalid": return t("diagnostics.seq.warning.seq_delivery_state_path_invalid")
    case "seq_delivery_startup_prerequisites_unavailable": return t("diagnostics.seq.warning.seq_delivery_startup_prerequisites_unavailable")
    case "diagnostics.seq_sink_configuration_failed": return t("diagnostics.seq.warning.diagnostics.seq_sink_configuration_failed")
    default: return t("diagnostics.seq.warning.unknown")
  }
}

function yesNo(value: boolean, t: Translate) {
  return value ? t("common.yes") : t("common.no")
}

function formatDate(value: string | null, language: UiLanguage) {
  if (!value) return "—"
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return "—"
  return formatDiagnosticsLocalDateTime(date, language)
}

function seqUiAuthorityProblem(value: string, t: Translate) {
  if (!value.trim()) return null
  return safeExternalUrl(value)
    ? null
    : t("diagnostics.seq.uiAuthority.invalid")
}

function safeExternalUrl(value: string | null) {
  if (!value) return null
  try {
    const url = new URL(value)
    if ((url.protocol !== "http:" && url.protocol !== "https:") ||
        url.username || url.password || url.search || url.hash ||
        url.hostname === "0.0.0.0" || url.hostname === "[::]" ||
        url.hostname === "::") {
      return null
    }
    return url.toString()
  } catch {
    return null
  }
}
