export type RuntimeStackBackupHistoryResponse = {
  source: string
  status: string
  backupsRootPath: string
  stacks: RuntimeStackBackupStackHistory[]
  detail: string | null
}

export type RuntimeStackBackupEntryListQuery = {
  page: number
  pageSize: number
  search: string | null
  stackSlug: string | null
  sortBy: string | null
  sortDirection: "asc" | "desc" | string | null
}

export type RuntimeStackBackupEntryListSummary = {
  totalBackups: number
  totalStacks: number
  totalBytes: number
  totalFiles: number
  latestBackup: RuntimeStackBackupHistoryItem | null
}

/**
 * Paged flat inventory for the Local Backups "By date" table.
 * The existing RuntimeStackBackupHistoryResponse remains the complete
 * stack-group catalog used by the separate "By stack" card view.
 */
export type RuntimeStackBackupEntryListResponse = {
  source: string
  status: string
  backupsRootPath: string
  query: RuntimeStackBackupEntryListQuery
  summary: RuntimeStackBackupEntryListSummary
  totalBackups: number
  page: number
  pageSize: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
  availableStacks: string[]
  backups: RuntimeStackBackupHistoryItem[]
  warnings: string[]
  detail: string | null
}

export type RuntimeStackBackupStackHistoryResponse = {
  source: string
  status: string
  backupsRootPath: string
  stack: RuntimeStackBackupStackHistory | null
  detail: string | null
}

export type RuntimeStackBackupDetailResponse = {
  source: string
  status: string
  stackSlug: string
  backupId: string
  backupRootPath: string
  manifestPath: string | null
  manifestPresent: boolean
  createdAtUtc: string | null
  manifest: RuntimeStackBackupManifest | null
  components: RuntimeStackBackupHistoryComponents
  totalBytes: number
  totalFiles: number
  warnings: string[]
  detail: string | null
}

/**
 * Result of intentionally deleting one local backup artifact. Historical
 * restore records and portable export/import artifacts are not changed.
 */
export type RuntimeStackBackupDeleteResponse = {
  source: string
  status: string
  stackSlug: string
  backupId: string
  backupRootPath: string
  deletedBytes: number
  deletedFiles: number
  stackDirectoryRemoved: boolean
  deletedAtUtc: string | null
  warnings: string[]
  detail: string | null
}

export type RuntimeStackBackupExportResult = {
  source: string
  status: string
  exportId: string
  stackSlug: string
  backupId: string
  exportPath: string
  downloadName: string
  downloadPath: string
  sizeBytes: number
  warnings: string[]
  detail: string | null
}

export type RuntimeStackBackupExportDownloadResult = {
  blob: Blob
  downloadName: string
  contentType: string
}

export type RuntimeStackBackupStackHistory = {
  stackSlug: string
  backupCount: number
  latestBackupId: string | null
  latestCreatedAtUtc: string | null
  totalBytes: number
  totalFiles: number
  backups: RuntimeStackBackupHistoryItem[]
  warnings: string[]
}

export type RuntimeStackBackupHistoryItem = {
  stackSlug: string
  backupId: string
  backupRootPath: string
  manifestPath: string | null
  manifestPresent: boolean
  createdAtUtc: string | null
  components: RuntimeStackBackupHistoryComponents
  totalBytes: number
  totalFiles: number
  warnings: string[]
}

export type RuntimeStackBackupHistoryComponents = {
  databaseDump: RuntimeStackBackupHistoryFileComponent
  matrixConfig: RuntimeStackBackupHistoryFileComponent
  matrixSigningKey: RuntimeStackBackupHistoryFileComponent
  matrixMediaStore: RuntimeStackBackupHistoryDirectoryComponent
  elementConfig: RuntimeStackBackupHistoryFileComponent
}

export type RuntimeStackBackupHistoryFileComponent = {
  present: boolean
  relativePath: string
  absolutePath: string
  bytes: number
}

export type RuntimeStackBackupHistoryDirectoryComponent = {
  present: boolean
  relativePath: string
  absolutePath: string
  bytes: number
  files: number
}

export type RuntimeStackBackupManifest = {
  backupVersion: string
  backupId: string
  createdAtUtc: string
  runtimeStackId: string
  stackSlug: string
  database: RuntimeStackBackupDatabaseManifest
  matrix: RuntimeStackBackupMatrixManifest
  element: RuntimeStackBackupElementManifest | null
  stats: RuntimeStackBackupStats | null
  warnings: string[]
}

