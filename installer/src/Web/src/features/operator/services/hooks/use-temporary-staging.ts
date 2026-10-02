import { useQuery } from "@tanstack/react-query"
import { getTemporaryStagingInventory } from "../api/temporary-staging.api"

export function useTemporaryStaging() {
  return useQuery({
    queryKey: ["services", "temporary-staging"],
    queryFn: getTemporaryStagingInventory,
    refetchInterval: 15_000,
    retry: 0,
  })
}
