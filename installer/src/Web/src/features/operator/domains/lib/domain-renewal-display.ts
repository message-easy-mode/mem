import type { useI18n } from "@/app/i18n/i18n-context"
import type { OperatorDomainRenewalStatus } from "@/features/shared/domains/api/domains.types"

type Translate = ReturnType<typeof useI18n>["t"]

export function renewalOperationalLabel(
  renewal: OperatorDomainRenewalStatus | undefined,
  t: Translate,
): string {
  switch (renewal?.operationalStatus) {
    case "ready":
      return t("operatorDomains.workspace.renewal.operational.ready")
    case "scheduled":
      return t("operatorDomains.workspace.renewal.operational.scheduled")
    case "queued":
      return t("operatorDomains.workspace.renewal.operational.queued")
    case "running":
      return t("operatorDomains.workspace.renewal.operational.running")
    case "awaiting-activation":
      return t("operatorDomains.workspace.renewal.operational.awaitingActivation")
    case "failed":
      return t("operatorDomains.workspace.renewal.operational.failed")
    case "disabled":
      return t("operatorDomains.workspace.renewal.disabled")
    case "unready":
      return t("operatorDomains.workspace.renewal.operational.unready")
    default:
      return t("operatorDomains.common.unknown")
  }
}

export function renewalBadgeVariant(
  renewal: OperatorDomainRenewalStatus | undefined,
): "default" | "secondary" | "destructive" | "outline" {
  switch (renewal?.operationalStatus) {
    case "ready":
      return "default"
    case "scheduled":
    case "queued":
    case "running":
    case "awaiting-activation":
      return "secondary"
    case "failed":
      return "destructive"
    default:
      return "outline"
  }
}

export function renewalReadinessDescription(
  readinessStatus: string | undefined,
  t: Translate,
): string {
  switch (readinessStatus) {
    case "Ready":
      return t("operatorDomains.workspace.renewal.readiness.readyDescription")
    case "Disabled":
      return t("operatorDomains.workspace.renewal.readiness.disabledDescription")
    case "AcmeEmailRequired":
      return t("operatorDomains.workspace.renewal.readiness.emailRequiredDescription")
    case "ProductionCertificateRequired":
      return t("operatorDomains.workspace.renewal.readiness.productionRequiredDescription")
    case "UnsupportedProvider":
      return t("operatorDomains.workspace.renewal.readiness.unsupportedProviderDescription")
    default:
      return t("operatorDomains.workspace.renewal.readiness.credentialRequiredDescription")
  }
}
