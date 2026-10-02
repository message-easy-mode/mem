// src/features/setup/host-checks/hooks/host-checks.queries.ts

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import type { HostCheckRunResponse } from "../api/host-checks.types"
import {
  getCurrentHostCheckRun,
  getHostCheckRun,
  startHostCheckRun,
} from "../api/host-checks.api"

export const hostCheckQueryKeys = {
  current: ["host-check-run-current"] as const,
  run: (runId: string | undefined) => ["host-check-run", runId] as const,
}

export function useCurrentHostCheckRun() {
  return useQuery<HostCheckRunResponse | null>({
    queryKey: hostCheckQueryKeys.current,
    queryFn: getCurrentHostCheckRun,
  })
}

export function useHostCheckRun(runId: string | undefined) {
  return useQuery({
    queryKey: hostCheckQueryKeys.run(runId),
    queryFn: () => getHostCheckRun(runId!),
    enabled: !!runId,
  })
}

export function useStartHostCheckRun() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: startHostCheckRun,
    onSuccess: async (run) => {
      queryClient.setQueryData(hostCheckQueryKeys.current, run)
      queryClient.setQueryData(hostCheckQueryKeys.run(run.id), run)

      await queryClient.invalidateQueries({
        queryKey: hostCheckQueryKeys.current,
      })
    },
  })
}
