import { useQuery } from "@tanstack/react-query"
import {
  Boxes,
  ExternalLink,
  FileText,
  HeartPulse,
  RefreshCw,
  ShieldCheck,
  Wrench,
} from "lucide-react"
import { Link, useSearchParams } from "react-router-dom"

import { cn } from "@/lib/utils"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
} from "@/components/ui/card"
import { getDiagnosticsOverview } from "./api/diagnostics.api"
import type { DiagnosticsOverviewResponse } from "./api/diagnostics.types"
import { DiagnosticIncidentReferenceCard } from "./components/diagnostic-incident-reference-card"
import { DiagnosticsActiveContextBanner } from "./components/diagnostics-active-context-banner"
import { DiagnosticsSectionErrorBoundary } from "./components/diagnostics-error-boundary"
import { DiagnosticsHealthStrip } from "./components/diagnostics-health-strip"
import { DiagnosticsPipelineSelfTestCard } from "./components/diagnostics-pipeline-self-test-card"
import { DiagnosticsRuntimeCard } from "./components/diagnostics-runtime-card"
import { DiagnosticsSummary } from "./components/diagnostics-summary"
import { diagnosticsUtcIso, formatDiagnosticsLocalDateTime } from "./diagnostics-time"

export function DiagnosticsPage() {
  const { language, t } = useI18n()
  const [searchParams] = useSearchParams()
  const incidentId = searchParams.get("incidentId")?.trim() || null
  const overview = useQuery({
    queryKey: ["diagnostics", "overview"],
    queryFn: getDiagnosticsOverview,
    staleTime: 10_000,
    refetchInterval: 30_000,
    refetchIntervalInBackground: false,
    retry: 0,
  })
  const refresh = () => void overview.refetch()

  return (
    <div className="space-y-6">
      <PageHeader
        refreshing={overview.isFetching}
        onRefresh={refresh}
        generatedAtUtc={overview.data?.generatedAtUtc ?? null}
      />

      {overview.isLoading && !overview.data ? (
        <DiagnosticsSummary
          data={undefined}
          error={undefined}
          loading
          fetching={overview.isFetching}
          onRefresh={refresh}
        />
      ) : null}

      {overview.error && !overview.data ? (
        <DiagnosticsSummary
          data={undefined}
          error={overview.error}
          loading={false}
          fetching={overview.isFetching}
          onRefresh={refresh}
        />
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

          <DiagnosticsSectionErrorBoundary
            resetKey={`health-strip:${language}:${overview.dataUpdatedAt}`}
          >
            <DiagnosticsHealthStrip data={overview.data} />
          </DiagnosticsSectionErrorBoundary>

          <DiagnosticsSectionErrorBoundary
            resetKey={`runtime-context:${language}:${overview.dataUpdatedAt}`}
          >
            <DiagnosticsRuntimeCard />
          </DiagnosticsSectionErrorBoundary>

          {overview.data.activeContext ? (
            <DiagnosticsSectionErrorBoundary
              resetKey={`active-context:${language}:${overview.data.activeContext.code}:${overview.data.activeContext.updatedAtUtc}`}
            >
              <DiagnosticsActiveContextBanner context={overview.data.activeContext} />
            </DiagnosticsSectionErrorBoundary>
          ) : null}

          {incidentId ? (
            <DiagnosticsSectionErrorBoundary
              resetKey={`incident:${language}:${incidentId}`}
            >
              <div className="space-y-3">
                <DiagnosticIncidentReferenceCard incidentId={incidentId} />
                <Button asChild variant="outline" size="sm">
                  <Link to={`/diagnostics/logs?incident=${encodeURIComponent(incidentId)}`}>
                    {t("diagnostics.incident.openFullWorkspace")}
                  </Link>
                </Button>
              </div>
            </DiagnosticsSectionErrorBoundary>
          ) : null}

          <DiagnosticsCommandCentre overview={overview.data} />
        </>
      ) : null}
    </div>
  )
}

