import { Link } from "react-router-dom"
import {
  Archive,
  ExternalLink,
  Server,
} from "lucide-react"
import { useQueryClient } from "@tanstack/react-query"

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import type {
  RuntimeStackBackupStackHistory,
} from "@/features/operator/backups/api/types/backups.types"
import { BackupHistoryItemCard } from "@/features/operator/backups/pages/backups/components/backup-history-item-card"
import {
  backupHistoryKeys,
} from "@/features/operator/backups/hooks/use-backups"
import type { RuntimeStackSummaryResponse } from "@/features/operator/stacks/api/stacks.types"
import {
  useBackupRuntimeStack,
} from "@/features/operator/stacks/hooks/use-runtime-stacks"

import { Info, WarningList } from "@/features/operator/backups/shared/components/backup-common"
import { formatBytes, formatDate } from "@/features/operator/backups/shared/components/backup-formatting"

export type SelectedBackup = {
  stackSlug: string
  backupId: string
}

export function BackupStackHistoryCard({
  stack,
  activeStack,
}: {
  stack: RuntimeStackBackupStackHistory
  activeStack: RuntimeStackSummaryResponse | null
}) {
  const queryClient = useQueryClient()
  const backup = useBackupRuntimeStack(stack.stackSlug)

  const createBackup = () => {
    backup.mutate(undefined, {
      onSuccess: () => {
        void queryClient.invalidateQueries({ queryKey: backupHistoryKeys.all })
      },
    })
  }

  return (
    <div className="rounded-2xl border border-emerald-500/45 bg-emerald-500/[0.035] p-5 shadow-sm shadow-emerald-950/20">
      <div className="flex flex-col gap-4 border-b border-emerald-500/20 pb-4 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <div className="flex h-9 w-9 items-center justify-center rounded-xl border border-emerald-500/30 bg-emerald-500/10 text-emerald-300">
              <Server className="h-4 w-4" />
            </div>

            <h2 className="text-xl font-semibold tracking-tight text-emerald-300">
              {stack.stackSlug}
            </h2>

            <Badge
              variant={activeStack ? "secondary" : "outline"}
              className={activeStack ? "bg-emerald-500/15 text-emerald-200" : ""}
            >
              {activeStack ? "active" : "history only"}
            </Badge>

            {stack.warnings.length > 0 && (
              <Badge variant="destructive">
                {stack.warnings.length} warning{stack.warnings.length === 1 ? "" : "s"}
              </Badge>
            )}
          </div>

          <p className="mt-2 text-sm text-muted-foreground">
            {stack.backupCount} backup{stack.backupCount === 1 ? "" : "s"} · latest {stack.latestBackupId ?? "unknown"}
          </p>
        </div>

        <div className="flex flex-wrap gap-2">
          {activeStack && (
            <Button
              size="sm"
              onClick={createBackup}
              disabled={backup.isPending}
            >
              <Archive className="mr-2 h-4 w-4" />
              {backup.isPending ? "Backing up..." : "Create backup"}
            </Button>
          )}

          {activeStack && (
            <Button variant="outline" size="sm" asChild>
              <Link to={`/stacks/${encodeURIComponent(activeStack.slug)}`}>
                <ExternalLink className="mr-2 h-4 w-4" />
                Stack details
              </Link>
            </Button>
          )}
        </div>
      </div>

      {backup.error && (
        <Alert className="mt-4" variant="destructive">
          <AlertTitle>Backup failed</AlertTitle>
          <AlertDescription>{backup.error.message}</AlertDescription>
        </Alert>
      )}

      {backup.data && (
        <div className="mt-4 rounded-lg border border-emerald-500/20 bg-emerald-500/10 p-3 text-sm">
          <div className="flex items-center gap-2 font-medium text-emerald-100">
            <Archive className="h-4 w-4" />
            Backup created: {backup.data.backupId}
          </div>
          <div className="mt-2 text-muted-foreground">
            {formatBytes(backup.data.stats?.totalBytes ?? 0)} · {backup.data.stats?.totalFiles ?? 0} files ·{" "}
            {backup.data.warnings.length} warning{backup.data.warnings.length === 1 ? "" : "s"}
          </div>
        </div>
      )}

      {stack.warnings.length > 0 && (
        <WarningList className="mt-4" warnings={stack.warnings} />
      )}

      <div className="mt-4 grid gap-3 rounded-xl border border-emerald-500/15 bg-background/35 p-4 text-sm md:grid-cols-2 xl:grid-cols-4">
        <Info label="Latest backup" value={stack.latestBackupId ?? "unknown"} />
        <Info label="Latest created" value={formatDate(stack.latestCreatedAtUtc)} />
        <Info label="Total size" value={formatBytes(stack.totalBytes)} />
        <Info label="Total files" value={stack.totalFiles.toString()} />
      </div>

      <div className="mt-5 space-y-3">
        {stack.backups.map((item) => (
          <BackupHistoryItemCard
            key={`${item.stackSlug}:${item.backupId}`}
            item={item}
          />
        ))}
      </div>
    </div>
  )
}