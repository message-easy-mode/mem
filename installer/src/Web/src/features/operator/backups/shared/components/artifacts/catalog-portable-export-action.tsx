import { Archive, Download, LoaderCircle, ShieldAlert } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  useDownloadCatalogPortableZip,
  useExportCatalogPortableZip,
} from "@/features/operator/backups/hooks/use-backup-catalog"
import {
  formatBytes,
  saveBlob,
} from "@/features/operator/backups/shared/components/backup-formatting"

export function CatalogPortableExportAction({
  catalogEntryId,
  payloadPresent,
}: {
  catalogEntryId: string
  payloadPresent: boolean
}) {
  const { t, language } = useI18n()
  const exportMutation = useExportCatalogPortableZip()
  const downloadMutation = useDownloadCatalogPortableZip()
  const exportResult = exportMutation.data

  const exportZip = () => {
    exportMutation.mutate(catalogEntryId)
  }

  const downloadZip = () => {
    if (!exportResult) return

    downloadMutation.mutate(exportResult.exportId, {
      onSuccess: (download) => {
        saveBlob(download.blob, download.downloadName || exportResult.downloadName)
      },
    })
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div>
          <div className="font-medium">{t("backupArtifacts.portable.title")}</div>
          <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
            {t("backupArtifacts.portable.description")}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button
            variant="outline"
            size="sm"
            disabled={!payloadPresent || exportMutation.isPending}
            onClick={exportZip}
          >
            {exportMutation.isPending
              ? <LoaderCircle className="mr-2 h-4 w-4 animate-spin" />
              : <Archive className="mr-2 h-4 w-4" />}
            {exportMutation.isPending
              ? t("backupArtifacts.portable.exporting")
              : exportResult
                ? t("backupArtifacts.portable.reexport")
                : t("backupArtifacts.portable.export")}
          </Button>
          {exportResult ? (
            <Button
              size="sm"
              disabled={downloadMutation.isPending}
              onClick={downloadZip}
            >
              {downloadMutation.isPending
                ? <LoaderCircle className="mr-2 h-4 w-4 animate-spin" />
                : <Download className="mr-2 h-4 w-4" />}
              {downloadMutation.isPending
                ? t("backupArtifacts.portable.downloading")
                : t("backupArtifacts.portable.download")}
            </Button>
          ) : null}
        </div>
      </div>

      {!payloadPresent ? (
        <p className="text-sm text-muted-foreground">
          {t("backupArtifacts.portable.unavailable")}
        </p>
      ) : null}

      {exportMutation.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("backupArtifacts.portable.exportError")}</AlertTitle>
          <AlertDescription>{exportMutation.error.message}</AlertDescription>
        </Alert>
      ) : null}

      {downloadMutation.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("backupArtifacts.portable.downloadError")}</AlertTitle>
          <AlertDescription>{downloadMutation.error.message}</AlertDescription>
        </Alert>
      ) : null}

      {exportResult ? (
        <div className="rounded-lg border border-emerald-500/20 bg-emerald-500/10 p-3 text-sm">
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant="secondary">{t("backupArtifacts.portable.ready")}</Badge>
            <span className="font-medium">{exportResult.downloadName}</span>
            <span className="text-muted-foreground">
              {formatBytes(exportResult.sizeBytes, language)}
            </span>
          </div>
          <p className="mt-2 text-xs text-muted-foreground">
            {t("backupArtifacts.portable.generated", { catalogEntryId: exportResult.catalogEntryId })}
          </p>
          {exportResult.warnings.length > 0 ? (
            <ul className="mt-2 list-disc space-y-1 pl-5 text-xs text-muted-foreground">
              {exportResult.warnings.map((warning) => <li key={warning}>{warning}</li>)}
            </ul>
          ) : null}
        </div>
      ) : null}

      <Alert>
        <ShieldAlert className="h-4 w-4" />
        <AlertTitle>{t("backupArtifacts.portable.identityTitle")}</AlertTitle>
        <AlertDescription>{t("backupArtifacts.portable.identityDescription")}</AlertDescription>
      </Alert>
    </div>
  )
}
