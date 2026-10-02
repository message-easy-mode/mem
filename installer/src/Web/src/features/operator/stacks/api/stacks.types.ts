export type RuntimeStackHealth =
  | "healthy"
  | "needs_attention"
  | "offline"
  | "setting_up"
  | "unknown"

export type RuntimeStackVerificationFreshness = "current" | "stale"

export type RuntimeStackStatusFilter = "all" | RuntimeStackHealth

export type RuntimeStackListRequest = {
  page?: number
  pageSize?: 10 | 25 | 50
  search?: string | null
  status?: RuntimeStackStatusFilter | null
  category?: string | null
  sortBy?: "name" | "lastChecked"
  sortDirection?: "asc" | "desc"
}

export type RuntimeStackSummaryResponse = {
  stackId: string
  slug: string
  displayName?: string | null
  category?: string | null
  logoUrl?: string | null
  lastVerifiedStatus: string
  lastVerifiedAtUtc: string
  matrixPublicBaseUrl: string | null
  elementPublicBaseUrl: string | null
  health?: RuntimeStackHealth
  verificationFreshness?: RuntimeStackVerificationFreshness
}

export type RuntimeStackInventorySummary = {
  totalStacks: number
  healthy: number
  needsAttention: number
  offline: number
  settingUp: number
  unknown: number
}

export type RuntimeStackInventoryCategoryFacet = {
  category: string
  count: number
}

export type RuntimeStackListResponse = {
  source: string
  status: string
  stacks: RuntimeStackSummaryResponse[]
  detail: string | null
  summary?: RuntimeStackInventorySummary
  categories?: RuntimeStackInventoryCategoryFacet[]
  totalMatchingStacks?: number
  page?: number
  pageSize?: number
  totalPages?: number
  hasPreviousPage?: boolean
  hasNextPage?: boolean
  isPaged?: boolean
}

export type RuntimeStackServiceInspectResponse = {
  serviceKey: string
  instanceId: string
  containerId: string | null
  containerName: string | null
  internalHost: string | null
  internalBaseUrl: string | null
  publicHost: string | null
  publicBaseUrl: string | null
  dataPath: string | null
  configPath: string | null
  publicRouteId: string | null
  npmCertificateId: number | null
  runtimeMetadata: Record<string, string | null>
}

export type RuntimeStackInspectResponse = {
  source: string
  status: string
  stackId: string | null
  slug: string
  displayName?: string | null
  category?: string | null
  logoUrl?: string | null
  health?: RuntimeStackHealth
  verificationFreshness?: RuntimeStackVerificationFreshness
  matrix: RuntimeStackServiceInspectResponse | null
  element: RuntimeStackServiceInspectResponse | null
  lastVerifiedAtUtc: string | null
  detail: string | null
}

export type RuntimeStackTurnDiagnosticResponse = {
  code: string
  status: string
  message: string
}

export type RuntimeStackTurnLiveConfigurationResponse = {
  supported: boolean
  anyTurnSettings: boolean
  memManagedMarkerPresent: boolean
  turnUris: string[]
  credentialMechanism: string
  sharedSecretPresent: boolean
  sharedSecretMatchesPlatform: boolean | null
  userLifetime: string | null
  allowGuests: boolean | null
  publicHost: string | null
  realm: string | null
  fileSha256: string
  problemCode: string | null
  detail: string | null
}

export type RuntimeStackTurnPersistedMetadataResponse = {
  recorded: boolean
  configured: boolean | null
  turnUris: string[]
  publicHost: string | null
  realm: string | null
  configurationSource: string | null
  relayPortsPublished: boolean | null
  sharedSecretPresent: boolean | null
  userLifetime: string | null
  allowGuests: boolean | null
  matchesLiveConfiguration: boolean | null
}

export type RuntimeStackTurnPlatformResponse = {
  status: string
  readiness: string
  running: boolean
  ownershipVerified: boolean
  imageApproved: boolean
  publicHost: string
  turnUris: string[]
  secretPresent: boolean
  relayPortsPublished: boolean
  securityPolicyApplied: boolean
  detail: string | null
}

