import { useState } from "react"
import { Link } from "react-router-dom"
import {
  ChevronDown,
  ChevronRight,
  Clock,
  Eye,
} from "lucide-react"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import type { RuntimeStackBackupHistoryItem } from "@/features/operator/backups/api/types/backups.types"

import { BackupExportActions } from "@/features/operator/backups/shared/components/artifacts/backup-export-actions"
import { DeleteLocalBackupAction } from "@/features/operator/backups/shared/components/artifacts/delete-local-backup-action"
import { ComponentGrid } from "@/features/operator/backups/shared/components/artifacts/backup-component-grid"
import { PathInfo, WarningList } from "@/features/operator/backups/shared/components/backup-common"
import { formatBytes, formatDate } from "@/features/operator/backups/shared/components/backup-formatting"

export function BackupHistoryItemCard({
  item,
}: {
  item: RuntimeStackBackupHistoryItem
}) {
  const [expanded, setExpanded] = useState(false)

  const detailHref = `/backups/${encodeURIComponent(
    item.stackSlug,
  )}/${encodeURIComponent(item.backupId)}`

  return (
    <div className="overflow-hidden rounded-xl border border-border/80 bg-card/55 transition hover:border-emerald-500/35 hover:bg-card/75">
      <div className="flex flex-col gap-3 px-4 py-3 lg:flex-row lg:items-center lg:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <div className="font-mono text-sm font-semibold">
              {item.backupId}
            </div>

            <Badge variant={item.manifestPresent ? "secondary" : "destructive"}>
              {item.manifestPresent ? "manifest" : "manifest missing"}
            </Badge>

            {item.warnings.length > 0 ? (
              <Badge variant="destructive">
                {item.warnings.length} warning{item.warnings.length === 1 ? "" : "s"}
              </Badge>
            ) : (
              <Badge variant="outline">no warnings</Badge>
            )}
          </div>

          <div className="mt-1 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
            <span className="inline-flex items-center gap-1">
              <Clock className="h-3.5 w-3.5" />
              {formatDate(item.createdAtUtc)}
            </span>

            <span>{formatBytes(item.totalBytes)}</span>

            <span>
              {item.totalFiles} file{item.totalFiles === 1 ? "" : "s"}
            </span>
          </div>
        </div>

        <div className="flex flex-wrap gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setExpanded((value) => !value)}
          >
            {expanded ? (
              <ChevronDown className="mr-2 h-4 w-4" />
            ) : (
              <ChevronRight className="mr-2 h-4 w-4" />
            )}
            Details
          </Button>

          <Button variant="outline" size="sm" asChild>
            <Link to={detailHref}>
              <Eye className="mr-2 h-4 w-4" />
              View
            </Link>
          </Button>
        </div>
      </div>

      {expanded && (
        <div className="space-y-4 border-t border-border/70 px-4 py-4">
          <ComponentGrid components={item.components} />

          {item.warnings.length > 0 && (
            <WarningList warnings={item.warnings} />
          )}

          <div className="rounded-lg border border-border/70 bg-background/35 p-3">
            <BackupExportActions item={item} />
          </div>

          <div className="flex flex-col gap-3 rounded-lg border border-destructive/25 bg-destructive/5 p-3 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <div className="text-sm font-medium">Delete this local backup</div>
              <div className="mt-1 text-xs text-muted-foreground">
                This removes only the local artifact. Exports, validated imports, and
                restore-session history remain available.
              </div>
            </div>
            <DeleteLocalBackupAction backup={item} />
          </div>

          <div className="grid gap-2 border-t border-border/60 pt-4 text-xs md:grid-cols-2">
            <PathInfo label="Backup root" value={item.backupRootPath} />
            <PathInfo label="Manifest" value={item.manifestPath ?? "missing"} />
          </div>
        </div>
      )}
    </div>
  )
}