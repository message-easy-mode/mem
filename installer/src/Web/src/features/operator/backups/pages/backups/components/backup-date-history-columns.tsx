import type { ColumnDef } from "@tanstack/react-table"
import { ChevronDown, ChevronRight, Eye } from "lucide-react"
import { Link } from "react-router-dom"

import { DataTableColumnHeader } from "@/components/data-table/data-table-column-header"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import type { RuntimeStackBackupHistoryItem } from "@/features/operator/backups/api/types/backups.types"
import { BackupExportActions } from "@/features/operator/backups/shared/components/artifacts/backup-export-actions"
import { DeleteLocalBackupAction } from "@/features/operator/backups/shared/components/artifacts/delete-local-backup-action"
import { ComponentGrid } from "@/features/operator/backups/shared/components/artifacts/backup-component-grid"
import { PathInfo, WarningList } from "@/features/operator/backups/shared/components/backup-common"
import {
  formatBytes,
  formatDate,
} from "@/features/operator/backups/shared/components/backup-formatting"

import "@/components/data-table/data-table-types"

export const BACKUP_DATE_ACTIONS_COLUMN_ID = "actions"

export function createBackupDateHistoryColumns(): ColumnDef<RuntimeStackBackupHistoryItem>[] {
  return [
    {
      id: "backup",
      accessorFn: (backup) => backup.backupId,
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Backup" />
      ),
      cell: ({ row }) => <BackupCell backup={row.original} />,
      enableHiding: false,
      meta: { label: "Backup" },
    },
    {
      id: "stack",
      accessorFn: (backup) => backup.stackSlug,
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Stack" />
      ),
      cell: ({ row }) => (
        <span className="block min-w-40 break-words text-sm text-muted-foreground">
          {row.original.stackSlug}
        </span>
      ),
      meta: { label: "Stack" },
    },
    {
      id: "created",
      accessorFn: (backup) => backup.createdAtUtc ?? "",
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Created" />
      ),
      cell: ({ row }) => (
        <span className="block min-w-36 text-sm text-muted-foreground">
          {formatDate(row.original.createdAtUtc)}
        </span>
      ),
      meta: { label: "Created" },
    },
    {
      id: "size",
      accessorFn: (backup) => backup.totalBytes,
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Size" />
      ),
      cell: ({ row }) => (
        <div className="min-w-28 text-sm text-muted-foreground">
          <div>{formatBytes(row.original.totalBytes)}</div>
          <div className="mt-1 text-xs">
            {row.original.totalFiles} file{row.original.totalFiles === 1 ? "" : "s"}
          </div>
        </div>
      ),
      meta: { label: "Size" },
    },
    {
      id: BACKUP_DATE_ACTIONS_COLUMN_ID,
      enableSorting: false,
      enableHiding: false,
      header: () => <span>Actions</span>,
      cell: ({ row }) => (
        <BackupActions backup={row.original} expanded={row.getIsExpanded()} onToggle={row.toggleExpanded} />
      ),
      meta: { align: "right", label: "Actions" },
    },
  ]
}

export function BackupDateHistoryDetails({
  backup,
}: {
  backup: RuntimeStackBackupHistoryItem
}) {
  return (
    <div className="space-y-4 bg-background/35 px-1 py-1">
      <ComponentGrid components={backup.components} />

      {backup.warnings.length > 0 ? <WarningList warnings={backup.warnings} /> : null}

      <div className="rounded-lg border border-border/70 bg-card/45 p-3">
        <BackupExportActions item={backup} />
      </div>

      <div className="flex flex-col gap-3 rounded-lg border border-destructive/25 bg-destructive/5 p-3 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <div className="text-sm font-medium">Delete this local backup</div>
          <div className="mt-1 text-xs text-muted-foreground">
            This removes only the local artifact. Exports, validated imports, and
            restore-session history remain available.
          </div>
        </div>
        <DeleteLocalBackupAction backup={backup} />
      </div>

      <div className="grid gap-2 border-t border-border/60 pt-4 text-xs md:grid-cols-2">
        <PathInfo label="Backup root" value={backup.backupRootPath} />
        <PathInfo label="Manifest" value={backup.manifestPath ?? "missing"} />
      </div>
    </div>
  )
}

function BackupCell({
  backup,
}: {
  backup: RuntimeStackBackupHistoryItem
}) {
  const detailHref = getBackupDetailHref(backup)

  return (
    <div className="min-w-64 space-y-1">
      <Link
        to={detailHref}
        className="font-mono text-sm font-semibold hover:text-primary hover:underline"
      >
        {backup.backupId}
      </Link>
      <div className="flex flex-wrap gap-2 pt-1">
        <Badge variant={backup.manifestPresent ? "secondary" : "destructive"}>
          {backup.manifestPresent ? "manifest" : "manifest missing"}
        </Badge>
        {backup.warnings.length > 0 ? (
          <Badge variant="destructive">
            {backup.warnings.length} warning{backup.warnings.length === 1 ? "" : "s"}
          </Badge>
        ) : (
          <Badge variant="outline">no warnings</Badge>
        )}
      </div>
    </div>
  )
}

function BackupActions({
  backup,
  expanded,
  onToggle,
}: {
  backup: RuntimeStackBackupHistoryItem
  expanded: boolean
  onToggle: () => void
}) {
  const detailHref = getBackupDetailHref(backup)

  return (
    <div className="flex min-w-44 flex-wrap justify-end gap-2">
      <Button type="button" variant="outline" size="sm" onClick={onToggle}>
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
  )
}

function getBackupDetailHref(backup: RuntimeStackBackupHistoryItem) {
  return `/backups/${encodeURIComponent(backup.stackSlug)}/${encodeURIComponent(
    backup.backupId,
  )}`
}