export type RuntimeStackBackupDatabaseManifest = {
  engine: string
  host: string
  port: number
  databaseName: string
  databaseUsername: string
  passwordSecretKind: string
  dumpPath: string
}

export type RuntimeStackBackupMatrixManifest = {
  dataPath: string
  homeserverYamlPath: string | null
  signingKeyPath: string | null
  mediaStorePath: string | null
  backupPath: string
}

export type RuntimeStackBackupElementManifest = {
  dataPath: string
  configPath: string | null
  backupPath: string
}

export type RuntimeStackBackupFileStats = {
  included: boolean
  path: string | null
  bytes: number
}

export type RuntimeStackBackupDirectoryStats = {
  included: boolean
  path: string | null
  bytes: number
  files: number
}

export type RuntimeStackBackupStats = {
  databaseDump: RuntimeStackBackupFileStats
  homeserverConfig: RuntimeStackBackupFileStats
  signingKey: RuntimeStackBackupFileStats
  mediaStore: RuntimeStackBackupDirectoryStats
  elementConfig: RuntimeStackBackupFileStats
  totalBytes: number
  totalFiles: number
}

export type RuntimeStackBackupUploadValidationResponse = {
  source: string
  status: string
  validationId: string
  /** Legacy validated-import restore fields. New imports intentionally leave these unset until an operator starts recovery from the catalog. */
  restoreSessionId?: string
  restoreAttemptCreated?: boolean
  restoreAttemptResumed?: boolean
  /** Canonical Backup Catalog identity created automatically for a valid uploaded ZIP. */
  catalogEntryId?: string
  catalogPayloadState?: string
  catalogMaterialisationAction?: string
  uploadedFileName: string
  storedZipPath: string
  zipBytes: number
  zipEntryCount: number
  totalUncompressedBytes: number
  manifestPresent: boolean
  checksumsPresent: boolean
  manifest: MemStackExportManifestSummary | null
  integrity: RuntimeStackBackupUploadIntegritySummary
  checks: RuntimeStackBackupUploadValidationCheck[]
  warnings: string[]
  errors: string[]
  detail: string | null
}

export type RuntimeStackBackupUploadValidationCheck = {
  code: string
  severity: string
  passed: boolean
  message: string
  detail: string | null
}

export type RuntimeStackBackupUploadIntegritySummary = {
  checksumLines: number
  checkedFiles: number
  missingFiles: number
  failedFiles: number
  passedFiles: number
}

export type MemStackExportManifestSummary = {
  manifestVersion: number
  exportKind: string | null
  createdAtUtc: string | null
  createdBy: string | null
  memVersion: string | null
  stack: MemStackExportStackSummary
  database: MemStackExportDatabaseSummary
  matrix: MemStackExportMatrixSummary
  element: MemStackExportElementSummary
  routes: MemStackExportRoutesSummary
  coturn: MemStackExportCoturnSummary
  restorePolicy: MemStackExportRestorePolicySummary
  includedFiles: string[]
  warnings: string[]
}

export type MemStackExportStackSummary = {
  stackId: string | null
  slug: string | null
  displayName: string | null
  matrixServerName: string | null
  matrixPublicUrl: string | null
  elementPublicUrl: string | null
}

export type MemStackExportDatabaseSummary = {
  engine: string | null
  dumpFile: string | null
  databaseName: string | null
  username: string | null
  present: boolean
}

export type MemStackExportMatrixSummary = {
  homeserverConfig: string | null
  signingKey: string | null
  mediaStore: string | null
  mediaBytes: number
  mediaFiles: number
  present: boolean
}

export type MemStackExportElementSummary = {
  config: string | null
  present: boolean
}

export type MemStackExportRoutesSummary = {
  matrixHost: string | null
  elementHost: string | null
  requiresDns: boolean
}

export type MemStackExportCoturnSummary = {
  configured: boolean
  publicHost: string | null
  realm: string | null
  turnUris: string[]
  sharedSecretPresent?: boolean
  userLifetime?: string | null
  allowGuests?: boolean | null
  state?: string
  management?: string
  configurationSource?: string | null
  configurationSha256?: string | null
}

