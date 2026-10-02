import { useEffect, useState } from "react"
import { Trash2 } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import { isStepUpRequiredHostAgentProblem } from "@/features/operator/backups/api/transport/host-agent"
import { useDeleteBackupCatalogEntry } from "@/features/operator/backups/hooks/use-backup-catalog"
import { formatBytes } from "@/features/operator/backups/shared/components/backup-formatting"

type Props = {
  catalogEntryId: string
  displayName: string
  payloadBytes: number | null
  payloadPresent: boolean
  onDeleted?: () => void
}

/**
 * The file name is retained for compatibility with existing import paths. The
 * action itself is intentionally a true catalog deletion, not a payload-only
 * lifecycle transition.
 *
 * A destructive confirmation alone is not sufficient authority. If the
 * server requires recent step-up, the already-confirmed deletion pauses while
 * the operator provides password plus current TOTP, then resumes only this
 * deletion after the server-side grant succeeds.
 */
export function DeleteCatalogEntryAction({
  catalogEntryId,
  displayName,
  payloadBytes,
  payloadPresent,
  onDeleted,
}: Props) {
  const { t, language } = useI18n()
  const [open, setOpen] = useState(false)
  const [isStepUpOpen, setIsStepUpOpen] = useState(false)
  const [stepUpPending, setStepUpPending] = useState(false)
  const deletion = useDeleteBackupCatalogEntry()
  const purgeOnly = !payloadPresent

  const buttonLabel = purgeOnly
    ? t("backupArtifacts.deleteEntry.purgeButton")
    : t("backupArtifacts.deleteEntry.deleteButton")
  const title = purgeOnly
    ? t("backupArtifacts.deleteEntry.purgeTitle")
    : t("backupArtifacts.deleteEntry.deleteTitle")

  useEffect(() => {
    if (!stepUpPending || open) {
      return
    }

    // Radix AlertDialog temporarily disables pointer interaction outside its
    // content. Close that confirmation first, then open the independent
    // viewport-level verifier on the next task after Radix has restored the
    // document interaction state. The already-confirmed deletion is held only
    // in this component's memory and is retried only after step-up succeeds.
    const timer = window.setTimeout(() => {
      setIsStepUpOpen(true)
    }, 0)

    return () => {
      window.clearTimeout(timer)
    }
  }, [open, stepUpPending])

  function changeOpen(nextOpen: boolean) {
    if (nextOpen) {
      deletion.reset()
      setStepUpPending(false)
      setIsStepUpOpen(false)
    }

    setOpen(nextOpen)
  }

  function changeStepUpOpen(nextOpen: boolean) {
    setIsStepUpOpen(nextOpen)

    if (!nextOpen) {
      setStepUpPending(false)
    }
  }

  function performDeletion() {
    deletion.mutate(
      { catalogEntryId },
      {
        onSuccess: () => {
          setOpen(false)
          setStepUpPending(false)
          setIsStepUpOpen(false)
          onDeleted?.()
        },
        onError: (error) => {
          if (isStepUpRequiredHostAgentProblem(error)) {
            // The action was explicitly confirmed, but we must not keep a
            // Radix modal active beneath the independent credential dialog:
            // it makes the verifier intentionally inert. Closing it does not
            // broaden authority; this exact deletion remains the only queued
            // continuation and is retried only after server-side step-up.
            deletion.reset()
            setOpen(false)
            setStepUpPending(true)
          }
        },
      },
    )
  }

  function resumeConfirmedDeletion() {
    setStepUpPending(false)
    performDeletion()
  }

  return (
    <>
      <Button
        type="button"
        variant="destructive"
        size="sm"
        onClick={() => changeOpen(true)}
        disabled={deletion.isPending}
      >
        <Trash2 className="mr-2 h-4 w-4" />
        {buttonLabel}
      </Button>
      <ConfirmationDialog
        open={open}
        onOpenChange={changeOpen}
        title={title}
        description={purgeOnly
          ? t("backupArtifacts.deleteEntry.purgeDescription")
          : t("backupArtifacts.deleteEntry.deleteDescription")}
        confirmLabel={buttonLabel}
        confirmingLabel={purgeOnly
          ? t("backupArtifacts.deleteEntry.purgeConfirming")
          : t("backupArtifacts.deleteEntry.deleteConfirming")}
        confirmVariant="destructive"
        onConfirm={performDeletion}
        isConfirming={deletion.isPending}
      >
        <div className="rounded-md border p-3 text-sm">
          <div className="font-medium break-words">{displayName}</div>
          <div className="mt-1 font-mono text-xs text-muted-foreground break-all">{catalogEntryId}</div>
          {payloadBytes !== null ? (
            <div className="mt-2 text-muted-foreground">
              {t("backupArtifacts.deleteEntry.payloadSize", { size: formatBytes(payloadBytes, language) })}
            </div>
          ) : null}
        </div>
        <Alert variant="destructive">
          <Trash2 className="h-4 w-4" />
          <AlertTitle>{t("backupArtifacts.deleteEntry.cannotUndo")}</AlertTitle>
          <AlertDescription>
            {purgeOnly
              ? t("backupArtifacts.deleteEntry.purgeEffect")
              : t("backupArtifacts.deleteEntry.deleteEffect")}
          </AlertDescription>
        </Alert>
        <Alert>
          <AlertTitle>{t("backupArtifacts.deleteEntry.intactTitle")}</AlertTitle>
          <AlertDescription>{t("backupArtifacts.deleteEntry.intactDescription")}</AlertDescription>
        </Alert>
        {deletion.error && !isStepUpRequiredHostAgentProblem(deletion.error) ? (
          <Alert variant="destructive">
            <AlertTitle>{t("backupArtifacts.deleteEntry.error")}</AlertTitle>
            <AlertDescription>{deletion.error.message}</AlertDescription>
          </Alert>
        ) : null}
      </ConfirmationDialog>
      <OperatorStepUpDialog
        open={isStepUpOpen}
        onOpenChange={changeStepUpOpen}
        onVerified={resumeConfirmedDeletion}
      />
    </>
  )
}
