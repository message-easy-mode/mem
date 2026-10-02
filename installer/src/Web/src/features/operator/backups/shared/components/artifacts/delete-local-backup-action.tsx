import { useState } from "react"
import { Trash2 } from "lucide-react"

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import type {
  RuntimeStackBackupDeleteResponse,
} from "@/features/operator/backups/api/types/backups.types"
import { useDeleteLocalBackup } from "@/features/operator/backups/hooks/use-backups"
import { formatBytes } from "@/features/operator/backups/shared/components/backup-formatting"

type LocalBackupDeletionTarget = {
  stackSlug: string
  backupId: string
  totalBytes: number
  totalFiles: number
}

type DeleteLocalBackupActionProps = {
  backup: LocalBackupDeletionTarget
  onDeleted?: (result: RuntimeStackBackupDeleteResponse) => void
  size?: "default" | "sm"
  label?: string
  className?: string
}

/**
 * Operator-facing deletion action for one local backup artifact.
 *
 * The HostAgent remains the safety authority: it requires
 * acknowledgeDelete=true and performs its own path and reparse-point checks.
 * This prompt adds deliberate operator confirmation and clearly explains that
 * portable exports, validated imports, and restore-session history remain.
 */
export function DeleteLocalBackupAction({
  backup,
  onDeleted,
  size = "sm",
  label = "Delete backup",
  className,
}: DeleteLocalBackupActionProps) {
  const [open, setOpen] = useState(false)
  const deleteBackup = useDeleteLocalBackup()

  const handleOpenChange = (nextOpen: boolean) => {
    if (nextOpen) {
      deleteBackup.reset()
    }

    setOpen(nextOpen)
  }

  const confirmDelete = () => {
    deleteBackup.mutate(
      {
        stackSlug: backup.stackSlug,
        backupId: backup.backupId,
      },
      {
        onSuccess: (result) => {
          setOpen(false)
          onDeleted?.(result)
        },
      },
    )
  }

  return (
    <>
      <Button
        type="button"
        variant="destructive"
        size={size}
        className={className}
        onClick={() => handleOpenChange(true)}
        disabled={deleteBackup.isPending}
      >
        <Trash2 className="mr-2 h-4 w-4" />
        {label}
      </Button>

      <ConfirmationDialog
        open={open}
        onOpenChange={handleOpenChange}
        title="Delete local backup?"
        description={
          "This permanently removes the selected local backup directory from this MEM host. The action cannot be undone."
        }
        confirmLabel="Delete backup"
        confirmingLabel="Deleting backup..."
        confirmVariant="destructive"
        onConfirm={confirmDelete}
        isConfirming={deleteBackup.isPending}
      >
        <div className="grid gap-3 rounded-lg border border-border bg-background/35 p-3 text-sm sm:grid-cols-2">
          <DeleteContext label="Stack" value={backup.stackSlug} />
          <DeleteContext label="Backup ID" value={backup.backupId} mono />
          <DeleteContext label="Storage to reclaim" value={formatBytes(backup.totalBytes)} />
          <DeleteContext
            label="Backup files"
            value={`${backup.totalFiles} file${backup.totalFiles === 1 ? "" : "s"}`}
          />
        </div>

        <Alert>
          <Trash2 className="h-4 w-4" />
          <AlertTitle>What stays intact</AlertTitle>
          <AlertDescription>
            Portable exports, validated imports, and restore-session history are
            retained. Restore sessions will continue to show their historical
            source reference, but this local backup can no longer be exported or
            used to start a new local restore.
          </AlertDescription>
        </Alert>

        {deleteBackup.error ? (
          <Alert variant="destructive">
            <AlertTitle>Could not delete local backup</AlertTitle>
            <AlertDescription>{deleteBackup.error.message}</AlertDescription>
          </Alert>
        ) : null}
      </ConfirmationDialog>
    </>
  )
}

function DeleteContext({
  label,
  value,
  mono = false,
}: {
  label: string
  value: string
  mono?: boolean
}) {
  return (
    <div className="min-w-0">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className={mono ? "mt-1 break-all font-mono text-sm" : "mt-1 text-sm"}>
        {value}
      </div>
    </div>
  )
}
