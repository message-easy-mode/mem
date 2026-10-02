import { CircleAlert, RefreshCw } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import type { NpmReadinessResponse } from "@/features/shared/domains/api/domains.types"
import {
  getErrorMessage,
  ReadinessTile,
  StatusBadge,
} from "@/features/shared/domains/components/ingress-tls-shared"

export function NpmReadinessCard({
  isLoading,
  error,
  status,
  onRefresh,
}: {
  isLoading: boolean
  error: unknown
  status: NpmReadinessResponse | undefined
  onRefresh: () => void
}) {
  const { t } = useI18n()
  const ready = status?.recommendedAction === "ready"

  return (
    <Card>
      <CardHeader>
        <div className="flex items-start justify-between gap-4">
          <div>
            <CardTitle>{t("operatorDomains.certificates.npm.title")}</CardTitle>
            <CardDescription>
              {t("operatorDomains.certificates.npm.description")}
            </CardDescription>
          </div>

          <Button variant="outline" size="sm" onClick={onRefresh}>
            <RefreshCw className="mr-2 h-4 w-4" />
            {t("operatorDomains.common.refresh")}
          </Button>
        </div>
      </CardHeader>

      <CardContent className="space-y-4">
        {isLoading ? (
          <div className="text-sm text-muted-foreground">
            {t("operatorDomains.certificates.npm.checking")}
          </div>
        ) : null}

        {error ? (
          <Alert variant="destructive">
            <CircleAlert className="h-4 w-4" />
            <AlertTitle>{t("operatorDomains.certificates.npm.failed")}</AlertTitle>
            <AlertDescription>{getErrorMessage(error)}</AlertDescription>
          </Alert>
        ) : null}

        {status ? (
          <>
            <div className="flex flex-wrap items-center gap-2">
              <StatusBadge
                label={
                  ready
                    ? t("operatorDomains.certificates.npm.ready")
                    : t("operatorDomains.certificates.npm.needsAction")
                }
                ok={ready}
              />

              <Badge variant="outline">
                {t("operatorDomains.certificates.npm.recommendedAction", {
                  action: status.recommendedAction,
                })}
              </Badge>

              {status.baseUrl ? (
                <Badge variant="secondary">
                  {t("operatorDomains.certificates.npm.baseUrl", { url: status.baseUrl })}
                </Badge>
              ) : null}
            </div>

            <div className="grid gap-3 md:grid-cols-3 lg:grid-cols-6">
              <ReadinessTile
                label={t("operatorDomains.certificates.npm.container")}
                ok={status.containerRunning}
                value={status.containerRunning ? "OK" : t("operatorDomains.common.no")}
              />
              <ReadinessTile
                label={t("operatorDomains.certificates.npm.adminUi")}
                ok={status.adminUiReachable}
                value={status.adminUiReachable ? "OK" : t("operatorDomains.common.no")}
              />
              <ReadinessTile
                label={t("operatorDomains.certificates.npm.initialized")}
                ok={status.initialized}
                value={status.initialized ? "OK" : t("operatorDomains.common.no")}
              />
              <ReadinessTile
                label={t("operatorDomains.certificates.npm.apiAuth")}
                ok={status.apiAuthenticated}
                value={status.apiAuthenticated ? "OK" : t("operatorDomains.common.no")}
              />
              <ReadinessTile
                label={t("operatorDomains.certificates.npm.certificateApi")}
                ok={status.certificateApiReachable}
                value={status.certificateApiReachable ? "OK" : t("operatorDomains.common.no")}
              />
              <ReadinessTile
                label={t("operatorDomains.certificates.npm.certificates")}
                ok={status.certificateCount >= 0}
                value={String(status.certificateCount)}
              />
            </div>

            {status.warnings.length > 0 ? (
              <Alert>
                <CircleAlert className="h-4 w-4" />
                <AlertTitle>{t("operatorDomains.certificates.npm.warnings")}</AlertTitle>
                <AlertDescription>
                  <ul className="list-disc space-y-1 pl-5">
                    {status.warnings.map((warning) => (
                      <li key={warning}>{warning}</li>
                    ))}
                  </ul>
                </AlertDescription>
              </Alert>
            ) : null}
          </>
        ) : null}
      </CardContent>
    </Card>
  )
}
