import { useQuery } from "@tanstack/react-query"
import { pingDocker } from "../api/runtime.api"


export function useDockerPing() {
  return useQuery({
    queryKey: ["docker", "ping"],
    queryFn: pingDocker,
    refetchInterval: 15000,
  })
}