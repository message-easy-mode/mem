import { useI18n } from "@/app/i18n/i18n-context"
import type { InstallationWorkflowStatusDto } from "../hooks/use-install-workflow"

export function InstallBanner({
  workflow,
  lastRunMessage,
  isContinuePending,
}: {
  workflow: InstallationWorkflowStatusDto | undefined
  lastRunMessage: string | undefined
  isContinuePending: boolean
}) {
  const { t } = useI18n()
  const status = workflow?.installationStatus

  if (status === "Succeeded") {
    return (
      <div className="rounded-xl border border-emerald-500/30 bg-emerald-500/10 p-4">
        <div className="font-medium text-emerald-100">{t("setup.activity.successTitle")}</div>
        <div className="mt-1 text-sm text-muted-foreground">{t("setup.activity.successDescription")}</div>
      </div>
    )
  }

  if (status === "Failed") {
    return (
      <div className="rounded-xl border border-red-500/30 bg-red-500/10 p-4">
        <div className="font-medium text-red-100">{t("setup.activity.failedTitle")}</div>
        <div className="mt-1 text-sm text-muted-foreground">{t("setup.activity.failedDescription")}</div>
      </div>
    )
  }

  if (isContinuePending) {
    return (
      <div className="rounded-xl border border-sky-500/30 bg-sky-500/10 p-4">
        <div className="font-medium text-sky-100">{t("setup.activity.applyingTitle")}</div>
        <div className="mt-1 text-sm text-muted-foreground">{t("setup.activity.resuming")}</div>
      </div>
    )
  }

  if (status === "Running") {
    return (
      <div className="rounded-xl border border-sky-500/30 bg-sky-500/10 p-4">
        <div className="font-medium text-sky-100">{t("setup.activity.runningTitle")}</div>
        <div className="mt-1 text-sm text-muted-foreground">{t("setup.activity.runningDescription")}</div>
      </div>
    )
  }

  if (status === "WaitingForUser") {
    return (
      <div className="rounded-xl border border-amber-500/30 bg-amber-500/10 p-4">
        <div className="font-medium text-amber-100">{t("setup.activity.pausedTitle")}</div>
        <div className="mt-1 text-sm text-muted-foreground">{t("setup.activity.pausedDescription")}</div>
      </div>
    )
  }

  if (lastRunMessage) {
    return (
      <div className="rounded-xl border border-border bg-muted/30 p-4">
        <div className="font-medium">{t("setup.activity.lastAction")}</div>
        <div className="mt-1 text-sm text-muted-foreground">{lastRunMessage}</div>
      </div>
    )
  }

  return null
}
