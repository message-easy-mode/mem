import { AlertTriangle, CheckCircle2, CircleAlert, LoaderCircle } from "lucide-react"
import { Link, useSearchParams } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { ApiProblemAlert } from "@/components/operator/api-problem-alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import type { DiagnosticsOverviewResponse } from "../api/diagnostics.types"
import { diagnosticsUtcIso, formatDiagnosticsLocalDateTime } from "../diagnostics-time"
import { DiagnosticsOutageFallback } from "./diagnostics-outage-fallback"

type DiagnosticsSummaryProps = {
  data?: DiagnosticsOverviewResponse
  error?: unknown
  loading: boolean
  fetching: boolean
  onRefresh: () => void
  compact?: boolean
}

type SummaryFactProps = {
  label: string
  value: number
  to?: string
  active?: boolean
}

export function DiagnosticsSummary({
  data,
  error,
  loading,
  fetching,
  onRefresh,
  compact = false,
}: DiagnosticsSummaryProps) {
  const { language, t } = useI18n()
  const [searchParams] = useSearchParams()

  if (loading && !data) {
    return (
      <Card aria-live="polite">
        <CardContent className="flex items-center gap-3 py-6 text-sm text-muted-foreground">
          <LoaderCircle className="h-4 w-4 animate-spin" />
          {t("diagnostics.summary.loading")}
        </CardContent>
      </Card>
    )
  }

  if (error && !data) {
    return (
      <div className="space-y-4">
        <ApiProblemAlert
          error={error}
          title={t("diagnostics.summary.loadErrorTitle")}
          fallbackDescription={t("diagnostics.summary.loadErrorDescription")}
          onRetry={onRefresh}
          retrying={fetching}
          showDiagnosticsLink={false}
        />
        <DiagnosticsOutageFallback />
      </div>
    )
  }

  if (!data) return null

  const tab = searchParams.get("tab") === "events" || searchParams.get("tab") === "health"
    ? searchParams.get("tab")
    : "incidents"
  const level = normalizeEventLevel(searchParams.get("level"))
  const hasAttention = data.counts.incidentCount > 0
  const ready = data.status === "ready" && !hasAttention && !data.partial
  const canReadEvents = data.capabilities.canReadTechnicalEvents

  const eventFact = (
    label: string,
    value: number,
    eventLevel: string | null,
  ): SummaryFactProps => ({
    label,
    value,
    to: canReadEvents
      ? eventLevel
        ? `/diagnostics/logs?tab=events&level=${eventLevel}`
        : "/diagnostics/logs?tab=events"
      : undefined,
    active: tab === "events" && level === (eventLevel ?? ""),
  })

  return (
    <Card className={ready ? "border-emerald-500/30" : "border-amber-500/30"}>
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2 text-base">
              {ready ? (
                <CheckCircle2 className="h-4 w-4 text-emerald-500" />
              ) : (
                <CircleAlert className="h-4 w-4 text-amber-500" />
              )}
              {t("diagnostics.summary.title")}
            </CardTitle>
            <CardDescription className="mt-1">
              {hasAttention ? t("diagnostics.summary.attention") : t("diagnostics.summary.ready")}
            </CardDescription>
          </div>
          <div className="flex flex-wrap gap-2">
            <Badge variant={ready ? "outline" : "secondary"}>{data.status}</Badge>
            {data.partial ? <Badge variant="secondary">{t("diagnostics.summary.partial")}</Badge> : null}
            {data.truncated ? <Badge variant="secondary">{t("diagnostics.summary.truncated")}</Badge> : null}
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {compact ? (
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <SummaryFact
              label={t("diagnostics.summary.incidents")}
              value={data.counts.incidentCount}
              to="/diagnostics/logs?tab=incidents"
              active={tab === "incidents"}
            />
            <SummaryFact {...eventFact(t("diagnostics.summary.warning"), data.counts.warning, "warning")} />
            <SummaryFact {...eventFact(t("diagnostics.summary.error"), data.counts.error, "error")} />
            <SummaryFact {...eventFact(t("diagnostics.summary.critical"), data.counts.critical, "critical")} />
          </div>
        ) : (
          <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,5fr)]">
            <section aria-labelledby="diagnostics-summary-attention-title" className="space-y-2">
              <h3 id="diagnostics-summary-attention-title" className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                {t("diagnostics.summary.attentionGroup")}
              </h3>
              <SummaryFact
                label={t("diagnostics.summary.incidents")}
                value={data.counts.incidentCount}
                to="/diagnostics/logs?tab=incidents"
                active={tab === "incidents"}
              />
            </section>
            <section aria-labelledby="diagnostics-summary-activity-title" className="space-y-2">
              <h3 id="diagnostics-summary-activity-title" className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                {t("diagnostics.summary.activityGroup")}
              </h3>
              <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-5">
                <SummaryFact {...eventFact(t("diagnostics.summary.warning"), data.counts.warning, "warning")} />
                <SummaryFact {...eventFact(t("diagnostics.summary.error"), data.counts.error, "error")} />
                <SummaryFact {...eventFact(t("diagnostics.summary.critical"), data.counts.critical, "critical")} />
                <SummaryFact {...eventFact(t("diagnostics.summary.information"), data.counts.information, "information")} />
                <SummaryFact {...eventFact(t("diagnostics.summary.events"), data.counts.eventCount, null)} />
              </div>
            </section>
          </div>
        )}

        {error && data ? (
          <div className="flex items-center gap-2 rounded-md border border-amber-500/30 bg-amber-500/5 p-3 text-sm">
            <AlertTriangle className="h-4 w-4 text-amber-500" />
            {t("diagnostics.summary.stale")}
          </div>
        ) : null}

        <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
          <span title={`${t("diagnostics.time.utcEvidence")}: ${diagnosticsUtcIso(data.generatedAtUtc)}`}>{t("diagnostics.summary.generated")}: {formatDiagnosticsLocalDateTime(data.generatedAtUtc, language, true)}</span>
          <Button type="button" variant="outline" size="sm" onClick={onRefresh} disabled={fetching}>
            {fetching ? t("diagnostics.refreshing") : t("diagnostics.refresh")}
          </Button>
          {compact ? (
            <Button asChild variant="outline" size="sm">
              <Link to="/diagnostics/logs">{t("diagnostics.workspace.open")}</Link>
            </Button>
          ) : null}
        </div>
      </CardContent>
    </Card>
  )
}

function SummaryFact({ label, value, to, active = false }: SummaryFactProps) {
  const className = active
    ? "block rounded-lg border border-primary/50 bg-primary/10 p-3 outline-none ring-1 ring-primary/20 focus-visible:ring-2 focus-visible:ring-ring"
    : to
      ? "block rounded-lg border bg-muted/20 p-3 outline-none transition hover:border-primary/40 hover:bg-muted/50 focus-visible:ring-2 focus-visible:ring-ring"
      : "block rounded-lg border bg-muted/20 p-3"

  const content = (
    <>
      <span className="block text-xs text-muted-foreground">{label}</span>
      <span className="mt-1 block text-xl font-semibold">{value}</span>
    </>
  )

  return to ? (
    <Link to={to} className={className} aria-current={active ? "page" : undefined}>
      {content}
    </Link>
  ) : (
    <div className={className}>{content}</div>
  )
}

function normalizeEventLevel(value: string | null): string {
  switch (value?.toLowerCase()) {
    case "warning":
    case "error":
    case "critical":
    case "information":
      return value.toLowerCase()
    default:
      return ""
  }
}