export type MemStackExportRestorePolicySummary = {
  canRestoreToFreshMemServer: boolean
  requiresPostgres: boolean
  requiresDomainMapping: boolean
  requiresSigningKey: boolean
  requiresOldServerStoppedForSameServerName: boolean
}

export type RuntimeStackBackupRestoreLabDatabaseRequest = {
  validationId: string
  keepOnFailure?: boolean
  postgresImage?: string | null
}

export type RuntimeStackBackupRestoreLabDatabaseResult = {
  source: string
  status: string
  mode: string
  labId: string
  validationId: string
  startedAtUtc: string
  finishedAtUtc: string
  uploadedZipPath: string
  workspacePath: string
  networkName: string | null
  networkId: string | null
  postgresContainerName: string | null
  postgresContainerId: string | null
  postgresImage: string
  databaseName: string
  databaseUser: string
  databaseDumpBytes: number
  database: RuntimeStackBackupRestoreLabDatabaseSummary
  checks: RuntimeStackBackupRestoreLabCheck[]
  warnings: string[]
  errors: string[]
  cleanup: RuntimeStackBackupRestoreLabCleanupSummary
  detail: string | null
}

export type RuntimeStackBackupRestoreLabCheck = {
  code: string
  severity: string
  passed: boolean
  message: string
  detail: string | null
}

export type RuntimeStackBackupRestoreLabDatabaseSummary = {
  importSucceeded: boolean
  publicTableCount: number
    synapseKnownTableCount: number
    usersCount: number | null
    eventsCount: number | null
    roomsCount: number | null
    stateEventsCount: number | null
    detectedSynapseTables: string[]
}

export type RuntimeStackBackupRestoreLabCleanupSummary = {
  requestedKeepOnFailure: boolean
  keptResources: boolean
  containerRemoved: boolean
  networkRemoved: boolean
  workspaceRemoved: boolean
  warnings: string[]
}

export type RuntimeStackBackupRestoreLabHistoryResponse = {
  source: string
  status: string
  historyRootPath: string
  totalRuns: number
  runs: RuntimeStackBackupRestoreLabRunSummary[]
  warnings: string[]
  detail: string | null
}

export type RuntimeStackBackupRestoreLabRunDetailResponse = {
  source: string
  status: string
  historyRootPath: string
  run: RuntimeStackBackupRestoreLabDatabaseResult | null
  warnings: string[]
  detail: string | null
}

export type RuntimeStackBackupRestoreLabRunSummary = {
  labId: string
  validationId: string
  mode: string
  status: string
  startedAtUtc: string
  finishedAtUtc: string
  durationSeconds: number
  uploadedZipName: string
  uploadedZipPath: string
  postgresImage: string
  databaseDumpBytes: number
  importSucceeded: boolean
  publicTableCount: number
    synapseKnownTableCount: number
    usersCount: number | null
    eventsCount: number | null
    roomsCount: number | null
    stateEventsCount: number | null
    containerRemoved: boolean
    networkRemoved: boolean
    workspaceRemoved: boolean
    keptResources: boolean
    warningCount: number
    errorCount: number
    detail: string | null
}

export type RuntimeStackBackupRestorePlanRequest = {
  validationId: string
  restoreMode?: "replacement" | "new-isolated-stack" | string
  targetStackSlug?: string | null
}

export type RuntimeStackBackupRestorePlanResponse = {
  source: string
  status: string
  validationId: string
  restoreMode: string
  plannedAtUtc: string
  sourceExport: RuntimeStackBackupRestorePlanSource
  target: RuntimeStackBackupRestorePlanTarget
  restoreLabEvidence: RuntimeStackBackupRestorePlanLabEvidence
  conflicts: RuntimeStackBackupRestorePlanConflictSummary
  requirements: RuntimeStackBackupRestorePlanRequirement[]
  steps: RuntimeStackBackupRestorePlanStep[]
  blockingIssues: string[]
  warnings: string[]
  operatorConfirmations: string[]
  detail: string | null
}

export type RuntimeStackBackupRestorePlanSource = {
  uploadedZipPath: string
  zipBytes: number
  zipEntryCount: number
  manifest: MemStackExportManifestSummary | null
  stackSlug: string | null
  matrixServerName: string | null
  matrixPublicUrl: string | null
  elementPublicUrl: string | null
  databaseDumpPresent: boolean
  homeserverConfigPresent: boolean
  signingKeyPresent: boolean
  mediaStorePresent: boolean
  mediaFiles: number
  mediaBytes: number
  elementConfigPresent: boolean
}

