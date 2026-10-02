import { CircleAlert, Crown, RefreshCw } from "lucide-react"

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
import type { StoredCertificateMetadata } from "@/features/shared/domains/api/domains.types"

export function CertificateListCard({
  certificates,
  selectedCertificateId,
  onSelectCertificate,
  isLoading,
  error,
  onRefresh,
}: {
  certificates: StoredCertificateMetadata[]
  selectedCertificateId: string | null
  onSelectCertificate: (certificateId: string) => void
  isLoading: boolean
  error: unknown
  onRefresh: () => void
}) {
  const { intlLocale, t } = useI18n()

  return (
    <Card>
      <CardHeader>
        <div className="flex items-start justify-between gap-4">
          <div>
            <CardTitle>{t("operatorDomains.certificates.list.title")}</CardTitle>
            <CardDescription>
              {t("operatorDomains.certificates.list.description")}
            </CardDescription>
          </div>

          <Button variant="outline" size="sm" onClick={onRefresh}>
            <RefreshCw className="mr-2 h-4 w-4" />
            {t("operatorDomains.common.refresh")}
          </Button>
        </div>
      </CardHeader>

      <CardContent className="space-y-3">
        {isLoading ? (
          <div className="text-sm text-muted-foreground">
            {t("operatorDomains.certificates.list.loading")}
          </div>
        ) : null}

        {error ? (
          <Alert variant="destructive">
            <CircleAlert className="h-4 w-4" />
            <AlertTitle>{t("operatorDomains.certificates.list.failed")}</AlertTitle>
            <AlertDescription>
              {error instanceof Error ? error.message : String(error)}
            </AlertDescription>
          </Alert>
        ) : null}

        {!isLoading && certificates.length === 0 ? (
          <div className="rounded-lg border border-dashed border-border p-4 text-sm text-muted-foreground">
            {t("operatorDomains.certificates.list.empty")}
          </div>
        ) : null}

        <div className="grid gap-3">
          {certificates.map((certificate) => {
            const selected = certificate.certificateId === selectedCertificateId
            const environment = certificate.isStaging
              ? t("operatorDomains.common.staging")
              : t("operatorDomains.common.production")

            return (
              <button
                key={certificate.certificateId}
                type="button"
                aria-pressed={selected}
                aria-label={t("operatorDomains.certificates.list.ariaLabel", {
                  domain: certificate.domain,
                  environment,
                  certificateId: certificate.certificateId,
                  main: certificate.isMainPlatformCertificate
                    ? t("operatorDomains.certificates.list.ariaMainSuffix")
                    : "",
                  inUse: certificate.isInUse
                    ? t("operatorDomains.certificates.list.ariaInUseSuffix")
                    : "",
                })}
                onClick={() => onSelectCertificate(certificate.certificateId)}
                className={[
                  "rounded-xl border p-4 text-left transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/50",
                  selected
                    ? "border-sky-400/60 bg-sky-500/5 ring-1 ring-sky-400/20"
                    : certificate.isMainPlatformCertificate
                      ? "border-emerald-500/30 bg-emerald-500/5 hover:bg-emerald-500/10"
                      : "border-border bg-background hover:bg-muted/50",
                ].join(" ")}
              >
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="font-medium">{certificate.domain}</div>

                  <div className="flex flex-wrap gap-2">
                    {certificate.isMainPlatformCertificate ? (
                      <Badge>
                        <Crown data-icon="inline-start" />
                        {t("operatorDomains.certificates.list.mainPlatform")}
                      </Badge>
                    ) : null}
                    {certificate.isInUse ? (
                      <Badge className="bg-emerald-500 text-emerald-950 hover:bg-emerald-500">
                        {t("operatorDomains.certificates.list.inUseForDomain", {
                          domain: certificateBaseDomain(certificate.domain),
                        })}
                      </Badge>
                    ) : null}
                    {selected ? (
                      <Badge
                        variant="outline"
                        className="border-sky-400/40 bg-sky-500/10 text-sky-700 dark:text-sky-300"
                      >
                        {t("operatorDomains.certificates.list.selectedForTools")}
                      </Badge>
                    ) : null}
                    <Badge variant={certificate.isStaging ? "outline" : "secondary"}>
                      {environment}
                    </Badge>
                  </div>
                </div>

                <div className="mt-3 grid gap-2 text-xs text-muted-foreground sm:grid-cols-2">
                  <div>
                    <span className="font-medium text-foreground/80">
                      {t("operatorDomains.common.dnsZone")}:
                    </span>{" "}
                    {certificate.zone}
                  </div>
                  <div>
                    <span className="font-medium text-foreground/80">
                      {t("operatorDomains.common.expires")}:
                    </span>{" "}
                    {certificate.expiresAtUtc
                      ? new Date(certificate.expiresAtUtc).toLocaleString(intlLocale)
                      : t("operatorDomains.common.unknown")}
                  </div>
                </div>
              </button>
            )
          })}
        </div>
      </CardContent>
    </Card>
  )
}

function certificateBaseDomain(value: string) {
  return value.trim().replace(/^\*\./, "")
}
