import {
  Archive,
  Download,
  ShieldAlert,
} from "lucide-react"

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import type {
  RuntimeStackBackupExportResult,
  RuntimeStackBackupHistoryItem,
} from "@/features/operator/backups/api/types/backups.types"
import {
  useDownloadBackupExport,
  useExportBackup,
} from "@/features/operator/backups/hooks/use-backups"

import { PathInfo, WarningList } from "@/features/operator/backups/shared/components/backup-common"
import { formatBytes, saveBlob } from "@/features/operator/backups/shared/components/backup-formatting"

export function BackupExportActions({ item }: { item: RuntimeStackBackupHistoryItem }) {
  const exportMutation = useExportBackup()
  const downloadMutation = useDownloadBackupExport()

  const exportResult = exportMutation.data

  const exportZip = () => {
    exportMutation.mutate({
      stackSlug: item.stackSlug,
      backupId: item.backupId,
    })
  }

  const downloadZip = (result: RuntimeStackBackupExportResult) => {
    downloadMutation.mutate(result.exportId, {
      onSuccess: (download) => {
        saveBlob(download.blob, download.downloadName || result.downloadName)
      },
    })
  }

  return (
    <div className="mt-3 rounded-lg border border-border bg-background/40 p-3">
      <div className="flex flex-col gap-3 xl:flex-row xl:items-start xl:justify-between">
        <div>
          <div className="flex items-center gap-2 text-sm font-medium">
            <Download className="h-4 w-4" />
            Portable export ZIP
          </div>
          <div className="mt-1 max-w-3xl text-xs text-muted-foreground">
            Creates a self-describing ZIP with database dump, media store, homeserver config, signing key,
            Element config, manifests, restore notes, and checksums.
          </div>
        </div>

        <div className="flex flex-wrap gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={exportZip}
            disabled={exportMutation.isPending}
          >
            <Archive className="mr-2 h-4 w-4" />
            {exportMutation.isPending
              ? "Exporting..."
              : exportResult
                ? "Re-export ZIP"
                : "Export ZIP"}
          </Button>

          {exportResult && (
            <Button
              size="sm"
              onClick={() => downloadZip(exportResult)}
              disabled={downloadMutation.isPending}
            >
              <Download className="mr-2 h-4 w-4" />
              {downloadMutation.isPending ? "Downloading..." : "Download ZIP"}
            </Button>
          )}
        </div>
      </div>

      {exportMutation.error && (
        <Alert className="mt-3" variant="destructive">
          <AlertTitle>Export failed</AlertTitle>
          <AlertDescription>{exportMutation.error.message}</AlertDescription>
        </Alert>
      )}

      {downloadMutation.error && (
        <Alert className="mt-3" variant="destructive">
          <AlertTitle>Download failed</AlertTitle>
          <AlertDescription>{downloadMutation.error.message}</AlertDescription>
        </Alert>
      )}

      {exportResult && (
        <div className="mt-3 space-y-3 rounded-lg border border-emerald-500/20 bg-emerald-500/10 p-3 text-sm">
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant="secondary">export ready</Badge>
            <span className="font-medium">{exportResult.downloadName}</span>
            <span className="text-muted-foreground">{formatBytes(exportResult.sizeBytes)}</span>
          </div>

          <div className="grid gap-2 text-xs md:grid-cols-2">
            <PathInfo label="Export ID" value={exportResult.exportId} />
            <PathInfo label="Download path" value={exportResult.downloadPath} />
            <PathInfo label="Export path" value={exportResult.exportPath} />
            <PathInfo label="Status" value={exportResult.status} />
          </div>

          {exportResult.warnings.length > 0 && (
            <WarningList warnings={exportResult.warnings} />
          )}
        </div>
      )}

      <Alert className="mt-3">
        <ShieldAlert className="h-4 w-4" />
        <AlertTitle>Export contains Matrix identity material</AlertTitle>
        <AlertDescription>
          The ZIP includes the Matrix signing key when present. Store downloaded exports securely and do not
          run two public homeservers with the same Matrix server name at the same time.
        </AlertDescription>
      </Alert>
    </div>
  )
}