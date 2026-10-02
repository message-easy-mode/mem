// src/features/services/hooks/use-npm.ts

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  deployNpm,
  inspectNpm,
  planNpm,
  removeNpm,
  startNpm,
  stopNpm,
} from "@/features/operator/services/api/runtime.api"

export function useNpm() {
  return useQuery({
    queryKey: ["runtime", "npm"],
    queryFn: inspectNpm,
    refetchInterval: 10000,
  })
}

export function usePlanNpm() {
  return useMutation({
    mutationFn: planNpm,
  })
}

export function useDeployNpm() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: deployNpm,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["runtime", "npm"] })
      await queryClient.invalidateQueries({ queryKey: ["docker", "containers"] })
    },
  })
}

export function useStartNpm() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: startNpm,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["runtime", "npm"] })
      await queryClient.invalidateQueries({ queryKey: ["docker", "containers"] })
    },
  })
}

export function useStopNpm() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: stopNpm,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["runtime", "npm"] })
      await queryClient.invalidateQueries({ queryKey: ["docker", "containers"] })
    },
  })
}

export function useRemoveNpm() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: removeNpm,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["runtime", "npm"] })
      await queryClient.invalidateQueries({ queryKey: ["docker", "containers"] })
    },
  })
}