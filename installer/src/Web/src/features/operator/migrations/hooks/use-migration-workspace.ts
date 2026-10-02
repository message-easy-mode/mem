import { useQuery } from "@tanstack/react-query"

import { getMigrationWorkspace } from "@/features/operator/migrations/api/migration-workspace"
import { migrationSessionKeys } from "@/features/operator/migrations/hooks/use-migration-sessions"

export const migrationWorkspaceKeys = {
  detail: (migrationId: string) =>
    [...migrationSessionKeys.detail(migrationId), "workspace"] as const,
}

export function useMigrationWorkspace(migrationId: string | undefined) {
  return useQuery({
    queryKey: migrationWorkspaceKeys.detail(migrationId ?? ""),
    queryFn: () => getMigrationWorkspace(migrationId!),
    enabled: Boolean(migrationId?.trim()),
    staleTime: 5_000,
    retry: 0,
    refetchInterval: (query) =>
      query.state.data?.stages.some((stage) => stage.state === "running")
        ? 3_000
        : 15_000,
  })
}
