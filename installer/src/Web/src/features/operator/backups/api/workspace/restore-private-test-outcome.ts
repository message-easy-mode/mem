import { getRestoreWorkspace } from "./restore-workspace.api"

import type {
  RestoreWorkspaceResponse,
  RestoreWorkspaceStandardStage,
} from "../types/restore-workspace.types"

export type RestorePrivateTestBaseline = Readonly<{
  operationId: string | null
  completedAtUtc: string | null
}>

export type RestorePrivateTestOutcome =
  | Readonly<{ kind: "running"; workspace: RestoreWorkspaceResponse }>
  | Readonly<{ kind: "completed"; workspace: RestoreWorkspaceResponse }>
  | Readonly<{ kind: "failed"; workspace: RestoreWorkspaceResponse }>

type ReadWorkspace = (
  restoreSessionId: string,
) => Promise<RestoreWorkspaceResponse>

type ReconcileRestorePrivateTestOptions = {
  attempts?: number
  delayMs?: number
  readWorkspace?: ReadWorkspace
  wait?: (milliseconds: number) => Promise<void>
}

const DEFAULT_ATTEMPTS = 12
const DEFAULT_DELAY_MS = 750

/**
 * Capture only durable private-test identity before the mutation. Reconciliation
 * must prove that a later workspace reflects a newer operation/evidence record;
 * it must not mistake a historical failed/completed private test for the
 * outcome of the operator's newest click.
 */
export function captureRestorePrivateTestBaseline(
  workspace: RestoreWorkspaceResponse,
): RestorePrivateTestBaseline {
  const stage = getPrivateTestStage(workspace)

  return {
    operationId: stage?.operationSummary?.operationId ?? null,
    completedAtUtc: stage?.privateTestEvidence?.completedAtUtc ?? null,
  }
}

/**
 * A browser transport failure does not prove that the accepted private-test
 * mutation failed. Poll the authoritative Restore Workspace briefly until the
 * server proves that a new private-test operation is running, completed, or
 * genuinely failed. Ongoing running operations are then followed by the normal
 * workspace query polling contract.
 */
export async function reconcileRestorePrivateTestOutcome(
  restoreSessionId: string,
  baseline: RestorePrivateTestBaseline,
  options: ReconcileRestorePrivateTestOptions = {},
): Promise<RestorePrivateTestOutcome | null> {
  const attempts = Math.max(1, options.attempts ?? DEFAULT_ATTEMPTS)
  const delayMs = Math.max(0, options.delayMs ?? DEFAULT_DELAY_MS)
  const readWorkspace = options.readWorkspace ?? getRestoreWorkspace
  const wait = options.wait ?? delay

  for (let attempt = 0; attempt < attempts; attempt += 1) {
    try {
      const workspace = await readWorkspace(restoreSessionId)
      const outcome = classifyRestorePrivateTestOutcome(workspace, baseline)

      if (outcome) {
        return outcome
      }
    } catch {
      // The same browser/network boundary that made the POST inconclusive can
      // temporarily affect read-only reconciliation. Keep the check bounded.
    }

    if (attempt < attempts - 1) {
      await wait(delayMs)
    }
  }

  return null
}

export function classifyRestorePrivateTestOutcome(
  workspace: RestoreWorkspaceResponse,
  baseline: RestorePrivateTestBaseline,
): RestorePrivateTestOutcome | null {
  const stage = getPrivateTestStage(workspace)
  if (!stage) {
    return null
  }

  const evidenceCompletedAt = stage.privateTestEvidence?.completedAtUtc ?? null
  if (
    stage.privateTestEvidence &&
    evidenceCompletedAt &&
    evidenceCompletedAt !== baseline.completedAtUtc
  ) {
    return { kind: "completed", workspace }
  }

  const operation = stage.operationSummary
  if (!operation || operation.operationId === baseline.operationId) {
    return null
  }

  const status = operation.status.trim().toLowerCase()

  if (["running", "queued", "active"].includes(status)) {
    return { kind: "running", workspace }
  }

  if (["succeeded", "completed", "success"].includes(status)) {
    return { kind: "completed", workspace }
  }

  if (["failed", "cancelled", "canceled"].includes(status)) {
    return { kind: "failed", workspace }
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
