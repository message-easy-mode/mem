export type SetupReviewPreflightSummary = {
  available: boolean
  ready: boolean
  runId: string | null
  completedAtUtc: string | null
  passed: number
  warnings: number
  failed: number
  skipped: number
  unavailable: number
  unknown: number
  blockingIssueCount: number
}

export type SetupReviewDomainSummary = {
  validated: boolean
  baseDomain: string
  wildcardCertificate: string
  dnsProvider: string
  acmeEmail: string
  certificateEnvironment: string
  providerAccessConfirmed: boolean
  providerCredentialStored: boolean
}

export type SetupReviewNpmAdministratorSummary = {
  administratorEmail: string | null
  credentialStored: boolean
  verifiedAtUtc: string | null
}

export type SetupReviewPlatformSummary = {
  networkName: string
  postgresContainerName: string
  postgresVolumeName: string
  npmContainerName: string
  npmHttpPort: number
  npmHttpsPort: number
  npmAdminPort: number
  coturnContainerName: string
  coturnPublicHost: string
  coturnTurnPort: number
  coturnRelayPortRange: string
  enabledSupportTools: string[]
}

export type SetupReviewResponse = {
  installationId: string | null
  installationStatus: string
  canAccept: boolean
  reviewAccepted: boolean
  message: string
  errorCode: string | null
  planSha256: string | null
  reviewedAtUtc: string | null
  preflight: SetupReviewPreflightSummary
  domain: SetupReviewDomainSummary
  npmAdministrator: SetupReviewNpmAdministratorSummary
  platform: SetupReviewPlatformSummary
  plannedActions: string[]
  willNotChange: string[]
  blockers: string[]
}
