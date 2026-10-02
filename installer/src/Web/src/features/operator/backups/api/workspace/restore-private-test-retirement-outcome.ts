import { getRestoreWorkspace } from "./restore-workspace.api"

import type {
  RestoreWorkspaceResponse,
  RestoreWorkspaceStandardStage,
} from "../types/restore-workspace.types"

export type RestorePrivateTestRetirementOutcome =
  | Readonly<{ kind: "destroyed"; workspace: RestoreWorkspaceResponse }>
  | Readonly<{ kind: "needs-attention"; workspace: RestoreWorkspaceResponse }>

type ReadWorkspace = (
  restoreSessionId: string,
) => Promise<RestoreWorkspaceResponse>

type ReconcileRestorePrivateTestRetirementOptions = {
  attempts?: number
  delayMs?: number
  readWorkspace?: ReadWorkspace
  wait?: (milliseconds: number) => Promise<void>
}

const DEFAULT_ATTEMPTS = 12
const DEFAULT_DELAY_MS = 750

/**
 * Retirement is an accepted destructive host mutation. Browser transport loss
 * does not prove that cleanup failed. Re-read the canonical Restore Workspace
 * until the matching staging runtime is authoritatively destroyed or the
 * backend records a retirement-needs-attention state.
 */
export async function reconcileRestorePrivateTestRetirementOutcome(
  restoreSessionId: string,
  stagingId: string,
  options: ReconcileRestorePrivateTestRetirementOptions = {},
): Promise<RestorePrivateTestRetirementOutcome | null> {
  const attempts = Math.max(1, options.attempts ?? DEFAULT_ATTEMPTS)
  const delayMs = Math.max(0, options.delayMs ?? DEFAULT_DELAY_MS)
  const readWorkspace = options.readWorkspace ?? getRestoreWorkspace
  const wait = options.wait ?? delay

  for (let attempt = 0; attempt < attempts; attempt += 1) {
    try {
      const workspace = await readWorkspace(restoreSessionId)
      const outcome = classifyRestorePrivateTestRetirementOutcome(
        workspace,
        stagingId,
      )

      if (outcome) {
        return outcome
      }
    } catch {
      // The same network boundary that made the destructive POST inconclusive
      // can temporarily affect read-only reconciliation. Keep this bounded.
    }

    if (attempt < attempts - 1) {
      await wait(delayMs)
    }
  }

  return null
}

export function classifyRestorePrivateTestRetirementOutcome(
  workspace: RestoreWorkspaceResponse,
  stagingId: string,
): RestorePrivateTestRetirementOutcome | null {
  const stage = getPrivateTestStage(workspace)
  const evidence = stage?.privateTestEvidence

  if (!evidence || evidence.stagingId !== stagingId) {
    return null
  }

  const runtimeStatus = evidence.stagingRuntimeStatus.trim().toLowerCase()

  if (
    evidence.stagingRuntimeDestroyed === true ||
    runtimeStatus === "destroyed" ||
    runtimeStatus === "retired"
  ) {
    return { kind: "destroyed", workspace }
  }

  if (runtimeStatus === "retirement-needs-attention") {
    return { kind: "needs-attention", workspace }
  }

  return null
}

function getPrivateTestStage(
  workspace: RestoreWorkspaceResponse,
): RestoreWorkspaceStandardStage | undefined {
  return workspace.standardStages.find((stage) => stage.code === "private-test")
}

function delay(milliseconds: number) {
  return new Promise<void>((resolve) => {
    window.setTimeout(resolve, milliseconds)
  })
}
