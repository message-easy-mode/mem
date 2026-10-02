import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  getMigrationConversionOptions,
  listMigrationConversionAttempts,
  startMigrationConversion,
  type MigrationConversionAttempt,
} from "@/features/operator/migrations/api/migration-conversions"
import { startMigrationOperationWithReconciliation } from "@/features/operator/migrations/api/migration-start-reconciliation"
import { MemApiProblemError } from "@/lib/api-problem"
import { migrationSessionKeys } from "@/features/operator/migrations/hooks/use-migration-sessions"

export const migrationConversionKeys = {
  all: ["migration-conversions"] as const,
  list: (migrationId: string) => [...migrationConversionKeys.all, migrationId] as const,
  options: (migrationId: string) => [...migrationConversionKeys.all, migrationId, "options"] as const,
}

export function useMigrationConversionOptions(migrationId: string, enabled = true) {
  return useQuery({
    queryKey: migrationConversionKeys.options(migrationId),
    queryFn: () => getMigrationConversionOptions(migrationId),
    staleTime: 0,
    retry: 0,
    enabled,
  })
}

export function useMigrationConversionAttempts(migrationId: string, enabled = true) {
  return useQuery({
    queryKey: migrationConversionKeys.list(migrationId),
    queryFn: () => listMigrationConversionAttempts(migrationId),
    staleTime: 5_000,
    retry: 0,
    enabled,
    refetchInterval: (query) =>
      query.state.data?.some((attempt) => attempt.status === "pending" || attempt.status === "running")
        ? 2_000
        : false,
  })
}

export type StartMigrationConversionInput = {
  retryOfConversionAttemptId?: string
}

export function useStartMigrationConversion(migrationId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (input: StartMigrationConversionInput = {}) =>
      startMigrationOperationWithReconciliation({
        list: () => listMigrationConversionAttempts(migrationId),
        start: () => startMigrationConversion(migrationId, input),
        identity: (attempt) => attempt.conversionAttemptId,
        matches: (attempt) => attempt.migrationId === migrationId,
        isHttpProblem: (error) => error instanceof MemApiProblemError,
      }),
    onSuccess: (attempt) => {
      queryClient.setQueryData<MigrationConversionAttempt[]>(
        migrationConversionKeys.list(migrationId),
        (previous = []) => [
          attempt,
          ...previous.filter((item) => item.conversionAttemptId !== attempt.conversionAttemptId),
        ],
      )
    },
    onSettled: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: migrationConversionKeys.list(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.detail(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.list() }),
      ])
    },
  })
}
