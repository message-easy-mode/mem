import { useState } from "react"
import { Link, useNavigate, useParams } from "react-router-dom"
import { ArrowLeft, CheckCircle2, Crown, Trash2 } from "lucide-react"

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
import { getErrorMessage } from "@/features/shared/domains/components/ingress-tls-shared"
import {
  useDeleteOperatorDomainCertificate,
  useOperatorDomain,
  useOperatorDomainCertificate,
  useSetActiveOperatorDomainCertificate,
} from "@/features/shared/domains/hooks/use-domains"

export function OperatorDomainCertificateDetailPage() {
  const navigate = useNavigate()
  const { domainId, certificateId } = useParams()
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false)
  const { intlLocale, t } = useI18n()
  const domainQuery = useOperatorDomain(domainId)
  const certificateQuery = useOperatorDomainCertificate(domainId, certificateId)
  const setActiveMutation = useSetActiveOperatorDomainCertificate(domainId)
  const deleteMutation = useDeleteOperatorDomainCertificate(domainId)

  const domain = domainQuery.data
  const certificate = certificateQuery.data

  if (domainQuery.isLoading || certificateQuery.isLoading) {
    return <StateCard message={t("operatorDomains.certificateDetail.loading")} />
  }

  if (domainQuery.error || certificateQuery.error || !domain || !certificate) {
    const error = domainQuery.error ?? certificateQuery.error
    return (
      <StateCard
        message={error
          ? t("operatorDomains.certificateDetail.loadFailed", { error: getErrorMessage(error) })
          : t("operatorDomains.certificateDetail.notFound")}
      />
    )
  }

  const resolvedDomain = domain
  const resolvedCertificate = certificate
  const stagingBlocked = resolvedCertificate.isStaging
  const mainDomainBlocked =
    resolvedDomain.isMainPlatformDomain && !resolvedCertificate.isMainPlatformCertificate
  const canSetActive =
    !resolvedCertificate.isInUse &&
    resolvedCertificate.isActive &&
    !stagingBlocked &&
    !mainDomainBlocked &&
    !setActiveMutation.isPending
  const deleteBlocked =
    resolvedCertificate.isMainPlatformCertificate ||
    (resolvedDomain.isMainPlatformDomain && resolvedCertificate.isInUse)
  const canDelete = !deleteBlocked && !deleteMutation.isPending

  function handleSetActive() {
    if (!canSetActive) return

    const confirmed = window.confirm(
      t("operatorDomains.certificateDetail.setActiveConfirm", {
        certificate: resolvedCertificate.commonName,
        domain: resolvedDomain.baseDomain,
      }),
    )
    if (!confirmed) return

    setActiveMutation.mutate(resolvedCertificate.certificateId)
  }

  function openDeleteDialog() {
    if (!canDelete) return

    deleteMutation.reset()
    setDeleteDialogOpen(true)
  }

  function handleDeleteConfirm() {
    if (!canDelete) return

    deleteMutation.mutate(resolvedCertificate.certificateId, {
      onSuccess: () => {
        setDeleteDialogOpen(false)
        navigate(`/domains/${resolvedDomain.id}/certificates`)
      },
    })
  }

  const deleteDescription = deleteBlocked
    ? t("operatorDomains.certificateDetail.deleteMainBlocked")
    : resolvedCertificate.isInUse
      ? t("operatorDomains.certificateDetail.deleteActiveDescription")
      : t("operatorDomains.certificateDetail.deleteDescription")

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <PageBreadcrumbs
            items={[
              { label: t("operatorDomains.common.domains"), to: "/domains" },
              { label: domain.baseDomain, to: `/domains/${domain.id}` },
              { label: t("operatorDomains.domainCertificates.title"), to: `/domains/${domain.id}/certificates` },
              { label: certificate.commonName },
            ]}
          />
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-2xl font-semibold tracking-tight">
              {certificate.commonName}
            </h1>
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
          <p className="max-w-3xl text-sm text-muted-foreground">
            {t("operatorDomains.certificateDetail.description", { domain: domain.baseDomain })}
          </p>
        </div>

        <Button asChild variant="outline">
          <Link to={`/domains/${domain.id}/certificates`}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            {t("operatorDomains.certificateDetail.backToCertificates")}
          </Link>
        </Button>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>{t("operatorDomains.certificateDetail.safeFactsTitle")}</CardTitle>
          <CardDescription>{t("operatorDomains.certificateDetail.safeFactsDescription")}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          <Fact label={t("operatorDomains.common.domain")} value={certificate.domainBaseDomain} />
          <Fact label={t("operatorDomains.certificateDetail.certificateId")} value={certificate.certificateId} />
          <Fact label={t("operatorDomains.certificateDetail.source")} value={certificate.provider} />
          <Fact label={t("operatorDomains.common.status")} value={certificate.status} />
          <Fact label={t("operatorDomains.common.dnsZone")} value={certificate.domainDnsZone ?? certificate.domainBaseDomain} />
          <Fact label={t("operatorDomains.common.npmCertificateId")} value={certificate.npmCertificateId?.toString() ?? t("operatorDomains.common.notKnown")} />
          <Fact label={t("operatorDomains.common.importedToNpm")} value={certificate.importedToNpm ? t("operatorDomains.common.yes") : t("operatorDomains.common.no")} />
          <Fact label={t("operatorDomains.certificateDetail.created")} value={formatDate(certificate.createdAtUtc, intlLocale)} />
          <Fact label={t("operatorDomains.common.expires")} value={certificate.expiresAtUtc ? formatDate(certificate.expiresAtUtc, intlLocale) : t("operatorDomains.common.expiryUnknown")} />
          <Fact label={t("operatorDomains.certificateDetail.thumbprint")} value={certificate.thumbprint ?? t("operatorDomains.common.notKnown")} />
          <Fact label={t("operatorDomains.certificateDetail.lastValidated")} value={certificate.lastValidatedAtUtc ? formatDate(certificate.lastValidatedAtUtc, intlLocale) : t("operatorDomains.common.notKnown")} />
          <Fact label={t("operatorDomains.certificateDetail.lastNpmImport")} value={certificate.lastImportedToNpmAtUtc ? formatDate(certificate.lastImportedToNpmAtUtc, intlLocale) : t("operatorDomains.common.notKnown")} />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{t("operatorDomains.certificateDetail.assignmentTitle")}</CardTitle>
          <CardDescription>{t("operatorDomains.certificateDetail.assignmentDescription")}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {certificate.isInUse ? (
            <div className="flex items-start gap-3 rounded-xl border border-emerald-500/30 bg-emerald-500/5 p-4 text-sm">
              <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-500" />
              <div>
                <div className="font-medium text-foreground">
                  {t("operatorDomains.certificateDetail.activeTitle")}
                </div>
                <div className="mt-1 text-muted-foreground">
                  {t("operatorDomains.certificateDetail.activeDescription")}
                </div>
              </div>
            </div>
          ) : stagingBlocked ? (
            <div className="rounded-xl border border-amber-500/30 bg-amber-500/5 p-4 text-sm text-muted-foreground">
              {t("operatorDomains.certificateDetail.stagingBlocked")}
            </div>
          ) : mainDomainBlocked ? (
            <div className="rounded-xl border border-amber-500/30 bg-amber-500/5 p-4 text-sm text-muted-foreground">
              {t("operatorDomains.certificateDetail.mainDomainBlocked")}
            </div>
          ) : (
            <Button onClick={handleSetActive} disabled={!canSetActive}>
              <CheckCircle2 className="mr-2 h-4 w-4" />
              {setActiveMutation.isPending
                ? t("operatorDomains.certificateDetail.settingActive")
                : t("operatorDomains.certificateDetail.setActive")}
            </Button>
          )}

          {setActiveMutation.error ? (
            <div className="rounded-lg border border-red-500/30 bg-red-500/5 p-3 text-sm text-red-600 dark:text-red-300">
              {t("operatorDomains.certificateDetail.setActiveFailed", {
                error: getErrorMessage(setActiveMutation.error),
              })}
            </div>
          ) : null}
        </CardContent>
      </Card>

      <Card className="border-destructive/30">
        <CardContent>
          <div className="flex flex-col gap-4 rounded-xl border border-destructive/30 bg-destructive/5 p-4 sm:flex-row sm:items-center sm:justify-between">
            <div className="min-w-0">
              <div className="font-medium">
                {t("operatorDomains.certificateDetail.deleteTitle")}
              </div>
              <div className="mt-1 max-w-3xl text-sm text-muted-foreground">
                {deleteDescription}
              </div>
            </div>

            <Button
              variant="destructive"
              className="w-full shrink-0 sm:w-auto"
              disabled={!canDelete}
              onClick={openDeleteDialog}
            >
              <Trash2 className="mr-2 h-4 w-4" />
              {deleteMutation.isPending
                ? t("operatorDomains.certificateDetail.deleting")
                : t("operatorDomains.certificateDetail.deleteTitle")}
            </Button>
          </div>
        </CardContent>
      </Card>

      <ConfirmationDialog
        open={deleteDialogOpen}
        onOpenChange={setDeleteDialogOpen}
        title={t("operatorDomains.certificateDetail.deleteConfirmTitle", {
          certificate: resolvedCertificate.commonName,
        })}
        description={resolvedCertificate.isInUse
          ? t("operatorDomains.certificateDetail.deleteActiveConfirmDescription")
          : t("operatorDomains.certificateDetail.deleteConfirmDescription")}
        confirmLabel={t("operatorDomains.certificateDetail.deleteTitle")}
        confirmingLabel={t("operatorDomains.certificateDetail.deleting")}
        cancelLabel={t("common.cancel")}
        confirmVariant="destructive"
        onConfirm={handleDeleteConfirm}
        isConfirming={deleteMutation.isPending}
      >
        {deleteMutation.error ? (
          <div className="rounded-lg border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">
            {t("operatorDomains.certificateDetail.deleteFailed", {
              error: getErrorMessage(deleteMutation.error),
            })}
          </div>
        ) : null}
      </ConfirmationDialog>

      {certificate.lastError ? (
        <Card>
          <CardHeader>
            <CardTitle>{t("operatorDomains.certificateDetail.lastErrorTitle")}</CardTitle>
          </CardHeader>
          <CardContent className="break-words text-sm text-muted-foreground">
            {certificate.lastError}
          </CardContent>
        </Card>
      ) : null}
    </div>
  )
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0 rounded-xl border border-border bg-background/60 p-4">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-1 break-all text-sm font-medium">{value}</div>
    </div>
  )
}

function StateCard({ message }: { message: string }) {
  return (
    <Card>
      <CardContent className="p-6 text-sm text-muted-foreground">{message}</CardContent>
    </Card>
  )
}

function formatDate(value: string, locale: string) {
  return new Intl.DateTimeFormat(locale, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value))
}
