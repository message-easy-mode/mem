import { useState } from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { AlertTriangle, Container, ExternalLink, RefreshCw } from "lucide-react"

import { formatNumber } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { ApiProblemAlert } from "@/components/operator/api-problem-alert"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import {
  getDiagnosticDockerEvidence,
  refreshDiagnosticDockerEvidence,
} from "../api/diagnostics.api"
import type { DiagnosticsDockerEvidenceResponse } from "../api/diagnostics.types"
import { formatDiagnosticsLocalDateTime } from "../diagnostics-time"

export function DiagnosticDockerEvidence({
  incidentId,
  canOpenPortainer = false,
}: {
  incidentId: string
  canOpenPortainer?: boolean
}) {
  const { language, t } = useI18n()
  const [requested, setRequested] = useState(false)
  const queryClient = useQueryClient()
  const queryKey = ["diagnostics", "incident", incidentId, "docker-evidence"] as const
  const evidence = useQuery({
    queryKey,
    queryFn: () => getDiagnosticDockerEvidence(incidentId),
    enabled: requested,
    retry: 0,
    staleTime: 10_000,
  })

  const refresh = useMutation({
    mutationFn: () => refreshDiagnosticDockerEvidence(incidentId),
    onSuccess: (result) => {
      queryClient.setQueryData(queryKey, result)
    },
  })

  if (!requested) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Container className="h-4 w-4" />
            {t("diagnostics.docker.title")}
          </CardTitle>
          <CardDescription>{t("diagnostics.docker.description")}</CardDescription>
        </CardHeader>
        <CardContent>
          <Button type="button" variant="outline" size="sm" onClick={() => setRequested(true)}>
            {t("diagnostics.docker.load")}
          </Button>
        </CardContent>
      </Card>
    )
  }

  if (evidence.isLoading && !evidence.data) {
    return (
      <Card>
        <CardContent className="flex items-center gap-2 py-5 text-sm text-muted-foreground">
          <RefreshCw className="h-4 w-4 animate-spin" />
          {t("diagnostics.docker.loading")}
        </CardContent>
      </Card>
    )
  }

  if (evidence.error && !evidence.data) {
    return (
      <ApiProblemAlert
        error={evidence.error}
        title={t("diagnostics.docker.loadErrorTitle")}
        fallbackDescription={t("diagnostics.docker.loadErrorDescription")}
        onRetry={() => void evidence.refetch()}
        retrying={evidence.isFetching}
        showDiagnosticsLink={false}
      />
    )
  }

  if (!evidence.data) return null

  return (
    <div className="space-y-3">
      {refresh.error ? (
        <ApiProblemAlert
          error={refresh.error}
          title={t("diagnostics.docker.loadErrorTitle")}
          fallbackDescription={t("diagnostics.docker.loadErrorDescription")}
          onRetry={() => refresh.mutate()}
          retrying={refresh.isPending}
          showDiagnosticsLink={false}
        />
      ) : null}
      <DockerEvidenceResult
        incidentId={incidentId}
        canOpenPortainer={canOpenPortainer}
        evidence={evidence.data}
        refreshing={evidence.isFetching || refresh.isPending}
        onRefresh={() => {
          setRequested(true)
          refresh.reset()
          refresh.mutate()
        }}
        formatTime={(value) => formatDiagnosticsLocalDateTime(value, language, true)}
        formatCount={(value) => formatNumber(value, language)}
      />
    </div>
  )
}

