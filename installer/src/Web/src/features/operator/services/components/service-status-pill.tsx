import { useI18n } from "@/app/i18n/i18n-context"
import { localizeServiceState } from "@/features/operator/services/lib/service-state"
import type { ManagedServiceStatusTone } from "@/features/operator/services/api/runtime-ui.types"

type Props = {
  exists: boolean
  running: boolean
  state?: string | null
  supported?: boolean
  statusLabel?: string
  statusTone?: ManagedServiceStatusTone
}

export function ServiceStatusPill({
  exists,
  running,
  state,
  supported = true,
  statusLabel,
  statusTone = "neutral",
}: Props) {
  const { t } = useI18n()

  if (statusLabel) {
    const toneClass = statusTone === "positive"
      ? "border-emerald-500/20 bg-emerald-500/10 text-emerald-300"
      : statusTone === "warning"
        ? "border-amber-500/20 bg-amber-500/10 text-amber-300"
        : statusTone === "danger"
          ? "border-red-500/20 bg-red-500/10 text-red-300"
          : "border-border bg-background text-muted-foreground"

    return (
      <span className={`inline-flex rounded-full border px-2.5 py-1 text-xs font-medium ${toneClass}`}>
        {statusLabel}
      </span>
    )
  }

  if (!supported) {
    return (
      <span className="inline-flex rounded-full border border-border bg-background px-2.5 py-1 text-xs font-medium text-muted-foreground">
        {t("services.status.comingSoon")}
      </span>
    )
  }

  if (running) {
    return (
      <span className="inline-flex rounded-full border border-emerald-500/20 bg-emerald-500/10 px-2.5 py-1 text-xs font-medium text-emerald-300">
        {t("services.status.running")}
      </span>
    )
  }

  if (exists) {
    return (
      <span className="inline-flex rounded-full border border-amber-500/20 bg-amber-500/10 px-2.5 py-1 text-xs font-medium text-amber-300">
        {localizeServiceState(t, state ?? t("services.status.stopped"))}
      </span>
    )
  }

  return (
    <span className="inline-flex rounded-full border border-border bg-background px-2.5 py-1 text-xs font-medium text-muted-foreground">
      {t("services.status.notDeployed")}
    </span>
  )
}
