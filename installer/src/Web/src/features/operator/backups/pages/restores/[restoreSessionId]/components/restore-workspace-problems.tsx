import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey, TranslationValues } from "@/app/i18n/messages"
import {
  isHostAgentProblemError,
  type HostAgentProblem,
} from "@/features/operator/backups/api/transport/host-agent"

const restoreWorkspaceProblemTranslations: Readonly<
  Record<string, TranslationKey>
> = {
  "restore.attempt.not-found": "restoreWorkspace.api.attemptNotFound",
  "restore.workspace-request.invalid":
    "restoreWorkspace.api.workspaceRequestInvalid",
  "restore.private-test.not-available":
    "restoreWorkspace.api.privateTestNotAvailable",
  "restore.cancel.acknowledgement-required":
    "restoreWorkspace.api.cancelAcknowledgementRequired",
  "restore.cancel.not-available": "restoreWorkspace.api.cancelNotAvailable",
  "restore.handover.acknowledgement-required":
    "restoreWorkspace.api.handoverAcknowledgementRequired",
  "restore.handover.not-available": "restoreWorkspace.api.handoverNotAvailable",
}

export type RestoreWorkspaceProblemMessage = Readonly<{
  key: TranslationKey
  values: TranslationValues
}>

/**
 * Restore Workspace owns this explicit code-to-Web-key map. It never derives
 * a browser translation key from a server code or treats raw detail as UI
 * copy, so old and newer HostAgent versions both retain a safe fallback.
 */
export function getRestoreWorkspaceProblemMessage(
  problem?: HostAgentProblem,
): RestoreWorkspaceProblemMessage | undefined {
  const message = problem?.message
  if (!message) {
    return undefined
  }

  const key = restoreWorkspaceProblemTranslations[message.code]
  const restoreSessionId = message.arguments?.restoreSessionId
  if (!key || typeof restoreSessionId !== "string") {
    return undefined
  }

  return {
    key,
    values: { restoreSessionId },
  }
}

export function getRestoreWorkspaceProblem(
  error: unknown,
): HostAgentProblem | undefined {
  return isHostAgentProblemError(error) ? error.problem : undefined
}

/**
 * Prefer the server-provided raw diagnostic detail. Older, malformed, and
 * non-HostAgent failures retain their existing Error message as a support
 * fallback without presenting it as translated browser UI.
 */
export function getRestoreWorkspaceTechnicalDetail(
  error: unknown,
  problem = getRestoreWorkspaceProblem(error),
) {
  if (problem?.detail) {
    return problem.detail
  }

  return error instanceof Error && error.message.trim().length > 0
    ? error.message
    : undefined
}

export function RestoreWorkspaceTechnicalDetails({
  detail,
}: {
  detail?: string
}) {
  const { t } = useI18n()

  if (!detail) {
    return null
  }

  return (
    <details className="mt-3 rounded-md border border-current/20 bg-background/40 px-3 py-2 text-foreground">
      <summary className="cursor-pointer font-medium">
        {t("common.technicalDetails")}
      </summary>
      <div className="mt-2 break-words whitespace-pre-wrap font-mono text-xs text-muted-foreground">
        {detail}
      </div>
    </details>
  )
}
