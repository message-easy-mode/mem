import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { deleteSelectedOrphanedNpmProxyHosts, getRuntimeReconciliationReport } from "../api/runtime-reconciliation.api"

export function useRuntimeReconciliationReport() {
  return useQuery({
    queryKey: ["maintenance", "runtime-reconciliation"],
    queryFn: getRuntimeReconciliationReport,
    staleTime: 10_000,
    retry: 0,
  })
}


export function useDeleteSelectedOrphanedNpmProxyHosts() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: deleteSelectedOrphanedNpmProxyHosts,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["maintenance", "runtime-reconciliation"] })
    },
  })
}
