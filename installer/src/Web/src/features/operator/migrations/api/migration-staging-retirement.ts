import { migrationStagingRequest } from "./migration-staging"

export type StagingRetirementTarget = { migrationId: string; stagingRunId: string }
export type StagingRetirementRequest = { reviewFingerprint: string; confirmRetirement: true; retry: boolean }
export type StagingRetirementOperation = {
  operationId: string
  status: "queued" | "running" | "needs-attention" | "retired"
  currentStep: string
  attemptCount: number
  requestedAtUtc: string
  updatedAtUtc: string
  completedAtUtc: string | null
  failureCode: string | null
}
export type StagingRetirementReview = StagingRetirementTarget & {
  displayName: string
  canRetire: boolean
  blockerCode: string | null
  reviewFingerprint: string | null
  containerCount: number
  networkCount: number
  workspacePresent: boolean
  operation: StagingRetirementOperation | null
}
const path = (target: StagingRetirementTarget) =>
  `/api/operator/migrations/sessions/${encodeURIComponent(target.migrationId)}/staging-runs/${encodeURIComponent(target.stagingRunId)}/retirement`
export const getStagingRetirementReview = (target: StagingRetirementTarget, signal?: AbortSignal) =>
  migrationStagingRequest<StagingRetirementReview>("GET", path(target), undefined, signal)
export const acceptStagingRetirement = (target: StagingRetirementTarget, request: StagingRetirementRequest) =>
  migrationStagingRequest<StagingRetirementOperation>("POST", path(target), request)
