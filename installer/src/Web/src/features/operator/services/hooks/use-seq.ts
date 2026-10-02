// src/features/services/hooks/use-seq.ts

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  deploySeq,
  inspectSeq,
  planSeq,
  removeSeq,
  startSeq,
  stopSeq,
} from "@/features/operator/services/api/runtime.api"

export function useSeq() {
  return useQuery({
    queryKey: ["runtime", "seq"],
    queryFn: inspectSeq,
    refetchInterval: 10000,
  })
}

export function usePlanSeq() {
  return useMutation({
    mutationFn: planSeq,
  })
}

export function useDeploySeq() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: deploySeq,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["runtime", "seq"] })
      await queryClient.invalidateQueries({ queryKey: ["docker", "containers"] })
    },
  })
}

export function useStartSeq() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: startSeq,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["runtime", "seq"] })
      await queryClient.invalidateQueries({ queryKey: ["docker", "containers"] })
    },
  })
}

export function useStopSeq() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: stopSeq,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["runtime", "seq"] })
      await queryClient.invalidateQueries({ queryKey: ["docker", "containers"] })
    },
  })
}

export function useRemoveSeq() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: removeSeq,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["runtime", "seq"] })
      await queryClient.invalidateQueries({ queryKey: ["docker", "containers"] })
    },
  })
}