function PageHeader({
  refreshing,
  onRefresh,
  generatedAtUtc,
}: {
  refreshing: boolean
  onRefresh: () => void
  generatedAtUtc: string | null
}) {
  const { language, t } = useI18n()

  return (
    <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">{t("diagnostics.title")}</h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
          {t("diagnostics.command.description")}
        </p>
      </div>
      <div className="flex flex-wrap items-center gap-3 md:justify-end">
        {generatedAtUtc ? (
          <p className="text-xs text-muted-foreground" data-diagnostics-last-updated>
            <span title={`${t("diagnostics.time.utcEvidence")}: ${diagnosticsUtcIso(generatedAtUtc)}`}>
              {t("diagnostics.command.lastUpdated", {
                time: formatDiagnosticsLocalDateTime(generatedAtUtc, language),
              })}
            </span>
          </p>
        ) : null}
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
      </div>
    </div>
  )
}

function DiagnosticsCommandCentre({ overview }: { overview: DiagnosticsOverviewResponse }) {
  const { t } = useI18n()
  const attentionCount = overview.counts.incidentCount
  const health = overview.loggingHealth
  const canReadEvents = overview.capabilities.canReadTechnicalEvents
  const canReadDocker = overview.capabilities.canReadDockerEvidence
  const canOpenPortainer = overview.capabilities.canOpenPortainer
  const seqReady = health.seq.status === "ready" && health.seq.reachable
  const seqNeutral = !health.seq.configured && !health.seq.sinkEnabled

  return (
    <section aria-labelledby="diagnostics-command-centre-title" className="space-y-4">
      <div>
        <h2 id="diagnostics-command-centre-title" className="text-lg font-semibold">
          {t("diagnostics.command.operatorTitle")}
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          {t("diagnostics.command.operatorDescription")}
        </p>
      </div>

      <div className="grid gap-4 md:grid-cols-2 2xl:grid-cols-4" data-diagnostics-capability-grid>
        <ContainedCommandCard id="incidents">
          <CapabilityCard
            icon={FileText}
            iconTone="incidents"
            title={t("diagnostics.command.incidents.title")}
            description={t("diagnostics.command.incidents.description")}
            state={attentionCount === 0
              ? t("diagnostics.command.incidents.clear")
              : t("diagnostics.command.incidents.attention", { count: attentionCount })}
            stateTone={attentionCount === 0 ? "ready" : "attention"}
            primaryAction={{
              label: t("diagnostics.command.incidents.open"),
              to: "/diagnostics/logs?tab=incidents",
            }}
            secondaryAction={canReadEvents ? {
              label: t("diagnostics.command.events.open"),
              to: "/diagnostics/logs?tab=events",
            } : null}
          />
        </ContainedCommandCard>

        <ContainedCommandCard id="logging-health">
          <CapabilityCard
            icon={HeartPulse}
            iconTone="logging"
            title={t("diagnostics.command.logging.title")}
            description={t("diagnostics.command.logging.description")}
            state={statusLabel(health.status, t)}
            stateTone={health.status === "ready" && !health.partial ? "ready" : "degraded"}
            primaryAction={{
              label: t("diagnostics.command.logging.open"),
              to: "/diagnostics/logs?tab=health",
            }}
          />
        </ContainedCommandCard>

        <ContainedCommandCard id="seq">
          <CapabilityCard
            brandIconSrc="/brands/seq-mark.svg"
            brandIconClassName="h-7 w-7"
            title={t("diagnostics.command.seq.title")}
            description={seqNeutral
              ? t("diagnostics.command.seq.notConfiguredDescription")
              : t("diagnostics.command.seq.configuredDescription")}
            state={seqNeutral
              ? t("diagnostics.command.status.notConfigured")
              : seqReady
                ? t("diagnostics.command.status.ready")
                : t("diagnostics.command.status.degraded")}
            stateTone={seqNeutral ? "neutral" : seqReady ? "ready" : "degraded"}
            primaryAction={canReadEvents ? {
              label: t("diagnostics.command.seq.review"),
              to: "/diagnostics/seq",
            } : {
              label: t("diagnostics.command.seq.reviewHealth"),
              to: "/diagnostics/logs?tab=health",
            }}
            secondaryAction={canReadEvents && health.seq.serverUrl ? {
              label: t("diagnostics.command.seq.open"),
              href: health.seq.serverUrl,
              prominent: true,
            } : null}
          />
        </ContainedCommandCard>

        <ContainedCommandCard id="container-diagnostics">
          <CapabilityCard
            icon={Boxes}
            iconTone="containers"
            title={t("diagnostics.command.containers.title")}
            description={canOpenPortainer
              ? t("diagnostics.command.containers.portainerDescription")
              : canReadDocker
                ? t("diagnostics.command.containers.description")
                : t("diagnostics.command.containers.readOnlyDescription")}
            state={canOpenPortainer || canReadDocker
              ? t("diagnostics.command.status.available")
              : t("diagnostics.command.status.restricted")}
            stateTone={canOpenPortainer || canReadDocker ? "ready" : "neutral"}
            primaryAction={canOpenPortainer ? {
              label: t("diagnostics.command.containers.openPortainer"),
              to: "/diagnostics/portainer",
            } : canReadDocker ? {
              label: t("diagnostics.command.containers.open"),
              to: "/diagnostics/logs?tab=incidents",
            } : null}
          />
        </ContainedCommandCard>

        <ContainedCommandCard id="pipeline-self-test">
          {overview.capabilities.canVerifyPipeline ? (
            <DiagnosticsPipelineSelfTestCard enabled commandCentre />
          ) : (
            <CapabilityCard
              icon={ShieldCheck}
              iconTone="verification"
              title={t("diagnostics.selfTest.title")}
              description={t("diagnostics.selfTest.description")}
              state={t("diagnostics.command.status.ownerOnly")}
              stateTone="neutral"
            />
          )}
        </ContainedCommandCard>

        <ContainedCommandCard id="runtime-reconciliation">
          <CapabilityCard
            icon={Wrench}
            iconTone="reconciliation"
            title={t("diagnostics.command.reconciliation.title")}
            description={t("diagnostics.command.reconciliation.description")}
            state={t("diagnostics.command.status.available")}
            stateTone="ready"
            primaryAction={{
              label: t("diagnostics.command.reconciliation.open"),
              to: "/diagnostics/runtime-reconciliation",
            }}
          />
        </ContainedCommandCard>
      </div>
    </section>
  )
}

