import {
  CheckCircle2,
  CircleAlert,
  CircleHelp,
  CircleX,
  Globe2,
  LockKeyhole,
  Route,
  Server,
  ShieldCheck,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Badge } from "@/components/ui/badge"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { cn } from "@/lib/utils"

import type {
  FederationCheck,
  FederationCheckStatus,
  FederationConfigurationState,
  FederationMode,
  RuntimeStackFederationState,
} from "../api/federation.types"

type FederationCurrentStateProps = {
  state: RuntimeStackFederationState
}

export function FederationCurrentState({ state }: FederationCurrentStateProps) {
  const { t } = useI18n()

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle>{t("federation.current.title")}</CardTitle>
              <CardDescription>{t("federation.current.description")}</CardDescription>
            </div>
            <div className="flex flex-wrap gap-2">
              <Badge variant={modeBadgeVariant(state.mode)}>{t(modeLabelKey(state.mode))}</Badge>
              <Badge variant={stateBadgeVariant(state.configurationState)}>
                {t(stateLabelKey(state.configurationState))}
              </Badge>
            </div>
          </div>
        </CardHeader>
        <CardContent className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          <StateFact
            icon={Server}
            label={t("federation.current.matrixRuntime")}
            value={state.matrixContainerRunning
              ? t("federation.value.running")
              : t("federation.value.notRunning")}
          />
          <StateFact
            icon={Route}
            label={t("federation.current.ingress")}
            value={t(ingressLabelKey(state.ingressMode))}
          />
          <StateFact
            icon={ShieldCheck}
            label={t("federation.current.enforcement")}
            value={t(enforcementLabelKey(state.enforcementKind))}
          />
          <StateFact
            icon={LockKeyhole}
            label={t("federation.current.directExposure")}
            value={state.matrixDirectHostPortExposed
              ? t("federation.value.detected")
              : t("federation.value.notDetected")}
          />
        </CardContent>
      </Card>

      {state.mode === "restricted" ? (
        <Card>
          <CardHeader>
            <CardTitle>{t("federation.allowlist.title")}</CardTitle>
            <CardDescription>{t("federation.allowlist.description")}</CardDescription>
          </CardHeader>
          <CardContent>
            {state.allowlist.length > 0 ? (
              <ul className="grid gap-2 sm:grid-cols-2">
                {state.allowlist.map((domain) => (
                  <li key={domain} className="rounded-md border bg-muted/30 px-3 py-2 font-mono text-sm">
                    {domain}
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-sm text-muted-foreground">{t("federation.allowlist.empty")}</p>
            )}
          </CardContent>
        </Card>
      ) : null}

      <Card>
        <CardHeader>
          <CardTitle>{t("federation.ingress.title")}</CardTitle>
          <CardDescription>{t("federation.ingress.description")}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-2">
          <BooleanRow label={t("federation.ingress.routeEnabled")} value={state.canonicalRouteEnabled} />
          <BooleanRow label={t("federation.ingress.routeTarget")} value={state.canonicalRouteTargetsMatrix} />
          <BooleanRow label={t("federation.ingress.certificate")} value={state.canonicalCertificatePresent} />
          <ObservationRow
            label={t("federation.ingress.serverWellKnown")}
            active={state.serverWellKnownPublished}
            activeLabel={t("federation.value.published")}
            inactiveLabel={t("federation.value.notPublished")}
          />
          <ObservationRow
            label={t("federation.ingress.federationPaths")}
            active={state.federationPathsPubliclyForwarded}
            activeLabel={t("federation.value.forwarded")}
            inactiveLabel={t("federation.value.blocked")}
          />
          <ObservationRow
            label={t("federation.ingress.signingKeyPaths")}
            active={state.signingKeyPathsPubliclyForwarded}
            activeLabel={t("federation.value.forwarded")}
            inactiveLabel={t("federation.value.blocked")}
          />
          <BooleanRow
            label={t("federation.ingress.alternateRoute")}
            value={!state.alternateMatrixRouteDetected}
            positiveLabel={t("federation.value.noneDetected")}
            negativeLabel={t("federation.value.detected")}
          />
          <BooleanRow
            label={t("federation.ingress.directPort")}
            value={!state.matrixDirectHostPortExposed}
            positiveLabel={t("federation.value.noneDetected")}
            negativeLabel={t("federation.value.detected")}
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{t("federation.checks.title")}</CardTitle>
          <CardDescription>{t("federation.checks.description")}</CardDescription>
        </CardHeader>
        <CardContent>
          <ul className="divide-y rounded-lg border">
            {state.checks.map((check) => (
              <FederationCheckRow key={check.code} check={check} />
            ))}
          </ul>
        </CardContent>
      </Card>
    </div>
  )
}

function StateFact({
  icon: Icon,
  label,
  value,
}: {
  icon: typeof Globe2
  label: string
  value: string
}) {
  return (
    <div className="rounded-lg border bg-muted/20 p-3">
      <div className="flex items-center gap-2 text-xs font-medium uppercase tracking-wide text-muted-foreground">
        <Icon className="h-4 w-4" aria-hidden="true" />
        {label}
      </div>
      <p className="mt-2 text-sm font-medium">{value}</p>
    </div>
  )
}

function BooleanRow({
  label,
  value,
  positiveLabel,
  negativeLabel,
}: {
  label: string
  value: boolean
  positiveLabel?: string
  negativeLabel?: string
}) {
  const { t } = useI18n()
  return (
    <div className="flex items-center justify-between gap-3 rounded-lg border px-3 py-2">
      <span className="text-sm text-muted-foreground">{label}</span>
      <span className={cn("text-sm font-medium", value ? "text-emerald-700 dark:text-emerald-400" : "text-destructive")}>
        {value
          ? positiveLabel ?? t("federation.value.yes")
          : negativeLabel ?? t("federation.value.no")}
      </span>
    </div>
  )
}


function ObservationRow({
  label,
  active,
  activeLabel,
  inactiveLabel,
}: {
  label: string
  active: boolean
  activeLabel: string
  inactiveLabel: string
}) {
  return (
    <div className="flex items-center justify-between gap-3 rounded-lg border px-3 py-2">
      <span className="text-sm text-muted-foreground">{label}</span>
      <span className="text-sm font-medium">{active ? activeLabel : inactiveLabel}</span>
    </div>
  )
}

function FederationCheckRow({ check }: { check: FederationCheck }) {
  const { t } = useI18n()
  const Icon = checkIcon(check.status)
  const detailKey = checkDetailKey(check.code, check.status)
  return (
    <li className="flex gap-3 px-3 py-3">
      <Icon className={cn("mt-0.5 h-4 w-4 shrink-0", checkIconClass(check.status))} aria-hidden="true" />
      <div className="min-w-0">
        <p className="font-mono text-xs text-muted-foreground">{check.code}</p>
        <p className="mt-1 text-sm">{detailKey ? t(detailKey) : check.detail}</p>
      </div>
    </li>
  )
}


function checkDetailKey(code: string, status: FederationCheckStatus): TranslationKey | null {
  const key = `${code}:${status}`
  switch (key) {
    case "federation.config.supported:passed": return "federation.check.configSupported.passed"
    case "federation.config.supported:failed": return "federation.check.configSupported.failed"
    case "federation.config.supported:unknown": return "federation.check.configSupported.unknown"
    case "federation.matrix.running:passed": return "federation.check.matrixRunning.passed"
    case "federation.matrix.running:failed": return "federation.check.matrixRunning.failed"
    case "federation.matrix.running:unknown": return "federation.check.matrixRunning.unknown"
    case "federation.direct_host_port.absent:passed": return "federation.check.directPort.passed"
    case "federation.direct_host_port.absent:failed": return "federation.check.directPort.failed"
    case "federation.direct_host_port.absent:unknown": return "federation.check.directPort.unknown"
    case "federation.ingress.canonical_route:passed": return "federation.check.canonicalRoute.passed"
    case "federation.ingress.canonical_route:failed": return "federation.check.canonicalRoute.failed"
    case "federation.ingress.canonical_route:unknown": return "federation.check.canonicalRoute.unknown"
    case "federation.ingress.alternate_route.absent:passed": return "federation.check.alternateRoute.passed"
    case "federation.ingress.alternate_route.absent:warning": return "federation.check.alternateRoute.warning"
    case "federation.ingress.alternate_route.absent:unknown": return "federation.check.alternateRoute.unknown"
    default: return null
  }
}

function modeLabelKey(mode: FederationMode): TranslationKey {
  switch (mode) {
    case "public": return "federation.mode.public"
    case "restricted": return "federation.mode.restricted"
    case "local_only": return "federation.mode.localOnly"
    default: return "federation.mode.unknown"
  }
}

function stateLabelKey(state: FederationConfigurationState): TranslationKey {
  switch (state) {
    case "healthy": return "federation.state.healthy"
    case "incomplete": return "federation.state.incomplete"
    case "custom_unsupported": return "federation.state.customUnsupported"
    default: return "federation.state.unavailable"
  }
}

function ingressLabelKey(mode: RuntimeStackFederationState["ingressMode"]): TranslationKey {
  switch (mode) {
    case "normal": return "federation.ingressMode.normal"
    case "local_only": return "federation.ingressMode.localOnly"
    case "missing": return "federation.ingressMode.missing"
    case "custom_unsupported": return "federation.ingressMode.customUnsupported"
    default: return "federation.ingressMode.unavailable"
  }
}

function enforcementLabelKey(kind: string): TranslationKey {
  switch (kind) {
    case "synapse_unrestricted": return "federation.enforcement.unrestricted"
    case "synapse_exact_domain_allowlist": return "federation.enforcement.allowlist"
    case "synapse_empty_allowlist_and_npm_ingress": return "federation.enforcement.localOnly"
    default: return "federation.enforcement.unknown"
  }
}

function modeBadgeVariant(mode: FederationMode): "default" | "secondary" | "outline" {
  return mode === "public" ? "default" : mode === "unknown" ? "outline" : "secondary"
}

function stateBadgeVariant(state: FederationConfigurationState): "secondary" | "destructive" | "outline" {
  return state === "healthy" ? "secondary" : state === "unavailable" ? "destructive" : "outline"
}

function checkIcon(status: FederationCheckStatus) {
  switch (status) {
    case "passed": return CheckCircle2
    case "warning": return CircleAlert
    case "failed": return CircleX
    default: return CircleHelp
  }
}

function checkIconClass(status: FederationCheckStatus) {
  switch (status) {
    case "passed": return "text-emerald-600"
    case "warning": return "text-amber-600"
    case "failed": return "text-destructive"
    default: return "text-muted-foreground"
  }
}
