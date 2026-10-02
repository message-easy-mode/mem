import {
  createMigrationPrivateServer,
  getMigrationProductionAdoptionState,
  makeMigrationServerLive,
  MigrationProductionAdoptionProblemError,
  type CreateMigrationPrivateServerRequest,
  type MakeMigrationServerLiveRequest,
  type MigrationProductionAdoptionState,
} from "./migration-production-adoption"
import { startMigrationOperationWithReconciliation } from "./migration-start-reconciliation"

const isHttpProblem = (error: unknown) => error instanceof MigrationProductionAdoptionProblemError

/** A prepared plan is NOT evidence that materialization was accepted. */
export function createPrivateServerWithReconciliation(
  migrationId: string,
  request: CreateMigrationPrivateServerRequest,
): Promise<MigrationProductionAdoptionState> {
  return startMigrationOperationWithReconciliation({
    list: async () => {
      const state = await getMigrationProductionAdoptionState(migrationId)
      return state.plan?.materialization.materializationId ? [state] : []
    },
    start: () => createMigrationPrivateServer(migrationId, request),
    identity: (state) => state.plan?.materialization.materializationId ?? "",
    matches: (state) => state.migrationId === migrationId &&
      Boolean(request.targetStackSlug?.trim() && request.elementPublicHost?.trim()) &&
      state.plan?.targetStackSlug === request.targetStackSlug?.trim() &&
      state.plan?.elementPublicHost === request.elementPublicHost?.trim() &&
      Boolean(state.plan?.materialization.materializationId),
    isHttpProblem,
  })
}

/** Bind a lost go-live response to new execution/verification evidence on the same plan. */
export function makeServerLiveWithReconciliation(
  migrationId: string,
  request: MakeMigrationServerLiveRequest,
): Promise<MigrationProductionAdoptionState> {
  let observedBeforePost = false
  let expectedPlanId: string | null = null

  return startMigrationOperationWithReconciliation({
    list: async () => {
      const state = await getMigrationProductionAdoptionState(migrationId)
      if (!observedBeforePost) {
        observedBeforePost = true
        expectedPlanId = state.plan?.adoptionPlanId ?? null
      }
      return state.plan?.cutover.execution.executionId || state.plan?.productionVerification.verificationId
        ? [state]
        : []
    },
    start: () => makeMigrationServerLive(migrationId, request),
    identity: (state) => JSON.stringify([
      state.plan?.cutover.execution.executionId,
      state.plan?.productionVerification.verificationId,
    ]),
    matches: (state) => state.migrationId === migrationId &&
      Boolean(expectedPlanId) && state.plan?.adoptionPlanId === expectedPlanId,
    isHttpProblem,
  })
}
