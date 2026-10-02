import { Loader2 } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"

export type CertificateIssuancePlaceholderOperation = {
  operationId: string
  domainId: string
  baseDomain: string
  status: string
  phaseCode: string | null
  phaseSummary: string | null
  useStaging: boolean
  requestedAtUtc: string
  startedAtUtc: string | null
}

export function CertificateIssuancePlaceholder({
  operation,
  intlLocale,
  compact = false,
}: {
  operation: CertificateIssuancePlaceholderOperation
  intlLocale: string
  compact?: boolean
}) {
  const { t } = useI18n()
  const startedAt = operation.startedAtUtc ?? operation.requestedAtUtc

  return (
    <div
      className={`rounded-xl border border-sky-500/35 bg-sky-500/5 ${compact ? "p-3" : "p-4"}`}
      data-testid={`certificate-issuance-placeholder-${operation.domainId}`}
    >
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <Loader2 className="h-4 w-4 shrink-0 animate-spin text-sky-400" />
            <div className="font-medium">*.{operation.baseDomain}</div>
            <Badge variant="outline">
              {t("operatorDomains.certificates.activeIssuance.badge")}
            </Badge>
            <Badge variant={operation.useStaging ? "outline" : "secondary"}>
              {operation.useStaging
                ? t("operatorDomains.common.staging")
                : t("operatorDomains.common.production")}
            </Badge>
          </div>
          <div className="mt-2 text-sm font-medium text-foreground">
            {phaseLabel(operation.phaseCode, operation.phaseSummary, t)}
          </div>
          {!compact ? (
            <div className="mt-1 text-xs text-muted-foreground">
              {t("operatorDomains.certificates.activeIssuance.started", {
                date: new Date(startedAt).toLocaleString(intlLocale),
              })}
            </div>
          ) : null}
        </div>

        <Button asChild variant="outline" size="sm" className="shrink-0">
          <Link to={`/domains/${operation.domainId}/certificates/new`}>
            {t("operatorDomains.certificates.activeIssuance.viewProgress")}
          </Link>
        </Button>
      </div>
    </div>
  )
}

function phaseLabel(
  phaseCode: string | null,
  fallback: string | null,
  t: ReturnType<typeof useI18n>["t"],
) {
  switch (phaseCode) {
    case "certificate.queued":
    case "certificate.start":
    case "certificate.acme-account":
    case "certificate.acme-order":
      return t("operatorDomains.certificates.issue.progressPhasePrepare")
    case "certificate.dns-publish":
      return t("operatorDomains.certificates.issue.progressPhasePublishDns")
    case "certificate.dns-authoritative":
    case "certificate.dns-stability":
      return t("operatorDomains.certificates.issue.progressPhaseWaitDns")
    case "certificate.acme-validation":
    case "certificate.acme-validation-retry":
    case "certificate.acme-dns-recovery":
      return t("operatorDomains.certificates.issue.progressPhaseValidateDns")
    case "certificate.acme-finalize":
    case "certificate.download":
      return t("operatorDomains.certificates.issue.progressPhaseIssue")
    case "certificate.store":
    case "certificate.validate":
      return t("operatorDomains.certificates.issue.progressPhaseStoreValidate")
    case "certificate.renewal-credential":
      return t("operatorDomains.certificates.issue.progressPhaseRenewalCredential")
    default:
      return fallback ?? t("operatorDomains.certificates.issue.progressQueued")
  }
}
