import { keepPreviousData, useQuery } from "@tanstack/react-query"

import {
  getMigrationSession,
  listMigrationSessions,
  type MigrationSessionInventoryRequest,
} from "@/features/operator/migrations/api/migration-sessions"

export const migrationSessionKeys = {
  all: ["migration-sessions"] as const,
  list: (request?: MigrationSessionInventoryRequest) =>
    request
      ? ([...migrationSessionKeys.all, "list", request] as const)
      : ([...migrationSessionKeys.all, "list"] as const),
  detail: (migrationId: string) =>
    [...migrationSessionKeys.all, "detail", migrationId] as const,
}

export function useMigrationSessions(request: MigrationSessionInventoryRequest) {
  return useQuery({
    queryKey: migrationSessionKeys.list(request),
    queryFn: () => listMigrationSessions(request),
    placeholderData: keepPreviousData,
    staleTime: 10_000,
    retry: 0,
  })
}

export function useMigrationSession(migrationId: string | undefined) {
  return useQuery({
    queryKey: migrationSessionKeys.detail(migrationId ?? ""),
    queryFn: () => getMigrationSession(migrationId!),
    enabled: Boolean(migrationId?.trim()),
    staleTime: 10_000,
    retry: 0,
  })
}
