import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  confirmMigrationCutover,
  createMigrationCutoverPreview,
  executeMigrationCutover,
  getMigrationCutoverReadiness,
  getMigrationCutoverState,
  getMigrationPostCutover,
  prepareMigrationCutoverCandidate,
  type ConfirmMigrationCutoverRequest,
  type ExecuteMigrationCutoverRequest,
  type MigrationCutoverState,
} from "@/features/operator/migrations/api/migration-cutover"
import { migrationSessionKeys } from "@/features/operator/migrations/hooks/use-migration-sessions"

export const migrationCutoverKeys = {
  all: ["migration-cutover"] as const,
  state: (migrationId: string) => [...migrationCutoverKeys.all, migrationId] as const,
}

export function useMigrationCutoverState(migrationId: string, enabled = true) {
  return useQuery({
    queryKey: migrationCutoverKeys.state(migrationId),
    queryFn: () => getMigrationCutoverState(migrationId),
    staleTime: 5_000,
    retry: 0,
    enabled,
  })
}

async function invalidateCutoverState(
  queryClient: ReturnType<typeof useQueryClient>,
  migrationId: string,
) {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: migrationCutoverKeys.state(migrationId) }),
    queryClient.invalidateQueries({ queryKey: migrationSessionKeys.detail(migrationId) }),
    queryClient.invalidateQueries({ queryKey: migrationSessionKeys.list() }),
  ])
}

export function usePrepareMigrationCutoverCandidate(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => prepareMigrationCutoverCandidate(migrationId),
    onSuccess: () => invalidateCutoverState(queryClient, migrationId),
  })
}

export function useCreateMigrationCutoverPreview(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => createMigrationCutoverPreview(migrationId),
    onSuccess: () => invalidateCutoverState(queryClient, migrationId),
  })
}

export function useConfirmMigrationCutover(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: ConfirmMigrationCutoverRequest) =>
      confirmMigrationCutover(migrationId, request),
    onSuccess: () => invalidateCutoverState(queryClient, migrationId),
  })
}

export function useRefreshMigrationCutoverReadiness(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => getMigrationCutoverReadiness(migrationId),
    onSuccess: (state: MigrationCutoverState) => {
      queryClient.setQueryData(migrationCutoverKeys.state(migrationId), state)
    },
  })
}

export function useExecuteMigrationCutover(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: ExecuteMigrationCutoverRequest) =>
      executeMigrationCutover(migrationId, request),
    onSuccess: () => invalidateCutoverState(queryClient, migrationId),
  })
}

export function useRefreshMigrationPostCutover(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => getMigrationPostCutover(migrationId),
    onSuccess: (state: MigrationCutoverState) => {
      queryClient.setQueryData(migrationCutoverKeys.state(migrationId), state)
    },
  })
}
