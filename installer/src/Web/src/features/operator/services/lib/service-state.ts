import type { I18nContextValue } from "@/app/i18n/i18n-context"

type Translate = I18nContextValue["t"]

export function localizeServiceState(
  t: Translate,
  state: string | null | undefined,
): string {
  switch (state?.trim().toLowerCase()) {
    case "running":
      return t("services.status.running")
    case "stopped":
    case "exited":
      return t("services.status.stopped")
    case "not deployed":
    case "not_deployed":
      return t("services.status.notDeployed")
    case "partially running":
    case "partially_running":
      return t("services.status.partiallyRunning")
    case "needs reconfigure":
    case "needs_reconfigure":
      return t("services.status.needsReconfigure")
    case "coming soon":
    case "coming_soon":
      return t("services.status.comingSoon")
    case "unknown":
      return t("services.status.unknown")
    default:
      return state?.trim() || t("services.status.unknown")
  }
}

export function localizeServiceRuntime(
  t: Translate,
  exists: boolean,
  running: boolean,
): string {
  if (running) {
    return t("services.status.running")
  }

  if (exists) {
    return t("services.status.stopped")
  }

  return t("services.status.notDeployed")
}
