import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { backupCatalogKeys } from "@/features/operator/backups/hooks/use-backup-catalog"
import {
  acceptMigration,
  finishMigration,
  getMigrationAcceptanceState,
  MigrationAcceptanceProblemError,
  retryMigrationBaselineBackup,
  type AcceptMigrationRequest,
  type FinishMigrationRequest,
} from "@/features/operator/migrations/api/migration-acceptance"
import { migrationCutoverKeys } from "@/features/operator/migrations/hooks/use-migration-cutover"
import { migrationProductionAdoptionKeys } from "@/features/operator/migrations/hooks/use-migration-production-adoption"
import { migrationSessionKeys } from "@/features/operator/migrations/hooks/use-migration-sessions"
import { runtimeStackKeys } from "@/features/operator/stacks/hooks/use-runtime-stacks"

export const migrationAcceptanceKeys = {
  all: ["migration-acceptance"] as const,
  state: (migrationId: string) => [...migrationAcceptanceKeys.all, migrationId] as const,
}

export function useMigrationAcceptanceState(migrationId: string, enabled = true) {
  return useQuery({
    queryKey: migrationAcceptanceKeys.state(migrationId),
    queryFn: () => getMigrationAcceptanceState(migrationId),
    staleTime: 5_000,
    retry: 0,
    enabled,
    refetchInterval: (query) => {
      const state = query.state.data
      return state?.accepted &&
        (!state.baselineBackup || state.baselineBackup.status === "pending")
        ? 3_000
        : false
    },
  })
}


async function finishMigrationWithReconciliation(
  migrationId: string,
  request: FinishMigrationRequest,
) {
  try {
    return await finishMigration(migrationId, request)
  } catch (caught) {
    // An HTTP problem response is authoritative and should keep its normal
    // step-up/blocker handling. A fetch/response failure is ambiguous because
    // acceptance becomes durable before the baseline backup completes.
    if (caught instanceof MigrationAcceptanceProblemError) {
      throw caught
    }

    try {
      const current = await getMigrationAcceptanceState(migrationId)
      if (current.accepted) {
        return current
      }
    } catch {
      // Preserve the initiating transport failure when reconciliation itself
      // cannot establish a durable accepted state.
    }

    throw caught
  }
}

export function useFinishMigration(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: FinishMigrationRequest) =>
      finishMigrationWithReconciliation(migrationId, request),
    onSuccess: async (state) => {
      queryClient.setQueryData(migrationAcceptanceKeys.state(migrationId), state)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: migrationCutoverKeys.state(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationProductionAdoptionKeys.state(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.detail(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.list() }),
        queryClient.invalidateQueries({ queryKey: runtimeStackKeys.all }),
        queryClient.invalidateQueries({ queryKey: backupCatalogKeys.all }),
      ])
    },
  })
}

export function useAcceptMigration(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: AcceptMigrationRequest) => acceptMigration(migrationId, request),
    onSuccess: async (state) => {
      queryClient.setQueryData(migrationAcceptanceKeys.state(migrationId), state)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: migrationCutoverKeys.state(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationProductionAdoptionKeys.state(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.detail(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.list() }),
        queryClient.invalidateQueries({ queryKey: runtimeStackKeys.all }),
        queryClient.invalidateQueries({ queryKey: backupCatalogKeys.all }),
      ])
    },
  })
}

export function useRetryMigrationBaselineBackup(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => retryMigrationBaselineBackup(migrationId),
    onSuccess: async (state) => {
      queryClient.setQueryData(migrationAcceptanceKeys.state(migrationId), state)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: migrationProductionAdoptionKeys.state(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.detail(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.list() }),
        queryClient.invalidateQueries({ queryKey: runtimeStackKeys.all }),
        queryClient.invalidateQueries({ queryKey: backupCatalogKeys.all }),
      ])
    },
  })
}
