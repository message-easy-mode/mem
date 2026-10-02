/**
 * Safe Backup Catalog projections returned by HostAgent.
 * These mirror HostAgent.Runtime.Backups.Catalog contracts; do not add local
 * lifecycle rules here.
 */
export type BackupCatalogOriginKind = "local-captured" | "imported-zip" | string
export type BackupCatalogPayloadState =
  | "available"
  | "materialising"
  | "failed"
  | "removed"
  | string
export type BackupCatalogIntegrityStatus = "valid" | "warning" | "invalid" | "unknown" | string

export type BackupCatalogListItem = {
  catalogEntryId: string
  originKind: BackupCatalogOriginKind
  displayName: string
  sourceStackSlug: string | null
  sourceBackupId: string | null
  capturedAtUtc: string | null
  payloadState: BackupCatalogPayloadState
  integrityStatus: BackupCatalogIntegrityStatus
  warningCount: number
  payloadBytes: number | null
  createdAtUtc: string
  importedAtUtc: string | null
  materialisedAtUtc: string | null
  payloadRemovedAtUtc: string | null
  /** Non-blocking imported ZIP guidance. It is separate from integrity warnings. */
  advisoryCount?: number
}

export type BackupCatalogListResponse = {
  totalCount: number
  entries: BackupCatalogListItem[]
}

export type BackupCatalogDetailResponse = {
  catalogEntryId: string
  originKind: BackupCatalogOriginKind
  displayName: string
  sourceStackSlug: string | null
  sourceBackupId: string | null
  validationId: string | null
  manifestVersion: number | null
  memVersion: string | null
  matrixServerName: string | null
  matrixHost: string | null
  elementHost: string | null
  capturedAtUtc: string | null
  payloadState: BackupCatalogPayloadState
  integrityStatus: BackupCatalogIntegrityStatus
  integritySummary: string | null
  warningCount: number
  payloadBytes: number | null
  createdAtUtc: string
  importedAtUtc: string | null
  materialisedAtUtc: string | null
  payloadRemovedAtUtc: string | null
  payloadRemovedBy: string | null
  /** Non-blocking imported ZIP guidance. It is separate from integrity warnings. */
  advisoryCount?: number
  advisories?: BackupCatalogAdvisory[] | null
}

export type BackupCatalogAdvisory = {
  category: string
  title: string
  message: string
}

export type BackupCatalogLocalBackfillItem = {
  stackSlug: string
  backupId: string
  catalogEntryId: string
  action: string
  payloadState: BackupCatalogPayloadState
  integrityStatus: BackupCatalogIntegrityStatus
  warningCount: number
}

export type BackupCatalogLocalBackfillFailure = {
  stackSlug: string
  backupId: string
  error: string
}

export type BackupCatalogLocalBackfillResponse = {
  source: string
  status: string
  scanned: number
  created: number
  updated: number
  skippedRemoved: number
  failed: number
  entries: BackupCatalogLocalBackfillItem[]
  failures: BackupCatalogLocalBackfillFailure[]
  detail: string | null
}

export type BackupCatalogImportArchiveResponse = {
  validationId: string
  archiveState: string
  originalFileName: string | null
  archiveBytes: number | null
  catalogEntryLinked: boolean
  catalogEntryId: string | null
  detail: string
}

export type BackupCatalogLifecycleResponse = {
  catalogEntryId: string
  payloadState: BackupCatalogPayloadState
  payloadPresent: boolean
  hasActiveRestore: boolean
  activeRestoreSessionId: string | null
  canDelete: boolean
  deleteBlockReason: string | null
  originalArchive: BackupCatalogImportArchiveResponse | null
}

export type BackupCatalogPermanentDeleteRequest = { operator?: string | null }

export type BackupCatalogPermanentDeleteResponse = {
  source: string
  status: string
  catalogEntryId: string
  originKind: BackupCatalogOriginKind
  deletedBy: string | null
  payloadDeleted: boolean
  originalArchiveDeleted: boolean
  portableExportsDeleted: number
  detachedRestoreAttempts: number
  detail: string
}

export type BackupCatalogImportArchiveDeleteRequest = { operator?: string | null }

export type BackupCatalogImportArchiveDeleteResponse = {
  source: string
  status: string
  validationId: string
  archiveState: string
  catalogEntryId: string | null
  detail: string
}

export type BackupCatalogImportedMaterialisationResponse = {
  source: string
  status: string
  validationId: string
  catalogEntryId: string
  action: string
  payloadState: BackupCatalogPayloadState
  integrityStatus: BackupCatalogIntegrityStatus
  warningCount: number
  payloadBytes: number | null
  materialisedAtUtc: string | null
  detail: string | null
  advisoryCount?: number
}

export type CatalogRestoreSessionResponse = {
  source: string
  status: string
  catalogEntryId: string
  restoreSessionId: string
  restoreAttemptCreated: boolean
  restoreAttemptResumed: boolean
  sourceKind: string
  payloadState: BackupCatalogPayloadState
  integrityStatus: BackupCatalogIntegrityStatus
  warningCount: number
  detail: string
}