export type RuntimeStackTurnMatrixRuntimeResponse = {
  exists: boolean
  running: boolean
  identityMatches: boolean
  problemCode: string | null
  detail: string | null
}

export type RuntimeStackTurnInspectionResponse = {
  source: string
  status: string
  runtimeStackId: string
  slug: string
  inspectedAtUtc: string
  state: "connected" | "not-connected" | "external" | "drift" | "unknown"
  management: "mem-managed" | "external-observed" | "none" | "unknown"
  liveConfiguration: RuntimeStackTurnLiveConfigurationResponse | null
  persistedMetadata: RuntimeStackTurnPersistedMetadataResponse
  platform: RuntimeStackTurnPlatformResponse | null
  matrixRuntime: RuntimeStackTurnMatrixRuntimeResponse
  diagnostics: RuntimeStackTurnDiagnosticResponse[]
  warnings: string[]
  detail: string
}

export type RuntimeStackTurnConnectReviewResponse = {
  source: string
  status: "ready" | "no_change"
  runtimeStackId: string
  slug: string
  matrixServerName: string
  mode: "configure" | "adopt-existing" | "replace-external" | "no-change"
  configurationChangeRequired: boolean
  restartRequired: boolean
  platformPublicHost: string
  turnUris: string[]
  userLifetime: string
  allowGuests: boolean
  currentConfigurationSha256: string
  reviewHash: string
  confirmationText: string
  consequences: string[]
}

export type RuntimeStackTurnConnectRequest = {
  reviewHash: string
  idempotencyKey: string
  confirmConnectToPlatformTurn: boolean
  confirmReplaceExternalTurn: boolean
}

export type RuntimeStackTurnConnectResponse = {
  source: string
  status: "running" | "succeeded" | "candidate_rejected" | "rolled_back" | "failed" | "unresolved"
  operationId: string
  runtimeStackId: string
  slug: string
  mode: "configure" | "adopt-existing" | "replace-external" | "no-change"
  configurationChanged: boolean
  matrixRestarted: boolean
  rollbackAttempted: boolean
  rollbackSucceeded: boolean | null
  stateAfter: string | null
  errorCode: string | null
  detail: string
}

export type RuntimeStackTurnDisconnectReviewResponse = {
  source: string
  status: "ready" | "no_change"
  runtimeStackId: string
  slug: string
  matrixServerName: string
  configurationChangeRequired: boolean
  restartRequired: boolean
  platformPublicHost: string | null
  turnUris: string[]
  currentConfigurationSha256: string
  reviewHash: string
  confirmationText: string
  consequences: string[]
}

export type RuntimeStackTurnDisconnectRequest = {
  reviewHash: string
  idempotencyKey: string
  confirmDisconnectFromPlatformTurn: boolean
}

export type RuntimeStackTurnDisconnectResponse = {
  source: string
  status: "running" | "succeeded" | "candidate_rejected" | "rolled_back" | "failed" | "unresolved"
  operationId: string
  runtimeStackId: string
  slug: string
  configurationChanged: boolean
  matrixRestarted: boolean
  rollbackAttempted: boolean
  rollbackSucceeded: boolean | null
  stateAfter: string | null
  errorCode: string | null
  detail: string
}

export type RuntimeStackDoctorCheckResponse = {
  code: string
  name: string
  url: string
  success: boolean
  statusCode: number | null
  detail: string | null
  bodyPreview: string | null
}

export type RuntimeStackLatestDoctorResponse = {
  source: string
  status: string
  stackId: string | null
  slug: string
  report: RuntimeStackDoctorResponse | null
  detail: string | null
}

export type RuntimeStackDoctorResponse = {
  source: string
  status: string
  stackId: string | null
  slug: string
  lastVerifiedStatus: string | null
  lastVerifiedAtUtc: string | null
  checkedAtUtc: string
  allPassed: boolean
  checks: RuntimeStackDoctorCheckResponse[]
  detail: string | null
  operationId?: string | null
  reportId?: string | null
}

