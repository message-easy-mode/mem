import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  deployPostgres,
  inspectPostgres,
  planPostgres,
  removePostgres,
  startPostgres,
  stopPostgres,
} from "@/features/operator/services/api/runtime.api"

export function usePostgres() {
  return useQuery({
    queryKey: ["runtime", "postgres"],
    queryFn: inspectPostgres,
    refetchInterval: 3000,
  })
}

export function usePlanPostgres() {
  return useMutation({
    mutationFn: planPostgres,
  })
}

export function useDeployPostgres() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: deployPostgres,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["runtime", "postgres"] })
    },
  })
}

export function useStartPostgres() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: startPostgres,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["runtime", "postgres"] })
    },
  })
}

export function useStopPostgres() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: stopPostgres,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["runtime", "postgres"] })
    },
  })
}

export function useRemovePostgres() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: removePostgres,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["runtime", "postgres"] })
    },
  })
}