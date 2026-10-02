import { controlPlaneGet, controlPlanePost, BACKUPS_BASE } from "../transport/host-agent"
import type {
  RestoreWorkspaceAttempt,
  RestoreWorkspacePrivateTestActionResponse,
  RestoreWorkspaceResponse,
  RestoreWorkspaceStandardRecreateActionRequest,
  RestoreWorkspaceStandardRecreateActionResponse,
} from "../types/restore-workspace.types"

export const restoreWorkspaceApiRoutes = {
  workspace: (restoreSessionId: string) =>
    `${BACKUPS_BASE}/restores/${encodeURIComponent(restoreSessionId)}/workspace`,
  privateTest: (restoreSessionId: string) =>
    `${BACKUPS_BASE}/restores/${encodeURIComponent(restoreSessionId)}/private-test`,
  standardRecreate: (restoreSessionId: string) =>
    `${BACKUPS_BASE}/restores/${encodeURIComponent(restoreSessionId)}/standard-recreate`,
  completeHandover: (restoreSessionId: string) =>
    `${BACKUPS_BASE}/restores/${encodeURIComponent(restoreSessionId)}/complete-handover?acknowledgeCompletion=true`,
  cancel: (restoreSessionId: string) =>
    `${BACKUPS_BASE}/restores/${encodeURIComponent(restoreSessionId)}/cancel?acknowledgeCancel=true`,
} as const

/**
 * Safe, read-only projection for a durable restore attempt.
 * Do not substitute legacy validation-ID workflow summaries for this response.
 */
export function getRestoreWorkspace(restoreSessionId: string) {
  return controlPlaneGet<RestoreWorkspaceResponse>(
    restoreWorkspaceApiRoutes.workspace(restoreSessionId),
  )
}

/**
 * Starts the source-aware private test for a canonical Restore Workspace.
 * The backend resolves the attempt source; callers must not infer this from
 * a legacy validation identifier.
 */
export function runRestoreWorkspacePrivateTest(restoreSessionId: string) {
  return controlPlanePost<undefined, RestoreWorkspacePrivateTestActionResponse>(
    restoreWorkspaceApiRoutes.privateTest(restoreSessionId),
  )
}


/**
 * Executes Standard Recreate from a canonical Restore Workspace. The endpoint
 * resolves the managed Backup Catalog entry from restoreSessionId.
 */
export function executeRestoreWorkspaceStandardRecreate(
  request: RestoreWorkspaceStandardRecreateActionRequest,
) {
  const query = new URLSearchParams()
  query.set("targetStackSlug", request.targetStackSlug)
  query.set("elementHost", request.elementHost)
  query.set(
    "executeProductionRecreate",
    request.executeProductionRecreate ? "true" : "false",
  )
  query.set(
    "acknowledgeCreatesRealStack",
    request.acknowledgeCreatesRealStack ? "true" : "false",
  )
  query.set(
    "acknowledgeMutatesProductionPostgres",
    request.acknowledgeMutatesProductionPostgres ? "true" : "false",
  )
  query.set(
    "acknowledgeMutatesNpmRoutes",
    request.acknowledgeMutatesNpmRoutes ? "true" : "false",
  )
  query.set(
    "acknowledgeNoAutomaticRollback",
    request.acknowledgeNoAutomaticRollback ? "true" : "false",
  )

  if (request.requestedDomainId?.trim()) {
    query.set("requestedDomainId", request.requestedDomainId.trim())
  }
  if (request.matrixImage?.trim()) {
    query.set("matrixImage", request.matrixImage.trim())
  }
  if (request.elementImage?.trim()) {
    query.set("elementImage", request.elementImage.trim())
  }
  if (request.operator?.trim()) {
    query.set("operatorName", request.operator.trim())
  }
  if (request.note?.trim()) {
    query.set("note", request.note.trim())
  }

  return controlPlanePost<
    undefined,
    RestoreWorkspaceStandardRecreateActionResponse
  >(`${restoreWorkspaceApiRoutes.standardRecreate(request.restoreSessionId)}?${query.toString()}`)
}

/**
 * Cancels a non-terminal canonical Restore Workspace. The backend releases
 * temporary claims only after re-checking that no queued or running operation
 * can be left half-mutated. The source backup and audit history are retained.
 */
export function cancelRestoreWorkspace(restoreSessionId: string) {
  return controlPlanePost<undefined, RestoreWorkspaceAttempt>(
    restoreWorkspaceApiRoutes.cancel(restoreSessionId),
  )
}

/** Deliberate terminal audit transition after a successful public check. */
export function completeRestoreHandover(restoreSessionId: string) {
  return controlPlanePost<undefined, unknown>(
    restoreWorkspaceApiRoutes.completeHandover(restoreSessionId),
  )
}