export type RuntimeStackBackupRestorePlanTarget = {
  targetStackSlug: string
  targetServerName: string | null
  wouldReuseServerName: boolean
  wouldCreatePublicRoutes: boolean
  matrixHost: string | null
  elementHost: string | null
  plannedRuntimeRoot: string
  plannedMatrixDataPath: string
  plannedElementDataPath: string | null
  plannedDatabaseName: string
}

export type RuntimeStackBackupRestorePlanLabEvidence = {
  hasAnyRun: boolean
  latestRunId: string | null
  latestRunStatus: string | null
  latestRunStartedAtUtc: string | null
  importSucceeded: boolean
  publicTableCount: number
    synapseKnownTableCount: number
    usersCount: number | null
    eventsCount: number | null
    roomsCount: number | null
    stateEventsCount: number | null
    cleanupClean: boolean
    detail: string | null
}

export type RuntimeStackBackupRestorePlanConflictSummary = {
  hasConflicts: boolean
  items: RuntimeStackBackupRestorePlanConflict[]
}

export type RuntimeStackBackupRestorePlanConflict = {
  code: string
  severity: string
  present: boolean
  message: string
  existingStackSlug: string | null
  detail: string | null
}

export type RuntimeStackBackupRestorePlanRequirement = {
  code: string
  status: string
  message: string
  detail: string | null
}

export type RuntimeStackBackupRestorePlanStep = {
  order: number
  code: string
  title: string
  action: string
  mutatesState: boolean
  detail: string | null
}

export type RuntimeStackBackupRestoreStagingRequest = {
  validationId: string
  keepOnFailure?: boolean
  postgresImage?: string | null
  synapseImage?: string | null
  targetStackSlug?: string | null
}

export type RuntimeStackBackupRestoreStagingResult = {
  source: string
  status: string
  mode: string
  stagingId: string
  validationId: string
  startedAtUtc: string
  finishedAtUtc: string | null
  uploadedZipPath: string
  workspacePath: string
  runtimePath: string
  matrixDataPath: string
  elementDataPath: string | null
  databaseDumpPath: string
  networkName: string
  networkId: string | null
  postgresContainerName: string
  postgresContainerId: string | null
  synapseContainerName: string
  synapseContainerId: string | null
  postgresImage: string
  synapseImage: string
  databaseName: string
  databaseUser: string
  matrixServerName: string | null
  targetStackSlug: string
  safety: RuntimeStackBackupRestoreStagingSafetySummary
  database: RuntimeStackBackupRestoreStagingDatabaseSummary
  runtime: RuntimeStackBackupRestoreStagingRuntimeSummary
  checks: RuntimeStackBackupRestoreStagingCheck[]
  warnings: string[]
  errors: string[]
  destroy: RuntimeStackBackupRestoreStagingDestroySummary | null
  detail: string | null
}

export type RuntimeStackBackupRestoreStagingSafetySummary = {
  privateOnly: boolean
    dockerNetworkInternal: boolean
    publicRoutesCreated: boolean
      dnsChanged: boolean
      certificatesChanged: boolean
      productionContainersTouched: boolean
      productionDatabasesTouched: boolean
      requiresExplicitDestroy: boolean
      notes: string[]
}

export type RuntimeStackBackupRestoreStagingDatabaseSummary = {
  importSucceeded: boolean
  publicTableCount: number
    synapseKnownTableCount: number
    usersCount: number | null
    eventsCount: number | null
    roomsCount: number | null
    stateEventsCount: number | null
}

export type RuntimeStackBackupRestoreStagingRuntimeSummary = {
  homeserverConfigExtracted: boolean
  homeserverConfigPatched: boolean
  signingKeyExtracted: boolean
  mediaStoreExtracted: boolean
  mediaFiles: number
  mediaBytes: number
  elementConfigExtracted: boolean
  postgresContainerStarted: boolean
  synapseContainerStarted: boolean
  synapseHealthPassed: boolean
  healthResponse: string | null
  synapseLogsTail: string | null
}

export type RuntimeStackBackupRestoreStagingCheck = {
  code: string
  severity: string
  passed: boolean
  message: string
  detail: string | null
}

export type RuntimeStackBackupRestoreStagingDestroySummary = {
  destroyedAtUtc: string
  synapseContainerRemoved: boolean
  postgresContainerRemoved: boolean
  networkRemoved: boolean
  workspaceRemoved: boolean
  warnings: string[]
}

