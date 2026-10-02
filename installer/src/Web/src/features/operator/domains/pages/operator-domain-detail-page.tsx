// src/features/operator/domains/pages/operator-domain-detail-page.tsx

import { useEffect, useState } from "react"
import { Link, useNavigate, useParams } from "react-router-dom"
import { Crown, RefreshCw, ShieldCheck, Trash2 } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"

import type { OperatorCertificateSummary } from "@/features/shared/domains/api/domains.types"
import { CertificateIssuancePlaceholder } from "@/features/shared/domains/components/certificate-issuance-placeholder"
import {
  renewalBadgeVariant,
  renewalOperationalLabel,
  renewalReadinessDescription,
} from "@/features/operator/domains/lib/domain-renewal-display"
import {
  useDeleteOperatorDomain,
  useLatestOperatorDomainCertificateIssuance,
  useOperatorDomain,
  useOperatorDomainRenewalStatus,
  useSetMainOperatorDomain,
} from "@/features/shared/domains/hooks/use-domains"

export function OperatorDomainDetailPage() {
  const navigate = useNavigate()
  const { domainId } = useParams()
  const { intlLocale, t } = useI18n()
  const [setMainDialogOpen, setSetMainDialogOpen] = useState(false)
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false)

  const domainQuery = useOperatorDomain(domainId)
  const renewalQuery = useOperatorDomainRenewalStatus(domainId)
  const issuanceQuery = useLatestOperatorDomainCertificateIssuance(domainId)
  const deleteDomain = useDeleteOperatorDomain()
  const setMainDomain = useSetMainOperatorDomain()

  const domain = domainQuery.data
  const issuance = issuanceQuery.data
  const activeIssuance =
    issuance && isActiveIssuanceStatus(issuance.status) ? issuance : null
  const refetchDomain = domainQuery.refetch

  useEffect(() => {
    if (issuance?.isTerminal) {
      void refetchDomain()
    }
  }, [issuance?.isTerminal, issuance?.operationId, refetchDomain])

  if (domainQuery.isLoading) {
    return (
      <Card>
        <CardContent className="p-6 text-sm text-muted-foreground">
          {t("operatorDomains.detail.loading")}
        </CardContent>
      </Card>
    )
  }

  if (domainQuery.error) {
    return (
      <Card>
        <CardContent className="p-6 text-sm text-red-300">
          {t("operatorDomains.detail.loadFailed", {
            error: String(domainQuery.error),
          })}
        </CardContent>
      </Card>
    )
  }

  if (!domain) {
    return (
      <Card>
        <CardContent className="p-6 text-sm text-muted-foreground">
          {t("operatorDomains.detail.notFound")}
        </CardContent>
      </Card>
    )
  }

  const selectedCertificate =
    (domain.isMainPlatformDomain
      ? domain.certificates.find((item) => item.isMainPlatformCertificate)
      : undefined) ??
    domain.certificates.find((item) => item.id === domain.activeCertificateEntityId) ??
    null

  const productionCertificateReady = Boolean(
    selectedCertificate?.isActive && !selectedCertificate.isStaging,
  )
  const canSetMain =
    !domain.isMainPlatformDomain &&
    productionCertificateReady &&
    !setMainDomain.isPending
  const setMainDomainId = domain.id
  const setMainDomainName = domain.baseDomain
  const hasCertificates = domain.certificates.length > 0
  const canDelete =
    !domain.isMainPlatformDomain &&
    !hasCertificates &&
    !deleteDomain.isPending

  function openSetMainDialog() {
    if (!canSetMain) return

    setMainDomain.reset()
    setSetMainDialogOpen(true)
  }

  function openDeleteDialog() {
    if (!canDelete) return

    deleteDomain.reset()
    setDeleteDialogOpen(true)
  }

  function handleSetMainConfirm() {
    if (!canSetMain) return

    setMainDomain.mutate(setMainDomainId, {
      onSuccess: () => setSetMainDialogOpen(false),
    })
  }

  function handleDeleteConfirm() {
    if (!canDelete) return

    deleteDomain.mutate(
      { domainId: setMainDomainId },
      {
        onSuccess: () => navigate("/domains"),
      },
    )
  }

  const certificateEnvironment = selectedCertificate
    ? selectedCertificate.isStaging
      ? t("operatorDomains.common.staging")
      : t("operatorDomains.common.production")
    : t("operatorDomains.detail.environmentUnavailable")

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <PageBreadcrumbs
            items={[
              { label: t("operatorDomains.common.domains"), to: "/domains" },
              { label: domain.baseDomain },
            ]}
          />

          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-2xl font-semibold tracking-tight">
              {domain.baseDomain}
            </h1>

            {domain.isMainPlatformDomain ? (
              <Badge>
                <Crown className="mr-1 h-3.5 w-3.5" />
                {t("operatorDomains.common.main")}
              </Badge>
            ) : null}
          </div>

          <p className="max-w-3xl text-sm text-muted-foreground">
            {t("operatorDomains.detail.description")}
          </p>
        </div>

        <div className="flex flex-col gap-2 sm:flex-row">
          <Button
            variant="outline"
            disabled={!canSetMain}
            onClick={openSetMainDialog}
          >
            <Crown className="mr-2 h-4 w-4" />
            {t("operatorDomains.common.setMain")}
          </Button>

          <Button asChild>
            <Link to={`/domains/${domain.id}/certificates`}>
              <ShieldCheck className="mr-2 h-4 w-4" />
              {t("operatorDomains.common.certificates")}
            </Link>
          </Button>
        </div>
      </div>

      <Card
        className={
          domain.isMainPlatformDomain
            ? "border-emerald-500/30 bg-emerald-500/5"
            : undefined
        }
      >
        <CardHeader>
          <CardTitle>{t("operatorDomains.detail.readinessTitle")}</CardTitle>
          <CardDescription>
            {t("operatorDomains.detail.readinessDescription")}
          </CardDescription>
        </CardHeader>

        <CardContent className="space-y-4">
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <InfoTile
              label={t("operatorDomains.common.dnsProvider")}
              value={displayProvider(domain.dnsProvider)}
              supporting={`${t("operatorDomains.common.dnsZone")}: ${domain.dnsZone ?? domain.baseDomain}`}
            />
            <InfoTile
              label={t("operatorDomains.common.certificate")}
              value={
                selectedCertificate?.commonName ??
                t("operatorDomains.detail.certificateNotIssued")
              }
              supporting={
                selectedCertificate
                  ? certificateStatusLabel(selectedCertificate.status, t)
                  : t("operatorDomains.detail.requiresProduction")
              }
            />
            <InfoTile
              label={t("operatorDomains.common.environment")}
              value={certificateEnvironment}
            />
            <InfoTile
              label={t("operatorDomains.common.mainMemDomain")}
              value={
                domain.isMainPlatformDomain
                  ? t("operatorDomains.common.yes")
                  : t("operatorDomains.common.no")
              }
            />
          </div>

        </CardContent>
      </Card>

      <Card>
        <CardHeader className="flex flex-row items-start justify-between gap-3">
          <div className="space-y-1">
            <CardTitle>{t("operatorDomains.workspace.renewal.title")}</CardTitle>
            <CardDescription>
              {t("operatorDomains.workspace.renewal.domainPageDescription")}
            </CardDescription>
          </div>
          <Button asChild variant="outline" size="sm">
            <Link to={`/domains/${domain.id}/renewal`}>
              <RefreshCw className="mr-2 h-4 w-4" />
              {t("operatorDomains.workspace.renewal.openDetails")}
            </Link>
          </Button>
        </CardHeader>
        <CardContent>
          {renewalQuery.isLoading ? (
            <div className="text-sm text-muted-foreground">
              {t("operatorDomains.workspace.renewal.loading")}
            </div>
          ) : renewalQuery.data ? (
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
              <InfoTile
                label={t("operatorDomains.workspace.renewal.autoRenew")}
                value={
                  renewalQuery.data.autoRenewEnabled
                    ? t("operatorDomains.workspace.renewal.enabled")
                    : t("operatorDomains.workspace.renewal.disabled")
                }
                supporting={renewalOperationalLabel(renewalQuery.data, t)}
              />
              <InfoTile
                label={t("operatorDomains.workspace.renewal.dnsCredential")}
                value={
                  renewalQuery.data.credentialConfigured
                    ? t("operatorDomains.workspace.renewal.configured")
                    : t("operatorDomains.workspace.renewal.required")
                }
                supporting={renewalReadinessDescription(
                  renewalQuery.data.readinessStatus,
                  t,
                )}
              />
              <InfoTile
                label={t("operatorDomains.workspace.renewal.nextWindowLabel")}
                value={
                  renewalQuery.data.nextEligibleRenewalAtUtc
                    ? formatDate(renewalQuery.data.nextEligibleRenewalAtUtc, intlLocale)
                    : t("operatorDomains.workspace.renewal.notScheduled")
                }
              />
              <div className="rounded-xl border border-border bg-background/60 p-4">
                <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                  {t("operatorDomains.common.status")}
                </div>
                <div className="mt-2">
                  <Badge variant={renewalBadgeVariant(renewalQuery.data)}>
                    {renewalOperationalLabel(renewalQuery.data, t)}
                  </Badge>
                </div>
                {renewalQuery.data.nextAutomaticAttemptAtUtc ? (
                  <div className="mt-2 text-xs text-muted-foreground">
                    {t("operatorDomains.workspace.renewal.nextAttempt")}: {formatDate(renewalQuery.data.nextAutomaticAttemptAtUtc, intlLocale)}
                  </div>
                ) : null}
              </div>
            </div>
          ) : (
            <div className="text-sm text-muted-foreground">
              {t("operatorDomains.workspace.renewal.loadFailedDescription")}
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{t("operatorDomains.detail.certificatesTitle")}</CardTitle>
          <CardDescription>
            {t("operatorDomains.detail.certificatesDescription")}
          </CardDescription>
        </CardHeader>

        <CardContent className="space-y-3">
          {issuanceQuery.error ? (
            <div className="rounded-lg border border-amber-500/30 bg-amber-500/5 p-4 text-sm text-amber-700 dark:text-amber-300">
              {t("operatorDomains.certificates.issue.progressReadFailed", {
                error: String(issuanceQuery.error),
              })}
            </div>
          ) : null}
          {activeIssuance ? (
            <CertificateIssuancePlaceholder
              operation={{ ...activeIssuance, baseDomain: domain.baseDomain }}
              intlLocale={intlLocale}
              compact
            />
          ) : null}
          {domain.certificates.length === 0 && !activeIssuance ? (
            <div className="flex flex-col gap-4 rounded-xl border border-dashed border-border bg-background/30 p-5 sm:flex-row sm:items-center sm:justify-between">
              <div className="min-w-0">
                <div className="font-medium">
                  {t("operatorDomains.detail.noCertificatesTitle")}
                </div>
                <div className="mt-1 max-w-3xl text-sm text-muted-foreground">
                  {t("operatorDomains.detail.noCertificatesDescription")}
                </div>
              </div>

              <Button asChild className="w-full shrink-0 sm:w-auto">
                <Link to={`/domains/${domain.id}/certificates/new`}>
                  <ShieldCheck className="mr-2 h-4 w-4" />
                  {t("operatorDomains.common.issueCertificate")}
                </Link>
              </Button>
            </div>
          ) : null}
          {domain.certificates.length > 0 ? (
            <div className="space-y-3">
              {domain.certificates.map((certificate) => (
                <CertificateRow
                  key={certificate.id}
                  certificate={certificate}
                  selectedForDomain={certificate.id === selectedCertificate?.id}
                  intlLocale={intlLocale}
                  domainId={domain.id}
                />
              ))}
            </div>
          ) : null}
        </CardContent>
      </Card>

      <Card className="border-destructive/30">
        <CardContent>
          <div className="flex flex-col gap-4 rounded-xl border border-destructive/30 bg-destructive/5 p-4 sm:flex-row sm:items-center sm:justify-between">
            <div className="min-w-0">
              <div className="font-medium">
                {t("operatorDomains.detail.deleteTitle")}
              </div>
              <div className="mt-1 max-w-3xl text-sm text-muted-foreground">
                {domain.isMainPlatformDomain
                  ? t("operatorDomains.detail.deleteMainBlocked")
                  : hasCertificates
                    ? t("operatorDomains.detail.deleteCertificatesBlocked")
                    : t("operatorDomains.detail.deleteReadyDescription")}
              </div>
              {deleteDomain.error ? (
                <div className="mt-2 text-sm text-destructive">
                  {t("operatorDomains.detail.deleteFailed")}
                </div>
              ) : null}
            </div>

            <Button
              variant="destructive"
              className="w-full shrink-0 sm:w-auto"
              disabled={!canDelete}
              onClick={openDeleteDialog}
            >
              <Trash2 className="mr-2 h-4 w-4" />
              {deleteDomain.isPending
                ? t("operatorDomains.detail.deleting")
                : t("operatorDomains.detail.deleteTitle")}
            </Button>
          </div>
        </CardContent>
      </Card>

      <ConfirmationDialog
        open={setMainDialogOpen}
        onOpenChange={setSetMainDialogOpen}
        title={t("operatorDomains.detail.setMainConfirmTitle", {
          domain: setMainDomainName,
        })}
        description={t("operatorDomains.detail.setMainConfirmDescription")}
        confirmLabel={t("operatorDomains.common.setMain")}
        confirmingLabel={t("operatorDomains.detail.settingMain")}
        cancelLabel={t("common.cancel")}
        onConfirm={handleSetMainConfirm}
        isConfirming={setMainDomain.isPending}
      >
        {setMainDomain.error ? (
          <div className="rounded-lg border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">
            {t("operatorDomains.detail.setMainFailed")}
          </div>
        ) : null}
      </ConfirmationDialog>

      <ConfirmationDialog
        open={deleteDialogOpen}
        onOpenChange={setDeleteDialogOpen}
        title={t("operatorDomains.detail.deleteConfirmTitle", {
          domain: domain.baseDomain,
        })}
        description={t("operatorDomains.detail.deleteConfirmDescription")}
        confirmLabel={t("operatorDomains.detail.deleteTitle")}
        confirmingLabel={t("operatorDomains.detail.deleting")}
        cancelLabel={t("common.cancel")}
        confirmVariant="destructive"
        onConfirm={handleDeleteConfirm}
        isConfirming={deleteDomain.isPending}
      >
        {deleteDomain.error ? (
          <div className="rounded-lg border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">
            {t("operatorDomains.detail.deleteFailed")}
          </div>
        ) : null}
      </ConfirmationDialog>
    </div>
  )
}

