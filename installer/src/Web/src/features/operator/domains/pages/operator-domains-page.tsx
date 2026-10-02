// src/features/operator/domains/pages/operator-domains-page.tsx

import { Link } from "react-router-dom"
import { AlertTriangle, Crown, ExternalLink, Plus, RefreshCw } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import type {
  OperatorDomainRenewalStatus,
  OperatorDomainSummary,
} from "@/features/shared/domains/api/domains.types"
import {
  renewalBadgeVariant,
  renewalOperationalLabel,
} from "@/features/operator/domains/lib/domain-renewal-display"
import {
  useOperatorDomainRenewalStatuses,
  useOperatorDomains,
} from "@/features/shared/domains/hooks/use-domains"

export function OperatorDomainsPage() {
  const { intlLocale, t } = useI18n()
  const domainsQuery = useOperatorDomains()
  const renewalQuery = useOperatorDomainRenewalStatuses()
  const domains = domainsQuery.data ?? []
  const renewalByDomain = new Map(
    (renewalQuery.data ?? []).map((item) => [item.domainId, item] as const),
  )
  const mainDomain = domains.find((domain) => domain.isMainPlatformDomain) ?? null

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <h1 className="text-2xl font-semibold tracking-tight">
            {t("operatorDomains.list.title")}
          </h1>
          <p className="max-w-3xl text-sm text-muted-foreground">
            {t("operatorDomains.list.description")}
          </p>
        </div>

        <div className="flex flex-col gap-2 sm:flex-row">
          <Button variant="outline" onClick={() => void domainsQuery.refetch()}>
            <RefreshCw className="mr-2 h-4 w-4" />
            {t("operatorDomains.common.refresh")}
          </Button>

          <Button asChild variant="outline">
            <Link to="/domains/certificates">
              {t("operatorDomains.common.certificates")}
            </Link>
          </Button>

          <Button asChild>
            <Link to="/domains/new">
              <Plus className="mr-2 h-4 w-4" />
              {t("operatorDomains.common.addDomain")}
            </Link>
          </Button>
        </div>
      </div>

      {!mainDomain ? (
        <Card className="border-amber-500/30 bg-amber-500/5">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <AlertTriangle className="h-5 w-5 text-amber-300" />
              {t("operatorDomains.list.noMainTitle")}
            </CardTitle>
            <CardDescription>
              {t("operatorDomains.list.noMainDescription")}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <Button asChild>
              <Link to="/domains/new">{t("operatorDomains.list.addMain")}</Link>
            </Button>
          </CardContent>
        </Card>
      ) : (
        <MainDomainCard domain={mainDomain} intlLocale={intlLocale} />
      )}

      <Card>
        <CardHeader>
          <CardTitle>{t("operatorDomains.list.registryTitle")}</CardTitle>
          <CardDescription>{t("operatorDomains.list.registryDescription")}</CardDescription>
        </CardHeader>

        <CardContent>
          {domainsQuery.isLoading ? (
            <div className="text-sm text-muted-foreground">
              {t("operatorDomains.list.loading")}
            </div>
          ) : null}

          {domainsQuery.error ? (
            <div className="rounded-xl border border-red-500/30 bg-red-500/5 p-4 text-sm text-red-200">
              {t("operatorDomains.list.loadFailed", {
                error: String(domainsQuery.error),
              })}
            </div>
          ) : null}

          {!domainsQuery.isLoading && !domainsQuery.error && domains.length === 0 ? (
            <EmptyDomainsState />
          ) : null}

          {domains.length > 0 ? (
            <div
              className="overflow-hidden rounded-xl border border-border"
              data-testid="domain-registry"
            >
              <Table className="min-w-[960px]">
                <TableHeader>
                  <TableRow className="hover:bg-transparent">
                    <TableHead className="min-w-56">
                      {t("operatorDomains.common.domain")}
                    </TableHead>
                    <TableHead className="min-w-52">
                      {t("operatorDomains.common.certificate")}
                    </TableHead>
                    <TableHead className="min-w-44">
                      {t("operatorDomains.common.readiness")}
                    </TableHead>
                    <TableHead className="min-w-44">
                      {t("operatorDomains.workspace.renewal.title")}
                    </TableHead>
                    <TableHead className="sticky right-0 z-30 min-w-40 border-l border-border bg-card text-right shadow-[-8px_0_12px_-10px_rgba(0,0,0,0.9)]">
                      {t("operatorDomains.common.actions")}
                    </TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {domains.map((domain) => (
                    <DomainRow
                      key={domain.id}
                      domain={domain}
                      renewal={renewalByDomain.get(domain.id)}
                      intlLocale={intlLocale}
                    />
                  ))}
                </TableBody>
              </Table>
            </div>
          ) : null}
        </CardContent>
      </Card>
    </div>
  )
}

