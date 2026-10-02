import { Link } from "react-router-dom"
import {
  Archive,
  ExternalLink,
} from "lucide-react"
import { useQueryClient } from "@tanstack/react-query"

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { backupHistoryKeys } from "@/features/operator/backups/hooks/use-backups"
import type { RuntimeStackSummaryResponse } from "@/features/operator/stacks/api/stacks.types"
import {
  useBackupRuntimeStack,
} from "@/features/operator/stacks/hooks/use-runtime-stacks"

import { formatDate } from "@/features/operator/backups/shared/components/backup-formatting"

export function ActiveStackWithoutBackupCard({ stack }: { stack: RuntimeStackSummaryResponse }) {
  const queryClient = useQueryClient()
  const backup = useBackupRuntimeStack(stack.slug)

  const createBackup = () => {
    backup.mutate(undefined, {
      onSuccess: () => {
        void queryClient.invalidateQueries({ queryKey: backupHistoryKeys.all })
      },
    })
  }

  return (
    <div className="rounded-xl border border-border bg-background/40 p-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <div className="font-medium">{stack.slug}</div>
          <div className="mt-1 text-sm text-muted-foreground">
            Last verified: {formatDate(stack.lastVerifiedAtUtc)}
          </div>
        </div>

        <div className="flex flex-wrap gap-2">
          <Button
            size="sm"
            onClick={createBackup}
            disabled={backup.isPending}
          >
            <Archive className="mr-2 h-4 w-4" />
            {backup.isPending ? "Backing up..." : "Create first backup"}
          </Button>
          <Button variant="outline" size="sm" asChild>
            <Link to={`/stacks/${encodeURIComponent(stack.slug)}`}>
              <ExternalLink className="mr-2 h-4 w-4" />
              Stack details
            </Link>
          </Button>
        </div>
      </div>

      {backup.error && (
        <Alert className="mt-4" variant="destructive">
          <AlertTitle>Backup failed</AlertTitle>
          <AlertDescription>{backup.error.message}</AlertDescription>
        </Alert>
      )}
    </div>
  )
}