import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  createOperatorAttestedSnapshotAuthority,
  getMigrationProductionAuthorityState,
  migrationProductionAuthorityKeys,
  type CreateOperatorAttestedSnapshotAuthorityRequest,
} from "@/features/operator/migrations/api/migration-production-authority"
import { migrationProductionAdoptionKeys } from "@/features/operator/migrations/hooks/use-migration-production-adoption"
import { migrationSessionKeys } from "@/features/operator/migrations/hooks/use-migration-sessions"

export { migrationProductionAuthorityKeys } from "@/features/operator/migrations/api/migration-production-authority"

export function useMigrationProductionAuthorityState(migrationId: string, enabled = true) {
  return useQuery({
    queryKey: migrationProductionAuthorityKeys.state(migrationId),
    queryFn: () => getMigrationProductionAuthorityState(migrationId),
    staleTime: 5_000,
    retry: 0,
    enabled: enabled && Boolean(migrationId.trim()),
  })
}

export function useCreateOperatorAttestedSnapshotAuthority(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateOperatorAttestedSnapshotAuthorityRequest) =>
      createOperatorAttestedSnapshotAuthority(migrationId, request),
    onSettled: async () => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: migrationProductionAuthorityKeys.state(migrationId),
        }),
        queryClient.invalidateQueries({
          queryKey: migrationProductionAdoptionKeys.state(migrationId),
        }),
        queryClient.invalidateQueries({
          queryKey: migrationSessionKeys.detail(migrationId),
        }),
        queryClient.invalidateQueries({
          queryKey: migrationSessionKeys.list(),
        }),
      ])
    },
  })
}
