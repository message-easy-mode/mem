import { useQuery } from "@tanstack/react-query"

import { getControlPlaneRuntimeContext } from "./api/runtime-context.api"

export const runtimeContextQueryKey = ["control-plane", "runtime-context"] as const

export function useRuntimeContext() {
  return useQuery({
    queryKey: runtimeContextQueryKey,
    queryFn: getControlPlaneRuntimeContext,
    staleTime: 30_000,
    refetchInterval: 60_000,
    refetchIntervalInBackground: false,
    retry: 0,
  })
}
