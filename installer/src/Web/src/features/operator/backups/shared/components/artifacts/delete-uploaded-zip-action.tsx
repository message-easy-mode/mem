import { useState } from "react"
import { Trash2 } from "lucide-react"

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import { useDeleteValidatedImportArtifact } from "@/features/operator/backups/hooks/use-backups"
import { formatBytes } from "@/features/operator/backups/shared/components/backup-formatting"

type UploadedZipDeletionTarget = {
  validationId: string
  uploadedFileName: string
  archiveBytes: number | null
}

type DeleteUploadedZipActionProps = {
  source: UploadedZipDeletionTarget
  onDeleted?: () => void
  size?: "default" | "sm"
  className?: string
}

/**
 * Deliberate Uploaded ZIP source deletion. The HostAgent remains the lifecycle
 * authority: it requires acknowledgement, blocks active work, and retains
 * durable restore/audit records when only the original archive is removed.
 */
export function DeleteUploadedZipAction({
  source,
  onDeleted,
  size = "sm",
  className,
}: DeleteUploadedZipActionProps) {
  const [open, setOpen] = useState(false)
  const deleteSource = useDeleteValidatedImportArtifact()

  const handleOpenChange = (nextOpen: boolean) => {
    if (nextOpen) {
      deleteSource.reset()
    }

    setOpen(nextOpen)
  }

  const confirmDelete = () => {
    deleteSource.mutate(source.validationId, {
      onSuccess: () => {
        setOpen(false)
        onDeleted?.()
      },
    })
  }

  return (
    <>
      <Button
        type="button"
        variant="destructive"
        size={size}
        className={className}
        onClick={() => handleOpenChange(true)}
        disabled={deleteSource.isPending}
      >
        <Trash2 className="mr-2 h-4 w-4" />
        Delete uploaded ZIP
      </Button>

      <ConfirmationDialog
        open={open}
        onOpenChange={handleOpenChange}
        title="Delete uploaded ZIP?"
        description="This removes the retained archive and disposable validation material from this MEM host. The action cannot be undone."
        confirmLabel="Delete uploaded ZIP"
        confirmingLabel="Deleting uploaded ZIP..."
        confirmVariant="destructive"
        onConfirm={confirmDelete}
        isConfirming={deleteSource.isPending}
      >
        <div className="grid gap-3 rounded-lg border border-border bg-background/35 p-3 text-sm sm:grid-cols-2">
          <DeleteContext label="Uploaded file" value={source.uploadedFileName} />
          <DeleteContext label="Source reference" value={source.validationId} mono />
          <DeleteContext
            label="Storage to reclaim"
            value={
              source.archiveBytes === null
                ? "Not recorded"
                : formatBytes(source.archiveBytes)
            }
          />
          <DeleteContext label="Source type" value="Uploaded ZIP" />
        </div>

        <Alert>
          <Trash2 className="h-4 w-4" />
          <AlertTitle>What stays intact</AlertTitle>
          <AlertDescription>
            Restore sessions, logs, evidence, support reports, and any restored
            stack remain available. A retained workspace becomes audit-only, and
            recovery work cannot continue from this archive unless the ZIP is
            uploaded again.
          </AlertDescription>
        </Alert>

        {deleteSource.error ? (
          <Alert variant="destructive">
            <AlertTitle>Could not delete uploaded ZIP</AlertTitle>
            <AlertDescription>{deleteSource.error.message}</AlertDescription>
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
      <div className={mono ? "mt-1 break-all font-mono text-sm" : "mt-1 break-words text-sm"}>
        {value}
      </div>
    </div>
  )
}