export type RuntimeStackBackupRestoreStagingHistoryResponse = {
  source: string
  status: string
  historyRootPath: string
  totalRuns: number
  runs: RuntimeStackBackupRestoreStagingRunSummary[]
  warnings: string[]
  detail: string | null
}

export type RuntimeStackBackupRestoreStagingRunDetailResponse = {
  source: string
  status: string
  historyRootPath: string
  run: RuntimeStackBackupRestoreStagingResult | null
  warnings: string[]
  detail: string | null
}

export type RuntimeStackBackupRestoreStagingRunSummary = {
  stagingId: string
  validationId: string
  mode: string
  status: string
  startedAtUtc: string
  finishedAtUtc: string | null
  durationSeconds: number | null
  uploadedZipName: string
  uploadedZipPath: string
  targetStackSlug: string
  matrixServerName: string | null
  postgresContainerName: string
  synapseContainerName: string
  networkName: string | null
  importSucceeded: boolean
  synapseHealthPassed: boolean
  publicTableCount: number
    synapseKnownTableCount: number
    usersCount: number | null
    eventsCount: number | null
    roomsCount: number | null
    destroyed: boolean
    warningCount: number
    errorCount: number
    detail: string | null
}

export type PrivateStagingSafeRunSummary = {
  stagingId: string
  validationId: string
  status: string
  startedAtUtc: string
  finishedAtUtc: string | null
  privateOnly: boolean
  publicRoutesCreated: boolean
  productionContainersTouched: boolean
  productionDatabasesTouched: boolean
  databaseImportSucceeded: boolean
  synapseHealthPassed: boolean
  destroyed: boolean
  destroyAvailable: boolean
  safeSummary: string
}

export type PrivateStagingSafeHistoryResponse = {
  status: string
  totalRuns: number
  runs: PrivateStagingSafeRunSummary[]
  detail: string | null
}

export type PrivateStagingSafeRunDetailResponse = {
  status: string
  run: PrivateStagingSafeRunSummary | null
  detail: string | null
}

export type RuntimeStackBackupProductionRecreatePreflightRequest = {
  restoreSessionId: string
  targetStackSlug: string
  requestedDomainId?: string | null
  /**
   * Kept optional for older API consumers. The canonical Standard Restore
   * path derives the Matrix identity from the backup and does not send this.
   */
  matrixHost?: string | null
  elementHost: string
}

export type RuntimeStackBackupProductionRecreatePreflightTargets = {
  targetStackSlug: string | null
  matrixHost: string | null
  elementHost: string | null
}

export type RuntimeStackBackupProductionRecreatePreflightCheck = {
  code: string
  title: string
  state: "passed" | "blocked" | string
  message: string
}

export type RuntimeStackBackupProductionRecreateTurnSummary = {
  mode: string
  state: string
  management: string
  platformTurnRequired: boolean
  platformTurnReady: boolean | null
  publicHost: string | null
  turnUris: string[]
  detail: string
}

export type RuntimeStackBackupProductionRecreatePreflightResponse = {
  source: string
  status: "ready" | "blocked" | string
  checkedAtUtc: string
  restoreSessionId: string
  validationId: string | null
  canCreate: boolean
  targets: RuntimeStackBackupProductionRecreatePreflightTargets
  checks: RuntimeStackBackupProductionRecreatePreflightCheck[]
  blockers: string[]
  warnings: string[]
  detail: string
  turn?: RuntimeStackBackupProductionRecreateTurnSummary | null
}