function MainDomainCard({
  domain,
  intlLocale,
}: {
  domain: OperatorDomainSummary
  intlLocale: string
}) {
  const { t } = useI18n()

  return (
    <Card className="border-emerald-500/30 bg-emerald-500/5">
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Crown className="h-5 w-5 text-emerald-300" />
          {t("operatorDomains.list.mainTitle")}
        </CardTitle>
        <CardDescription>{t("operatorDomains.list.mainDescription")}</CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4 md:grid-cols-3">
        <InfoTile label={t("operatorDomains.common.domain")} value={domain.baseDomain} />
        <InfoTile
          label={t("operatorDomains.common.certificate")}
          value={
            domain.activeCertificateCommonName
              ? `${domain.activeCertificateCommonName} · ${certificateEnvironmentLabel(domain, t)}`
              : t("operatorDomains.list.noSelectedCertificate")
          }
        />
        <InfoTile
          label={t("operatorDomains.common.expires")}
          value={
            domain.activeCertificateExpiresAtUtc
              ? formatDate(domain.activeCertificateExpiresAtUtc, intlLocale)
              : t("operatorDomains.common.unknown")
          }
        />
      </CardContent>
    </Card>
  )
}

function DomainRow({
  domain,
  renewal,
  intlLocale,
}: {
  domain: OperatorDomainSummary
  renewal: OperatorDomainRenewalStatus | undefined
  intlLocale: string
}) {
  const { t } = useI18n()
  const certificateLabel =
    domain.activeCertificateCommonName ?? t("operatorDomains.list.noCertificate")
  const certificateVariant = domain.activeCertificateCommonName ? "secondary" : "outline"
  const readyForMain = canBecomeMain(domain)

  return (
    <TableRow data-testid={`domain-row-${domain.id}`}>
      <TableCell className="min-w-56 align-top">
        <div className="flex flex-wrap items-center gap-2 font-medium">
          <Link
            to={`/domains/${domain.id}`}
            className="truncate text-emerald-400 transition-colors hover:text-emerald-300 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
          >
            {domain.baseDomain}
          </Link>
          {domain.isMainPlatformDomain ? (
            <Badge>{t("operatorDomains.common.main")}</Badge>
          ) : null}
        </div>
        <div className="mt-1 break-words text-xs text-muted-foreground">
          {t("operatorDomains.common.dns")}: {displayProvider(domain.dnsProvider)}
          {domain.dnsZone ? ` / ${domain.dnsZone}` : ""}
        </div>
      </TableCell>

      <TableCell className="min-w-52 align-top">
        <Badge className="max-w-full truncate" variant={certificateVariant}>
          {certificateLabel}
        </Badge>
        <div className="mt-1 text-xs text-muted-foreground">
          {certificateEnvironmentLabel(domain, t)}
          {domain.activeCertificateExpiresAtUtc
            ? ` · ${t("operatorDomains.common.expires")} ${formatDate(domain.activeCertificateExpiresAtUtc, intlLocale)}`
            : ""}
        </div>
      </TableCell>

      <TableCell className="min-w-44 align-top">
        <Badge variant={domain.isMainPlatformDomain || readyForMain ? "default" : "outline"}>
          {domainReadinessLabel(domain, t)}
        </Badge>
      </TableCell>

      <TableCell className="min-w-44 align-top">
        <Link
          to={`/domains/${domain.id}/renewal`}
          className="inline-flex rounded-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          <Badge variant={renewalBadgeVariant(renewal)}>
            {renewalOperationalLabel(renewal, t)}
          </Badge>
        </Link>
        {renewal?.nextAutomaticAttemptAtUtc ? (
          <div className="mt-1 text-xs text-muted-foreground">
            {t("operatorDomains.workspace.renewal.nextAttempt")}: {formatDate(renewal.nextAutomaticAttemptAtUtc, intlLocale)}
          </div>
        ) : null}
      </TableCell>

      <TableCell
        className="sticky right-0 z-20 min-w-40 border-l border-border bg-card align-top shadow-[-8px_0_12px_-10px_rgba(0,0,0,0.9)]"
        data-testid={`domain-actions-${domain.id}`}
      >
        <div className="flex items-center justify-end whitespace-nowrap">
          <Button asChild variant="outline" size="sm">
            <Link to={`/domains/${domain.id}`}>
              {t("operatorDomains.common.manage")}
              <ExternalLink className="ml-2 h-3.5 w-3.5" />
            </Link>
          </Button>
        </div>
      </TableCell>
    </TableRow>
  )
}

