import { useQuery } from "@tanstack/react-query"
import {
  Boxes,
  ExternalLink,
  RefreshCw,
  ShieldCheck,
  Wrench,
  XCircle,
} from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { useOperatorSession } from "@/features/auth/operator-session-context"
import { getDiagnosticsPortainerOverview } from "./api/diagnostics.api"
import type { DiagnosticsPortainerOverviewResponse } from "./api/diagnostics.types"

export function DiagnosticsPortainerPage({
  parent = "diagnostics",
}: {
  parent?: "diagnostics" | "services"
}) {
  const { session } = useOperatorSession()
  const { t } = useI18n()
  const isOwner = session.roles.includes("platform_owner")
  const overview = useQuery({
    queryKey: ["diagnostics", "portainer"],
    queryFn: getDiagnosticsPortainerOverview,
    enabled: isOwner,
    staleTime: 10_000,
    retry: 0,
  })

  if (!isOwner) {
    return (
      <div className="space-y-6">
        <PageHeader
          refreshing={false}
          onRefresh={() => undefined}
          hideRefresh
          parent={parent}
        />
        <Card>
          <CardHeader>
            <CardTitle>{t("diagnostics.portainer.restricted.title")}</CardTitle>
            <CardDescription>{t("diagnostics.portainer.restricted.description")}</CardDescription>
          </CardHeader>
          <CardContent>
            <Button asChild variant="outline" size="sm">
              <Link to={parent === "services" ? "/services" : "/diagnostics"}>
                {parent === "services" ? t("services.title") : t("diagnostics.portainer.back")}
              </Link>
            </Button>
          </CardContent>
        </Card>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <PageHeader
        refreshing={overview.isFetching}
        onRefresh={() => void overview.refetch()}
        parent={parent}
      />

      {overview.isLoading && !overview.data ? (
        <Card>
          <CardContent className="py-8 text-sm text-muted-foreground">
            {t("diagnostics.portainer.loading")}
          </CardContent>
        </Card>
      ) : null}

      {overview.error && !overview.data ? (
        <Alert variant="destructive">
          <XCircle className="h-4 w-4" />
          <AlertTitle>{t("diagnostics.portainer.unavailable.title")}</AlertTitle>
          <AlertDescription>{t("diagnostics.portainer.unavailable.description")}</AlertDescription>
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
          <PortainerOverview data={overview.data} />
          {!overview.data.environmentConfigured && overview.data.runtimeState === "ready" ? (
            <PortainerSetupGuidance data={overview.data} />
          ) : null}
          <ResponsibilityBoundary />
          <HandoffGuidance data={overview.data} />
        </>
      ) : null}
    </div>
  )
}

function PageHeader({
  refreshing,
  onRefresh,
  hideRefresh = false,
  parent,
}: {
  refreshing: boolean
  onRefresh: () => void
  hideRefresh?: boolean
  parent: "diagnostics" | "services"
}) {
  const { t } = useI18n()
  const title = parent === "services"
    ? t("services.portainer.title")
    : t("diagnostics.portainer.title")
  const description = parent === "services"
    ? t("services.portainer.description")
    : t("diagnostics.portainer.description")

  return (
    <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
      <div>
        <PageBreadcrumbs items={[
          parent === "services"
            ? { label: t("services.title"), to: "/services" }
            : { label: t("diagnostics.title"), to: "/diagnostics" },
          { label: title },
        ]} />
        <h1 className="text-2xl font-semibold tracking-tight">
          {title}
        </h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
          {description}
        </p>
      </div>
      {!hideRefresh ? (
        <Button type="button" variant="outline" size="sm" onClick={onRefresh} disabled={refreshing}>
          <RefreshCw className={`mr-2 h-4 w-4 ${refreshing ? "animate-spin" : ""}`} />
          {refreshing ? t("diagnostics.refreshing") : t("common.refresh")}
        </Button>
      ) : null}
    </div>
  )
}

function PortainerOverview({ data }: { data: DiagnosticsPortainerOverviewResponse }) {
  const { t } = useI18n()
  const home = safeExternalUrl(data.links.home)
  const environment = safeExternalUrl(data.links.environment)
  const containers = safeExternalUrl(data.links.containers)
  return (
    <section aria-labelledby="portainer-overview-title" className="space-y-4">
      <div>
        <h2 id="portainer-overview-title" className="text-lg font-semibold">
          {t("diagnostics.portainer.overview.title")}
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          {t("diagnostics.portainer.overview.description")}
        </p>
      </div>
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <StateCard title={t("diagnostics.portainer.runtime")} value={stateLabel(data.runtimeState, t)} ready={data.available} />
        <StateCard title={t("diagnostics.portainer.ownership")} value={ownershipLabel(data.ownershipState, t)} ready={data.managed} />
        <StateCard title={t("diagnostics.portainer.version")} value={data.version ?? data.approvedVersion} ready={data.version === data.approvedVersion} />
        <StateCard title={t("diagnostics.portainer.environment")} value={data.environmentConfigured ? t("diagnostics.portainer.configured") : t("diagnostics.portainer.notConfigured")} ready={data.environmentConfigured} />
      </div>
      <Card>
        <CardHeader>
          <CardTitle className="text-base">{t("diagnostics.portainer.actions.title")}</CardTitle>
          <CardDescription>{t("diagnostics.portainer.actions.description")}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-wrap gap-2">
          {data.capabilities.canOpenHome && home ? <ExternalAction href={home} label={t("diagnostics.portainer.open")} /> : null}
          {data.capabilities.canOpenEnvironment && environment ? <ExternalAction href={environment} label={t("diagnostics.portainer.openEnvironment")} /> : null}
          {data.capabilities.canOpenContainers && containers ? <ExternalAction href={containers} label={t("diagnostics.portainer.openContainers")} /> : null}
          {!data.available ? <span className="text-sm text-muted-foreground">{t("diagnostics.portainer.actions.unavailable")}</span> : null}
        </CardContent>
      </Card>
      {data.warnings.length > 0 ? (
        <Alert>
          <Wrench className="h-4 w-4" />
          <AlertTitle>{t("diagnostics.portainer.warnings.title")}</AlertTitle>
          <AlertDescription>
            <ul className="list-disc space-y-1 pl-5">
              {data.warnings.map((warning) => <li key={warning}>{warningLabel(warning, t)}</li>)}
            </ul>
          </AlertDescription>
        </Alert>
      ) : null}
    </section>
  )
}

function PortainerSetupGuidance({
  data,
}: {
  data: DiagnosticsPortainerOverviewResponse
}) {
  const { t } = useI18n()
  const home = safeExternalUrl(data.links.home)

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Wrench className="h-4 w-4" />
          {t("diagnostics.portainer.setup.title")}
        </CardTitle>
        <CardDescription>
          {t("diagnostics.portainer.setup.description")}
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4 text-sm">
        <div className="space-y-2 text-muted-foreground">
          <p>{t("diagnostics.portainer.setup.firstAdmin")}</p>
          <p>{t("diagnostics.portainer.setup.token")}</p>
        </div>

        <div className="rounded-lg border bg-background/40 p-3 font-mono text-xs break-all">
          docker logs portainer 2&gt;&amp;1 | grep 'setup_token=' | tail -n 1
        </div>

        <p className="text-sm text-muted-foreground">
          {t("diagnostics.portainer.setup.timeout")}
        </p>

        <div className="rounded-lg border bg-background/40 p-3 font-mono text-xs">
          docker restart portainer
        </div>

        <p className="text-xs text-muted-foreground">
          {t("diagnostics.portainer.setup.credentialsBoundary")}
        </p>

        {data.capabilities.canOpenHome && home ? (
          <ExternalAction
            href={home}
            label={t("diagnostics.portainer.setup.open")}
          />
        ) : null}
      </CardContent>
    </Card>
  )
}

