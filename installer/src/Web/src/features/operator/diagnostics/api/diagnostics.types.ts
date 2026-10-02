import type { ControlPlaneRuntimeContext } from "@/features/runtime-context/api/runtime-context.types"

export type DiagnosticsCapabilities = Readonly<{
  canReadTechnicalEvents: boolean
  canGenerateSupportReport: boolean
  canViewOwnerHealthFacts: boolean
  canReadDockerEvidence: boolean
  canVerifyPipeline: boolean
  canOpenPortainer: boolean
  canManageIncidentLifecycle?: boolean
}>

export type DiagnosticsResource = Readonly<{
  kind: string
  id: string
  displayName: string | null
  stackId: string | null
  stackSlug: string | null
  service: string | null
  workspacePath?: string | null
}>

export type DiagnosticsException = Readonly<{
  type: string
  message: string
  stackTrace: string | null
  innerExceptions: readonly DiagnosticsException[]
}>

export type DiagnosticsEvent = Readonly<{
  schemaVersion: number
  eventId: string
  timestampUtc: string
  severity: string
  eventCode: string
  source: string
  feature: string
  stage: string | null
  message: string
  incidentId: string | null
  traceId: string | null
  spanId: string | null
  requestId: string | null
  correlationId: string | null
  operationId: string | null
  resource: DiagnosticsResource | null
  expected: Readonly<Record<string, string>> | null
  observed: Readonly<Record<string, string>> | null
  details: Readonly<Record<string, string>> | null
  exception: DiagnosticsException | null
  suggestedAction: string | null
  retryable: boolean
  redactionsApplied: boolean
  truncated: boolean
}>

export type DiagnosticsOverviewCounts = Readonly<{
  information: number
  warning: number
  error: number
  critical: number
  incidentCount: number
  eventCount: number
}>

export type DiagnosticsAttentionItem = Readonly<{
  incidentId: string
  severity: string
  eventCode: string
  feature: string
  stage: string | null
  summary: string
  lastSeenAtUtc: string
  href: string
}>

export type DiagnosticsAttentionResponse = Readonly<{
  schemaVersion: number
  observedAtUtc: string
  state: string
  total: number
  highestSeverity: string | null
  items: readonly DiagnosticsAttentionItem[]
  partial: boolean
  warnings: readonly string[]
}>

export type DiagnosticsIncidentLifecycle = Readonly<{
  state: string
  reopened: boolean
  storedDisposition: string | null
  updatedAtUtc: string | null
  updatedByOperatorId: string | null
  observedThroughAtUtc: string | null
  observedThroughEventId: string | null
  snoozedUntilUtc: string | null
  resolutionCode: string | null
  revision: number | null
}>

export type DiagnosticsIncidentLifecycleFilter =
  | "all"
  | "open"
  | "acknowledged"
  | "snoozed"
  | "resolved"

export type DiagnosticsIncidentSummary = Readonly<{
  incidentId: string
  severity: string
  eventCode: string
  feature: string
  stage: string | null
  message: string
  firstSeenAtUtc: string
  lastSeenAtUtc: string
  occurrenceCount: number
  retryable: boolean
  truncated: boolean
  resource: DiagnosticsResource | null
  workspaceLink: string | null
  lifecycle?: DiagnosticsIncidentLifecycle | null
}>

export type DiagnosticsOperationSummary = Readonly<{
  operationId: string
  runtimeStackId: string | null
  operation: string
  status: string
  requestedAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
}>

export type DiagnosticsIncidentDetailResponse = Readonly<{
  incident: DiagnosticsIncidentSummary
  capabilities: DiagnosticsCapabilities
  relatedEventCount: number
  technicalEvents: readonly DiagnosticsEvent[] | null
  operations: readonly DiagnosticsOperationSummary[]
  truncated: boolean
  warnings: readonly string[]
}>

export type DiagnosticsIncidentPageResponse = Readonly<{
  fromUtc: string
  untilUtc: string
  pageSize: number
  windowClamped: boolean
  incidents: readonly DiagnosticsIncidentSummary[]
  nextCursor: string | null
  partial: boolean
  warnings: readonly string[]
}>

