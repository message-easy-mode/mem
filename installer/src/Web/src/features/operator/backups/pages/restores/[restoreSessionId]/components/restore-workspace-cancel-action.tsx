import { useState } from "react"

import { useI18n } from "@/app/i18n/i18n-context"
import { AlertCircle, LoaderCircle, XCircle } from "lucide-react"

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import type { RestoreWorkspaceResponse } from "@/features/operator/backups/api/types/restore-workspace.types"
import { useCancelRestoreWorkspace } from "@/features/operator/backups/hooks/use-restore-workspace"
import {
  getRestoreWorkspaceProblem,
  getRestoreWorkspaceProblemMessage,
  getRestoreWorkspaceTechnicalDetail,
  RestoreWorkspaceTechnicalDetails,
} from "./restore-workspace-problems"

type Props = {
  workspace: RestoreWorkspaceResponse
  onCancelled: () => Promise<unknown>
}

/**
 * Explicit terminal action for a still-active restore workspace. The server
 * projects eligibility and repeats its safety checks at mutation time; this
 * component never infers eligibility from catalog or validation identifiers.
 */
export function RestoreWorkspaceCancelAction({
  workspace,
  onCancelled,
}: Props) {
  const { t } = useI18n()
  const [open, setOpen] = useState(false)
  const cancelRestore = useCancelRestoreWorkspace()
  const cancellation = workspace.cancellation
  const cancellationProblem = getRestoreWorkspaceProblem(cancelRestore.error)
  const localisedCancellationProblem = getRestoreWorkspaceProblemMessage(
    cancellationProblem,
  )
  const cancellationTechnicalDetail = getRestoreWorkspaceTechnicalDetail(
    cancelRestore.error,
    cancellationProblem,
  )
  const isTerminal = ["cancelled", "completed", "abandoned", "superseded"].includes(
    workspace.attempt.status.toLowerCase(),
  )
  const canCancel = cancellation?.canCancel === true && !cancelRestore.isPending

  // Older API responses do not expose cancellation capability. Hiding the
  // action is safer than guessing from the attempt status.
  if (!cancellation || isTerminal) {
    return null
  }

  const confirmCancellation = async () => {
    if (!canCancel) return

    try {
      await cancelRestore.mutateAsync(workspace.restoreSessionId)
      setOpen(false)
      await onCancelled()
    } catch {
      // Keep the confirmation open and render the safe API failure below.
    }
  }

  if (!cancellation.canCancel) {
    return (
      <div className="hidden max-w-xs text-right text-xs leading-5 text-muted-foreground md:block">
        {cancellation.reasonUnavailable ?? cancellation.summary}
      </div>
    )
  }

  return (
    <>
      <Button
        type="button"
        variant="destructive"
        size="sm"
        onClick={() => setOpen(true)}
        disabled={cancelRestore.isPending}
      >
        {cancelRestore.isPending ? (
          <LoaderCircle className="mr-2 h-4 w-4 animate-spin" />
        ) : (
          <XCircle className="mr-2 h-4 w-4" />
        )}
        {t("restoreWorkspace.cancel.action")}
      </Button>

      <ConfirmationDialog
        open={open}
        onOpenChange={setOpen}
        title={t("restoreWorkspace.cancel.title")}
        description={t("restoreWorkspace.cancel.description")}
        confirmLabel={t("restoreWorkspace.cancel.action")}
        confirmingLabel={t("restoreWorkspace.cancel.confirming")}
        confirmVariant="destructive"
        onConfirm={() => void confirmCancellation()}
        isConfirming={cancelRestore.isPending}
      >
        <Alert>
          <AlertCircle className="h-4 w-4" />
          <AlertTitle>{t("restoreWorkspace.cancel.changes")}</AlertTitle>
          <AlertDescription>
            {t("restoreWorkspace.cancel.changesDescription")}
          </AlertDescription>
        </Alert>
        {cancelRestore.isError ? (
          <Alert variant="destructive">
            <AlertCircle className="h-4 w-4" />
            <AlertTitle>{t("restoreWorkspace.cancel.error")}</AlertTitle>
            <AlertDescription>
              <p>
                {localisedCancellationProblem
                  ? t(
                      localisedCancellationProblem.key,
                      localisedCancellationProblem.values,
                    )
                  : t("restoreWorkspace.cancel.errorDescription")}
              </p>
              <RestoreWorkspaceTechnicalDetails
                detail={cancellationTechnicalDetail}
              />
            </AlertDescription>
          </Alert>
        ) : null}
      </ConfirmationDialog>
    </>
  )
}
