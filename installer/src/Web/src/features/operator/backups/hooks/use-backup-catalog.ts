import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  backfillLocalBackupCatalog,
  deleteBackupCatalogEntry,
  deleteImportedZipArchive,
  downloadBackupExport,
  exportCatalogPortableZip,
  getBackupCatalogEntry,
  getBackupCatalogLifecycle,
  getImportedZipArchive,
  listBackupCatalog,
  materialiseImportedZip,
  prepareCatalogRestoreSession,
  type BackupCatalogImportArchiveDeleteRequest,
} from "../api"

export const backupCatalogKeys = {
  all: ["backup-catalog"] as const,
  list: () => [...backupCatalogKeys.all, "list"] as const,
  detail: (catalogEntryId: string) =>
    [...backupCatalogKeys.all, "detail", catalogEntryId] as const,
  lifecycle: (catalogEntryId: string) =>
    [...backupCatalogKeys.all, "lifecycle", catalogEntryId] as const,
  archive: (validationId: string) =>
    [...backupCatalogKeys.all, "archive", validationId] as const,
}

export function useBackupCatalog() {
  return useQuery({
    queryKey: backupCatalogKeys.list(),
    queryFn: listBackupCatalog,
    staleTime: 15_000,
  })
}

export function useBackupCatalogEntry(catalogEntryId: string | undefined) {
  return useQuery({
    queryKey: backupCatalogKeys.detail(catalogEntryId ?? ""),
    queryFn: () => getBackupCatalogEntry(catalogEntryId!),
    enabled: Boolean(catalogEntryId?.trim()),
  })
}

export function useBackupCatalogLifecycle(
  catalogEntryId: string | undefined,
  enabled = true,
) {
  return useQuery({
    queryKey: backupCatalogKeys.lifecycle(catalogEntryId ?? ""),
    queryFn: () => getBackupCatalogLifecycle(catalogEntryId!),
    enabled: enabled && Boolean(catalogEntryId?.trim()),
    staleTime: 10_000,
  })
}

export function useImportedZipArchive(validationId: string | undefined) {
  return useQuery({
    queryKey: backupCatalogKeys.archive(validationId ?? ""),
    queryFn: () => getImportedZipArchive(validationId!),
    enabled: Boolean(validationId?.trim()),
    staleTime: 10_000,
  })
}

export function useBackfillLocalBackupCatalog() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: backfillLocalBackupCatalog,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: backupCatalogKeys.all })
    },
  })
}

export function useMaterialiseImportedZip() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: materialiseImportedZip,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: backupCatalogKeys.all })
    },
  })
}

export function usePrepareCatalogRestoreSession() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: prepareCatalogRestoreSession,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["restore-attempts"] })
    },
  })
}

export function useExportCatalogPortableZip() {
  return useMutation({ mutationFn: exportCatalogPortableZip })
}

export function useDownloadCatalogPortableZip() {
  return useMutation({ mutationFn: downloadBackupExport })
}

export function useDeleteBackupCatalogEntry() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({
      catalogEntryId,
    }: {
      catalogEntryId: string
    }) => deleteBackupCatalogEntry(catalogEntryId),
    onSuccess: async (_response, { catalogEntryId }) => {
      // The successful DELETE makes this detail resource cease to exist. Do not
      // invalidate the active detail/lifecycle queries: doing so refetches the
      // just-deleted id before the route-level success callback can navigate away,
      // turning a successful destructive action into a misleading 404 screen.
      await Promise.all([
        queryClient.cancelQueries({
          queryKey: backupCatalogKeys.detail(catalogEntryId),
          exact: true,
        }),
        queryClient.cancelQueries({
          queryKey: backupCatalogKeys.lifecycle(catalogEntryId),
          exact: true,
        }),
      ])

      // Only inventories that survive deletion are invalidated. The Backup
      // Catalog list is inactive on the detail route, so it will refresh from
      // the authoritative server as soon as navigation returns to /backups.
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: backupCatalogKeys.list() }),
        queryClient.invalidateQueries({ queryKey: ["restore-attempts"] }),
      ])
    },
  })
}

export function useDeleteImportedZipArchive() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({
      validationId,
      request,
    }: {
      validationId: string
      request?: BackupCatalogImportArchiveDeleteRequest
    }) => deleteImportedZipArchive(validationId, request),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: backupCatalogKeys.all })
    },
  })
}
