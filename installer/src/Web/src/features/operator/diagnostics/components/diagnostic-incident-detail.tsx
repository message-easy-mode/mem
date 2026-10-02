import { ExternalLink } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import type {
  DiagnosticsEvent,
  DiagnosticsException,
  DiagnosticsIncidentDetailResponse,
} from "../api/diagnostics.types"
import { diagnosticsUtcIso, formatDiagnosticsLocalDateTime } from "../diagnostics-time"
import { DiagnosticIncidentLifecycleBadge, DiagnosticIncidentLifecyclePanel } from "./diagnostic-incident-lifecycle"
import { DiagnosticSeverityBadge } from "./diagnostic-severity-badge"

type DiagnosticIncidentDetailProps = {
  detail: DiagnosticsIncidentDetailResponse
  copying: boolean
  downloading: boolean
  onCopy: () => void
  onDownload: () => void
  canIncludeDockerEvidence: boolean
  includeDockerEvidence: boolean
  onIncludeDockerEvidenceChange: (value: boolean) => void
}

export function DiagnosticIncidentDetail({
  detail,
  copying,
  downloading,
  onCopy,
  onDownload,
  canIncludeDockerEvidence,
  includeDockerEvidence,
  onIncludeDockerEvidenceChange,
}: DiagnosticIncidentDetailProps) {
  const { language, t } = useI18n()
  const { incident } = detail
  const workspaceLink = isSafeWorkspaceLink(incident.workspaceLink)
    ? incident.workspaceLink
    : null

  return (
    <Card className="border-amber-500/30">
      <CardHeader>
        <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
          <div className="min-w-0">
            <CardTitle className="text-base [overflow-wrap:anywhere]">{incident.message}</CardTitle>
            <CardDescription className="mt-1 break-all">{incident.incidentId}</CardDescription>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <DiagnosticIncidentLifecycleBadge lifecycle={incident.lifecycle} />
            <DiagnosticSeverityBadge severity={incident.severity} />
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-5">
        <dl className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <Fact label={t("diagnostics.incident.code")} value={incident.eventCode} />
          <Fact label={t("diagnostics.incident.feature")} value={incident.feature} />
          <Fact
            label={t("diagnostics.incident.firstSeen")}
            value={formatDiagnosticsLocalDateTime(incident.firstSeenAtUtc, language, true)}
            title={`${t("diagnostics.time.utcEvidence")}: ${diagnosticsUtcIso(incident.firstSeenAtUtc)}`}
          />
          <Fact
            label={t("diagnostics.incident.lastSeen")}
            value={formatDiagnosticsLocalDateTime(incident.lastSeenAtUtc, language, true)}
            title={`${t("diagnostics.time.utcEvidence")}: ${diagnosticsUtcIso(incident.lastSeenAtUtc)}`}
          />
          <Fact label={t("diagnostics.incident.occurrences")} value={String(incident.occurrenceCount)} />
          <Fact label={t("diagnostics.incident.relatedEvents")} value={String(detail.relatedEventCount)} />
          {incident.stage ? <Fact label={t("diagnostics.incident.stage")} value={incident.stage} /> : null}
          {incident.resource ? (
            <Fact
              label={t("diagnostics.incident.resource")}
              value={incident.resource.displayName ?? `${incident.resource.kind}:${incident.resource.id}`}
            />
          ) : null}
        </dl>

        <DiagnosticIncidentLifecyclePanel detail={detail} />

        <div className="flex flex-wrap gap-2">
          {workspaceLink ? (
            <Button asChild variant="outline" size="sm">
              <Link to={workspaceLink}>
                <ExternalLink className="mr-2 h-4 w-4" />
                {t("diagnostics.incident.openWorkspace")}
              </Link>
            </Button>
          ) : null}
          {detail.capabilities.canGenerateSupportReport ? (
            <>
              {canIncludeDockerEvidence ? (
                <label className="flex items-center gap-2 rounded-md border px-3 py-1.5 text-sm">
                  <input
                    type="checkbox"
                    checked={includeDockerEvidence}
                    onChange={(event) => onIncludeDockerEvidenceChange(event.target.checked)}
                  />
                  {t("diagnostics.report.includeDockerEvidence")}
                </label>
              ) : null}
              <Button type="button" variant="outline" size="sm" onClick={onCopy} disabled={copying || downloading}>
                {copying ? t("diagnostics.report.copying") : t("diagnostics.report.copy")}
              </Button>
              <Button type="button" variant="outline" size="sm" onClick={onDownload} disabled={copying || downloading}>
                {downloading ? t("diagnostics.report.downloading") : t("diagnostics.report.download")}
              </Button>
            </>
          ) : null}
        </div>

        <div className="rounded-lg border bg-muted/20 p-3 text-sm text-muted-foreground">
          {t("diagnostics.report.redactionNotice")}
          {detail.truncated || incident.truncated ? ` ${t("diagnostics.report.truncationNotice")}` : ""}
        </div>

        {detail.operations.length > 0 ? (
          <section className="space-y-2">
            <h3 className="font-medium">{t("diagnostics.operations.title")}</h3>
            <div className="space-y-2">
              {detail.operations.map((operation) => (
                <div key={operation.operationId} className="min-w-0 rounded-lg border p-3 text-sm">
                  <div className="flex min-w-0 flex-wrap items-center justify-between gap-2">
                    <span className="min-w-0 [overflow-wrap:anywhere] font-medium">{operation.operation}</span>
                    <Badge variant="outline">{operation.status}</Badge>
                  </div>
                  <div className="mt-2 break-all text-xs text-muted-foreground">{operation.operationId}</div>
                  <div
                    className="mt-1 text-xs text-muted-foreground"
                    title={`${t("diagnostics.time.utcEvidence")}: ${diagnosticsUtcIso(operation.requestedAtUtc)}`}
                  >
                    {formatDiagnosticsLocalDateTime(operation.requestedAtUtc, language, true)}
                  </div>
                </div>
              ))}
            </div>
          </section>
        ) : null}

        {detail.technicalEvents && detail.technicalEvents.length > 0 ? (
          <section className="space-y-3">
            <h3 className="font-medium">{t("diagnostics.events.relatedTitle")}</h3>
            {detail.technicalEvents.map((event) => (
              <DiagnosticEventDisclosure key={event.eventId} event={event} />
            ))}
          </section>
        ) : null}

        {detail.warnings.length > 0 ? (
          <section className="rounded-lg border border-amber-500/30 bg-amber-500/5 p-3 text-sm">
            <div className="font-medium">{t("diagnostics.incident.warnings")}</div>
            <ul className="mt-2 list-disc space-y-1 pl-5 text-muted-foreground">
              {detail.warnings.map((warning) => <li key={warning}>{warning}</li>)}
            </ul>
          </section>
        ) : null}
      </CardContent>
    </Card>
  )
}

