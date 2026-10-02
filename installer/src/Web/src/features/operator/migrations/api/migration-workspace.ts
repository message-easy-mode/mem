import type { MigrationSessionDetail } from "./migration-sessions"

import { getJson } from "@/lib/api"

export type MigrationWorkspaceAction = {
  code: string
  enabled: boolean
  relatedStage: string
}

export type MigrationWorkspaceProblem = {
  code: string
  severity: string
  detail: string | null
}

export type MigrationWorkspaceStageEvidenceSummary = {
  itemCount: number
  latestOccurredAtUtc: string | null
  latestStatus: string | null
}

export type MigrationWorkspaceOperationSummary = {
  operationId: string
  operation: string
  status: string
  currentStep: string | null
  requestedAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
}

export type MigrationWorkspaceFailureOutcome = {
  failureCode: string
  failedComponent: string
  mutationState: string
  compensationState: string
  observedCurrentState: string
  safeToRetry: boolean
  safeStateSummaryCode: string
  nextAction: MigrationWorkspaceAction | null
}

export type MigrationWorkspaceStage = {
  code: string
  state: string
  unlocked: boolean
  completedAtUtc: string | null
  primaryAction: MigrationWorkspaceAction | null
  secondaryActions: MigrationWorkspaceAction[]
  summaryCode: string
  problems: MigrationWorkspaceProblem[]
  evidenceSummary: MigrationWorkspaceStageEvidenceSummary
  operationSummary: MigrationWorkspaceOperationSummary | null
  failureOutcome: MigrationWorkspaceFailureOutcome | null
}

export type MigrationWorkspaceActivityItem = {
  code: string
  status: string
  occurredAtUtc: string
  relatedObjectId: string | null
}

export type MigrationWorkspaceEvidenceCategory = {
  code: string
  status: string
  itemCount: number
  latestOccurredAtUtc: string | null
}

export type MigrationWorkspaceAdvancedTool = {
  code: string
  availability: string
  reasonUnavailable: string | null
  relatedStage: string
}

export type MigrationWorkspaceResponse = {
  schemaVersion: number
  guided: MigrationWorkspaceGuidedState
  migration: {
    migrationId: string
    displayName: string
    status: string
    createdAtUtc: string
    updatedAtUtc: string
  }
  source: {
    adapter: string
    displayName: string
    product: string | null
    productVersion: string | null
    matrixServerName: string | null
    usersCount: number | null
    roomsCount: number | null
    eventsCount: number | null
    blockerCount: number
    warningCount: number
    advisoryCount: number
  }
  target: {
    planPrepared: boolean
    status: string
    stackSlug: string | null
    displayName: string | null
    matrixHost: string | null
    elementHost: string | null
  }
  overallStatus: {
    code: string
    severity: string
    currentStageCode: string
    nextAction: MigrationWorkspaceAction | null
  }
  stages: MigrationWorkspaceStage[]
  currentOperation: MigrationWorkspaceOperationSummary | null
  activity: MigrationWorkspaceActivityItem[]
  verification: {
    status: string
    hasRun: boolean
    allPassed: boolean | null
    checkCount: number
    failedCheckCount: number
    checkedAtUtc: string | null
  }
  retention: {
    status: string
    retainUntilUtc: string | null
    automaticDeletionAllowed: boolean | null
  }
  evidence: {
    categories: MigrationWorkspaceEvidenceCategory[]
  }
  advancedTools: MigrationWorkspaceAdvancedTool[]
  warnings: MigrationWorkspaceProblem[]
}

const base = "/api/operator/migrations/sessions"

export const getMigrationWorkspace = (migrationId: string) =>
  getJson<MigrationWorkspaceResponse>(
    `${base}/${encodeURIComponent(migrationId)}/workspace`,
  )

export type MigrationWorkspaceGuidedState = {
  revision: string
  currentStageCode: string
  stageState: string
  currentOperation: MigrationWorkspaceOperationSummary | null
  nextAction: MigrationWorkspaceAction | null
  blocker: MigrationWorkspaceProblem | null
  uncertaintyState: "known" | "unknown"
  detail: MigrationSessionDetail
  productionAuthorized: boolean
  productionAuthorityType: string | null
  conversionAttemptId: string | null
  conversionStatus: string | null
  hasVerifiedCandidate: boolean
  stagingRunId: string | null
  stagingStatus: string | null
  stagingRetained: boolean
  adoptionPlanId: string | null
  privateRuntimeReady: boolean
  cutoverPreviewId: string | null
  cutoverPreviewStatus: string | null
  publicRoutesCreated: boolean
  runtimePromotionCompleted: boolean
  accepted: boolean
  acceptanceId: string | null
  baselineBackupStatus: string
  baselineBackupId: string | null
  baselineCatalogEntryId: string | null
  operationRevisions: Record<string, string>
  cancellation?: MigrationWorkspaceCancellation | null
}

/** Additive capability: an absent or unknown contract never enables cancellation. */
export type MigrationWorkspaceCancellation = {
  stateVersion: number
  lifecycleStatus: string
  canCancel: boolean
  confirmationKind: "empty-session" | "package" | "progressed" | "unavailable"
  blockedCode: string | null
  blockedReason: string | null
}
