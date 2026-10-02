import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { backupCatalogKeys } from "@/features/operator/backups/hooks/use-backup-catalog"
import {
  closeMigrationTwoServerQualification,
  getMigrationTwoServerQualificationClosureState,
  getMigrationTwoServerQualificationState,
  importMigrationTwoServerSourceEvidence,
  type MigrationTwoServerQualificationClosureRequest,
  type MigrationTwoServerSourceEvidenceEnvelope,
} from "@/features/operator/migrations/api/migration-two-server-qualification"
import { migrationAcceptanceKeys } from "@/features/operator/migrations/hooks/use-migration-acceptance"
import { migrationProductionAdoptionKeys } from "@/features/operator/migrations/hooks/use-migration-production-adoption"
import { migrationSessionKeys } from "@/features/operator/migrations/hooks/use-migration-sessions"
import { runtimeStackKeys } from "@/features/operator/stacks/hooks/use-runtime-stacks"

export const migrationTwoServerQualificationKeys = {
  all: ["migration-two-server-qualification"] as const,
  state: (migrationId: string) => [...migrationTwoServerQualificationKeys.all, migrationId] as const,
  closure: (migrationId: string) => [...migrationTwoServerQualificationKeys.state(migrationId), "closure"] as const,
}

export function useMigrationTwoServerQualificationState(migrationId: string, enabled = true) {
  return useQuery({
    queryKey: migrationTwoServerQualificationKeys.state(migrationId),
    queryFn: () => getMigrationTwoServerQualificationState(migrationId),
    staleTime: 5_000,
    retry: 0,
    enabled,
  })
}

export function useImportMigrationTwoServerSourceEvidence(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (envelope: MigrationTwoServerSourceEvidenceEnvelope) =>
      importMigrationTwoServerSourceEvidence(migrationId, envelope),
    onSuccess: async (state) => {
      queryClient.setQueryData(migrationTwoServerQualificationKeys.state(migrationId), state)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: migrationAcceptanceKeys.state(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationProductionAdoptionKeys.state(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.detail(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.list() }),
        queryClient.invalidateQueries({ queryKey: runtimeStackKeys.all }),
        queryClient.invalidateQueries({ queryKey: backupCatalogKeys.all }),
      ])
    },
  })
}

export function useMigrationTwoServerQualificationClosureState(migrationId: string, enabled = true) {
  return useQuery({
    queryKey: migrationTwoServerQualificationKeys.closure(migrationId),
    queryFn: () => getMigrationTwoServerQualificationClosureState(migrationId),
    staleTime: 5_000,
    retry: 0,
    enabled,
  })
}

export function useCloseMigrationTwoServerQualification(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: MigrationTwoServerQualificationClosureRequest) =>
      closeMigrationTwoServerQualification(migrationId, request),
    onSuccess: async (state) => {
      queryClient.setQueryData(migrationTwoServerQualificationKeys.closure(migrationId), state)
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: migrationTwoServerQualificationKeys.state(migrationId),
          exact: true,
        }),
        queryClient.invalidateQueries({ queryKey: migrationAcceptanceKeys.state(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationProductionAdoptionKeys.state(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.detail(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.list() }),
        queryClient.invalidateQueries({ queryKey: runtimeStackKeys.all }),
        queryClient.invalidateQueries({ queryKey: backupCatalogKeys.all }),
      ])
    },
  })
}