function ResponsibilityBoundary() {
  const { t } = useI18n()
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base"><ShieldCheck className="h-4 w-4" />{t("diagnostics.portainer.boundary.title")}</CardTitle>
        <CardDescription>{t("diagnostics.portainer.boundary.description")}</CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4 md:grid-cols-2">
        <Boundary title={t("diagnostics.portainer.boundary.mem")} description={t("diagnostics.portainer.boundary.memDescription")} />
        <Boundary title={t("diagnostics.portainer.boundary.portainer")} description={t("diagnostics.portainer.boundary.portainerDescription")} />
      </CardContent>
    </Card>
  )
}

function HandoffGuidance({ data }: { data: DiagnosticsPortainerOverviewResponse }) {
  const { t } = useI18n()
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base"><Boxes className="h-4 w-4" />{t("diagnostics.portainer.handoff.title")}</CardTitle>
        <CardDescription>{t("diagnostics.portainer.handoff.description")}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3 text-sm text-muted-foreground">
        <p>{data.exactResourceLinksSupported ? t("diagnostics.portainer.handoff.exact") : t("diagnostics.portainer.handoff.fallback")}</p>
        <p>{t("diagnostics.portainer.handoff.auth")}</p>
      </CardContent>
    </Card>
  )
}

function StateCard({ title, value, ready }: { title: string; value: string; ready: boolean }) {
  return (
    <Card>
      <CardHeader className="pb-2">
        <div className="flex items-center justify-between gap-2">
          <CardTitle className="text-sm">{title}</CardTitle>
          <Badge variant="outline">{ready ? "✓" : "—"}</Badge>
        </div>
      </CardHeader>
      <CardContent className="font-medium">{value}</CardContent>
    </Card>
  )
}

