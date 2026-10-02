import { useQuery } from "@tanstack/react-query"
import { AlertTriangle, ExternalLink, LoaderCircle } from "lucide-react"
import { Link } from "react-router-dom"

import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { ApiProblemAlert } from "@/components/operator/api-problem-alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { getDiagnosticIncident } from "../api/diagnostics.api"

export function DiagnosticIncidentReferenceCard({
  incidentId,
}: {
  incidentId: string
}) {
  const { language, t } = useI18n()
  const query = useQuery({
    queryKey: ["diagnostics", "incident", incidentId],
    queryFn: () => getDiagnosticIncident(incidentId),
    retry: 0,
  })

  if (query.isLoading) {
    return (
      <Card aria-live="polite">
        <CardHeader>
          <div className="flex items-start gap-3">
            <LoaderCircle className="mt-0.5 h-5 w-5 animate-spin text-muted-foreground" />
            <div>
              <CardTitle className="text-base">{t("diagnostics.incident.loadingTitle")}</CardTitle>
              <CardDescription>{t("diagnostics.incident.loadingDescription")}</CardDescription>
            </div>
          </div>
        </CardHeader>
      </Card>
    )
  }

  if (query.error || !query.data) {
    return (
      <ApiProblemAlert
        error={query.error}
        title={t("diagnostics.incident.loadErrorTitle")}
        fallbackDescription={t("diagnostics.incident.loadErrorDescription")}
        onRetry={() => void query.refetch()}
        retrying={query.isFetching}
        showDiagnosticsLink={false}
      />
    )
  }

  const { incident, relatedEventCount, warnings } = query.data
  const workspaceLink = isSafeWorkspaceLink(incident.workspaceLink)
    ? incident.workspaceLink
    : null

  return (
    <Card className="border-amber-500/30 bg-amber-500/5">
      <CardHeader>
        <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2 text-base">
              <AlertTriangle className="h-4 w-4 text-amber-500" />
              {t("diagnostics.incident.title")}
            </CardTitle>
            <CardDescription className="mt-1">
              {t("diagnostics.incident.description")}
            </CardDescription>
          </div>
          <Badge variant={severityVariant(incident.severity)}>{incident.severity}</Badge>
        </div>
      </CardHeader>

      <CardContent className="space-y-4">
        <div>
          <p className="font-medium">{incident.message}</p>
          <p className="mt-1 break-all text-xs text-muted-foreground">
            {incident.incidentId}
          </p>
        </div>

        <dl className="grid gap-3 text-sm sm:grid-cols-2 xl:grid-cols-4">
          <IncidentFact label={t("diagnostics.incident.code")} value={incident.eventCode} />
          <IncidentFact label={t("diagnostics.incident.feature")} value={incident.feature} />
          <IncidentFact
            label={t("diagnostics.incident.firstSeen")}
            value={formatDateTime(incident.firstSeenAtUtc, language)}
          />
          <IncidentFact
            label={t("diagnostics.incident.lastSeen")}
            value={formatDateTime(incident.lastSeenAtUtc, language)}
          />
          <IncidentFact
            label={t("diagnostics.incident.occurrences")}
            value={String(incident.occurrenceCount)}
          />
          <IncidentFact
            label={t("diagnostics.incident.relatedEvents")}
            value={String(relatedEventCount)}
          />
          {incident.stage ? (
            <IncidentFact label={t("diagnostics.incident.stage")} value={incident.stage} />
          ) : null}
          {incident.resource?.displayName ? (
            <IncidentFact
              label={t("diagnostics.incident.resource")}
              value={incident.resource.displayName}
            />
          ) : null}
        </dl>

        {warnings.length > 0 ? (
          <div className="rounded-lg border border-amber-500/20 bg-amber-500/10 p-3 text-sm">
            <div className="font-medium">{t("diagnostics.incident.warnings")}</div>
            <ul className="mt-2 list-disc space-y-1 pl-5 text-muted-foreground">
              {warnings.map((warning) => <li key={warning}>{warning}</li>)}
            </ul>
          </div>
        ) : null}

        {workspaceLink ? (
          <Button asChild variant="outline" size="sm">
            <Link to={workspaceLink}>
              <ExternalLink className="mr-2 h-4 w-4" />
              {t("diagnostics.incident.openWorkspace")}
            </Link>
          </Button>
        ) : null}
      </CardContent>
    </Card>
  )
}

function IncidentFact({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </dt>
      <dd className="mt-1 break-words font-medium">{value}</dd>
    </div>
  )
}

function severityVariant(severity: string): "destructive" | "secondary" | "outline" {
  const normalized = severity.toLowerCase()
  if (normalized === "error" || normalized === "critical" || normalized === "fatal") {
    return "destructive"
  }
  if (normalized === "warning") {
    return "secondary"
  }
  return "outline"
}

function isSafeWorkspaceLink(value: string | null): value is string {
  return Boolean(value && value.startsWith("/") && !value.startsWith("//"))
}