export function DiagnosticEventDisclosure({ event }: { event: DiagnosticsEvent }) {
  const { language, t } = useI18n()

  return (
    <details className="min-w-0 rounded-lg border p-3">
      <summary className="cursor-pointer list-none">
        <div className="flex min-w-0 flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
          <div className="min-w-0">
            <div className="[overflow-wrap:anywhere] font-medium">{event.message}</div>
            <div className="mt-1 min-w-0 [overflow-wrap:anywhere] text-xs text-muted-foreground">
              <span title={`${t("diagnostics.time.utcEvidence")}: ${diagnosticsUtcIso(event.timestampUtc)}`}>
                {formatDiagnosticsLocalDateTime(event.timestampUtc, language, true)}
              </span>
              {" · "}{event.eventCode}
            </div>
          </div>
          <DiagnosticSeverityBadge severity={event.severity} />
        </div>
      </summary>
      <div className="mt-4 space-y-4 border-t pt-4 text-sm">
        <dl className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <Fact label={t("diagnostics.events.source")} value={event.source} />
          <Fact label={t("diagnostics.incident.feature")} value={event.feature} />
          <Fact label={t("diagnostics.events.eventId")} value={event.eventId} />
          {event.traceId ? <Fact label={t("diagnostics.events.traceId")} value={event.traceId} /> : null}
          {event.operationId ? <Fact label={t("diagnostics.events.operationId")} value={event.operationId} /> : null}
          {event.stage ? <Fact label={t("diagnostics.incident.stage")} value={event.stage} /> : null}
        </dl>

        {event.expected ? <KeyValuePanel title={t("diagnostics.events.expected")} values={event.expected} /> : null}
        {event.observed ? <KeyValuePanel title={t("diagnostics.events.observed")} values={event.observed} /> : null}
        {event.details ? <KeyValuePanel title={t("diagnostics.events.details")} values={event.details} /> : null}
        {event.exception ? (
          <ExceptionPanel title={t("diagnostics.events.exception")} exception={event.exception} />
        ) : null}
        <div className="text-xs text-muted-foreground">
          {event.redactionsApplied ? t("diagnostics.events.redacted") : t("diagnostics.events.noRedactions")}
          {event.truncated ? ` · ${t("diagnostics.events.truncated")}` : ""}
        </div>
      </div>
    </details>
  )
}

function KeyValuePanel({ title, values }: { title: string; values: Readonly<Record<string, string>> }) {
  return (
    <div className="min-w-0 rounded-lg border bg-muted/20 p-3">
      <div className="min-w-0 [overflow-wrap:anywhere] font-medium">{title}</div>
      <dl className="mt-2 grid min-w-0 gap-2 sm:grid-cols-2">
        {Object.entries(values).map(([key, value]) => (
          <div key={key} className="min-w-0">
            <dt className="min-w-0 [overflow-wrap:anywhere] text-xs text-muted-foreground">{key}</dt>
            <dd className="min-w-0 whitespace-pre-wrap [overflow-wrap:anywhere] font-mono text-xs">{value}</dd>
          </div>
        ))}
      </dl>
    </div>
  )
}

function ExceptionPanel({ title, exception }: { title: string; exception: DiagnosticsException }) {
  return (
    <div className="min-w-0 rounded-lg border border-destructive/20 bg-destructive/5 p-3">
      <div className="min-w-0 [overflow-wrap:anywhere] font-medium">{title}: {exception.type}</div>
      <p className="mt-2 min-w-0 whitespace-pre-wrap [overflow-wrap:anywhere] text-sm">{exception.message}</p>
      {exception.stackTrace ? (
        <pre className="mt-3 max-h-64 max-w-full overflow-auto whitespace-pre-wrap rounded bg-background p-3 text-xs">
          {exception.stackTrace}
        </pre>
      ) : null}
      {exception.innerExceptions.map((inner, index) => (
        <div key={`${inner.type}-${index}`} className="mt-3 min-w-0 border-l-2 pl-3">
          <ExceptionPanel title="Inner exception" exception={inner} />
        </div>
      ))}
    </div>
  )
}

function Fact({ label, value, title }: { label: string; value: string; title?: string }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</dt>
      <dd className="mt-1 min-w-0 [overflow-wrap:anywhere] font-medium" title={title}>{value}</dd>
    </div>
  )
}

function isSafeWorkspaceLink(value: string | null): value is string {
  return Boolean(value && value.startsWith("/") && !value.startsWith("//"))
}