function CertificateRow({
  certificate,
  selectedForDomain,
  intlLocale,
  domainId,
}: {
  certificate: OperatorCertificateSummary
  selectedForDomain: boolean
  intlLocale: string
  domainId: string
}) {
  const { t } = useI18n()

  return (
    <Link
      to={`/domains/${domainId}/certificates/${encodeURIComponent(certificate.certificateId)}`}
      className="block rounded-xl border border-border bg-background/40 p-4 transition hover:bg-muted/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/50"
    >
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <div className="font-medium">{certificate.commonName}</div>

            {certificate.isMainPlatformCertificate ? (
              <Badge>
                <Crown className="mr-1 h-3.5 w-3.5" />
                {t("operatorDomains.common.mainPlatformCertificate")}
              </Badge>
            ) : null}

            {selectedForDomain ? (
              <Badge variant="secondary">
                {t("operatorDomains.common.inUseForDomain")}
              </Badge>
            ) : certificate.isActive ? (
              <Badge variant="secondary">
                {t("operatorDomains.common.available")}
              </Badge>
            ) : null}

            <Badge variant={certificate.isStaging ? "outline" : "default"}>
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
                date: formatDate(certificate.expiresAtUtc, intlLocale),
              })
            : t("operatorDomains.common.expiryUnknown")}
        </div>
      </div>

      <div className="mt-3 grid gap-3 text-sm md:grid-cols-3">
        <InfoTile
          label={t("operatorDomains.common.status")}
          value={certificateStatusLabel(certificate.status, t)}
          compact
        />
        <InfoTile
          label={t("operatorDomains.common.importedToNpm")}
          value={
            certificate.importedToNpm
              ? t("operatorDomains.common.yes")
              : t("operatorDomains.common.no")
          }
          compact
        />
        <InfoTile
          label={t("operatorDomains.common.npmCertificateId")}
          value={
            certificate.npmCertificateId?.toString() ??
            t("operatorDomains.common.notKnown")
          }
          compact
        />
      </div>
    </Link>
  )
}

