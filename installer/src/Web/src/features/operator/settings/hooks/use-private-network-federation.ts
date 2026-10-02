import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  applyPrivateNetworkFederationException,
  getPrivateNetworkFederationInventory,
  reviewPrivateNetworkFederationException,
  type PrivateNetworkFederationApplyRequest,
  type PrivateNetworkFederationReviewRequest,
} from "../api/private-network-federation.api"

export const privateNetworkFederationKeys = {
  all: ["settings", "private-network-federation"] as const,
  inventory: () => [...privateNetworkFederationKeys.all, "inventory"] as const,
}

export function usePrivateNetworkFederationInventory(enabled: boolean) {
  return useQuery({
    queryKey: privateNetworkFederationKeys.inventory(),
    queryFn: getPrivateNetworkFederationInventory,
    enabled,
    refetchInterval: 15000,
  })
}

export function useReviewPrivateNetworkFederationException(slugOrId: string) {
  return useMutation({
    mutationFn: (input: PrivateNetworkFederationReviewRequest) =>
      reviewPrivateNetworkFederationException(slugOrId, input),
  })
}

export function useApplyPrivateNetworkFederationException(slugOrId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: PrivateNetworkFederationApplyRequest) =>
      applyPrivateNetworkFederationException(slugOrId, input),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: privateNetworkFederationKeys.inventory() })
    },
  })
}
