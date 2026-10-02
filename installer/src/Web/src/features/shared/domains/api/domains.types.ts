// src/features/shared/domains/api/domains.types.ts

export type NpmReadinessResponse = {
  containerExists: boolean
  containerRunning: boolean
  adminUiReachable: boolean
  initialized: boolean
  apiAuthenticated: boolean
  certificateApiReachable: boolean
  runtimeState: string | null
  baseUrl: string | null
  baseUrlSource: string | null
  certificateCount: number
  recommendedAction: string
  warnings: string[]
}

// Safe operator certificate read model. Private-key paths and secret material are never exposed.
export type StoredCertificateMetadata = {
  id: string
  domainId: string
  domainBaseDomain: string
  domainDisplayName: string
  domainIsMainPlatformDomain: boolean
  domainActiveCertificateEntityId: string | null
  domainDnsProvider: string
  domainDnsZone: string | null

  certificateId: string
  commonName: string

  // Compatibility aliases retained for the intentional Advanced ingress diagnostics surface.
  domain: string
  zone: string

  provider: string
  isWildcard: boolean
  isStaging: boolean
  isMainPlatformCertificate: boolean
  isActive: boolean
  isInUse: boolean
  purpose: string | null
  status: string
  createdAtUtc: string
  expiresAtUtc: string | null
  thumbprint: string | null

  // NPM/import state
  npmCertificateId: number | null
  importedToNpm: boolean
  lastValidatedAtUtc: string | null
  lastImportedToNpmAtUtc: string | null
  lastError: string | null
}

export type CertificateOperationEvidence = {
  key: string
  value: string
  sensitive: boolean
  status: string | null
}

export type CertificateOperationResult = {
  succeeded: boolean
  status: string
  message: string
  errorCode: string | null
  errorDetail: string | null
  evidence: CertificateOperationEvidence[]
}

export type DomainCertificateIssueStartRequest = {
  requestId: string
  email: string
  providerToken: string
  useStaging: boolean
}

export type DomainCertificateIssueProgressSnapshot = {
  phaseCode: string
  safeSummary: string
  timestampUtc: string
}

export type DomainCertificateIssueOperation = {
  operationId: string
  domainId: string
  requestId: string
  status: string
  phaseCode: string | null
  phaseSummary: string | null
  useStaging: boolean
  requestedAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  attemptCount: number
  progress: DomainCertificateIssueProgressSnapshot[]
  result: CertificateOperationResult | null
  certificateId: string | null
  diagnosticsIncidentId: string | null
  isTerminal: boolean
}

export type DomainCertificateIssueLatestResponse = {
  operation: DomainCertificateIssueOperation | null
}

export type DomainCertificateIssueActiveOperation = {
  operationId: string
  domainId: string
  baseDomain: string
  status: string
  phaseCode: string | null
  phaseSummary: string | null
  useStaging: boolean
  requestedAtUtc: string
  startedAtUtc: string | null
}

export type DomainCertificateIssueActiveResponse = {
  operations: DomainCertificateIssueActiveOperation[]
}

export type DomainCertificateIssueQueueResult = {
  accepted: boolean
  status: string
  message: string
  operation: DomainCertificateIssueOperation | null
}

export type DomainActiveCertificateSelectionResult = {
  succeeded: boolean
  status: string
  message: string
  certificateId: string | null
  baseDomain: string | null
}

export type NpmCertificateProbeResult = {
  succeeded: boolean
  status: string
  message: string
  errorCode: string | null
  errorDetail: string | null
  matchingCertificateId: number | null
  evidence: CertificateOperationEvidence[]
}

export type NpmCertificateTestProxyHostRequest = {
  domain: string
  forwardHost: string
  forwardPort: number
  forwardScheme: string
}

export type NpmCertificateTestProxyHostResult = {
  succeeded: boolean
  status: string
  message: string
  errorCode: string | null
  errorDetail: string | null
  npmCertificateId: number | null
  proxyHostId: number | null
  evidence: CertificateOperationEvidence[]
}

