import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  getInstallation,
  getInstallationSteps,
  runInstallation,
} from "../api/installation-run.api"

export const installationRunQueryKeys = {
  detail: (installationId: string | undefined) =>
    ["installation", installationId] as const,
  steps: (installationId: string | undefined) =>
    ["installation-steps", installationId] as const,
}

export function useInstallation(installationId: string | undefined) {
  return useQuery({
    queryKey: installationRunQueryKeys.detail(installationId),
    queryFn: () => getInstallation(installationId!),
    enabled: !!installationId,
    refetchInterval: (query) =>
      isActiveInstallationStatus(query.state.data?.status) ? 2000 : false,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: false,
    refetchOnReconnect: "always",
  })
}

export function useRunInstallation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: runInstallation,
    onSuccess: async (result) => {
      await queryClient.invalidateQueries({
        queryKey: installationRunQueryKeys.detail(result.installationId),
      })
      await queryClient.invalidateQueries({
        queryKey: installationRunQueryKeys.steps(result.installationId),
      })
    },
  })
}

export function useInstallationSteps(
  installationId: string | undefined,
  isActive: boolean,
) {
  return useQuery({
    queryKey: installationRunQueryKeys.steps(installationId),
    queryFn: () => getInstallationSteps(installationId!),
    enabled: !!installationId,
    refetchInterval: isActive ? 2000 : false,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: false,
    refetchOnReconnect: "always",
  })
}

function isActiveInstallationStatus(status: string | undefined) {
  return status === "Running" || status === "WaitingForUser"
}