export type DiagnosticsEventPageResponse = Readonly<{
  fromUtc: string
  untilUtc: string
  pageSize: number
  windowClamped: boolean
  events: readonly DiagnosticsEvent[]
  nextCursor: string | null
  warnings: readonly string[]
}>

export type DiagnosticsStorageCapacityHealth = Readonly<{
  status: string
  availableBytes: number | null
  totalBytes: number | null
  warningCode: string | null
}>

export type DiagnosticsLocalRecorderHealth = Readonly<{
  enabled: boolean
  status: string
  persistentRecorderConfigured: boolean
  persistentRecorderActive: boolean
  persistentFilePath: string | null
  lastFileWriteAtUtc: string | null
  retainedFileCount: number
  retainedBytes: number
  serilogSelfLogMessageCount: number
  warningCode: string | null
  storage?: DiagnosticsStorageCapacityHealth | null
}>

export type DiagnosticsSafeEventStoreHealth = Readonly<{
  enabled: boolean
  status: string
  lastWriteAtUtc: string | null
  lastReadAtUtc: string | null
  storedEventCount: number
  droppedEventCount: number
  malformedLineCount: number
  lastWriteErrorCode: string | null
  lastReadWarningCode: string | null
  lastRetentionRunAtUtc: string | null
  lastRetentionDeletedFileCount: number
  lastRetentionDeletedBytes: number
  lastRetentionErrorCode: string | null
  storage?: DiagnosticsStorageCapacityHealth | null
  hasEverRecordedEvent: boolean
}>

export type DiagnosticsSeqHealth = Readonly<{
  status: string
  sinkEnabled: boolean
  managementEnabled: boolean
  configured: boolean
  serverUrl: string | null
  reachable: boolean
  lastCheckedAtUtc: string | null
  lastSuccessAtUtc: string | null
  warningCode: string | null
}>

export type DiagnosticsLoggingHealthResponse = Readonly<{
  observedAtUtc: string
  status: string
  localRecorder: DiagnosticsLocalRecorderHealth
  safeEventStore: DiagnosticsSafeEventStoreHealth
  seq: DiagnosticsSeqHealth
  capabilities: DiagnosticsCapabilities
  partial: boolean
  warnings: readonly string[]
}>


export type DiagnosticsPortainerOverviewResponse = Readonly<{
  schemaVersion: number
  observedAtUtc: string
  available: boolean
  managed: boolean
  runtimeState: string
  ownershipState: string
  version: string | null
  approvedVersion: string
  environmentConfigured: boolean
  exactResourceLinksSupported: boolean
  links: Readonly<{
    home: string | null
    environment: string | null
    containers: string | null
  }>
  capabilities: Readonly<{
    canOpenHome: boolean
    canOpenEnvironment: boolean
    canOpenContainers: boolean
    canOpenExactResource: boolean
  }>
  warnings: readonly string[]
}>

export type DiagnosticsSeqUiAuthorityUpdateResponse = Readonly<{
  schemaVersion: number
  configured: boolean
  url: string | null
  updatedAtUtc: string
}>

export type DiagnosticsSeqOverviewResponse = Readonly<{
  schemaVersion: number
  observedAtUtc: string
  configured: boolean
  delivery: Readonly<{
    enabled: boolean
    desiredEnabled: boolean
    configurationState: string
    secretState: string
    requiresApiRestartToChange: boolean
    restartRequired: boolean
    preferenceUpdatedAtUtc: string | null
    activationState?: string
    activationVerifiedAtUtc?: string | null
    lastActivationVerificationId?: string | null
    restart?: Readonly<{
      kind: string
      guidanceCode: string
      command: string | null
      commandAvailable: boolean
    }>
  }>
  runtime: Readonly<{
    managementEnabled: boolean
    present: boolean
    managed: boolean
    state: string
    running: boolean
    usesApprovedRuntime: boolean
    expectedVersion: string
    dataRetentionState: string
    publishesPublicIngress: boolean
    warningCode: string | null
  }>
  health: Readonly<{
    status: string
    reachable: boolean
    lastCheckedAtUtc: string | null
    lastSucceededAtUtc: string | null
    warningCode: string | null
  }>
  ui: Readonly<{
    configured: boolean
    available: boolean
    url: string | null
    configurable?: boolean
  }>
  capabilities: Readonly<{
    canReviewSetup: boolean
    canOpenUi: boolean
    canDeploy: boolean
    canStart: boolean
    canStop: boolean
    canRestart: boolean
    canRemove: boolean
    canEnableDelivery: boolean
    canDisableDelivery: boolean
    canCheckHealth: boolean
    canVerifyDelivery?: boolean
    canConnect?: boolean
    requiresRecentStepUp?: boolean
  }>
  warnings: readonly string[]
  connection?: Readonly<{
    credentialState: string
    verificationState: string
    verifiedAtUtc: string | null
    apiKeyId: string | null
    lastVerificationId: string | null
    lastEventId: string | null
  }> | null
}>

