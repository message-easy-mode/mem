import { AlertTriangle, ArrowRight, ExternalLink, LoaderCircle } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { cn } from "@/lib/utils"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import type { DiagnosticsActiveContext } from "../api/diagnostics.types"
import { diagnosticsUtcIso, formatDiagnosticsLocalDateTime } from "../diagnostics-time"

export function DiagnosticsActiveContextBanner({
  context,
}: {
  context: DiagnosticsActiveContext
}) {
  const { language, t } = useI18n()
  const attention = context.state === "attention"
  const workspaceHref = safeInternalHref(context.workspaceHref)
  const incidentHref = safeIncidentHref(context.incidentHref)

  return (
    <Card
      role={attention ? "alert" : "status"}
      aria-live={attention ? "assertive" : "polite"}
      className={cn(
        "border-l-4",
        attention
          ? "border-red-500/30 border-l-red-500 bg-red-500/[0.04]"
          : "border-sky-500/30 border-l-sky-500 bg-sky-500/[0.04]",
      )}
      data-active-context-state={context.state}
    >
      <CardContent className="flex flex-col gap-4 py-4 lg:flex-row lg:items-center lg:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            {attention ? (
              <AlertTriangle className="h-4 w-4 text-red-500" />
            ) : (
              <LoaderCircle className="h-4 w-4 text-sky-500" />
            )}
            <p className="font-medium">{contextTitle(context.code, t)}</p>
            <Badge variant={attention ? "destructive" : "outline"}>
              {attention
                ? t("diagnostics.command.status.attention")
                : t("diagnostics.command.status.running")}
            </Badge>
          </div>
          <p className="mt-1 text-sm text-muted-foreground">
            {context.summary ?? contextDescription(context.code, t)}
          </p>
          <div className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted-foreground">
            <span>{t("diagnostics.command.context.feature")}: {context.feature}</span>
            {context.stage ? <span>{t("diagnostics.command.context.stage")}: {context.stage}</span> : null}
            {context.resourceName ? <span>{t("diagnostics.command.context.resource")}: {context.resourceName}</span> : null}
            <span title={`${t("diagnostics.time.utcEvidence")}: ${diagnosticsUtcIso(context.updatedAtUtc)}`}>
              {t("diagnostics.command.context.updated")}: {formatDiagnosticsLocalDateTime(context.updatedAtUtc, language)}
            </span>
          </div>
        </div>

        <div className="flex shrink-0 flex-wrap gap-2">
          {workspaceHref ? (
            <Button asChild variant="outline" size="sm">
              <Link to={workspaceHref}>
                {t("diagnostics.command.context.openWorkspace")}
                <ArrowRight className="ml-2 h-4 w-4" />
              </Link>
            </Button>
          ) : null}
          {incidentHref ? (
            <Button asChild variant={workspaceHref ? "ghost" : "outline"} size="sm">
              <Link to={incidentHref}>
                {t("diagnostics.command.context.openIncident")}
                <ExternalLink className="ml-2 h-4 w-4" />
              </Link>
            </Button>
          ) : null}
        </div>
      </CardContent>
    </Card>
  )
}

function contextTitle(
  code: string,
  t: ReturnType<typeof useI18n>["t"],
): string {
  switch (code) {
    case "workflow_requires_attention":
      return t("diagnostics.command.context.workflowAttention")
    case "incident_requires_attention":
      return t("diagnostics.command.context.incidentAttention")
    case "migration_active":
      return t("diagnostics.command.context.migrationActive")
    case "restore_active":
      return t("diagnostics.command.context.restoreActive")
    case "installation_active":
      return t("diagnostics.command.context.installationActive")
    case "federation_operation_active":
      return t("diagnostics.command.context.federationActive")
    case "turn_operation_active":
      return t("diagnostics.command.context.turnActive")
    case "seq_operation_active":
      return t("diagnostics.command.context.seqActive")
    case "runtime_operation_active":
      return t("diagnostics.command.context.runtimeActive")
    default:
      return t("diagnostics.command.context.active")
  }
}

function contextDescription(
  code: string,
  t: ReturnType<typeof useI18n>["t"],
): string {
  return code.endsWith("_active")
    ? t("diagnostics.command.context.runningDescription")
    : t("diagnostics.command.context.attentionDescription")
}

function safeInternalHref(value: string | null): string | null {
  return value && value.startsWith("/") && !value.startsWith("//")
    ? value
    : null
}

function safeIncidentHref(value: string | null): string | null {
  const safe = safeInternalHref(value)
  return safe?.startsWith("/diagnostics/") ? safe : null
}
