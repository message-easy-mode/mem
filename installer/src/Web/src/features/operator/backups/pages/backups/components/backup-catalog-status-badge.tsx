import { useI18n } from "@/app/i18n/i18n-context"
import { Badge } from "@/components/ui/badge"
import type {
  BackupCatalogIntegrityStatus,
  BackupCatalogOriginKind,
  BackupCatalogPayloadState,
} from "@/features/operator/backups/api"

function titleCase(value: string) {
  return value.replace(/[-_]/g, " ").replace(/\b\w/g, (letter) => letter.toUpperCase())
}

export function BackupCatalogOriginBadge({ originKind }: { originKind: BackupCatalogOriginKind }) {
  const { t } = useI18n()

  return <Badge variant="secondary" className={originKind === "imported-zip" ? "bg-sky-500/15 text-sky-300 ring-1 ring-sky-400/20" : "bg-emerald-500/15 text-emerald-300 ring-1 ring-emerald-400/20"}>{originKind === "imported-zip" ? t("backupCatalog.origin.importedZip") : t("backupCatalog.origin.localBackup")}</Badge>
}

export function BackupCatalogPayloadBadge({ payloadState }: { payloadState: BackupCatalogPayloadState }) {
  const { t } = useI18n()
  const variant = payloadState === "available" ? "secondary" : payloadState === "failed" || payloadState === "removed" ? "destructive" : "outline"

  return <Badge variant={variant} className={payloadState === "available" ? "bg-emerald-500/15 text-emerald-300 ring-1 ring-emerald-400/20" : undefined}>{payloadStateLabel(payloadState, t)}</Badge>
}

export function BackupCatalogIntegrityBadge({ integrityStatus }: { integrityStatus: BackupCatalogIntegrityStatus }) {
  const { t } = useI18n()
  const variant = integrityStatus === "valid" ? "secondary" : integrityStatus === "invalid" ? "destructive" : "outline"

  return <Badge variant={variant} className={integrityStatus === "valid" ? "bg-emerald-500/15 text-emerald-300 ring-1 ring-emerald-400/20" : integrityStatus === "warning" ? "bg-amber-500/15 text-amber-300 ring-1 ring-amber-400/20" : undefined}>{integrityStatusLabel(integrityStatus, t)}</Badge>
}

function payloadStateLabel(
  payloadState: BackupCatalogPayloadState,
  t: ReturnType<typeof useI18n>["t"],
) {
  switch (payloadState) {
    case "available":
      return t("backupCatalog.payload.available")
    case "materialising":
      return t("backupCatalog.payload.materialising")
    case "failed":
      return t("backupCatalog.payload.failed")
    case "removed":
      return t("backupCatalog.payload.removed")
    case "unknown":
      return t("backupCatalog.payload.unknown")
    default:
      return titleCase(payloadState)
  }
}

function integrityStatusLabel(
  integrityStatus: BackupCatalogIntegrityStatus,
  t: ReturnType<typeof useI18n>["t"],
) {
  switch (integrityStatus) {
    case "valid":
      return t("backupCatalog.integrity.valid")
    case "warning":
      return t("backupCatalog.integrity.warning")
    case "invalid":
      return t("backupCatalog.integrity.invalid")
    case "unknown":
      return t("backupCatalog.integrity.unknown")
    default:
      return titleCase(integrityStatus)
  }
}