export type RuntimeStackDoctorHistoryItemResponse = {
  reportId: string
  operationId: string | null
  status: string
  allPassed: boolean
  checkedAtUtc: string
  checkCount: number
  failedCheckCount: number
  warningCount: number
  detail: string | null
}

export type RuntimeStackDoctorHistoryResponse = {
  source: string
  status: string
  stackId: string | null
  slug: string
  reports: RuntimeStackDoctorHistoryItemResponse[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
  detail: string | null
}

export type RuntimeStackOperationResponse = {
  id: string
  runtimeStackId: string | null
  operation: string
  status: string
  idempotencyKey: string | null
  requestedBy: string | null
  hostMutationLevel: string | null
  currentStep: string | null
  requestedAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  lastError: string | null
}

export type RuntimeStackOperationsResponse = {
  source: string
  status: string
  stackId: string | null
  slug: string
  operations: RuntimeStackOperationResponse[]
  detail: string | null
}

export type RuntimeStackUserResponse = {
  id: string
  runtimeStackId: string
  matrixInstanceId: string
  username: string
  matrixUserId: string | null
  isAdmin: boolean
  isFirstAdmin: boolean
  status: string
  origin: string
  displayName: string | null
  email: string | null
  lastError: string | null
  createdAtUtc: string
  updatedAtUtc: string
  matrixSyncedAtUtc: string | null
}

export type MatrixAdminAuthorityStatusResponse = {
  status: string
  canResetPasswords: boolean
  source: string | null
  adminUserId: string | null
  storedAtUtc: string | null
  lastValidatedAtUtc: string | null
  errorCode: string | null
}

export type RuntimeStackUsersResponse = {
  source: string
  status: string
  stackId: string
  slug: string
  inventorySource: string | null
  inventoryStatus: string
  inventoryLastAttemptedAtUtc: string | null
  inventoryLastSynchronizedAtUtc: string | null
  inventoryUserCount: number | null
  activeAdminCount: number
  inventoryErrorCode: string | null
  synchronizationRequired: boolean
  users: RuntimeStackUserResponse[]
  requiresFirstAdmin: boolean
  canCreateUsers: boolean
  adminAuthority: MatrixAdminAuthorityStatusResponse
  detail: string | null
}

export type ImportMatrixAdminAuthorityRequest = {
  accessToken?: string | null
  matrixUserId?: string | null
  password?: string | null
}

export type ResetRuntimeStackUserPasswordRequest = {
  newPassword: string
}

export type RuntimeStackUserPasswordResetResponse = {
  source: string
  status: string
  runtimeStackId: string
  userId: string
  matrixUserId: string
  logoutDevices: boolean
  adminAuthorityInvalidated: boolean
  completedAtUtc: string
}

export type DeactivateRuntimeStackUserRequest = {
  erase: boolean
}

export type ReactivateRuntimeStackUserRequest = {
  newPassword: string
}

export type RuntimeStackUserLifecycleResponse = {
  source: string
  status: string
  runtimeStackId: string
  userId: string
  matrixUserId: string
  isDeactivated: boolean
  logoutDevices: boolean
  completedAtUtc: string
}

export type CreateRuntimeStackUserRequest = {
  username: string
  password: string
  isAdmin: boolean
  displayName?: string | null
  email?: string | null
}

export type RuntimeStackIdentityUpdateRequest = {
  displayName: string
  category?: string | null
}

export type CreateRuntimeStackRequest = {
  slug: string
  displayName?: string
  category?: string | null
  requestedDomainId?: string | null
}

export type RuntimeStackImagePolicyEntry = Readonly<{
  component: string
  repository: string
  version: string
  approvedReference: string
}>

export type RuntimeStackImagePolicyResponse = Readonly<{
  schemaVersion: number
  synapse: RuntimeStackImagePolicyEntry
  element: RuntimeStackImagePolicyEntry
}>

export type CreateChatStackRuntimeCommand = {
  stackId: string
  matrixInstanceId: string
  elementInstanceId: string
  stackSlug: string
  displayName?: string | null
  category?: string | null
  requestedDomainId: string | null
  matrixImage?: string | null
  matrixVersion?: string | null
  elementImage?: string | null
  elementVersion?: string | null
  idempotencyKey: string
}

export type CreateChatStackRuntimeAcceptedResponse = {
  operationId: string
  stackId: string
  stackSlug: string
  status: string
  pollUrl: string
}

export type CreateChatStackRuntimeOperationResponse = {
  operationId: string
  runtimeStackId: string | null
  status: string
  currentStep: string | null
  requestedAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  lastError: string | null
  terminal: boolean
  succeeded: boolean
}

export type CreateChatStackRuntimeResult = {
  stackId: string
  status: string
  message: string
  matrix: RuntimeStackServiceInspectResponseLike
  element: RuntimeStackServiceInspectResponseLike | null
  warnings: string[]
  evidence: HostAgentEvidence[]
}

export type RuntimeStackServiceInspectResponseLike = {
  instanceId: string
  serviceKey: string
  containerId: string | null
  containerName: string | null
  hostPort: number
  dataPath: string | null
  serverName: string | null
  publicHost: string | null
  publicBaseUrl: string | null
  internalHost: string | null
  internalBaseUrl: string | null
  publicRouteId: string | null
  internalRouteId: string | null
  npmCertificateId: number | null
  runtimeMetadata: Record<string, string | null>
}

export type HostAgentEvidence = {
  code: string
  message: string
  detail: string | null
  data: Record<string, string | null> | null
}

export type RuntimeStackBackupResponse = {
  operationId: string
  runtimeStackId: string
  stackSlug: string
  backupId: string
  backupRootPath: string
  manifestPath: string
  databaseDumpPath: string
  matrixConfigPath: string | null
  matrixSigningKeyPath: string | null
  matrixMediaBackupPath: string | null
  elementConfigPath: string | null
  stats: RuntimeStackBackupStats
  warnings: string[]
  createdAtUtc: string
}

export type RuntimeStackStorageSectionResponse = {
  key: string
  displayName: string
  path: string
  exists: boolean
  bytes: number
  files: number
}

export type RuntimeStackMatrixStorageResponse = {
  dataPath: string
  homeserverYamlPath: string | null
  homeserverYamlBytes: number
  signingKeyPath: string | null
  signingKeyBytes: number
  mediaStorePath: string
  mediaStoreExists: boolean
  totalBytes: number
  totalFiles: number
  sections: RuntimeStackStorageSectionResponse[]
}

export type RuntimeStackElementStorageResponse = {
  dataPath: string | null
  configPath: string | null
  configBytes: number
}

export type RuntimeStackStorageResponse = {
  source: string
  status: string
  runtimeStackId: string
  slug: string
  matrix: RuntimeStackMatrixStorageResponse
  element: RuntimeStackElementStorageResponse | null
  detail: string | null
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

export type RuntimeStackDestroyRequest = {
  removeContainers: boolean
  removeRoutes: boolean
  removeDatabase: boolean
  removeFiles: boolean
  force: boolean
  idempotencyKey: string
}

export type RuntimeStackDestroyAcceptedResponse = {
  operationId: string
  runtimeStackId: string
  slug: string
  status: string
  pollUrl: string
  reusedExistingOperation: boolean
}

export type RuntimeStackDestroyStepResult = {
  code: string
  status: string
  message: string
  data: Record<string, string | null>
}

export type RuntimeStackDestroyResponse = {
  source: string
  status: string
  operationId: string
  runtimeStackId: string
  slug: string
  containersRequested: boolean
  routesRequested: boolean
  databaseRequested: boolean
  filesRequested: boolean
  steps: RuntimeStackDestroyStepResult[]
  warnings: string[]
  keptDataPath: string | null
  detail: string | null
  destroyedAtUtc: string
}
