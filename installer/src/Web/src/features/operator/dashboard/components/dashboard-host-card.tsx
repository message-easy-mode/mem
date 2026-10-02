import { ChartNoAxesCombined, Cpu, HardDrive, MemoryStick, ShieldCheck, Wrench } from "lucide-react"
import { Link } from "react-router-dom"

import { formatBytes, formatDateTime, formatNumber } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type { DashboardHostSummary, DashboardTone } from "../api/dashboard.types"
import type { ControlPlaneRuntimeContext } from "@/features/runtime-context/api/runtime-context.types"
import {
  controlPlaneAccessModeKey,
  controlPlaneExposureStateKey,
} from "@/features/runtime-context/runtime-context-labels"
import {
  getDashboardHostDiskMetrics,
  getDashboardHostStorageLabel,
  getDashboardHostStorageUnavailableDescription,
  getDashboardHostUnavailableDescription,
  getDashboardStateLabel,
  getDashboardTone,
} from "./dashboard-state"
import { DashboardStatusPill } from "./dashboard-status-pill"

type Props = {
  host: DashboardHostSummary
  exposure: ControlPlaneRuntimeContext["controlPlaneExposure"] | null
}

export function DashboardHostCard({ host, exposure }: Props) {
  const { language, t } = useI18n()
  const available = host.state === "available"
  const disk = getDashboardHostDiskMetrics(host)
  const notReported = t("dashboard.host.notReported")

  const cpuValue =
    host.cpuCount !== null
      ? t("dashboard.host.cpuCount", { count: formatNumber(host.cpuCount, language) })
      : notReported
  const memoryValue =
    host.memoryTotalBytes !== null && host.memoryTotalBytes > 0
      ? t("dashboard.host.memoryTotal", {
          value: formatBytes(host.memoryTotalBytes, language),
        })
      : notReported
  const dockerValue = host.dockerServerVersion
    ? t("dashboard.host.dockerVersion", { version: host.dockerServerVersion })
    : notReported
  const dockerInventory =
    host.containerCount !== null && host.imageCount !== null
      ? t("dashboard.host.dockerInventory", {
          containers: formatNumber(host.containerCount, language),
          images: formatNumber(host.imageCount, language),
        })
      : null
  const exposureMode = exposure ? t(controlPlaneAccessModeKey(exposure.accessMode)) : null
  const exposureState = exposure ? t(controlPlaneExposureStateKey(exposure.state)) : null
  const exposureBinding = exposure?.hostAddress && exposure.hostPort
    ? `${exposure.hostAddress}:${exposure.hostPort}`
    : exposureState

  return (
    <Card size="sm" role="region" aria-labelledby="dashboard-host-title">
      <CardHeader className="pb-2">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex items-center gap-3">
            <div className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-teal-500/20 bg-teal-500/10 text-teal-300">
              <ChartNoAxesCombined className="h-4 w-4" />
            </div>
            <div>
              <CardTitle id="dashboard-host-title" className="text-base">
                {t("dashboard.host.title")}
              </CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">
                {available
                  ? t("dashboard.host.availableDescription")
                  : t("dashboard.host.unavailableDescription")}
              </p>
            </div>
          </div>
          <DashboardStatusPill tone={getDashboardTone(host.state)}>
            {getDashboardStateLabel(host.state, t)}
          </DashboardStatusPill>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        {available ? (
          <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
            <HostFact
              icon={ChartNoAxesCombined}
              label={t("dashboard.host.system")}
              value={host.operatingSystem ?? notReported}
              detail={host.architecture ?? undefined}
            />
            <HostFact
              icon={Cpu}
              label={t("dashboard.host.processors")}
              value={cpuValue}
            />
            <HostFact
              icon={MemoryStick}
              label={t("dashboard.host.memory")}
              value={memoryValue}
            />
            <HostFact
              icon={ChartNoAxesCombined}
              label={t("dashboard.host.docker")}
              value={dockerValue}
              detail={dockerInventory ?? undefined}
            />
            {disk ? (
              <HostStorageMetric
                label={getDashboardHostStorageLabel(disk.scope, t)}
                value={t("dashboard.host.usage", {
                  used: formatBytes(disk.usedBytes, language),
                  total: formatBytes(disk.totalBytes, language),
                })}
                detail={formatPercent(disk.usagePercent, language)}
                percentage={disk.usagePercent}
              />
            ) : (
              <HostFact
                icon={HardDrive}
                label={t("dashboard.host.storage.hostRoot")}
                value={t("dashboard.host.notMeasured")}
                detail={getDashboardHostStorageUnavailableDescription(host, t)}
                className="sm:col-span-2 xl:col-span-4"
              />
            )}
          </div>
        ) : (
          <div className="rounded-xl border border-dashed border-border bg-background/40 px-4 py-5">
            <p className="font-medium">{t("dashboard.host.unavailableTitle")}</p>
            <p className="mt-1 text-sm text-muted-foreground">
              {getDashboardHostUnavailableDescription(host, t)}
            </p>
          </div>
        )}

        {exposure && exposureMode && exposureState ? (
          <ControlPlaneAccessFact
            mode={exposureMode}
            state={exposureState}
            binding={exposureBinding ?? exposureState}
            tone={getExposureTone(exposure.state)}
          />
        ) : null}

        <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
          {host.observedAtUtc ? (
            <p className="text-xs text-muted-foreground">
              {t("dashboard.host.observedAt", {
                value: formatDateTime(host.observedAtUtc, language),
              })}
            </p>
          ) : (
            <span />
          )}

          <Button variant="outline" size="sm" asChild>
            <Link to="/diagnostics">
              <Wrench className="mr-2 h-4 w-4" />
              {t("dashboard.action.openDiagnostics")}
            </Link>
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}

function ControlPlaneAccessFact({
  mode,
  state,
  binding,
  tone,
}: {
  mode: string
  state: string
  binding: string
  tone: DashboardTone
}) {
  const { t } = useI18n()

  return (
    <div
      className={[
        "flex flex-col gap-3 rounded-xl border px-3 py-2 sm:flex-row sm:items-center sm:justify-between",
        tone === "danger"
          ? "border-red-500/25 bg-red-500/[0.05]"
          : tone === "warning"
            ? "border-amber-500/25 bg-amber-500/[0.05]"
            : "border-border bg-background/40",
      ].join(" ")}
      data-testid="dashboard-control-plane-access"
    >
      <div className="flex min-w-0 items-start gap-2">
        <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
        <div className="min-w-0">
          <div className="text-sm font-medium">{t("runtime.card.access")}</div>
          <p className="mt-0.5 text-sm font-semibold text-foreground">{mode}</p>
          <p className="mt-0.5 break-all text-xs text-muted-foreground">{binding}</p>
        </div>
      </div>
      <DashboardStatusPill tone={tone}>{state}</DashboardStatusPill>
    </div>
  )
}

function getExposureTone(state: string): DashboardTone {
  switch (state) {
    case "private":
      return "good"
    case "needs-attention":
      return "danger"
    case "unavailable":
      return "warning"
    default:
      return "neutral"
  }
}

function HostFact({
  icon: Icon,
  label,
  value,
  detail,
  className,
}: {
  icon: typeof Cpu
  label: string
  value: string
  detail?: string
  className?: string
}) {
  return (
    <div
      className={[
        "rounded-xl border border-border bg-background/40 px-3 py-2",
        className,
      ]
        .filter(Boolean)
        .join(" ")}
    >
      <div className="flex items-center gap-2 text-sm font-medium">
        <Icon className="h-4 w-4 shrink-0 text-muted-foreground" />
        <span>{label}</span>
      </div>
      <p className="mt-1.5 text-sm font-semibold text-foreground">{value}</p>
      {detail ? <p className="mt-1 text-xs text-muted-foreground">{detail}</p> : null}
    </div>
  )
}

function HostStorageMetric({
  label,
  value,
  detail,
  percentage,
}: {
  label: string
  value: string
  detail: string
  percentage: number
}) {
  return (
    <div className="rounded-xl border border-border bg-background/40 px-3 py-2 sm:col-span-2 xl:col-span-4">
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 items-center gap-2">
          <HardDrive className="h-4 w-4 shrink-0 text-muted-foreground" />
          <div className="min-w-0">
            <div className="text-sm font-medium">{label}</div>
            <p className="mt-1 text-xs text-muted-foreground">{value}</p>
          </div>
        </div>
        <span className="shrink-0 text-sm font-semibold">{detail}</span>
      </div>
      <div className="mt-2.5 h-1.5 overflow-hidden rounded-full bg-muted">
        <div
          aria-hidden="true"
          className="h-full rounded-full bg-teal-500/70"
          style={{ width: `${percentage}%` }}
        />
      </div>
    </div>
  )
}

function formatPercent(value: number, language: ReturnType<typeof useI18n>["language"]): string {
  return formatNumber(value / 100, language, {
    style: "percent",
    maximumFractionDigits: 0,
  })
}
