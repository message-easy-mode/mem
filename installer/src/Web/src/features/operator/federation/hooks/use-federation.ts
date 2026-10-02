import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { runtimeStackKeys } from "@/features/operator/stacks/hooks/use-runtime-stacks"

import {
  applyRuntimeStackFederation,
  inspectRuntimeStackFederation,
  reviewRuntimeStackFederation,
} from "../api/federation.api"
import type {
  RuntimeStackFederationApplyRequest,
  RuntimeStackFederationPolicyRequest,
} from "../api/federation.types"

export const federationKeys = {
  all: ["federation"] as const,
  state: (slugOrId: string) => [...federationKeys.all, "state", slugOrId] as const,
}

export function useRuntimeStackFederation(
  slugOrId: string | undefined,
  rapidPolling = false,
) {
  return useQuery({
    queryKey: federationKeys.state(slugOrId ?? ""),
    queryFn: () => inspectRuntimeStackFederation(slugOrId!),
    enabled: Boolean(slugOrId),
    refetchInterval: (query) => {
      const latest = query.state.data?.latestOperation
      return rapidPolling || latest?.status === "running" ? 2500 : 15000
    },
  })
}

export function useReviewRuntimeStackFederation(slugOrId: string) {
  return useMutation({
    mutationFn: (request: RuntimeStackFederationPolicyRequest) =>
      reviewRuntimeStackFederation(slugOrId, request),
  })
}

export function useApplyRuntimeStackFederation(slugOrId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: RuntimeStackFederationApplyRequest) =>
      applyRuntimeStackFederation(slugOrId, request),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: federationKeys.state(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.detail(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.operations(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.doctor(slugOrId) })
    },
  })
}