function InfoTile({
  label,
  value,
  supporting,
  compact,
  className,
}: {
  label: string
  value: string
  supporting?: string
  compact?: boolean
  className?: string
}) {
  const classes = compact
    ? className ?? ""
    : `rounded-xl border border-border bg-background/60 p-4 ${className ?? ""}`

  return (
    <div className={classes.trim()}>
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className="mt-1 break-words text-sm font-medium">{value}</div>
      {supporting ? (
        <div className="mt-1 break-words text-xs text-muted-foreground">
          {supporting}
        </div>
      ) : null}
    </div>
  )
}

function isActiveIssuanceStatus(status: string) {
  const normalized = status.toLowerCase()
  return normalized === "queued" || normalized === "running"
}

function displayProvider(value: string) {
  return value.toLowerCase() === "desec" ? "deSEC" : value
}

function certificateStatusLabel(
  status: string,
  t: ReturnType<typeof useI18n>["t"],
) {
  switch (status.toLowerCase()) {
    case "succeeded":
    case "ready":
      return t("operatorDomains.status.ready")
    case "failed":
      return t("operatorDomains.status.failed")
    case "pending":
      return t("operatorDomains.status.pending")
    case "active":
      return t("operatorDomains.status.active")
    default:
      return status
  }
}

function formatDate(value: string, locale: string) {
  return new Intl.DateTimeFormat(locale, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value))
}
