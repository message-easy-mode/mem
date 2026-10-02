import {
  AlertTriangle,
  CheckCircle2,
  Circle,
  Loader2,
  XCircle,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { cn } from "@/lib/utils"

import type { InstallationProgressPhaseSnapshot } from "../api/installation-run.types"

const phaseTranslationKeys: Record<string, TranslationKey> = {
  "postgres.inspect": "setup.activity.phase.postgresInspect",
  "postgres.credential": "setup.activity.phase.postgresCredential",
  "postgres.container-start": "setup.activity.phase.postgresStart",
  "postgres.readiness": "setup.activity.phase.postgresReadiness",
  "npm.inspect": "setup.activity.phase.npmInspect",
  "npm.container-start": "setup.activity.phase.npmStart",
  "npm.readiness": "setup.activity.phase.npmReadiness",
  "npm.bootstrap.inspect": "setup.activity.phase.npmBootstrapInspect",
  "npm.bootstrap.create-admin": "setup.activity.phase.npmBootstrapCreateAdmin",
  "npm.bootstrap.verify-initial-login": "setup.activity.phase.npmBootstrapVerifyInitial",
  "npm.bootstrap.recreate-clean": "setup.activity.phase.npmBootstrapRecreate",
  "npm.bootstrap.verify-final-login": "setup.activity.phase.npmBootstrapVerifyFinal",
  "certificate.prepare": "setup.activity.phase.certificatePrepare",
  "certificate.reuse": "setup.activity.phase.certificateReuse",
  "certificate.credential": "setup.activity.phase.certificateCredential",
  "certificate.issue": "setup.activity.phase.certificateIssue",
  "certificate.acme-account": "setup.activity.phase.certificateAcmeAccount",
  "certificate.acme-order": "setup.activity.phase.certificateAcmeOrder",
  "certificate.dns-publish": "setup.activity.phase.certificateDnsPublish",
  "certificate.dns-authoritative": "setup.activity.phase.certificateDnsAuthoritative",
  "certificate.dns-public": "setup.activity.phase.certificateDnsPublic",
  "certificate.dns-stability": "setup.activity.phase.certificateDnsStability",
  "certificate.acme-validation": "setup.activity.phase.certificateAcmeValidation",
  "certificate.acme-dns-recovery": "setup.activity.phase.certificateAcmeDnsRecovery",
  "certificate.acme-validation-retry": "setup.activity.phase.certificateAcmeValidationRetry",
  "certificate.acme-finalize": "setup.activity.phase.certificateAcmeFinalize",
  "certificate.download": "setup.activity.phase.certificateDownload",
  "certificate.store": "setup.activity.phase.certificateStore",
  "certificate.validate": "setup.activity.phase.certificateValidate",
  "certificate.registry": "setup.activity.phase.certificateRegistry",
  "certificate.npm-import": "setup.activity.phase.certificateNpmImport",
  "certificate.persist": "setup.activity.phase.certificatePersist",
  "support.evaluate": "setup.activity.phase.supportEvaluate",
  "support.portainer.install": "setup.activity.phase.portainerInstall",
  "support.seq.review": "setup.activity.phase.seqReview",
  "support.pgadmin.review": "setup.activity.phase.pgAdminReview",
  "verification.postgres": "setup.activity.phase.verificationPostgres",
  "verification.npm": "setup.activity.phase.verificationNpm",
  "verification.certificate": "setup.activity.phase.verificationCertificate",
  "verification.complete": "setup.activity.phase.verificationComplete",
}

export function InstallProgressPhaseList({
  phases,
}: {
  phases: InstallationProgressPhaseSnapshot[]
}) {
  const { t } = useI18n()

  return (
    <div className="space-y-2">
      {phases.map((phase) => {
        const Icon = phaseIcon(phase.status)

        return (
          <div
            key={`${phase.code}-${phase.startedAtUtc ?? "unknown"}`}
            className="flex items-start gap-2 text-sm"
          >
            <Icon
              className={cn(
                "mt-0.5 h-4 w-4 shrink-0",
                phase.status === "Running" && "animate-spin text-sky-300",
                phase.status === "Succeeded" && "text-emerald-300",
                phase.status === "WaitingForUser" && "text-amber-300",
                phase.status === "Recovering" && "animate-spin text-amber-300",
                phase.status === "Recovered" && "text-amber-300",
                phase.status === "Failed" && "text-destructive",
                phase.status === "Pending" && "text-muted-foreground",
              )}
            />
            <span className={phase.status === "Running" ? "font-medium" : undefined}>
              {phaseLabel(phase.code, t)}
            </span>
          </div>
        )
      })}
    </div>
  )
}

export function installProgressPhaseLabel(
  phaseCode: string | null,
  t: (key: TranslationKey, values?: Readonly<Record<string, string | number>>) => string,
) {
  return phaseLabel(phaseCode, t)
}

function phaseIcon(status: string) {
  switch (status) {
    case "Running":
      return Loader2
    case "Succeeded":
      return CheckCircle2
    case "WaitingForUser":
      return AlertTriangle
    case "Recovering":
      return Loader2
    case "Recovered":
      return AlertTriangle
    case "Failed":
      return XCircle
    default:
      return Circle
  }
}

function phaseLabel(
  phaseCode: string | null,
  t: (key: TranslationKey, values?: Readonly<Record<string, string | number>>) => string,
) {
  if (!phaseCode) return t("setup.activity.processing")
  const key = phaseTranslationKeys[phaseCode]
  return key ? t(key) : t("setup.activity.processing")
}
