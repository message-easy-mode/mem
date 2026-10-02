import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  getSecuritySettings,
  updateHighRiskStepUpSettings,
  type UpdateHighRiskStepUpSettingsInput,
} from "../api/security-settings.api"

export const securitySettingsKeys = {
  all: ["security", "settings"] as const,
  current: () => [...securitySettingsKeys.all, "current"] as const,
}

export function useSecuritySettings(enabled: boolean) {
  return useQuery({
    queryKey: securitySettingsKeys.current(),
    queryFn: getSecuritySettings,
    enabled,
  })
}

export function useUpdateHighRiskStepUpSettings() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (input: UpdateHighRiskStepUpSettingsInput) =>
      updateHighRiskStepUpSettings(input),
    onSuccess: (settings) => {
      queryClient.setQueryData(securitySettingsKeys.current(), settings)
    },
  })
}
