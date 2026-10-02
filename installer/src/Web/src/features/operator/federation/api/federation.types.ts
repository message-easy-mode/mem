export type FederationMode = "public" | "restricted" | "local_only" | "unknown"
export type ManagedFederationMode = "public" | "restricted" | "local_only"

export type FederationConfigurationState =
  | "healthy"
  | "incomplete"
  | "custom_unsupported"
  | "unavailable"

export type FederationIngressMode =
  | "normal"
  | "local_only"
  | "missing"
  | "custom_unsupported"
  | "unavailable"

export type FederationCheckStatus = "passed" | "warning" | "failed" | "unknown"

export type FederationCheck = {
  code: string
  status: FederationCheckStatus
  detail: string
}

export type FederationProblem = {
  code: string
  detail: string
}

export type FederationOperationSummary = {
  id: string
  status: string
  currentStep: string | null
  requestedAtUtc: string
  completedAtUtc: string | null
}

export type RuntimeStackFederationState = {
  source: string
  status: string
  runtimeStackId: string
  slug: string
  mode: FederationMode
  configurationState: FederationConfigurationState
  allowlist: string[]
  enforcementKind: string
  matrixContainerRunning: boolean
  matrixDirectHostPortExposed: boolean
  ingressMode: FederationIngressMode
  serverWellKnownPublished: boolean
  federationPathsPubliclyForwarded: boolean
  signingKeyPathsPubliclyForwarded: boolean
  canonicalRouteEnabled: boolean
  canonicalRouteTargetsMatrix: boolean
  canonicalCertificatePresent: boolean
  alternateMatrixRouteDetected: boolean
  stateFingerprint: string | null
  latestOperation: FederationOperationSummary | null
  checks: FederationCheck[]
  warnings: FederationProblem[]
  problems: FederationProblem[]
}

export type RuntimeStackFederationPolicyRequest = {
  mode: ManagedFederationMode
  allowlist: string[]
}

export type FederationReviewWarning = {
  code: string
  detail: string
}

export type RuntimeStackFederationReview = {
  source: string
  status: "ready" | "no_change" | string
  runtimeStackId: string
  slug: string
  currentMode: FederationMode
  proposedMode: ManagedFederationMode
  currentAllowlist: string[]
  canonicalAllowlist: string[]
  addedDomains: string[]
  removedDomains: string[]
  restartRequired: boolean
  ingressChangeRequired: boolean
  noChange: boolean
  warnings: FederationReviewWarning[]
  confirmationText: string
  reviewHash: string
}

export type RuntimeStackFederationApplyRequest = RuntimeStackFederationPolicyRequest & {
  reviewHash: string
  idempotencyKey: string
}

export type FederationApplyStatus =
  | "succeeded"
  | "candidate_rejected"
  | "rolled_back"
  | "failed"
  | "running"
  | string

export type RuntimeStackFederationApplyResult = {
  source: string
  status: FederationApplyStatus
  operationId: string
  previousMode: FederationMode
  requestedMode: ManagedFederationMode
  observedMode: FederationMode
  rollbackAttempted: boolean
  rollbackSucceeded: boolean | null
  checks: FederationCheck[]
  errorCode: string | null
  detail: string
}