export type DiagnosticsSeqConnectionRequest = Readonly<{
  administratorPassword: string
}>

export type DiagnosticsSeqConnectionResponse = Readonly<{
  schemaVersion: number
  operationId: string
  status: string
  credentialState: string
  verificationState: string
  apiKeyId: string
  verificationId: string
  eventId: string
  verifiedAtUtc: string
  reusedCredential: boolean
  overview: DiagnosticsSeqOverviewResponse
}>

export type DiagnosticsSeqDeliveryVerificationResponse = Readonly<{
  schemaVersion: number
  operationId: string
  status: string
  verificationId: string
  emittedAtUtc: string
  overview: DiagnosticsSeqOverviewResponse
}>

export type DiagnosticsSeqOperationResponse = Readonly<{
  schemaVersion: number
  operationId: string
  operation: string
  status: string
  startedAtUtc: string
  completedAtUtc: string
  dataRetained: boolean
  overview: DiagnosticsSeqOverviewResponse
  warnings: readonly string[]
}>

export type DiagnosticsSeqBootstrapOverviewResponse = Readonly<{
  schemaVersion: number
  observedAtUtc: string
  state: string
  setupAvailable: boolean
  approvedVersion: string
  imageState: string
  storageState: string
  runtimeOwnershipState: string
  eulaAccepted: boolean
  administratorSecretState: string
  ingestionCredentialState: string
  uiAuthorityState: string
  canStartSetup: boolean
  warnings: readonly string[]
}>

export type DiagnosticsSeqBootstrapReviewRequest = Readonly<{
  acceptEula: boolean
  privateUiUrl: string | null
  enableEventDelivery: boolean
}>

export type DiagnosticsSeqBootstrapReviewResponse = Readonly<{
  schemaVersion: number
  reviewId: string
  observedAtUtc: string
  expiresAtUtc: string
  ready: boolean
  image: Readonly<{
    approvedReference: string
    expectedVersion: string
    local: boolean
    immutableIdentityAvailable: boolean
    willPullDuringSetup: boolean
    warningCode: string | null
  }>
  storage: Readonly<{
    serverOwned: boolean
    state: string
    displayName: string
    warningCode: string | null
  }>
  runtime: Readonly<{
    ownershipState: string
    selectedHostPort: number
    publishesPublicIngress: boolean
  }>
  security: Readonly<{
    eulaAcceptedInReview: boolean
    administratorPasswordRequired: boolean
    authenticatedIngestionRequired: boolean
    currentAdministratorPasswordRequired: boolean
  }>
  privateUiAuthorityConfigured: boolean
  actionCodes: readonly string[]
  warnings: readonly string[]
  enableEventDelivery: boolean
}>

export type DiagnosticsSeqBootstrapExecuteRequest = Readonly<{
  reviewId: string
  administratorPassword: string
  administratorPasswordConfirmation: string
  connectionAdministratorPassword: string
}>

export type DiagnosticsSeqBootstrapExecuteResponse = Readonly<{
  schemaVersion: number
  operationId: string
  status: string
  acceptedAtUtc: string
}>