function ContainedCommandCard({
  id,
  children,
}: {
  id: string
  children: React.ReactNode
}) {
  const { language } = useI18n()

  return (
    <DiagnosticsSectionErrorBoundary resetKey={`command-card:${language}:${id}`}>
      {children}
    </DiagnosticsSectionErrorBoundary>
  )
}

type CapabilityAction = Readonly<{
  label: string
  to?: string
  href?: string
  prominent?: boolean
}>

type CapabilityIconTone =
  | "incidents"
  | "logging"
  | "containers"
  | "verification"
  | "reconciliation"


const capabilityStateToneClasses: Record<"ready" | "attention" | "degraded" | "neutral", string> = {
  ready: "border-emerald-500/30 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300",
  attention: "",
  degraded: "border-amber-500/30 bg-amber-500/10 text-amber-700 dark:text-amber-300",
  neutral: "border-border/80 bg-muted/30 text-muted-foreground",
}

const capabilityIconToneClasses: Record<CapabilityIconTone, string> = {
  incidents: "border-rose-500/25 bg-rose-500/10 text-rose-400",
  logging: "border-teal-500/25 bg-teal-500/10 text-teal-400",
  containers: "border-sky-500/25 bg-sky-500/10 text-sky-400",
  verification: "border-cyan-500/25 bg-cyan-500/10 text-cyan-400",
  reconciliation: "border-blue-500/25 bg-blue-500/10 text-blue-400",
}

