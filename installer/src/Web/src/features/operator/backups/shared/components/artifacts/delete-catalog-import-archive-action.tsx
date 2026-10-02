import { useState } from "react"
import { Trash2 } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import { useDeleteImportedZipArchive } from "@/features/operator/backups/hooks/use-backup-catalog"
import { formatBytes } from "@/features/operator/backups/shared/components/backup-formatting"

type Props = {
  validationId: string
  fileName: string
  archiveBytes: number | null
  onDeleted?: () => void
}

export function DeleteCatalogImportArchiveAction({ validationId, fileName, archiveBytes, onDeleted }: Props) {
  const { t, language } = useI18n()
  const [open, setOpen] = useState(false)
  const deletion = useDeleteImportedZipArchive()

  function changeOpen(nextOpen: boolean) {
    if (nextOpen) deletion.reset()
    setOpen(nextOpen)
  }

  return (
    <>
      <Button type="button" variant="destructive" size="sm" onClick={() => changeOpen(true)} disabled={deletion.isPending}>
        <Trash2 className="mr-2 h-4 w-4" />
        {t("backupArtifacts.deleteArchive.button")}
      </Button>
      <ConfirmationDialog
        open={open}
        onOpenChange={changeOpen}
        title={t("backupArtifacts.deleteArchive.title")}
        description={t("backupArtifacts.deleteArchive.description")}
        confirmLabel={t("backupArtifacts.deleteArchive.button")}
        confirmingLabel={t("backupArtifacts.deleteArchive.confirming")}
        confirmVariant="destructive"
        onConfirm={() => deletion.mutate({ validationId }, { onSuccess: () => { setOpen(false); onDeleted?.() } })}
        isConfirming={deletion.isPending}
      >
        <div className="rounded-md border p-3 text-sm">
          <div className="font-medium break-words">{fileName}</div>
          <div className="mt-1 font-mono text-xs text-muted-foreground break-all">{validationId}</div>
          {archiveBytes !== null ? (
            <div className="mt-2 text-muted-foreground">
              {t("backupArtifacts.deleteArchive.size", { size: formatBytes(archiveBytes, language) })}
            </div>
          ) : null}
        </div>
        <Alert>
          <Trash2 className="h-4 w-4" />
          <AlertTitle>{t("backupArtifacts.deleteArchive.intactTitle")}</AlertTitle>
          <AlertDescription>{t("backupArtifacts.deleteArchive.intactDescription")}</AlertDescription>
        </Alert>
        {deletion.error ? (
          <Alert variant="destructive">
            <AlertTitle>{t("backupArtifacts.deleteArchive.error")}</AlertTitle>
            <AlertDescription>{deletion.error.message}</AlertDescription>
          </Alert>
        ) : null}
      </ConfirmationDialog>
    </>
  )
}