function Boundary({ title, description }: { title: string; description: string }) {
  return <div className="rounded-xl border p-4"><h3 className="font-medium">{title}</h3><p className="mt-2 text-sm text-muted-foreground">{description}</p></div>
}

function ExternalAction({ href, label }: { href: string; label: string }) {
  return <Button asChild variant="outline" size="sm"><a href={href} target="_blank" rel="noopener noreferrer">{label}<ExternalLink className="ml-2 h-3.5 w-3.5" /></a></Button>
}

function safeExternalUrl(value: string | null): string | null {
  if (!value) return null
  try {
    const url = new URL(value)
    return (url.protocol === "http:" || url.protocol === "https:") &&
      !url.username &&
      !url.password
      ? url.toString()
      : null
  } catch {
    return null
  }
}

function stateLabel(state: string, t: ReturnType<typeof useI18n>["t"]): string {
  switch (state) {
    case "ready": return t("diagnostics.portainer.state.ready")
    case "starting": return t("diagnostics.portainer.state.starting")
    case "stopped": return t("diagnostics.portainer.state.stopped")
    case "absent": return t("diagnostics.portainer.state.absent")
    default: return state
  }
}

function ownershipLabel(state: string, t: ReturnType<typeof useI18n>["t"]): string {
  switch (state) {
    case "managed": return t("diagnostics.portainer.ownership.managed")
    case "unmanaged": return t("diagnostics.portainer.ownership.unmanaged")
    case "identity-mismatch": return t("diagnostics.portainer.ownership.mismatch")
    case "runtime-missing": return t("diagnostics.portainer.ownership.missing")
    case "absent": return t("diagnostics.portainer.state.absent")
    default: return state
  }
}

function warningLabel(code: string, t: ReturnType<typeof useI18n>["t"]): string {
  const known: Record<string, string> = {
    portainer_ui_not_configured: t("diagnostics.portainer.warning.ui"),
    portainer_environment_not_configured: t("diagnostics.portainer.warning.environment"),
    portainer_unavailable: t("diagnostics.portainer.warning.unavailable"),
    portainer_unmanaged_container: t("diagnostics.portainer.warning.unmanaged"),
    portainer_managed_runtime_missing: t("diagnostics.portainer.warning.missing"),
    portainer_container_identity_mismatch: t("diagnostics.portainer.warning.mismatch"),
    portainer_approved_upgrade_required: t("diagnostics.portainer.warning.upgrade"),
  }
  return known[code] ?? code
}
