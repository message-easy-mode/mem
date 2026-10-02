import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  reviewMigrationPrivateServerTarget,
  executeMigrationProductionCutover,
  executeMigrationProductionRollback,
  getMigrationProductionAdoptionState,
  importMigrationProductionSourceCompletion,
  materializeMigrationProductionRuntime,
  prepareMigrationProductionAdoptionPlan,
  prepareMigrationProductionCutoverPreview,
  prepareMigrationProductionRollbackPreview,
  runMigrationProductionVerification,
  type CreateMigrationPrivateServerRequest,
  type MakeMigrationServerLiveRequest,
  type ReviewMigrationPrivateServerTargetRequest,
  type ExecuteMigrationProductionCutoverRequest,
  type ExecuteMigrationProductionRollbackRequest,
  type MaterializeMigrationProductionRuntimeRequest,
  type MigrationProductionSourceRestorationCompletionEnvelope,
  type PrepareMigrationProductionAdoptionRequest,
  type PrepareMigrationProductionCutoverRequest,
  type PrepareMigrationProductionRollbackRequest,
  type RunMigrationProductionVerificationRequest,
} from "@/features/operator/migrations/api/migration-production-adoption"
import { migrationProductionAuthorityKeys } from "@/features/operator/migrations/api/migration-production-authority"
import {
  createPrivateServerWithReconciliation,
  makeServerLiveWithReconciliation,
} from "@/features/operator/migrations/api/migration-production-operation-reconciliation"
import { migrationSessionKeys } from "@/features/operator/migrations/hooks/use-migration-sessions"

export const migrationProductionAdoptionKeys = {
  all: ["migration-production-adoption"] as const,
  state: (migrationId: string) => [...migrationProductionAdoptionKeys.all, migrationId] as const,
}

export function useMigrationProductionAdoptionState(
  migrationId: string,
  enabled = true,
  pollWhileRunning = false,
) {
  return useQuery({
    queryKey: migrationProductionAdoptionKeys.state(migrationId),
    queryFn: () => getMigrationProductionAdoptionState(migrationId),
    staleTime: 5_000,
    retry: 0,
    refetchOnMount: "always",
    enabled,
    refetchInterval: pollWhileRunning
      ? (query) => {
          const status = query.state.data?.status
          return status === "materializing" ||
            status === "cutover-executing" ||
            status === "production-verification-running"
            ? 3_000
            : false
        }
      : false,
  })
}

export const migrationPrivateServerTargetReviewKeys = {
  all: ["migration-private-server-target-review"] as const,
  request: (
    migrationId: string,
    targetStackSlug: string | null,
    elementPublicHost: string | null,
  ) => [
    ...migrationPrivateServerTargetReviewKeys.all,
    migrationId,
    targetStackSlug,
    elementPublicHost,
  ] as const,
}

export function useMigrationPrivateServerTargetReview(
  migrationId: string,
  request: ReviewMigrationPrivateServerTargetRequest,
  enabled = true,
) {
  const targetStackSlug = request.targetStackSlug?.trim() || null
  const elementPublicHost = request.elementPublicHost?.trim() || null

  return useQuery({
    queryKey: migrationPrivateServerTargetReviewKeys.request(
      migrationId,
      targetStackSlug,
      elementPublicHost,
    ),
    queryFn: () => reviewMigrationPrivateServerTarget(migrationId, {
      targetStackSlug,
      elementPublicHost,
    }),
    enabled,
    retry: 0,
    staleTime: 0,
    refetchOnMount: "always",
  })
}

export function useReviewMigrationPrivateServerTarget(migrationId: string) {
  return useMutation({
    mutationFn: (request: ReviewMigrationPrivateServerTargetRequest) =>
      reviewMigrationPrivateServerTarget(migrationId, request),
  })
}