export type OperatorDomainSummary = {
  id: string
  baseDomain: string
  displayName: string
  purpose: string
  isMainPlatformDomain: boolean
  dnsProvider: string
  dnsZone: string | null
  status: string
  activeCertificateEntityId: string | null
  activeCertificateId: string | null
  activeCertificateCommonName: string | null
  activeCertificateIsStaging: boolean | null
  activeCertificateExpiresAtUtc: string | null
  certificateCount: number
  createdAtUtc: string
  updatedAtUtc: string
}

export type OperatorCertificateSummary = {
  id: string
  domainId: string
  certificateId: string
  commonName: string
  provider: string
  isWildcard: boolean
  isStaging: boolean
  isMainPlatformCertificate: boolean
  isActive: boolean
  status: string
  createdAtUtc: string
  expiresAtUtc: string | null
  thumbprint: string | null
  npmCertificateId: number | null
  importedToNpm: boolean
  lastValidatedAtUtc: string | null
  lastImportedToNpmAtUtc: string | null
  lastError: string | null
}

export type OperatorDomainDetail = {
  id: string
  baseDomain: string
  displayName: string
  purpose: string
  isMainPlatformDomain: boolean
  dnsProvider: string
  dnsZone: string | null
  status: string
  notes: string | null
  activeCertificateEntityId: string | null
  certificates: OperatorCertificateSummary[]
  createdAtUtc: string
  updatedAtUtc: string
}

export type CreateOperatorDomainRequest = {
  baseDomain: string
  displayName?: string | null
  purpose?: string | null
  dnsProvider: string
  dnsZone?: string | null
  notes?: string | null
}

export type UpdateOperatorDomainRequest = {
  displayName?: string | null
  purpose?: string | null
  status?: string | null
  notes?: string | null
}

export type DeleteOperatorDomainResponse = {
  succeeded: boolean
  status: string
  message: string
  domainId: string
  warnings: string[]
}

export type DeleteCertificateResponse = {
  succeeded: boolean
  status: string
  message: string
  certificateId: string | null
  metadataDeleted: boolean
  filesDeleted: boolean
  npmCertificateDeleted: boolean
  warnings: string[]
}

export type OperatorDomainRenewalStatus = {
  domainId: string
  baseDomain: string
  dnsProvider: string
  dnsZone: string | null
  policyConfigured: boolean
  autoRenewEnabled: boolean
  acmeEmail: string | null
  renewalWindowDays: number
  retryIntervalHours: number
  credentialConfigured: boolean
  credentialUpdatedAtUtc: string | null
  hasActiveProductionCertificate: boolean
  activeCertificateId: string | null
  activeCertificateExpiresAtUtc: string | null
  readinessStatus: string
  readinessMessage: string
  policyUpdatedAtUtc: string | null
  operationalStatus: "ready" | "scheduled" | "queued" | "running" | "awaiting-activation" | "failed" | "disabled" | "unready"
  certificateExpired: boolean
  daysRemaining: number | null
  nextEligibleRenewalAtUtc: string | null
  nextAutomaticAttemptAtUtc: string | null
  lastAttemptAtUtc: string | null
  lastSuccessfulRenewalAtUtc: string | null
  latestOperationId: string | null
  latestOperationStatus: string | null
  latestOperationStep: string | null
  latestOperationAttemptCount: number
  latestRequestedBy: string | null
  latestErrorCode: string | null
  diagnosticsIncidentId: string | null
  diagnosticsHref: string | null
  manualRenewAvailable: boolean
}

export type OperatorDomainRenewalHistoryItem = {
  operationId: string
  status: string
  step: string | null
  requestedBy: string
  requestedAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  attemptCount: number
  errorCode: string | null
  diagnosticsIncidentId: string | null
  diagnosticsHref: string | null
}

export type OperatorDomainRenewalRunResponse = {
  accepted: boolean
  status: string
  message: string
  operationId: string | null
  renewal: OperatorDomainRenewalStatus | null
}

export type UpdateOperatorDomainRenewalPolicyRequest = {
  autoRenewEnabled: boolean
  acmeEmail: string | null
}

export type UpdateOperatorDomainRenewalCredentialRequest = {
  providerToken: string
  acmeEmail: string | null
}

export type OperatorDomainRenewalCredentialMutationResponse = {
  succeeded: boolean
  status: string
  message: string
  renewal: OperatorDomainRenewalStatus | null
}

