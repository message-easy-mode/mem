import {
  Activity,
  BellRing,
  CircleAlert,
  Database,
  ScrollText,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { cn } from "@/lib/utils"
import { Badge } from "@/components/ui/badge"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import type { DiagnosticsOverviewResponse } from "../api/diagnostics.types"

type HealthTone = "ready" | "attention" | "degraded" | "neutral"

type HealthFact = Readonly<{
  key: string
  label: string
  value: string
  detail: string
  tone: HealthTone
  icon?: React.ComponentType<{ className?: string }>
  iconClassName?: string
  brandIconSrc?: string
}>

export function DiagnosticsHealthStrip({ data }: { data: DiagnosticsOverviewResponse }) {
  const { t } = useI18n()
  const attentionCount = data.counts.incidentCount
  const recorder = data.loggingHealth.localRecorder
  const store = data.loggingHealth.safeEventStore
  const seq = data.loggingHealth.seq

  const facts: readonly HealthFact[] = [
    {
      key: "overall",
      label: t("diagnostics.command.health.overall"),
      value: statusLabel(data.status, t),
      detail: data.partial
        ? t("diagnostics.command.health.overallPartial")
        : data.status === "ready"
          ? t("diagnostics.command.health.overallReady")
          : t("diagnostics.command.health.overallNeedsReview"),
      tone: data.status === "ready" && !data.partial
        ? "ready"
        : data.status === "attention"
          ? "attention"
          : "degraded",
      icon: Activity,
      iconClassName: "text-cyan-400",
    },
    {
      key: "attention",
      label: t("diagnostics.command.health.attention"),
      value: String(attentionCount),
      detail: t("diagnostics.command.health.attentionCount", { count: attentionCount }),
      tone: attentionCount === 0 ? "ready" : "attention",
      icon: BellRing,
      iconClassName: "text-rose-400",
    },
    {
      key: "recorder",
      label: t("diagnostics.command.health.recorder"),
      value: statusLabel(recorder.status, t),
      detail: !recorder.enabled || !recorder.persistentRecorderConfigured
        ? t("diagnostics.command.health.recorderDisabled")
        : recorder.persistentRecorderActive && recorder.status === "ready"
          ? t("diagnostics.command.health.recorderReady")
          : t("diagnostics.command.health.recorderDegraded"),
      tone: !recorder.enabled || !recorder.persistentRecorderConfigured
        ? "neutral"
        : recorder.persistentRecorderActive && recorder.status === "ready"
          ? "ready"
          : "degraded",
      icon: ScrollText,
      iconClassName: "text-violet-400",
    },
    {
      key: "safe-store",
      label: t("diagnostics.command.health.safeStore"),
      value: statusLabel(store.enabled ? store.status : "disabled", t),
      detail: !store.enabled
        ? t("diagnostics.command.health.safeStoreDisabled")
        : store.status !== "ready"
          ? t("diagnostics.command.health.safeStoreDegraded")
          : store.hasEverRecordedEvent
            ? t("diagnostics.command.health.safeStoreReady")
            : t("diagnostics.command.health.safeStoreEmpty"),
      tone: !store.enabled
        ? "neutral"
        : store.status === "ready"
          ? "ready"
          : "degraded",
      icon: Database,
      iconClassName: "text-teal-400",
    },
    {
      key: "seq",
      label: "Seq",
      value: seqLabel(seq, t),
      detail: !seq.configured && !seq.sinkEnabled
        ? t("diagnostics.command.health.seqOptional")
        : seq.status === "ready" && seq.reachable
          ? t("diagnostics.command.health.seqReady")
          : t("diagnostics.command.health.seqDegraded"),
      tone: !seq.configured && !seq.sinkEnabled
        ? "neutral"
        : seq.status === "ready" && seq.reachable
          ? "ready"
          : "degraded",
      brandIconSrc: "/brands/seq-mark.svg",
    },
  ]

  return (
    <Card aria-labelledby="diagnostics-health-strip-title">
      <CardHeader>
        <CardTitle id="diagnostics-health-strip-title" className="text-base">
          {t("diagnostics.command.health.title")}
        </CardTitle>
        <CardDescription>{t("diagnostics.command.health.description")}</CardDescription>
      </CardHeader>
      <CardContent>
        <dl className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
          {facts.map((fact) => (
            <HealthFactView key={fact.key} fact={fact} />
          ))}
        </dl>
      </CardContent>
    </Card>
  )
}

const healthToneBadgeClasses: Record<HealthTone, string> = {
  ready: "border-emerald-500/30 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300",
  attention: "",
  degraded: "border-amber-500/30 bg-amber-500/10 text-amber-700 dark:text-amber-300",
  neutral: "border-border/80 bg-muted/30 text-muted-foreground",
}

function HealthFactView({ fact }: { fact: HealthFact }) {
  const Icon = fact.icon
  const toneClass = fact.tone === "ready"
    ? "border-emerald-500/25 bg-emerald-500/5"
    : fact.tone === "attention"
      ? "border-red-500/25 bg-red-500/5"
      : fact.tone === "degraded"
        ? "border-amber-500/25 bg-amber-500/5"
        : "border-border bg-muted/20"

  return (
    <div className={`min-w-0 rounded-xl border p-3 ${toneClass}`}>
      <dt className="flex items-center justify-between gap-2 text-xs text-muted-foreground">
        <span className="flex min-w-0 items-center gap-2">
          {fact.brandIconSrc ? (
            <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-md border border-orange-400/20 bg-slate-950 p-0.5">
              <img
                src={fact.brandIconSrc}
                alt=""
                aria-hidden="true"
                className="h-4 w-4 object-contain"
              />
            </span>
          ) : Icon ? (
            <Icon className={cn("h-3.5 w-3.5 shrink-0", fact.iconClassName)} />
          ) : null}
          <span className="truncate">{fact.label}</span>
        </span>
        {fact.tone === "attention" ? <CircleAlert className="h-3.5 w-3.5 text-red-500" /> : null}
      </dt>
      <dd className="mt-2">
        <Badge
          className={healthToneBadgeClasses[fact.tone]}
          variant={fact.tone === "attention" ? "destructive" : "outline"}
          data-health-tone={fact.tone}
        >
          {fact.value}
        </Badge>
        <p className="mt-2 text-xs leading-5 text-muted-foreground">{fact.detail}</p>
      </dd>
    </div>
  )
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
    case "disabled":
      return t("diagnostics.command.status.disabled")
    case "running":
      return t("diagnostics.command.status.running")
    default:
      return status
  }
}

function seqLabel(
  seq: DiagnosticsOverviewResponse["loggingHealth"]["seq"],
  t: ReturnType<typeof useI18n>["t"],
): string {
  if (!seq.configured && !seq.sinkEnabled) {
    return t("diagnostics.command.status.notConfigured")
  }

  return statusLabel(seq.status, t)
}
