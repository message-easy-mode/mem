import { useQuery } from "@tanstack/react-query"

import { getDashboardOverview } from "../api/dashboard.api"

export function useDashboardOverview() {
  return useQuery({
    queryKey: ["dashboard", "overview"],
    queryFn: getDashboardOverview,
    staleTime: 10_000,
    refetchInterval: 30_000,
    refetchIntervalInBackground: false,
    // Home keeps the last successful snapshot visible. Do not delay an operator
    // initiated refresh failure behind a hidden transport retry; the visible Retry
    // action gives the operator an explicit, immediate recovery path.
    retry: 0,
  })
}
