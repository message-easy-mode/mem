export type RestoreWorkspaceResponse = {
  schemaVersion: number
  restoreSessionId: string
  attempt: RestoreWorkspaceAttempt
  source: RestoreWorkspaceSource
  target: RestoreWorkspaceTarget
  overallStatus: RestoreWorkspaceOverallStatus
  standardStages: RestoreWorkspaceStandardStage[]
  advancedTools: RestoreWorkspaceAdvancedTool[]
  verification: RestoreWorkspaceVerification
  evidence: RestoreWorkspaceEvidenceSummary
  logs: RestoreWorkspaceLogsSummary
  warnings: string[]
  /** Explicit backend cancellation capability for this workspace. */
  cancellation?: RestoreWorkspaceCancellation | null
}

export type RestoreWorkspaceAttempt = {
  id: string
  status: string
  currentStage: string
  createdAtUtc: string
  updatedAtUtc: string
  terminalAtUtc: string | null
  lastEventAtUtc: string | null
  lastErrorCode: string | null
  lastErrorSummary: string | null
  warningCount: number
  errorCount: number
  currentOperationId: string | null
}

export type RestoreWorkspaceSource = {
  kind: string
  stackSlug: string | null
  backupId: string | null
  createdAtUtc: string | null
  sizeBytes: number | null
  matrixHost: string | null
  elementHost: string | null
  validationStatus: string
  validationSummary: string
  catalogEntryId: string | null
  sourceDisplayName: string
  sourceOriginKind: string
  sourceDeleted: boolean
}

export type RestoreWorkspaceTarget = {
  stackSlug: string | null
  matrixHost: string | null
  elementHost: string | null
  availability: string
  detail: string
  claims: RestoreWorkspaceTargetClaim[]
}

export type RestoreWorkspaceTargetClaim = {
  resourceType: string
  resourceValue: string
  status: string
  claimedAtUtc: string
  releasedAtUtc: string | null
  releaseReason: string | null
}

export type RestoreWorkspaceOverallStatus = {
  code: string
  title: string
  description: string
  severity: string
  nextAction: RestoreWorkspaceAction | null
}

export type RestoreWorkspaceCancellation = {
  canCancel: boolean
  reasonUnavailable: string | null
  summary: string
}

export type RestoreWorkspaceAction = {
  code: string
  title: string
  description: string
  enabled: boolean
  relatedStage: string | null
}

export type RestoreWorkspaceStandardStage = {
  code: string
  title: string
  description: string
  state: string
  required: boolean
  unlocked: boolean
  completedAtUtc: string | null
  primaryAction: RestoreWorkspaceAction | null
  secondaryActions: RestoreWorkspaceAction[]
  summary: string
  blockers: string[]
  evidenceSummary: RestoreWorkspaceStageEvidenceSummary
  operationSummary: RestoreWorkspaceOperationSummary | null
  /**
   * Curated, browser-safe evidence from the canonical private restore test.
   * This remains available after the disposable staging runtime is destroyed.
   */
  privateTestEvidence?: RestoreWorkspacePrivateTestEvidence | null
}

export type RestoreWorkspaceStageEvidenceSummary = {
  itemCount: number
  latestOccurredAtUtc: string | null
  latestStatus: string | null
}

export type RestoreWorkspaceOperationSummary = {
  operationId: string
  operation: string
  status: string
  currentStep: string | null
  requestedAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  lastActivityAtUtc?: string | null
  attemptNumber?: number
}

export type RestoreWorkspacePrivateTestEvidence = {
  sourceKind: string
  catalogEntryId: string | null
  stagingId: string | null
  matrixServerName: string | null
  status: string
  privateOnly: boolean
  dockerNetworkInternal: boolean
  databaseImportSucceeded: boolean
  synapseHealthPassed: boolean
  requiresExplicitDestroy: boolean
  completedAtUtc: string | null
  stagingRuntimeStatus: string
  stagingRuntimeDestroyed: boolean | null
  destroyAvailable: boolean | null
  destroyedAtUtc: string | null
}

export type RestoreWorkspacePrivateTestActionResponse = {
  source: string
  status: string
  restoreSessionId: string
  operationId: string
  sourceKind: string
  catalogEntryId: string | null
  stagingId: string | null
  privateOnly: boolean | null
  databaseImportSucceeded: boolean | null
  synapseHealthPassed: boolean | null
  requiresExplicitDestroy: boolean | null
  detail: string
}


/**
 * Explicit operator request to execute Standard Recreate from a canonical
 * Restore Workspace. The backend resolves the managed Backup Catalog source
 * from restoreSessionId; clients never send upload validation identity.
 */
export type RestoreWorkspaceStandardRecreateActionRequest = {
  restoreSessionId: string
  targetStackSlug: string
  requestedDomainId?: string | null
  elementHost: string
  matrixImage?: string | null
  elementImage?: string | null
  operator?: string | null
  note?: string | null
  executeProductionRecreate: boolean
  acknowledgeCreatesRealStack: boolean
  acknowledgeMutatesProductionPostgres: boolean
  acknowledgeMutatesNpmRoutes: boolean
  acknowledgeNoAutomaticRollback: boolean
}

/**
 * Safe summary returned after canonical Standard Recreate execution.
 */
export type RestoreWorkspaceStandardRecreateActionResponse = {
  source: string
  status: string
  recreateId: string
  sourceKind: string
  catalogEntryId: string | null
  targetStackSlug: string
  matrixHost: string
  elementHost: string
  detail: string
  errors: string[]
}

export type RestoreWorkspaceAdvancedTool = {
  code: string
  title: string
  availability: string
  reasonUnavailable: string | null
  relatedStage: string
}

export type RestoreWorkspaceVerification = {
  status: string
  hasRun: boolean
  allPassed: boolean | null
  checkedAtUtc: string | null
  checks: RestoreWorkspaceVerificationCheck[]
  summary: string
}

export type RestoreWorkspaceVerificationCheck = {
  code: string
  title: string
  status: string
}

export type RestoreWorkspaceEvidenceSummary = {
  categories: RestoreWorkspaceEvidenceCategory[]
  latestFailure: RestoreWorkspaceEvidenceItem | null
  latestSuccess: RestoreWorkspaceEvidenceItem | null
}

export type RestoreWorkspaceEvidenceCategory = {
  code: string
  title: string
  status: string
  itemCount: number
  latestOccurredAtUtc: string | null
  items: RestoreWorkspaceEvidenceItem[]
}

export type RestoreWorkspaceEvidenceItem = {
  code: string
  category: string
  title: string
  status: string
  occurredAtUtc: string
  eventCode: string | null
  operationId: string | null
  stage: string | null
  description: string
}

export type RestoreWorkspaceLogsSummary = {
  totalEvents: number
  warningCount: number
  errorCount: number
  latestEvent: RestoreWorkspaceLogEventSummary | null
  latestWarningOrError: RestoreWorkspaceLogEventSummary | null
  supportReportAvailable: boolean
  supportBundleAvailable: boolean
  warnings: string[]
}

export type RestoreWorkspaceLogEventSummary = {
  timestampUtc: string
  stage: string
  severity: string
  eventCode: string
  message: string
  operationId: string | null
}
