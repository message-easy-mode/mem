import { useState } from "react"
import {
  AlertTriangle,
  Boxes,
  ChevronDown,
  Cpu,
  Database,
  Fingerprint,
  Info,
  MonitorCog,
  Network,
  ServerCog,
  ShieldCheck,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { RuntimeContextDetailsTrigger } from "@/features/runtime-context/components/runtime-context-details"
import {
  controlPlaneAccessModeKey,
  controlPlaneExposureStateKey,
  dockerEndpointKindKey,
  runtimeModeKey,
  stateRootLabelKey,
  uiDeliveryKey,
} from "@/features/runtime-context/runtime-context-labels"
import { runtimeContextNeedsAttention } from "@/features/runtime-context/runtime-context-state"
import { useRuntimeContext } from "@/features/runtime-context/use-runtime-context"
import { cn } from "@/lib/utils"

export function DiagnosticsRuntimeCard() {
  const { t } = useI18n()
  const runtime = useRuntimeContext()
  const [expanded, setExpanded] = useState(false)

  if (runtime.isLoading && !runtime.data) {
    return (
      <Card>
        <CardHeader>
          <CardTitle role="heading" aria-level={2}>{t("runtime.card.title")}</CardTitle>
          <CardDescription>{t("runtime.card.loading")}</CardDescription>
        </CardHeader>
      </Card>
    )
  }

  if (runtime.error && !runtime.data) {
    return (
      <Alert
        className="border-amber-500/30 bg-amber-500/[0.04]"
        role="status"
        data-runtime-context-state="unavailable"
      >
        <AlertTriangle className="h-4 w-4 text-amber-500" />
        <AlertTitle>{t("runtime.card.unavailableTitle")}</AlertTitle>
        <AlertDescription>{t("runtime.card.unavailableDescription")}</AlertDescription>
      </Alert>
    )
  }

  const data = runtime.data
  if (!data) return null

  const needsAttention = runtimeContextNeedsAttention(data)
  const ownership = t(`runtime.ownership.${data.dockerOwnership.state}` as TranslationKey)
  const exposureMode = t(controlPlaneAccessModeKey(data.controlPlaneExposure.accessMode))
  const exposureState = t(controlPlaneExposureStateKey(data.controlPlaneExposure.state))
  const exposureBinding = data.controlPlaneExposure.hostAddress && data.controlPlaneExposure.hostPort
    ? t("runtime.card.accessBinding", {
        mode: exposureMode,
        address: data.controlPlaneExposure.hostAddress,
        port: data.controlPlaneExposure.hostPort,
      })
    : `${exposureMode} · ${exposureState}`
  const version = data.commit ? `${data.version} · ${data.commit}` : data.version
  const hasWarnings = data.warnings.length > 0 ||
    Boolean(data.dockerOwnership.warningCode) ||
    Boolean(data.controlPlaneExposure.warningCode)
  const hasCompetingContainers = data.dockerOwnership.competingContainers.length > 0

  return (
    <Card data-testid="diagnostics-runtime-card">
      <CardHeader className="gap-3">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle role="heading" aria-level={2}>
              {t("runtime.card.title")}
            </CardTitle>
            <CardDescription>{t("runtime.card.description")}</CardDescription>
          </div>
          <Badge variant={needsAttention ? "outline" : "secondary"}>
            {needsAttention ? t("runtime.validation.warning") : t("runtime.validation.valid")}
          </Badge>
        </div>

        <div className="flex flex-col gap-3 border-t border-border/70 pt-3 lg:flex-row lg:items-end lg:justify-between">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
              <span className="font-medium">{t(runtimeModeKey(data.runtimeMode))}</span>
              <span className="text-muted-foreground" aria-hidden="true">·</span>
              <span>{t(uiDeliveryKey(data.uiDeliveryMode))}</span>
              <span className="text-muted-foreground" aria-hidden="true">·</span>
              <span>
                {t("runtime.card.ownership")}: {ownership}
              </span>
            </div>
            <div className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-muted-foreground">
              <span>{t("runtime.card.access")}: {exposureBinding}</span>
              <span aria-hidden="true">·</span>
              <span>{t(stateRootLabelKey(data.stateRootKind, data.stateRootProfile))}</span>
              <span aria-hidden="true">·</span>
              <span>{version}</span>
            </div>
          </div>

          <div className="flex shrink-0 flex-wrap items-center gap-2">
            <RuntimeContextDetailsTrigger>
              <Button type="button" variant="outline" size="sm">
                <Info className="h-4 w-4" aria-hidden="true" />
                {t("runtime.card.openSystemInformation")}
              </Button>
            </RuntimeContextDetailsTrigger>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              aria-expanded={expanded}
              aria-controls="diagnostics-runtime-details"
              onClick={() => setExpanded((current) => !current)}
            >
              <ChevronDown
                className={cn("h-4 w-4 transition-transform", expanded && "rotate-180")}
                aria-hidden="true"
              />
              {expanded ? t("runtime.card.hideDetails") : t("runtime.card.showDetails")}
            </Button>
          </div>
        </div>
      </CardHeader>

      {expanded || hasWarnings || hasCompetingContainers ? (
        <CardContent className="space-y-4" id="diagnostics-runtime-details">
          {expanded ? (
            <>
              <dl className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                <RuntimeFact
                  icon={MonitorCog}
                  label={t("runtime.card.mode")}
                  value={t(runtimeModeKey(data.runtimeMode))}
                />
                <RuntimeFact
                  icon={Boxes}
                  label={t("runtime.card.uiDelivery")}
                  value={t(uiDeliveryKey(data.uiDeliveryMode))}
                />
                <RuntimeFact
                  icon={ServerCog}
                  label={t("runtime.card.environment")}
                  value={data.environmentName}
                />
                <RuntimeFact
                  icon={Database}
                  label={t("runtime.card.state")}
                  value={t(stateRootLabelKey(data.stateRootKind, data.stateRootProfile))}
                />
                <RuntimeFact
                  icon={Fingerprint}
                  label={t("runtime.card.instance")}
                  value={data.controlPlaneInstanceId}
                  mono
                />
                <RuntimeFact
                  icon={Cpu}
                  label={t("runtime.card.process")}
                  value={data.apiProcessInstanceId}
                  mono
                />
                <RuntimeFact
                  icon={Network}
                  label={t("runtime.card.docker")}
                  value={t(dockerEndpointKindKey(data.dockerEndpointKind))}
                />
                <RuntimeFact
                  icon={ServerCog}
                  label={t("runtime.card.ownership")}
                  value={ownership}
                />
                <RuntimeFact
                  icon={ShieldCheck}
                  label={t("runtime.card.access")}
                  value={exposureBinding}
                />
                <RuntimeFact
                  icon={MonitorCog}
                  label={t("runtime.card.version")}
                  value={version}
                />
              </dl>

              {data.configuredContainerName ? (
                <p className="text-sm text-muted-foreground">
                  {t("runtime.card.container", { name: data.configuredContainerName })}
                </p>
              ) : null}
            </>
          ) : null}

          {hasCompetingContainers ? (
            <p className="text-sm text-muted-foreground">
              {t("runtime.card.competingContainers", {
                names: data.dockerOwnership.competingContainers.join(", "),
              })}
            </p>
          ) : null}

          {hasWarnings ? (
            <Alert>
              <AlertTriangle className="h-4 w-4" />
              <AlertTitle>{t("runtime.card.warningsTitle")}</AlertTitle>
              <AlertDescription>
                <ul className="mt-1 list-disc space-y-1 pl-5 font-mono text-xs">
                  {data.dockerOwnership.warningCode ? (
                    <li>{data.dockerOwnership.warningCode}</li>
                  ) : null}
                  {data.controlPlaneExposure.warningCode ? (
                    <li>{data.controlPlaneExposure.warningCode}</li>
                  ) : null}
                  {data.warnings.map((warning) => <li key={warning}>{warning}</li>)}
                </ul>
              </AlertDescription>
            </Alert>
          ) : null}
        </CardContent>
      ) : null}
    </Card>
  )
}

function RuntimeFact({
  icon: Icon,
  label,
  value,
  mono = false,
}: {
  icon: typeof Cpu
  label: string
  value: string
  mono?: boolean
}) {
  return (
    <div className="rounded-lg border border-border/70 bg-muted/20 p-3">
      <dt className="flex items-center gap-2 text-xs font-medium uppercase tracking-wide text-muted-foreground">
        <Icon className="h-4 w-4" aria-hidden="true" />
        {label}
      </dt>
      <dd className={mono ? "mt-2 break-all font-mono text-xs" : "mt-2 text-sm font-medium"}>
        {value}
      </dd>
    </div>
  )
}
