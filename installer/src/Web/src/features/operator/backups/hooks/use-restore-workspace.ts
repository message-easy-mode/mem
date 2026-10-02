import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  cancelRestoreWorkspace,
  completeRestoreHandover,
  executeRestoreWorkspaceStandardRecreate,
  getRestoreWorkspace,
  runRestoreWorkspacePrivateTest,
} from "../api/workspace/restore-workspace.api"
import { generateRestoreSupportReport, getRestoreLogs } from "../api/workspace/restore-logs.api"
import type { RestoreLogQuery } from "../api/types/restore-logs.types"
import type { RestoreWorkspaceStandardRecreateActionRequest } from "../api/types/restore-workspace.types"

export const restoreWorkspaceKeys = {
  all: ["restore-workspace"] as const,
  detail: (restoreSessionId: string) =>
    [...restoreWorkspaceKeys.all, restoreSessionId] as const,
}

export function useRestoreWorkspace(restoreSessionId: string | undefined) {
  return useQuery({
    queryKey: restoreWorkspaceKeys.detail(restoreSessionId ?? ""),
    queryFn: () => getRestoreWorkspace(restoreSessionId!),
    enabled: Boolean(restoreSessionId),
    refetchInterval: (query) => {
      const workspace = query.state.data
      const attemptStatus = workspace?.attempt.status.toLowerCase()
      const hasRunningStageOperation = workspace?.standardStages.some((stage) =>
        ["running", "queued", "active"].includes(
          stage.operationSummary?.status.toLowerCase() ?? "",
        ),
      )

      return (
        ["running", "queued", "active"].includes(attemptStatus ?? "") ||
        hasRunningStageOperation
      )
        ? 2000
        : false
    },
  })
}

/**
 * Runs the source-aware private test owned by the canonical Restore Workspace.
 * The backend uses the restore attempt identity, not a validation-ID-shaped
 * client inference.
 */
export function useRunRestoreWorkspacePrivateTest() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (restoreSessionId: string) =>
      runRestoreWorkspacePrivateTest(restoreSessionId),
    onSuccess: async (_result, restoreSessionId) => {
      await queryClient.invalidateQueries({
        queryKey: restoreWorkspaceKeys.detail(restoreSessionId),
      })
      await queryClient.invalidateQueries({ queryKey: ["restore-attempts"] })
    },
  })
}


/**
 * Executes Standard Recreate through the canonical Restore Workspace endpoint.
 * A source-aware backend dispatcher resolves catalog versus validated-import
 * material from the restore session; this hook never uses validationId as an
 * execution key.
 */
export function useRunRestoreWorkspaceStandardRecreate() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: RestoreWorkspaceStandardRecreateActionRequest) =>
      executeRestoreWorkspaceStandardRecreate(request),
    onSuccess: async (_result, request) => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: restoreWorkspaceKeys.detail(request.restoreSessionId),
        }),
        queryClient.invalidateQueries({ queryKey: restoreWorkspaceKeys.all }),
        queryClient.invalidateQueries({ queryKey: restoreLogsKeys.all }),
        queryClient.invalidateQueries({ queryKey: ["restore-attempts"] }),
        queryClient.invalidateQueries({ queryKey: ["runtime-stacks"] }),
        queryClient.invalidateQueries({ queryKey: ["backup-history"] }),
      ])
    },
  })
}

/**
 * Cancels a canonical restore workspace after an explicit confirmation. This
 * source-independent action releases temporary claims and makes the catalog
 * source eligible for permanent deletion; it never deletes the backup itself.
 */
export function useCancelRestoreWorkspace() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (restoreSessionId: string) => cancelRestoreWorkspace(restoreSessionId),
    onSuccess: async (_result, restoreSessionId) => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: restoreWorkspaceKeys.detail(restoreSessionId),
        }),
        queryClient.invalidateQueries({ queryKey: restoreWorkspaceKeys.all }),
        queryClient.invalidateQueries({ queryKey: restoreLogsKeys.all }),
        queryClient.invalidateQueries({ queryKey: ["restore-attempts"] }),
        queryClient.invalidateQueries({ queryKey: ["backup-catalog"] }),
      ])
    },
  })
}

export function useCompleteRestoreHandover() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (restoreSessionId: string) => completeRestoreHandover(restoreSessionId),
    onSuccess: async (_result, restoreSessionId) => {
      await queryClient.invalidateQueries({
        queryKey: restoreWorkspaceKeys.detail(restoreSessionId),
      })
      await queryClient.invalidateQueries({ queryKey: ["restore-attempts"] })
    },
  })
}

export const restoreLogsKeys = {
  all: ["restore-logs"] as const,
  detail: (restoreSessionId: string, query: RestoreLogQuery) =>
    [...restoreLogsKeys.all, restoreSessionId, query] as const,
}

export function useRestoreLogs(
  restoreSessionId: string | undefined,
  query: RestoreLogQuery,
) {
  return useQuery({
    queryKey: restoreLogsKeys.detail(restoreSessionId ?? "", query),
    queryFn: () => getRestoreLogs(restoreSessionId!, query),
    enabled: Boolean(restoreSessionId),
    placeholderData: (previousData) => previousData,
  })
}

export function useGenerateRestoreSupportReport() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (restoreSessionId: string) => generateRestoreSupportReport(restoreSessionId),
    onSuccess: async (_result, restoreSessionId) => {
      await queryClient.invalidateQueries({
        queryKey: restoreWorkspaceKeys.detail(restoreSessionId),
      })
    },
  })
}
