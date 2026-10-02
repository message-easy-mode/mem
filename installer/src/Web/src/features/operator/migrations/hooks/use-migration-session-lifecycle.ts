import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  archiveMigrationSession,
  cancelMigrationSession,
  deleteMigrationSession,
  getMigrationSessionLifecycle,
  unarchiveMigrationSession,
  type MigrationSessionCancelRequest,
  type MigrationSessionDeleteRequest,
} from "@/features/operator/migrations/api/migration-sessions"
import { migrationSessionKeys } from "@/features/operator/migrations/hooks/use-migration-sessions"
import { migrationWorkspaceKeys } from "@/features/operator/migrations/hooks/use-migration-workspace"

export const migrationSessionLifecycleKeys = {
  detail: (migrationId: string) =>
    [...migrationSessionKeys.detail(migrationId), "lifecycle"] as const,
}

export function useMigrationSessionLifecycle(migrationId: string | undefined) {
  return useQuery({
    queryKey: migrationSessionLifecycleKeys.detail(migrationId ?? ""),
    queryFn: () => getMigrationSessionLifecycle(migrationId!),
    enabled: Boolean(migrationId?.trim()),
    staleTime: 5_000,
    retry: 0,
  })
}

export function useMigrationSessionLifecycleMutations(migrationId: string) {
  const queryClient = useQueryClient()

  async function invalidate() {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: migrationSessionKeys.all }),
      queryClient.invalidateQueries({
        queryKey: migrationSessionLifecycleKeys.detail(migrationId),
      }),
      queryClient.invalidateQueries({
        queryKey: migrationWorkspaceKeys.detail(migrationId),
      }),
    ])
  }


  function invalidateAfterDelete() {
    queryClient.removeQueries({
      queryKey: migrationSessionKeys.detail(migrationId),
    })
    queryClient.removeQueries({
      queryKey: migrationWorkspaceKeys.detail(migrationId),
    })
    void queryClient.invalidateQueries({
      queryKey: migrationSessionKeys.list(),
    })
  }

  const archive = useMutation({
    mutationFn: (expectedStateVersion: number) =>
      archiveMigrationSession(migrationId, expectedStateVersion),
    onSuccess: invalidate,
  })

  const unarchive = useMutation({
    mutationFn: (expectedStateVersion: number) =>
      unarchiveMigrationSession(migrationId, expectedStateVersion),
    onSuccess: invalidate,
  })

  const cancel = useMutation({
    mutationFn: (request: MigrationSessionCancelRequest) =>
      cancelMigrationSession(migrationId, request),
    onSuccess: invalidate,
  })

  const remove = useMutation({
    mutationFn: (request: MigrationSessionDeleteRequest) =>
      deleteMigrationSession(migrationId, request),
    onSuccess: invalidateAfterDelete,
  })

  return {
    archive,
    unarchive,
    cancel,
    delete: remove,
    isPending:
      archive.isPending ||
      unarchive.isPending ||
      cancel.isPending ||
      remove.isPending,
  }
}
