import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  getOperatorNpmSettings,
  revealOperatorNpmCredential,
  updateOperatorNpmCredential,
  type UpdateOperatorNpmCredentialInput,
} from "../api/npm-settings.api"

export const npmSettingsKeys = {
  all: ["operator", "npm", "settings"] as const,
  current: () => [...npmSettingsKeys.all, "current"] as const,
}

export function useOperatorNpmSettings(enabled: boolean) {
  return useQuery({
    queryKey: npmSettingsKeys.current(),
    queryFn: getOperatorNpmSettings,
    enabled,
  })
}

export function useRevealOperatorNpmCredential() {
  return useMutation({ mutationFn: revealOperatorNpmCredential })
}

export function useUpdateOperatorNpmCredential() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (input: UpdateOperatorNpmCredentialInput) =>
      updateOperatorNpmCredential(input),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: npmSettingsKeys.current() })
    },
  })
}
