import { useEffect } from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  checkCoturn,
  ensureCoturn,
  getCoturnInstallOperation,
  getActiveCoturnMaintenance,
  getCoturnStartupSupervision,
  getCoturnMaintenanceOperation,
  installCoturn,
  maintainCoturn,
  getCoturnLogs,
  getLatestCoturnCheck,
  inspectCoturn,
  type CoturnEnsureRequest,
  type CoturnInstallRequest,
  type CoturnMaintenanceRequest,
} from "../api/coturn.api"

export const coturnKeys = {
  all: ["coturn"] as const,
  status: () => [...coturnKeys.all, "status"] as const,
  latestCheck: () => [...coturnKeys.all, "latest-check"] as const,
  logs: (tail: number) => [...coturnKeys.all, "logs", tail] as const,
  installOperation: (operationId: string) => [...coturnKeys.all, "install-operation", operationId] as const,
  maintenanceOperation: (operationId: string) => [...coturnKeys.all, "maintenance-operation", operationId] as const,
  activeMaintenance: () => [...coturnKeys.all, "active-maintenance"] as const,
  startupSupervision: () => [...coturnKeys.all, "startup-supervision"] as const,
}

export function useCoturn() {
  return useQuery({
    queryKey: coturnKeys.status(),
    queryFn: inspectCoturn,
    refetchInterval: 15_000,
  })
}

export function useCoturnLatestCheck() {
  return useQuery({
    queryKey: coturnKeys.latestCheck(),
    queryFn: getLatestCoturnCheck,
    refetchInterval: 30_000,
  })
}

export function useEnsureCoturn() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: CoturnEnsureRequest = {}) => ensureCoturn(request),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: coturnKeys.all })
    },
  })
}

export function useCheckCoturn() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: checkCoturn,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: coturnKeys.latestCheck() })
      void queryClient.invalidateQueries({ queryKey: coturnKeys.status() })
    },
  })
}

export function useCoturnLogs(enabled: boolean, tail = 100) {
  return useQuery({
    queryKey: coturnKeys.logs(tail),
    queryFn: () => getCoturnLogs(tail),
    enabled,
    staleTime: 0,
  })
}

export function useInstallCoturn() {
  return useMutation({
    mutationFn: (request: CoturnInstallRequest = {}) => installCoturn(request),
  })
}

export function useCoturnInstallOperation(operationId: string | null) {
  const queryClient = useQueryClient()

  return useQuery({
    queryKey: coturnKeys.installOperation(operationId ?? "none"),
    queryFn: () => getCoturnInstallOperation(operationId!),
    enabled: Boolean(operationId),
    refetchInterval: (query) => {
      const operation = query.state.data
      if (!operation || !operation.terminal) return 1_000
      if (operation.succeeded) {
        void queryClient.invalidateQueries({ queryKey: coturnKeys.status() })
      }
      return false
    },
  })
}
export function useMaintainCoturn() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: CoturnMaintenanceRequest) => maintainCoturn(request),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: coturnKeys.activeMaintenance() })
    },
  })
}

export function useCoturnActiveMaintenance() {
  return useQuery({
    queryKey: coturnKeys.activeMaintenance(),
    queryFn: getActiveCoturnMaintenance,
    refetchInterval: (query) => query.state.data?.active ? 1_000 : 15_000,
  })
}

export function useCoturnStartupSupervision() {
  return useQuery({
    queryKey: coturnKeys.startupSupervision(),
    queryFn: getCoturnStartupSupervision,
    refetchInterval: (query) => {
      const status = query.state.data?.status
      return status === "waiting" || status === "recovering" ? 1_000 : 15_000
    },
  })
}

export function useCoturnMaintenanceOperation(operationId: string | null) {
  const queryClient = useQueryClient()
  const query = useQuery({
    queryKey: coturnKeys.maintenanceOperation(operationId ?? "none"),
    queryFn: () => getCoturnMaintenanceOperation(operationId!),
    enabled: Boolean(operationId),
    refetchInterval: (currentQuery) => {
      const operation = currentQuery.state.data
      return !operation || !operation.terminal ? 1_000 : false
    },
  })

  useEffect(() => {
    if (!query.data?.terminal) return

    // A terminal server-owned maintenance operation is the authoritative
    // convergence boundary. Refetch active projections immediately instead of
    // merely marking them stale and waiting for their ordinary 15/30s timers.
    // This keeps the Coturn page truthful without requiring a browser refresh
    // after the runtime identity changes during Restart & Verify or Repair.
    void Promise.all([
      queryClient.refetchQueries({ queryKey: coturnKeys.status(), type: "active" }),
      queryClient.refetchQueries({ queryKey: coturnKeys.latestCheck(), type: "active" }),
      queryClient.refetchQueries({ queryKey: coturnKeys.activeMaintenance(), type: "active" }),
      queryClient.refetchQueries({ queryKey: coturnKeys.startupSupervision(), type: "active" }),
    ])
  }, [query.data?.operationId, query.data?.terminal, queryClient])

  return query
}

