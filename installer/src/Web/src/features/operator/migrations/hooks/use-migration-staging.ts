import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  listMigrationStagingRuns,
  startMigrationStaging,
  MigrationStagingProblemError,
  type MigrationStagingRun,
} from "@/features/operator/migrations/api/migration-staging"
import { startMigrationOperationWithReconciliation } from "@/features/operator/migrations/api/migration-start-reconciliation"
import { migrationSessionKeys } from "@/features/operator/migrations/hooks/use-migration-sessions"

export const migrationStagingKeys = {
  all: ["migration-staging"] as const,
  list: (migrationId: string) => [...migrationStagingKeys.all, migrationId] as const,
}

export function useMigrationStagingRuns(migrationId: string, enabled = true) {
  return useQuery({
    queryKey: migrationStagingKeys.list(migrationId),
    queryFn: () => listMigrationStagingRuns(migrationId),
    staleTime: 5_000,
    retry: 0,
    enabled,
    refetchInterval: (query) =>
      query.state.data?.some((run) => ["pending", "running", "retiring"].includes(run.status))
        ? 2_000
        : false,
  })
}

async function invalidateMigrationState(queryClient: ReturnType<typeof useQueryClient>, migrationId: string) {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: migrationStagingKeys.list(migrationId) }),
    queryClient.invalidateQueries({ queryKey: migrationSessionKeys.detail(migrationId) }),
    queryClient.invalidateQueries({ queryKey: migrationSessionKeys.list() }),
  ])
}

export function useStartMigrationStaging(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (retryOfStagingRunId?: string) =>
      startMigrationOperationWithReconciliation({
        list: () => listMigrationStagingRuns(migrationId),
        start: () => startMigrationStaging(migrationId, { retryOfStagingRunId }),
        identity: (run) => run.stagingRunId,
        matches: (run) => run.migrationId === migrationId &&
          (run.retryOfStagingRunId ?? null) === (retryOfStagingRunId ?? null),
        isHttpProblem: (error) => error instanceof MigrationStagingProblemError,
      }),
    onSuccess: (run) => {
      queryClient.setQueryData<MigrationStagingRun[]>(
        migrationStagingKeys.list(migrationId),
        (previous = []) => [run, ...previous.filter((item) => item.stagingRunId !== run.stagingRunId)],
      )
    },
    onSettled: async () => invalidateMigrationState(queryClient, migrationId),
  })
}
