import { ArrowLeft, CheckCircle2 } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { usePlatformStatus } from "@/features/setup/start/hooks/use-platform-status"

export function DiagnosticsSetupFinishBanner() {
  const { t } = useI18n()
  const platformStatus = usePlatformStatus()
  const installationId = platformStatus.data?.activeInstallationId?.trim()
  const finishPending =
    platformStatus.data?.recommendedAction === "complete-handoff" &&
    platformStatus.data?.activeInstallationStage === "handoff" &&
    Boolean(installationId)

  if (!finishPending || !installationId) {
    return null
  }

  return (
    <Alert
      className="mb-6 border-primary/30 bg-primary/5"
      data-testid="diagnostics-setup-finish-banner"
    >
      <CheckCircle2 className="h-4 w-4 text-primary" />
      <AlertTitle>{t("setup.finish.diagnosticsReturnTitle")}</AlertTitle>
      <AlertDescription className="mt-1 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <span>{t("setup.finish.diagnosticsReturnDescription")}</span>
        <Button asChild variant="outline" size="sm" className="shrink-0">
          <Link to={`/setup/handoff/${encodeURIComponent(installationId)}`}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            {t("setup.finish.diagnosticsReturnAction")}
          </Link>
        </Button>
      </AlertDescription>
    </Alert>
  )
}
