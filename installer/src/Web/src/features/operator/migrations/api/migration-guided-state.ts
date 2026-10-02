import type { MigrationWorkspaceResponse } from "./migration-workspace"

export type GuidedOperation = "upload-package" | "conversion" | "private-test" |
  "create-new-server" | "review-go-live" | "make-server-live" | "finish-migration" | "baseline-backup" | "cancel-migration"

export type PendingGuidedOperation = {
  migrationId: string
  operation: GuidedOperation
  before: string
}

export const pendingGuidedOperationKey = (migrationId: string) =>
  `mem:migration-guided-operation:${migrationId}`

const operations = new Set<GuidedOperation>(["upload-package", "conversion", "private-test",
  "create-new-server", "review-go-live", "make-server-live", "finish-migration", "baseline-backup", "cancel-migration"])

export function readPendingGuidedOperation(migrationId: string): PendingGuidedOperation | null {
  try {
    const value: unknown = JSON.parse(sessionStorage.getItem(pendingGuidedOperationKey(migrationId)) ?? "null")
    if (typeof value !== "object" || value === null) return null
    const pending = value as Partial<PendingGuidedOperation>
    return pending.migrationId === migrationId && typeof pending.before === "string" &&
      pending.before.length > 0 && typeof pending.operation === "string" && operations.has(pending.operation)
      ? pending as PendingGuidedOperation : null
  } catch { return null }
}

export function writePendingGuidedOperation(migrationId: string, pending: PendingGuidedOperation | null) {
  try {
    // Only public migration/operation IDs and a server-authored content token.
    // Never persist confirmation bodies, files, passwords, or step-up credentials.
    if (pending) sessionStorage.setItem(pendingGuidedOperationKey(migrationId), JSON.stringify(pending))
    else sessionStorage.removeItem(pendingGuidedOperationKey(migrationId))
  } catch { /* Storage can be disabled; in-memory single-flight protection remains. */ }
}

export function hasGuidedOperationObservation(
  pending: PendingGuidedOperation, workspace: MigrationWorkspaceResponse,
) {
  const observed = workspace.guided.operationRevisions[pending.operation]
  // A cancellation is settled only by its own durable terminal state. Uploads,
  // expiry, a new conversion or a global revision change are not cancellation.
  if (pending.operation === "cancel-migration" && workspace.guided.cancellation?.lifecycleStatus !== "cancelled") return false
  return workspace.migration.migrationId === pending.migrationId &&
    typeof observed === "string" && observed.length > 0 && observed !== pending.before
}

export function isDefinitiveGuidedRejection(error: unknown, operation?: GuidedOperation) {
  if (typeof error !== "object" || error === null || !("status" in error)) return false
  const status = error.status
  // The lifecycle endpoint explicitly rejects these conditions before committing
  // cancellation. Keep this exception scoped to that endpoint; a generic 409
  // from a long-running migration operation remains uncertain.
  if (operation === "cancel-migration" && status === 409 && "code" in error &&
    typeof error.code === "string" && cancellationRejections.has(error.code)) return true
  if (operation === "cancel-migration" && status === 503 && "code" in error &&
    typeof error.code === "string" && cancellationCleanupRejections.has(error.code)) return true
  // 408/409 and 5xx can describe a command which has already crossed durable
  // acceptance. They require observation, never a blind retry.
  return typeof status === "number" && status >= 400 && status < 500 &&
    status !== 408 && status !== 409
}

export class MigrationOutcomeUncertain extends Error {
  constructor() { super("Migration operation outcome is being checked.") }
}

export const isMigrationOutcomeUncertain = (error: unknown) => error instanceof MigrationOutcomeUncertain

export function isGuidedActionAllowed(workspace: MigrationWorkspaceResponse, ...codes: string[]) {
  const next = workspace.guided.nextAction
  return workspace.guided.uncertaintyState === "known" &&
    next?.enabled === true && next.relatedStage === workspace.guided.currentStageCode && codes.includes(next.code)
}

const cancellationRejections = new Set([
  "migration_session_state_stale", "migration_session_archived", "migration_session_completed",
  "migration_session_production_owned", "migration_session_staging_retained", "migration_session_conversion_exists",
  "migration_session_conversion_active",
  "migration_session_lifecycle_terminal", "migration_session_package_state_not_eligible", "migration_session_cancel_not_allowed",
])

const cancellationCleanupRejections = new Set([
  "migration_session_package_cleanup_failed",
  "migration_session_progressed_cleanup_failed",
])

export function isGuidedCancellationAllowed(workspace: MigrationWorkspaceResponse) {
  const cancellation = workspace.guided.cancellation
  return workspace.guided.uncertaintyState === "known" && cancellation?.canCancel === true &&
    Number.isSafeInteger(cancellation.stateVersion) && cancellation.stateVersion > 0 &&
    (cancellation.confirmationKind === "empty-session" || cancellation.confirmationKind === "package" ||
      cancellation.confirmationKind === "progressed") &&
    Boolean(workspace.guided.operationRevisions["cancel-migration"])
}