export function useCreateMigrationPrivateServer(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateMigrationPrivateServerRequest) =>
      createPrivateServerWithReconciliation(migrationId, request),
    retry: false,
    onSuccess: (state) => {
      queryClient.setQueryData(migrationProductionAdoptionKeys.state(migrationId), state)
    },
    onSettled: async () => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: migrationProductionAuthorityKeys.state(migrationId),
        }),
        queryClient.invalidateQueries({
          queryKey: migrationProductionAdoptionKeys.state(migrationId),
        }),
        queryClient.invalidateQueries({
          queryKey: migrationSessionKeys.detail(migrationId),
        }),
        queryClient.invalidateQueries({
          queryKey: migrationSessionKeys.list(),
        }),
      ])
    },
  })
}

export function useMakeMigrationServerLive(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: MakeMigrationServerLiveRequest) =>
      makeServerLiveWithReconciliation(migrationId, request),
    retry: false,
    onSuccess: (state) => {
      queryClient.setQueryData(migrationProductionAdoptionKeys.state(migrationId), state)
    },
    onSettled: async () => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: migrationProductionAdoptionKeys.state(migrationId),
        }),
        queryClient.invalidateQueries({
          queryKey: migrationSessionKeys.detail(migrationId),
        }),
        queryClient.invalidateQueries({
          queryKey: migrationSessionKeys.list(),
        }),
      ])
    },
  })
}

export function usePrepareMigrationProductionAdoptionPlan(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: PrepareMigrationProductionAdoptionRequest) =>
      prepareMigrationProductionAdoptionPlan(migrationId, request),
    onSettled: async () => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: migrationProductionAdoptionKeys.state(migrationId),
        }),
        queryClient.invalidateQueries({
          queryKey: migrationSessionKeys.detail(migrationId),
        }),
        queryClient.invalidateQueries({
          queryKey: migrationSessionKeys.list(),
        }),
      ])
    },
  })
}

export function useMaterializeMigrationProductionRuntime(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: MaterializeMigrationProductionRuntimeRequest) =>
      materializeMigrationProductionRuntime(migrationId, request),
    onSettled: async () => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: migrationProductionAdoptionKeys.state(migrationId),
        }),
        queryClient.invalidateQueries({
          queryKey: migrationSessionKeys.detail(migrationId),
        }),
        queryClient.invalidateQueries({
          queryKey: migrationSessionKeys.list(),
        }),
      ])
    },
  })
}


export function usePrepareMigrationProductionCutoverPreview(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: PrepareMigrationProductionCutoverRequest) =>
      prepareMigrationProductionCutoverPreview(migrationId, request),
    onSettled: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: migrationProductionAdoptionKeys.state(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.detail(migrationId) }),
      ])
    },
  })
}

export function useExecuteMigrationProductionCutover(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: ExecuteMigrationProductionCutoverRequest) =>
      executeMigrationProductionCutover(migrationId, request),
    onSettled: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: migrationProductionAdoptionKeys.state(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.detail(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.list() }),
      ])
    },
  })
}


export function useRunMigrationProductionVerification(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: RunMigrationProductionVerificationRequest) =>
      runMigrationProductionVerification(migrationId, request),
    onSettled: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: migrationProductionAdoptionKeys.state(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.detail(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.list() }),
      ])
    },
  })
}

export function usePrepareMigrationProductionRollbackPreview(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: PrepareMigrationProductionRollbackRequest) =>
      prepareMigrationProductionRollbackPreview(migrationId, request),
    onSettled: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: migrationProductionAdoptionKeys.state(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.detail(migrationId) }),
      ])
    },
  })
}

export function useExecuteMigrationProductionRollback(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: ExecuteMigrationProductionRollbackRequest) =>
      executeMigrationProductionRollback(migrationId, request),
    onSettled: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: migrationProductionAdoptionKeys.state(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.detail(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.list() }),
      ])
    },
  })
}


export function useImportMigrationProductionSourceCompletion(migrationId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (envelope: MigrationProductionSourceRestorationCompletionEnvelope) =>
      importMigrationProductionSourceCompletion(migrationId, envelope),
    onSettled: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: migrationProductionAdoptionKeys.state(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.detail(migrationId) }),
        queryClient.invalidateQueries({ queryKey: migrationSessionKeys.list() }),
      ])
    },
  })
}
