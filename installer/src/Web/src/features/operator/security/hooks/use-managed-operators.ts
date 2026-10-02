import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  createPendingManagedOperator,
  getManagedOperators,
  issueManagedOperatorEnrollmentGrant,
  revokeManagedOperatorSessions,
  setManagedOperatorEnabled,
  setManagedOperatorRoles,
  type CreatePendingManagedOperatorInput,
} from "../api/operator-directory.api"

export const managedOperatorKeys = {
  all: ["security", "operators"] as const,
  directory: () => [...managedOperatorKeys.all, "directory"] as const,
}

function useManagedOperatorDirectoryInvalidation() {
  const queryClient = useQueryClient()

  return () => queryClient.invalidateQueries({
    queryKey: managedOperatorKeys.directory(),
  })
}

export function useManagedOperators(enabled: boolean) {
  return useQuery({
    queryKey: managedOperatorKeys.directory(),
    queryFn: getManagedOperators,
    enabled,
  })
}

export function useCreatePendingManagedOperator() {
  const invalidateDirectory = useManagedOperatorDirectoryInvalidation()

  return useMutation({
    mutationFn: (input: CreatePendingManagedOperatorInput) =>
      createPendingManagedOperator(input),
    onSuccess: invalidateDirectory,
  })
}

export function useIssueManagedOperatorEnrollmentGrant() {
  const invalidateDirectory = useManagedOperatorDirectoryInvalidation()

  return useMutation({
    mutationFn: ({ operatorId }: { operatorId: string }) =>
      issueManagedOperatorEnrollmentGrant(operatorId),
    onSuccess: invalidateDirectory,
  })
}

export function useSetManagedOperatorEnabled() {
  const invalidateDirectory = useManagedOperatorDirectoryInvalidation()

  return useMutation({
    mutationFn: ({ operatorId, isEnabled }: { operatorId: string; isEnabled: boolean }) =>
      setManagedOperatorEnabled(operatorId, isEnabled),
    onSuccess: invalidateDirectory,
  })
}

export function useSetManagedOperatorRoles() {
  const invalidateDirectory = useManagedOperatorDirectoryInvalidation()

  return useMutation({
    mutationFn: ({ operatorId, roles }: { operatorId: string; roles: string[] }) =>
      setManagedOperatorRoles(operatorId, roles),
    onSuccess: invalidateDirectory,
  })
}

export function useRevokeManagedOperatorSessions() {
  const invalidateDirectory = useManagedOperatorDirectoryInvalidation()

  return useMutation({
    mutationFn: ({ operatorId }: { operatorId: string }) =>
      revokeManagedOperatorSessions(operatorId),
    onSuccess: invalidateDirectory,
  })
}
