import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import type { InstallationWorkflowStatusDto } from "../hooks/use-install-workflow"

export function InstallActionsBar({
  workflow,
  onContinue,
  onRefresh,
  isContinuePending,
}: {
  workflow: InstallationWorkflowStatusDto | undefined
  onContinue: () => void
  onRefresh: () => void
  isContinuePending: boolean
}) {
  const { t } = useI18n()
  const status = workflow?.installationStatus

  return (
    <div className="flex flex-wrap gap-3">
      <Button variant="outline" onClick={onRefresh}>
        {t("setup.activity.refresh")}
      </Button>

      {(status === "WaitingForUser" ||
        status === "Failed" ||
        status === "Draft" ||
        status === "Ready") && (
        <Button onClick={onContinue} disabled={isContinuePending}>
          {status === "WaitingForUser"
            ? t("setup.activity.resume")
            : status === "Failed"
              ? t("setup.activity.retry")
              : t("setup.activity.apply")}
        </Button>
      )}
    </div>
  )
}
