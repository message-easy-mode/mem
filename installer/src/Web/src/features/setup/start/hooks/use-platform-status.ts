import { useQuery } from "@tanstack/react-query"

import { getPlatformStatus } from "../api/platform-status-api"
import type { PlatformStatusResponse } from "../api/platform-status"

export const platformStatusQueryKeys = {
  current: ["installer-platform-status"] as const,
}

export function usePlatformStatus() {
  return useQuery<PlatformStatusResponse>({
    queryKey: platformStatusQueryKeys.current,
    queryFn: getPlatformStatus,
    refetchInterval: 5000,
  })
}