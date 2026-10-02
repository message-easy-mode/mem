import { useQuery } from "@tanstack/react-query"
import { getManagedContainers } from "../api/runtime.api"


export function useManagedContainers() {
  return useQuery({
    queryKey: ["docker", "containers"],
    queryFn: getManagedContainers,
    refetchInterval: 15000,
  })
}