function CapabilityCard({
  icon: Icon,
  iconTone,
  brandIconSrc,
  brandIconClassName,
  title,
  description,
  state,
  stateTone,
  primaryAction = null,
  secondaryAction = null,
}: {
  icon?: React.ComponentType<{ className?: string }>
  iconTone?: CapabilityIconTone
  brandIconSrc?: string
  brandIconClassName?: string
  title: string
  description: string
  state: string
  stateTone: "ready" | "attention" | "degraded" | "neutral"
  primaryAction?: CapabilityAction | null
  secondaryAction?: CapabilityAction | null
}) {
  return (
    <Card className="group h-full transition-[border-color,box-shadow,background-color] hover:border-foreground/20 hover:bg-muted/10 focus-within:ring-2 focus-within:ring-ring/40">
      <CardHeader className="flex-1">
        <div className="flex flex-wrap items-start gap-3">
          <div className="flex min-w-[12rem] flex-1 items-start gap-3">
            {brandIconSrc ? (
              <div
                className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border border-orange-400/25 bg-slate-950 p-2 shadow-sm"
                data-capability-icon="seq"
              >
                <img
                  src={brandIconSrc}
                  alt=""
                  aria-hidden="true"
                  className={cn("object-contain", brandIconClassName)}
                />
              </div>
            ) : Icon && iconTone ? (
              <div
                className={cn(
                  "flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border shadow-sm",
                  capabilityIconToneClasses[iconTone],
                )}
                data-capability-icon={iconTone}
              >
                <Icon className="h-5 w-5" />
              </div>
            ) : null}
            <div className="min-w-0">
              <h3 className="text-base leading-snug font-semibold">
                {primaryAction?.to && safeInternalTo(primaryAction.to) ? (
                  <Link
                    to={primaryAction.to}
                    className="rounded-sm underline-offset-4 transition-colors hover:text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
                  >
                    {title}
                  </Link>
                ) : title}
              </h3>
              <CardDescription className="mt-1 leading-5">{description}</CardDescription>
            </div>
          </div>
          <Badge
            className={cn("ml-auto shrink-0", capabilityStateToneClasses[stateTone])}
            variant={stateTone === "attention" ? "destructive" : "outline"}
            data-state-tone={stateTone}
          >
            {state}
          </Badge>
        </div>
      </CardHeader>
      {(primaryAction || secondaryAction) ? (
        <CardContent className="mt-auto flex flex-wrap gap-2">
          {primaryAction ? <CapabilityActionButton action={primaryAction} /> : null}
          {secondaryAction ? <CapabilityActionButton action={secondaryAction} secondary /> : null}
        </CardContent>
      ) : null}
    </Card>
  )
}


function CapabilityActionButton({
  action,
  secondary = false,
}: {
  action: CapabilityAction
  secondary?: boolean
}) {
  if (action.to && safeInternalTo(action.to)) {
    return (
      <Button asChild variant={action.prominent ? "default" : secondary ? "ghost" : "outline"} size="sm">
        <Link to={action.to}>{action.label}</Link>
      </Button>
    )
  }

  if (action.href && safeExternalHref(action.href)) {
    return (
      <Button asChild variant={action.prominent ? "default" : secondary ? "ghost" : "outline"} size="sm">
        <a href={action.href} target="_blank" rel="noopener noreferrer">
          {action.label}
          <ExternalLink className="ml-2 h-4 w-4" />
        </a>
      </Button>
    )
  }

  return null
}

function safeInternalTo(value: string): boolean {
  return value.startsWith("/") && !value.startsWith("//")
}

function safeExternalHref(value: string): boolean {
  try {
    const url = new URL(value)
    return url.protocol === "https:" || url.protocol === "http:"
  } catch {
    return false
  }
}

function statusLabel(
  status: string,
  t: ReturnType<typeof useI18n>["t"],
): string {
  switch (status.toLowerCase()) {
    case "ready":
      return t("diagnostics.command.status.ready")
    case "attention":
      return t("diagnostics.command.status.attention")
    case "warning":
    case "degraded":
      return t("diagnostics.command.status.degraded")
    case "unavailable":
      return t("diagnostics.command.status.unavailable")
    default:
      return status
  }
}
