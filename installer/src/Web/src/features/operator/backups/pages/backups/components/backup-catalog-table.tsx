import { ArrowRight, Database } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import type { BackupCatalogListItem } from "@/features/operator/backups/api"
import { formatBytes, formatDate } from "@/features/operator/backups/shared/components/backup-formatting"

import {
  BackupCatalogIntegrityBadge,
  BackupCatalogOriginBadge,
  BackupCatalogPayloadBadge,
} from "./backup-catalog-status-badge"

export function BackupCatalogTable({ entries }: { entries: BackupCatalogListItem[] }) {
  const { language, t } = useI18n()

  return (
    <div className="border-y border-border">
      <Table className="min-w-[1160px]" data-testid="backup-catalog-table">
        <TableHeader>
          <TableRow>
            <TableHead>{t("backupCatalog.table.backupId")}</TableHead>
            <TableHead>{t("backupCatalog.table.stack")}</TableHead>
            <TableHead>{t("backupCatalog.table.origin")}</TableHead>
            <TableHead>{t("backupCatalog.table.capturedImported")}</TableHead>
            <TableHead>{t("backupCatalog.table.size")}</TableHead>
            <TableHead>{t("backupCatalog.table.integrity")}</TableHead>
            <TableHead>{t("backupCatalog.table.status")}</TableHead>
            <TableHead
              className="sticky right-0 z-30 min-w-40 border-l border-border bg-card text-right shadow-[-8px_0_12px_-10px_rgba(0,0,0,0.9)]"
              data-testid="backup-catalog-actions-header"
            >
              {t("backupCatalog.table.actions")}
            </TableHead>
          </TableRow>
        </TableHeader>

        <TableBody>
          {entries.map((entry) => {
            const detailPath = `/backups/catalog/${encodeURIComponent(entry.catalogEntryId)}`
            const capturedAt = entry.capturedAtUtc ?? entry.importedAtUtc ?? entry.createdAtUtc
            const advisoryCount = entry.advisoryCount ?? 0
            const primaryLabel = entry.sourceBackupId ?? entry.displayName

            return (
              <TableRow key={entry.catalogEntryId}>
                <TableCell>
                  <div className="flex items-start gap-3">
                    <Database
                      className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground"
                      aria-hidden="true"
                    />

                    <div className="space-y-1">
                      <Link
                        to={detailPath}
                        aria-label={t("backupCatalog.table.openDetails", { name: entry.displayName })}
                        className="block font-medium text-emerald-400 transition-colors hover:text-emerald-300 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
                      >
                        {primaryLabel}
                      </Link>

                      <div className="text-xs text-muted-foreground">{entry.displayName}</div>
                    </div>
                  </div>
                </TableCell>

                <TableCell>
                  <span className="text-sm">{entry.sourceStackSlug ?? t("backupCatalog.table.externalBackup")}</span>
                </TableCell>

                <TableCell>
                  <BackupCatalogOriginBadge originKind={entry.originKind} />
                </TableCell>

                <TableCell>
                  <div className="space-y-1 text-sm">
                    <div>{formatDate(capturedAt, language)}</div>
                    <div className="text-xs text-muted-foreground">
                      {entry.originKind === "imported-zip" ? t("backupCatalog.table.importedArchive") : t("backupCatalog.table.capturedByMem")}
                    </div>
                  </div>
                </TableCell>

                <TableCell>
                  <div className="space-y-1 text-sm">
                    <div>{formatBytes(entry.payloadBytes, language)}</div>
                    <div className="text-xs text-muted-foreground">{t("backupCatalog.table.payloadSize")}</div>
                  </div>
                </TableCell>

                <TableCell>
                  <div className="space-y-1">
                    <BackupCatalogIntegrityBadge integrityStatus={entry.integrityStatus} />

                    <div className="text-xs text-muted-foreground">
                      {entry.warningCount > 0
                        ? t("backupCatalog.summary.integrityWarnings", { count: entry.warningCount })
                        : advisoryCount > 0
                          ? t("backupCatalog.summary.importAdvisories", { count: advisoryCount })
                          : t("backupCatalog.table.checksPassed")}
                    </div>
                  </div>
                </TableCell>

                <TableCell>
                  <BackupCatalogPayloadBadge payloadState={entry.payloadState} />
                </TableCell>

                <TableCell
                  className="sticky right-0 z-20 min-w-40 border-l border-border bg-card align-top text-right shadow-[-8px_0_12px_-10px_rgba(0,0,0,0.9)]"
                  data-testid={`backup-catalog-actions-${entry.catalogEntryId}`}
                >
                  <div className="flex justify-end gap-2">
                    <Button size="sm" variant="outline" asChild>
                      <Link to={detailPath}>
                        {t("backupCatalog.table.details")}
                        <ArrowRight className="ml-2 h-4 w-4" />
                      </Link>
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            )
          })}
        </TableBody>
      </Table>
    </div>
  )
}

export function BackupCatalogEmptyState() {
  const { t } = useI18n()

  return (
    <div className="flex min-h-48 flex-col items-center justify-center rounded-lg border border-dashed p-6 text-center">
      <Database className="mb-3 h-8 w-8 text-muted-foreground" />
      <h3 className="font-medium">{t("backupCatalog.empty.title")}</h3>
      <p className="mt-1 max-w-md text-sm text-muted-foreground">
        {t("backupCatalog.empty.description")}
      </p>
    </div>
  )
}