export type RuntimeStackBackupProductionRecreateRequest = {
  validationId: string
  targetStackSlug: string
  requestedDomainId?: string | null
  /**
   * Kept optional for older API consumers. The canonical Standard Restore
   * path derives the Matrix identity from the backup and does not send this.
   */
  matrixHost?: string | null
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

export type RuntimeStackBackupProductionRecreateCheck = {
  code: string
  severity: string
  passed: boolean
  message: string
  detail: string | null
}

export type RuntimeStackBackupProductionRecreateResult = {
  source: string
  status: string
  recreateId: string
  validationId: string
  runtimeStackId: string
  matrixInstanceId: string
  elementInstanceId: string
  targetStackSlug: string
  matrixHost: string
  elementHost: string
  startedAtUtc: string
  finishedAtUtc: string
  operator: string | null
  note: string | null
  database: {
    provisioned: boolean
    host: string
    port: number
    databaseName: string
    databaseUsername: string
    importSucceeded: boolean
    publicTableCount: number
      synapseKnownTableCount: number
      usersCount: number | null
      eventsCount: number | null
      roomsCount: number | null
      stateEventsCount: number | null
  }
  runtime: {
    matrixContainerName: string
    matrixContainerId: string | null
    matrixStarted: boolean
    matrixHealthPassed: boolean
    matrixHealthResponse: string | null
    elementContainerName: string
    elementContainerId: string | null
    elementStarted: boolean
    elementHealthPassed: boolean
    elementHealthResponse: string | null
    runtimeNetworkName: string
    matrixDataPath: string
    elementDataPath: string
    homeserverPath: string
    elementConfigPath: string
    manifestSaved: boolean
    databaseOwnershipSaved: boolean
    stackRegistered: boolean
  }
  routes: {
    matrixPublicHost: string
    matrixForwardHost: string
    matrixForwardPort: number
    matrixRouteId: string | null
    matrixRouteReady: boolean
    elementPublicHost: string
    elementForwardHost: string
    elementForwardPort: number
    elementRouteId: string | null
    elementRouteReady: boolean
    publicReadinessPassed: boolean
  }
  mutations: {
    runtimeStackCreated: boolean
    productionPostgresMutated: boolean
    productionContainersTouched: boolean
    npmRoutesChanged: boolean
    dnsChanged: boolean
    certificatesChanged: boolean
    oldStacksDeleted: boolean
    notes: string[]
  }
  checks: RuntimeStackBackupProductionRecreateCheck[]
  warnings: string[]
  errors: string[]
  detail: string
}

export type RuntimeStackBackupProductionRecreateCleanupCheck = {
  code: string
  severity: string
  passed: boolean
  message: string
  detail: string | null
}

export type RuntimeStackBackupProductionRecreateCleanupAssessment = {
  source: string
  status: string
  recreateId: string
  validationId: string
  runtimeStackId: string
  targetStackSlug: string
  candidateId: string | null
  candidateStatus: string | null
  candidateNetworkName: string | null
  cutoverIngressNetworkName: string | null
  candidateResolved: boolean
  candidateAlreadyRetired: boolean
  requiresOperatorConfirmation: boolean
  autoCleanupAvailable: boolean
  recommendedAction: string
  checks: RuntimeStackBackupProductionRecreateCleanupCheck[]
  warnings: string[]
  errors: string[]
  detail: string
}

export type RuntimeStackBackupProductionRecreateCleanupResult = {
  source: string
  status: string
  cleanupId: string
  recreateId: string
  validationId: string
  runtimeStackId: string
  targetStackSlug: string
  candidateId: string | null
  retirementMode: string
  startedAtUtc: string
  finishedAtUtc: string
  candidateDestroyRequested: boolean
  candidateDestroyed: boolean
  candidateElementContainerRemoved: boolean
  candidatePrivateRuntimeDestroyed: boolean
  cutoverIngressNetworkName: string | null
  cutoverIngressNetworkRemoved: boolean
  assessment: RuntimeStackBackupProductionRecreateCleanupAssessment
  warnings: string[]
  errors: string[]
  detail: string
}

export type RuntimeStackBackupRestoreWorkflowSource = {
  sourceKind: "local-backup" | "uploaded-zip" | string
  sourceLabel: string
  sourceStackSlug: string | null
  sourceBackupId: string | null
  uploadedFileName: string | null
  preparedAtUtc: string
}

export type RuntimeStackBackupRestoreWorkflowPrivateEvidence = {
  latestDatabaseLab: RuntimeStackBackupRestoreLabRunSummary | null
  latestPrivateStaging: RuntimeStackBackupRestoreStagingRunSummary | null
  databaseLabPassed: boolean
  privateStagingPassed: boolean
    detail: string
}

export type RuntimeStackBackupRestoreWorkflowProductionState = {
  recreateId: string | null
  status: string | null
  runtimeStackId: string | null
  targetStackSlug: string | null
  matrixHost: string | null
  elementHost: string | null
  stackRegistered: boolean
  publicReadinessPassed: boolean
    finishedAtUtc: string | null
    detail: string
}

export type RuntimeStackBackupRestoreWorkflowCandidateState = {
  candidateId: string | null
  status: string | null
  candidateFound: boolean
  retired: boolean
  cleanupPending: boolean
  detail: string
}

export type RuntimeStackBackupRestoreWorkflowNextAction = {
  code: string
  title: string
  description: string
  canRunPrivateTest: boolean
  canRecreateProduction: boolean
  canRetireCandidate: boolean
}

export type RuntimeStackBackupRestoreWorkflowSummaryResponse = {
  source: string
  status: string
  validationId: string
  restoreSource: RuntimeStackBackupRestoreWorkflowSource
  validation: RuntimeStackBackupUploadValidationResponse
  privateEvidence: RuntimeStackBackupRestoreWorkflowPrivateEvidence
    production: RuntimeStackBackupRestoreWorkflowProductionState
    candidate: RuntimeStackBackupRestoreWorkflowCandidateState
    nextAction: RuntimeStackBackupRestoreWorkflowNextAction
    warnings: string[]
    detail: string
}


export type RuntimeStackBackupRestoreWorkflowSessionQuery = {
  page: number
  pageSize: number
  search: string | null
  status: string | null
  sourceKind: string | null
  targetStack: string | null
  sortBy: string | null
  sortDirection: "asc" | "desc" | string | null
}

export type RuntimeStackBackupRestoreWorkflowHistorySummary = {
  totalSessions: number
  productionRecreateCount: number
  publiclyVerifiedCount: number
    needsActionCount: number
}

export type RuntimeStackBackupRestoreWorkflowHistoryResponse = {
  source: string
  status: string
  historyRootPath: string
  query: RuntimeStackBackupRestoreWorkflowSessionQuery
  summary: RuntimeStackBackupRestoreWorkflowHistorySummary
  totalSessions: number
  page: number
  pageSize: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
  targetStacks: string[]
  sessions: RuntimeStackBackupRestoreWorkflowHistoryItem[]
  warnings: string[]
  detail: string | null
}

export type RuntimeStackBackupRestoreWorkflowHistoryItem = {
  restoreSessionId: string
  validationId: string
  canonicalRestoreSessionId: string | null
  workspaceAvailable: boolean
  sourceKind: string
  sourceLabel: string
  sourceStackSlug: string | null
  sourceBackupId: string | null
  uploadedFileName: string | null
  preparedAtUtc: string
  lastUpdatedAtUtc: string
  validationStatus: string
  privateEvidencePassed: boolean
    targetStackSlug: string | null
    recreateId: string | null
    productionStatus: string | null
    stackRegistered: boolean
    publicReadinessPassed: boolean
      candidateCleanupPending: boolean
      nextActionCode: string
      nextActionTitle: string
      warningCount: number
      errorCount: number
      detail: string
}

/**
 * Canonical server-paged Restore Attempt contract for the operator /restores
 * screen. This intentionally contains no validation receipt identity: the
 * Restore Attempt is always anchored to a Backup Catalog source.
 */
export type RestoreAttemptListQuery = {
  page: number
  pageSize: number
  search: string | null
  status: string | null
  targetStack: string | null
  sortBy: string | null
  sortDirection: "asc" | "desc" | string | null
}

export type RestoreAttemptListSummary = {
  totalSessions: number
  productionRecreateCount: number
  publiclyVerifiedCount: number
  needsActionCount: number
}

export type RestoreAttemptListItem = {
  restoreSessionId: string
  workspaceAvailable: boolean
  sourceKind: string
  sourceLabel: string
  sourceDeleted: boolean
  catalogEntryId: string | null
  sourceStackSlug: string | null
  sourceBackupId: string | null
  targetStackSlug: string | null
  status: string
  statusLabel: string
  currentStage: string
  currentStageLabel: string
  progressPercent: number | null
  startedAtUtc: string
  lastUpdatedAtUtc: string
  terminalAtUtc: string | null
  warningCount: number
  errorCount: number
  productionRecreateStarted: boolean
  publiclyVerified: boolean
  nextActionCode: string
  nextActionTitle: string
  detail: string
}

export type RestoreAttemptListResponse = {
  source: string
  status: string
  query: RestoreAttemptListQuery
  summary: RestoreAttemptListSummary
  totalSessions: number
  page: number
  pageSize: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
  targetStacks: string[]
  sessions: RestoreAttemptListItem[]
  warnings: string[]
  detail: string | null
}