function DockerEvidenceResult({
  incidentId,
  canOpenPortainer,
  evidence,
  refreshing,
  onRefresh,
  formatTime,
  formatCount,
}: {
  incidentId: string
  canOpenPortainer: boolean
  evidence: DiagnosticsDockerEvidenceResponse
  refreshing: boolean
  onRefresh: () => void
  formatTime: (value: string) => string
  formatCount: (value: number) => string
}) {
  const { t } = useI18n()
  const additionalWarnings = evidence.warnings.filter(
    (warning) => warning !== evidence.warningCode,
  )

  return (
    <Card className={evidence.available ? "border-emerald-500/30" : "border-amber-500/30"}>
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2 text-base">
              <Container className="h-4 w-4" />
              {t("diagnostics.docker.title")}
            </CardTitle>
            <CardDescription>
              {evidence.available ? t("diagnostics.docker.available") : t("diagnostics.docker.unavailable")}
            </CardDescription>
          </div>
          <Button type="button" variant="outline" size="sm" onClick={onRefresh} disabled={refreshing}>
            <RefreshCw className={refreshing ? "mr-2 h-4 w-4 animate-spin" : "mr-2 h-4 w-4"} />
            {t("diagnostics.docker.refresh")}
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {!evidence.available ? (
          <Alert>
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("diagnostics.docker.partialTitle")}</AlertTitle>
            <AlertDescription>
              {evidence.warningCode ?? t("diagnostics.docker.unavailableDescription")}
            </AlertDescription>
          </Alert>
        ) : null}

        {evidence.container ? (
          <dl className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <Fact label={t("diagnostics.docker.logicalName")} value={evidence.container.logicalName} />
            <Fact label={t("diagnostics.docker.state")} value={evidence.container.observedState} />
            <Fact label={t("diagnostics.docker.health")} value={evidence.container.health ?? t("diagnostics.health.notConfigured")} />
            <Fact label={t("diagnostics.docker.exitCode")} value={evidence.container.exitCode === null ? "—" : String(evidence.container.exitCode)} />
            <Fact label={t("diagnostics.docker.restartCount")} value={formatCount(evidence.container.restartCount)} />
            {evidence.container.image ? <Fact label={t("diagnostics.docker.image")} value={evidence.container.image} /> : null}
            {evidence.container.startedAtUtc ? <Fact label={t("diagnostics.docker.started")} value={formatTime(evidence.container.startedAtUtc)} /> : null}
            {evidence.container.finishedAtUtc ? <Fact label={t("diagnostics.docker.finished")} value={formatTime(evidence.container.finishedAtUtc)} /> : null}
          </dl>
        ) : null}

        {evidence.logTail ? (
          <section className="space-y-2">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h3 className="font-medium">{t("diagnostics.docker.logTail")}</h3>
              <div className="flex gap-2">
                {evidence.logTail.redactionsApplied ? <Badge variant="outline">{t("diagnostics.docker.redacted")}</Badge> : null}
                {evidence.logTail.truncated ? <Badge variant="secondary">{t("diagnostics.docker.truncated")}</Badge> : null}
              </div>
            </div>
            <pre className="max-h-80 overflow-auto whitespace-pre-wrap break-words rounded-lg border bg-background p-3 text-xs">
              {evidence.logTail.content}
            </pre>
          </section>
        ) : null}

        {additionalWarnings.length > 0 ? (
          <ul className="list-disc space-y-1 pl-5 text-xs text-muted-foreground">
            {additionalWarnings.map((warning) => <li key={warning}>{warning}</li>)}
          </ul>
        ) : null}

        {canOpenPortainer && evidence.available ? (
          <div className="rounded-lg border p-3">
            <p className="text-sm font-medium">{t("diagnostics.portainer.incident.title")}</p>
            <p className="mt-1 text-xs text-muted-foreground">
              {t("diagnostics.portainer.incident.description")}
            </p>
            <Button asChild variant="outline" size="sm" className="mt-3">
              <a
                href={`/api/operator/diagnostics/portainer/incidents/${encodeURIComponent(incidentId)}/container`}
                target="_blank"
                rel="noopener noreferrer"
              >
                {t("diagnostics.portainer.incident.open")}
                <ExternalLink className="ml-2 h-3.5 w-3.5" />
              </a>
            </Button>
          </div>
        ) : null}

        <p className="text-xs text-muted-foreground">{t("diagnostics.docker.privacy")}</p>
      </CardContent>
    </Card>
  )
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</dt>
      <dd className="mt-1 break-words font-medium">{value}</dd>
    </div>
  )
}
