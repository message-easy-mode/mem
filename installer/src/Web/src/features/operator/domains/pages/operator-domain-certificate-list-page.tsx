import { useEffect } from "react"
import { Link, useParams } from "react-router-dom"
import { ArrowLeft, Crown, ShieldCheck } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { CertificateIssuancePlaceholder } from "@/features/shared/domains/components/certificate-issuance-placeholder"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import type { StoredCertificateMetadata } from "@/features/shared/domains/api/domains.types"
import {
  useLatestOperatorDomainCertificateIssuance,
  useOperatorDomain,
  useOperatorDomainCertificates,
} from "@/features/shared/domains/hooks/use-domains"
import { getErrorMessage } from "@/features/shared/domains/components/ingress-tls-shared"

export function OperatorDomainCertificateListPage() {
  const { domainId } = useParams()
  const { intlLocale, t } = useI18n()
  const domainQuery = useOperatorDomain(domainId)
  const certificatesQuery = useOperatorDomainCertificates(domainId)
  const issuanceQuery = useLatestOperatorDomainCertificateIssuance(domainId)
  const domain = domainQuery.data
  const issuance = issuanceQuery.data
  const activeIssuance = issuance && isActiveIssuanceStatus(issuance.status) ? issuance : null
  const refetchCertificates = certificatesQuery.refetch

  useEffect(() => {
    if (issuance?.isTerminal) {
      void refetchCertificates()
    }
  }, [issuance?.isTerminal, issuance?.operationId, refetchCertificates])

  if (domainQuery.isLoading) {
    return <StateCard message={t("operatorDomains.detail.loading")} />
  }

  if (domainQuery.error || !domain) {
    return (
      <StateCard
        message={domainQuery.error
          ? t("operatorDomains.detail.loadFailed", { error: getErrorMessage(domainQuery.error) })
          : t("operatorDomains.detail.notFound")}
      />
    )
  }

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <PageBreadcrumbs
            items={[
              { label: t("operatorDomains.common.domains"), to: "/domains" },
              { label: domain.baseDomain, to: `/domains/${domain.id}` },
              { label: t("operatorDomains.domainCertificates.title") },
            ]}
          />
          <h1 className="text-2xl font-semibold tracking-tight">
            {t("operatorDomains.domainCertificates.domainTitle", { domain: domain.baseDomain })}
          </h1>
          <p className="max-w-3xl text-sm text-muted-foreground">
            {t("operatorDomains.domainCertificates.description")}
          </p>
        </div>

        <div className="flex flex-col gap-2 sm:flex-row">
          <Button asChild>
            <Link to={`/domains/${domain.id}/certificates/new`}>
              <ShieldCheck className="mr-2 h-4 w-4" />
              {t("operatorDomains.inventory.issueCertificate")}
            </Link>
          </Button>
          <Button asChild variant="outline">
            <Link to={`/domains/${domain.id}`}>
              <ArrowLeft className="mr-2 h-4 w-4" />
              {t("operatorDomains.domainCertificates.backToDomain")}
            </Link>
          </Button>
        </div>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>{t("operatorDomains.domainCertificates.listTitle")}</CardTitle>
          <CardDescription>
            {t("operatorDomains.domainCertificates.listDescription")}
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          {issuanceQuery.error ? (
            <div className="rounded-lg border border-amber-500/30 bg-amber-500/5 p-4 text-sm text-amber-700 dark:text-amber-300">
              {t("operatorDomains.certificates.issue.progressReadFailed", {
                error: getErrorMessage(issuanceQuery.error),
              })}
            </div>
          ) : null}
          {activeIssuance ? (
            <CertificateIssuancePlaceholder
              operation={{ ...activeIssuance, baseDomain: domain.baseDomain }}
              intlLocale={intlLocale}
            />
          ) : null}
          {certificatesQuery.isLoading ? (
            <div className="text-sm text-muted-foreground">
              {t("operatorDomains.inventory.loading")}
            </div>
          ) : null}
          {certificatesQuery.error ? (
            <div className="rounded-lg border border-red-500/30 bg-red-500/5 p-4 text-sm text-red-600 dark:text-red-300">
              {t("operatorDomains.inventory.loadFailed", { error: getErrorMessage(certificatesQuery.error) })}
            </div>
          ) : null}
          {!certificatesQuery.isLoading &&
          !issuanceQuery.isLoading &&
          !activeIssuance &&
          (certificatesQuery.data?.length ?? 0) === 0 ? (
            <div className="rounded-lg border border-dashed border-border p-5 text-sm text-muted-foreground">
              {t("operatorDomains.domainCertificates.empty")}
            </div>
          ) : null}

          {certificatesQuery.data?.map((certificate) => (
            <DomainCertificateRow
              key={certificate.certificateId}
              certificate={certificate}
              intlLocale={intlLocale}
            />
          ))}
        </CardContent>
      </Card>
    </div>
  )
}

function DomainCertificateRow({
  certificate,
  intlLocale,
}: {
  certificate: StoredCertificateMetadata
  intlLocale: string
}) {
  const { t } = useI18n()

  return (
    <Link
      to={`/domains/${certificate.domainId}/certificates/${encodeURIComponent(certificate.certificateId)}`}
      className="block rounded-xl border border-border bg-background/40 p-4 transition hover:bg-muted/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/50"
    >
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <div className="font-medium">{certificate.commonName}</div>
            {certificate.isInUse ? (
              <Badge>{t("operatorDomains.inventory.activeForDomain")}</Badge>
            ) : null}
            {certificate.isMainPlatformCertificate ? (
              <Badge variant="secondary">
                <Crown className="mr-1 h-3.5 w-3.5" />
                {t("operatorDomains.common.mainPlatformCertificate")}
              </Badge>
            ) : null}
            <Badge variant={certificate.isStaging ? "outline" : "secondary"}>
              {certificate.isStaging
                ? t("operatorDomains.common.staging")
                : t("operatorDomains.common.production")}
            </Badge>
          </div>
          <div className="mt-1 break-all text-xs text-muted-foreground">
            {certificate.certificateId}
          </div>
        </div>
        <div className="text-sm text-muted-foreground">
          {certificate.expiresAtUtc
            ? t("operatorDomains.detail.certificateExpires", {
                date: new Date(certificate.expiresAtUtc).toLocaleString(intlLocale),
              })
            : t("operatorDomains.common.expiryUnknown")}
        </div>
      </div>
    </Link>
  )
}

function isActiveIssuanceStatus(status: string) {
  const normalized = status.toLowerCase()
  return normalized === "queued" || normalized === "running"
}

function StateCard({ message }: { message: string }) {
  return (
    <Card>
      <CardContent className="p-6 text-sm text-muted-foreground">{message}</CardContent>
    </Card>
  )
}