export type DiagnosticsSeqBootstrapOperationResponse = Readonly<{
  schemaVersion: number
  operationId: string
  status: string
  currentStep: string
  requestedAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  checks: readonly Readonly<{
    code: string
    status: string
    warningCode: string | null
  }>[]
  warningCode: string | null
  warnings: readonly string[]
}>

export type DiagnosticsSeqSetupReviewResponse = Readonly<{
  schemaVersion: number
  reviewId: string
  observedAtUtc: string
  expiresAtUtc: string
  readyForDeployment: boolean
  readyForDelivery: boolean
  image: Readonly<{
    approvedReference: string
    expectedVersion: string
    local: boolean
    immutableIdentityAvailable: boolean
    warningCode: string | null
  }>
  storage: Readonly<{
    serverOwned: boolean
    state: string
    displayName: string
    warningCode: string | null
  }>
  secrets: Readonly<{
    administratorPasswordHashAvailable: boolean
    ingestionApiKeyRequired: boolean
    ingestionApiKeyAvailable: boolean
  }>
  uiAuthority: Readonly<{
    configured: boolean
  }>
  eulaAccepted: boolean
  publishesPublicIngress: boolean
  runtimeOwnershipState: string
  actionCodes: readonly string[]
  warnings: readonly string[]
}>

export type DiagnosticsPipelineSelfTestCheck = Readonly<{
  code: string
  status: string
  warningCode: string | null
}>

export type DiagnosticsPipelineSelfTestResponse = Readonly<{
  schemaVersion: number
  verificationId: string
  status: string
  startedAtUtc: string
  completedAtUtc: string
  eventId: string | null
  checks: readonly DiagnosticsPipelineSelfTestCheck[]
  warnings: readonly string[]
}>

export type DiagnosticsActiveContext = Readonly<{
  state: string
  kind: string
  code: string
  feature: string
  stage: string | null
  resourceName: string | null
  summary: string | null
  updatedAtUtc: string
  workspaceHref: string | null
  incidentId: string | null
  incidentHref: string | null
}>

export type DiagnosticsOverviewResponse = Readonly<{
  schemaVersion: number
  generatedAtUtc: string
  status: string
  counts: DiagnosticsOverviewCounts
  loggingHealth: DiagnosticsLoggingHealthResponse
  capabilities: DiagnosticsCapabilities
  activeContext: DiagnosticsActiveContext | null
  partial: boolean
  truncated: boolean
  warnings: readonly string[]
}>

export type DiagnosticsDockerEvidenceContainer = Readonly<{
  logicalName: string
  observedState: string
  exitCode: number | null
  health: string | null
  startedAtUtc: string | null
  finishedAtUtc: string | null
  restartCount: number
  image: string | null
}>

export type DiagnosticsDockerEvidenceLogTail = Readonly<{
  requestedLines: number
  returnedLines: number
  maximumCharacters: number
  content: string
  truncated: boolean
  redactionsApplied: boolean
}>

export type DiagnosticsDockerEvidenceResponse = Readonly<{
  available: boolean
  resource: DiagnosticsResource | null
  observedAtUtc: string | null
  container: DiagnosticsDockerEvidenceContainer | null
  logTail: DiagnosticsDockerEvidenceLogTail | null
  warningCode: string | null
  warnings: readonly string[]
}>

export type DiagnosticsSupportReport = Readonly<{
  schemaVersion: number
  generatedAtUtc: string
  memVersion: string
  runtimeContext: ControlPlaneRuntimeContext
  incident: DiagnosticsIncidentSummary
  events: readonly DiagnosticsEvent[]
  operations: readonly DiagnosticsOperationSummary[]
  loggingHealth: DiagnosticsLoggingHealthResponse
  dockerEvidence: DiagnosticsDockerEvidenceResponse | null
  redaction: Readonly<{
    policyVersion: string
    redactionsApplied: boolean
    omittedContent: readonly string[]
  }>
  truncated: boolean
  warnings: readonly string[]
}>

export type DiagnosticsQuery = Readonly<{
  severity?: string
  feature?: string
  search?: string
  incidentId?: string
  lifecycle?: DiagnosticsIncidentLifecycleFilter
  cursor?: string
  pageSize?: number
}>