/**
 * Browser-safe result for a fresh portable ZIP generated from a managed
 * Backup Catalog payload. Host filesystem paths are intentionally absent.
 */
export type BackupCatalogPortableExportResponse = {
  source: string
  status: string
  catalogEntryId: string
  originKind: BackupCatalogOriginKind
  sourceStackSlug: string
  exportId: string
  downloadName: string
  downloadPath: string
  sizeBytes: number
  warnings: string[]
  detail: string | null
}


export type CatalogProductionCandidateResponse = {
  source: string
  status: string
  catalogEntryId: string
  restoreSessionId: string
  candidateCreated: boolean
  candidateResumed: boolean
  candidate: {
    candidateId: string
    status: string
    privateRuntimeStatus: string
    matrixServerName: string | null
    safety: { privateOnly: boolean; publicRoutesCreated: boolean; productionExecutionLocked: boolean }
    runtime: { synapseHealthPassed: boolean; elementHealthPassed: boolean }
    warnings: string[]
    errors: string[]
  }
  detail: string
}

export type CatalogCutoverPreviewResponse = {
  status: string
  catalogEntryId: string
  candidateId: string
  preview: {
    previewId: string
    status: string
    productionExecutionLocked: boolean
    blockers: string[]
    warnings: string[]
    errors: string[]
  }
  detail: string
}

export type CatalogCutoverConfirmationRequest = {
  previewId: string
  candidateId: string
  note?: string | null
  acknowledgePreviewReviewed: boolean
  acknowledgeCandidateIsPrivateAndHealthy: boolean
  acknowledgePublicRouteExposureRisk: boolean
  acknowledgeNoAutomaticRollback: boolean
  acknowledgeFinalBackupRequired: boolean
  acknowledgeExecutionStillLocked: boolean
}

export type CatalogCutoverConfirmationResponse = {
  status: string
  catalogEntryId: string
  previewId: string
  candidateId: string
  confirmation: {
    confirmationId: string
    status: string
    productionExecutionLocked: boolean
    executionAvailable: boolean
    blockers: string[]
    warnings: string[]
    errors: string[]
  }
  detail: string
}

export type CatalogPreCutoverReadinessResponse = {
  source: string
  status: string
  catalogEntryId: string
  catalog: BackupCatalogDetailResponse
  executionReady: boolean
  blockers: string[]
  warnings: string[]
  detail: string
}

export type CatalogCutoverExecutionRequest = {
  confirmationId: string
  candidateId: string
  oldRuntimeStackSlug: string
  operator?: string | null
  note?: string | null
  execute: true
  acknowledgeFinalApproval: boolean
  acknowledgeFinalBackupWillBeCaptured: boolean
  acknowledgeOldRuntimeWillBeStopped: boolean
  acknowledgePublicRouteMutation: boolean
  acknowledgeManualRollback: boolean
  gateEvaluationOnly?: false
}

export type CatalogCutoverExecutionResponse = {
  source: string
  status: string
  catalogEntryId: string
  restoreSessionId: string
  confirmationId: string
  candidateId: string
  execution: {
    executionId: string
    status: string
    oldRuntimeStackSlug: string
    finalBackup: { captured: boolean; backupId: string | null; backupCatalogEntryId: string | null; detail: string | null }
    oldRuntime: { retainedForRollback: boolean; matrixContainerStopped: boolean; elementContainerStopped: boolean; detail: string | null }
    routes: { allRequiredRoutesSucceeded: boolean; anyRouteMutated: boolean; detail: string | null }
    publicVerification: { attempted: boolean; passed: boolean; detail: string | null }
    rollback: { required: boolean; attempted: boolean; completed: boolean; detail: string | null; warnings: string[] }
    blockers: string[]
    warnings: string[]
    errors: string[]
    detail: string | null
  }
  detail: string
}


export type CatalogPostCutoverProjectionResponse = {
  source: string
  status: string
  catalogEntryId: string
  restoreSessionId: string | null
  cutover: {
    state: string
    executionRecorded: boolean
    routeTransitionCompleted: boolean
    publicVerificationPassed: boolean
    finalBackupCaptured: boolean
    rolledBack: boolean
    oldRuntimeRetainedForRollback: boolean
    executionId: string | null
    candidateId: string | null
    confirmationId: string | null
    startedAtUtc: string | null
    finishedAtUtc: string | null
    detail: string | null
  }
  latestExecution: { status: string; rollbackAttempted: boolean; rollbackCompleted: boolean; detail: string | null } | null
  latestSuccessfulExecution: { executionId: string; status: string; finishedAtUtc: string | null; detail: string | null } | null
  executionCount: number
  canOpenRestoreWorkspace: boolean
  warnings: string[]
  detail: string
}