function EmptyDomainsState() {
  const { t } = useI18n()

  return (
    <div className="rounded-xl border border-dashed border-border p-8 text-center">
      <div className="text-lg font-medium">{t("operatorDomains.list.emptyTitle")}</div>
      <div className="mx-auto mt-2 max-w-xl text-sm text-muted-foreground">
        {t("operatorDomains.list.emptyDescription")}
      </div>
      <Button asChild className="mt-4">
        <Link to="/domains/new">
          <Plus className="mr-2 h-4 w-4" />
          {t("operatorDomains.common.addDomain")}
        </Link>
      </Button>
    </div>
  )
}

function InfoTile({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-border bg-background/60 p-4">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className="mt-1 break-words text-sm font-medium">{value}</div>
    </div>
  )
}

function canBecomeMain(domain: OperatorDomainSummary) {
  return (
    !domain.isMainPlatformDomain &&
    Boolean(domain.activeCertificateId) &&
    domain.activeCertificateIsStaging === false
  )
}

function domainReadinessLabel(
  domain: OperatorDomainSummary,
  t: ReturnType<typeof useI18n>["t"],
) {
  if (domain.isMainPlatformDomain) {
    return t("operatorDomains.list.readinessMain")
  }

  if (!domain.activeCertificateId) {
    return t("operatorDomains.list.readinessCertificateNeeded")
  }

  if (domain.activeCertificateIsStaging === true) {
    return t("operatorDomains.list.readinessProductionNeeded")
  }

  if (domain.activeCertificateIsStaging === false) {
    return t("operatorDomains.list.readinessReady")
  }

  return t("operatorDomains.list.readinessReview")
}

function certificateEnvironmentLabel(
  domain: OperatorDomainSummary,
  t: ReturnType<typeof useI18n>["t"],
) {
  if (!domain.activeCertificateId) {
    return t("operatorDomains.list.noSelectedCertificate")
  }

  if (domain.activeCertificateIsStaging === true) {
    return t("operatorDomains.common.staging")
  }

  if (domain.activeCertificateIsStaging === false) {
    return t("operatorDomains.common.production")
  }

  return t("operatorDomains.list.typeUnknown")
}

function displayProvider(value: string) {
  return value.toLowerCase() === "desec" ? "deSEC" : value
}

function formatDate(value: string, locale: string) {
  return new Intl.DateTimeFormat(locale, {
    dateStyle: "medium",
  }).format(new Date(value))
}
