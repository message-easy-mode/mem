import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query"

import { backupCatalogKeys } from "./use-backup-catalog"

import type {
  RuntimeStackBackupProductionRecreatePreflightRequest,
} from "../api/types/backups.types"

import {
  deleteLocalBackup,
  deleteValidatedImportArtifact,
  destroyRestoreStagingRun,
  downloadBackupExport,
  exportBackup,
  inspectBackup,
  inspectValidatedImportArtifact,
  listBackupEntries,
  listBackupHistory,
  listRestoreAttempts,
  listStackBackupHistory,
  preflightProductionRecreate,
  runRestoredServerChecks,
  validateBackupExportUpload,
  type LocalBackupEntriesRequest,
  type RestoreAttemptListRequest,
} from "../api"

export const backupHistoryKeys = {
  all: ["backup-history"] as const,
  list: () => [...backupHistoryKeys.all, "list"] as const,
  entryList: (request: LocalBackupEntriesRequest) =>
    [...backupHistoryKeys.all, "entries", request] as const,
  stack: (stackSlug: string) =>
    [...backupHistoryKeys.all, "stack", stackSlug] as const,
  detail: (stackSlug: string, backupId: string) =>
    [...backupHistoryKeys.all, "detail", stackSlug, backupId] as const,
  export: (stackSlug: string, backupId: string) =>
    [...backupHistoryKeys.all, "export", stackSlug, backupId] as const,
}

export function useBackupHistory(options: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey: backupHistoryKeys.list(),
    queryFn: listBackupHistory,
    enabled: options.enabled ?? true,
    refetchInterval: 15_000,
  })
}

export function useBackupHistoryEntries(request: LocalBackupEntriesRequest) {
  return useQuery({
    queryKey: backupHistoryKeys.entryList(request),
    queryFn: () => listBackupEntries(request),
    placeholderData: keepPreviousData,
    refetchInterval: 15_000,
  })
}

export function useStackBackupHistory(stackSlug: string | undefined) {
  return useQuery({
    queryKey: backupHistoryKeys.stack(stackSlug ?? ""),
    queryFn: () => listStackBackupHistory(stackSlug!),
    enabled: Boolean(stackSlug),
    refetchInterval: 15_000,
  })
}

export function useBackupDetail(
  stackSlug: string | undefined,
  backupId: string | undefined,
) {
  return useQuery({
    queryKey: backupHistoryKeys.detail(stackSlug ?? "", backupId ?? ""),
    queryFn: () => inspectBackup(stackSlug!, backupId!),
    enabled: Boolean(stackSlug && backupId),
  })
}

export function useDeleteLocalBackup() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({
      stackSlug,
      backupId,
    }: {
      stackSlug: string
      backupId: string
    }) => deleteLocalBackup(stackSlug, backupId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: backupHistoryKeys.all })
      await queryClient.invalidateQueries({ queryKey: backupCatalogKeys.all })
    },
  })
}

export function useValidatedImportArtifact(validationId: string | undefined) {
  return useQuery({
    queryKey: ["validated-import-artifact", validationId ?? ""] as const,
    queryFn: () => inspectValidatedImportArtifact(validationId!),
    enabled: Boolean(validationId?.trim()),
    staleTime: 15_000,
  })
}

export function useDeleteValidatedImportArtifact() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (validationId: string) => deleteValidatedImportArtifact(validationId),
    onSuccess: async (_result, validationId) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: backupCatalogKeys.all }),
        queryClient.invalidateQueries({
          queryKey: ["validated-import-artifact", validationId],
        }),
      ])
    },
  })
}

export function useExportBackup() {
  return useMutation({
    mutationFn: ({
      stackSlug,
      backupId,
    }: {
      stackSlug: string
      backupId: string
    }) => exportBackup(stackSlug, backupId),
  })
}

export function useDownloadBackupExport() {
  return useMutation({
    mutationFn: (exportId: string) => downloadBackupExport(exportId),
  })
}

export function useValidateBackupExportUpload() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: validateBackupExportUpload,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: backupCatalogKeys.all })
    },
  })
}

export const restoreAttemptKeys = {
  all: ["restore-attempts"] as const,
  list: (request: RestoreAttemptListRequest) =>
    [...restoreAttemptKeys.all, "list", request] as const,
}

export function useRestoreAttempts(request: RestoreAttemptListRequest = {}) {
  return useQuery({
    queryKey: restoreAttemptKeys.list(request),
    queryFn: () => listRestoreAttempts(request),
    placeholderData: keepPreviousData,
    staleTime: 30_000,
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
  })
}

export const restoreWorkspacePreflightKeys = {
  all: ["restore-workspace-preflight"] as const,
  request: (request: RuntimeStackBackupProductionRecreatePreflightRequest | null) =>
    [...restoreWorkspacePreflightKeys.all, request] as const,
}

export function useProductionRecreatePreflight(
  request: RuntimeStackBackupProductionRecreatePreflightRequest | null,
) {
  return useQuery({
    queryKey: restoreWorkspacePreflightKeys.request(request),
    queryFn: () => preflightProductionRecreate(request!),
    enabled: Boolean(
      request?.restoreSessionId &&
        request.targetStackSlug.trim() &&
        request.elementHost.trim(),
    ),
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
  })
}

export function useRunRestoredServerChecks() {
  return useMutation({
    mutationFn: runRestoredServerChecks,
  })
}


export function useDestroyRestoreStagingRun() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: destroyRestoreStagingRun,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["restore-workspace"] }),
        queryClient.invalidateQueries({ queryKey: restoreAttemptKeys.all }),
      ])
    },
  })
}
