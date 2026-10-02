import { Navigate } from "react-router-dom"
import { AlertTriangle, Loader2 } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { usePlatformStatus } from "@/features/setup/start/hooks/use-platform-status"
import { getStartupResumePath } from "@/features/setup/start/api/platform-status"

export function StartupResolverPage() {
  const { t } = useI18n()
  const platformStatus = usePlatformStatus()

  if (platformStatus.isLoading) {
    return (
      <StartupFrame>
        <Card>
          <CardContent className="flex items-center gap-3 p-6 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" />
            {t("startup.checking")}
          </CardContent>
        </Card>
      </StartupFrame>
    )
  }

  const target = platformStatus.data?.startupTarget

  if (target === "dashboard") {
    return <Navigate to="/dashboard" replace />
  }

  if (target === "setup-start") {
    return <Navigate to="/setup/start" replace />
  }

  if (target === "resume-installation" && platformStatus.data) {
    const resumePath = getStartupResumePath(platformStatus.data)
    if (resumePath) {
      return <Navigate to={resumePath} replace />
    }
  }

  return (
    <StartupFrame>
      <Alert variant="destructive">
        <AlertTriangle className="h-4 w-4" />
        <AlertTitle>{t("startup.unavailableTitle")}</AlertTitle>
        <AlertDescription className="space-y-4">
          <p>{t("startup.unavailableDescription")}</p>
          <Button
            type="button"
            variant="outline"
            onClick={() => void platformStatus.refetch()}
            disabled={platformStatus.isFetching}
          >
            {platformStatus.isFetching ? t("startup.retrying") : t("startup.retry")}
          </Button>
        </AlertDescription>
      </Alert>
    </StartupFrame>
  )
}

function StartupFrame({ children }: { children: React.ReactNode }) {
  return (
    <main className="flex min-h-screen items-center justify-center bg-background px-6 text-foreground">
      <div className="w-full max-w-2xl">{children}</div>
    </main>
  )
}
