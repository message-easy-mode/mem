/**
 * Browser contract for GET /api/operator/dashboard/overview.
 *
 * The control plane returns machine-oriented state codes and UTC timestamps.
 * Presentation, localisation, navigation labels, and date formatting remain in
 * the Web client so this feature never depends on an invented browser model.
 */
export type DashboardTone = "good" | "warning" | "danger" | "neutral"

export type DashboardActionCode =
  | "create_chat_server"
  | "manage_domains"
  | "open_services"
  | "open_backups"
  | "open_restores"
  | "open_restore_workspace"
  | "open_diagnostics"

export type DashboardAction = {
  code: DashboardActionCode
  stackSlug?: string | null
  restoreSessionId?: string | null
}

export type DashboardCapabilities = {
  canOperate: boolean
  canManagePlatform: boolean
}

export type DashboardHero = {
  state: "ready" | "attention" | "degraded" | "unavailable"
  headlineCode:
    | "platform_ready"
    | "platform_needs_attention"
    | "restore_needs_attention"
    | "recovery_needed"
    | "create_first_chat_server"
    | "platform_unavailable"
  action: DashboardAction | null
}

export type DashboardOnboardingStep = {
  code: string
  state: "available" | "blocked"
  action: DashboardAction | null
}

export type DashboardOnboarding = {
  state: "not_needed" | "available" | "blocked"
  completedStepCodes: string[]
  nextSteps: DashboardOnboardingStep[]
}

export type DashboardDockerSummary = {
  state: "responsive" | "unavailable" | "unknown"
  observedAtUtc: string | null
}

export type DashboardServiceSummary = {
  key: "postgres" | "npm_ingress" | "coturn" | string
  requirement: "required" | "optional"
  state: "running" | "stopped" | "not_deployed" | "degraded" | "verification_limited" | "unknown"
  observedAtUtc: string | null
}

export type DashboardPlatformSummary = {
  state: "ready" | "degraded" | "verification_limited" | "unavailable" | "unknown"
  services: DashboardServiceSummary[]
  requiredServiceCount: number
  runningRequiredServiceCount: number
  docker: DashboardDockerSummary
}

export type DashboardCertificateSummary = {
  state: "valid" | "renewing" | "expiring" | "expired" | "missing" | "staging" | "unknown"
  commonName: string | null
  expiresAtUtc: string | null
  renewalState?: string | null
  renewalNextAttemptAtUtc?: string | null
}

export type DashboardIngressSummary = {
  state: "ready" | "degraded" | "unavailable" | "unknown"
  observedAtUtc: string | null
}

export type DashboardPublicAccessSummary = {
  state: "ready" | "attention" | "not_configured" | "unknown"
  mainDomain: string | null
  certificate: DashboardCertificateSummary
  ingress: DashboardIngressSummary
  lastPlatformRouteVerificationAtUtc: string | null
}

export type DashboardLastVerification = {
  state: "passed" | "failed" | "unknown"
  verifiedAtUtc: string | null
}

export type DashboardStackSummary = {
  stackId: string
  slug: string
  lastVerification: DashboardLastVerification
  matrixPublicBaseUrl: string | null
  elementPublicBaseUrl: string | null
}

export type DashboardStacksSummary = {
  total: number
  healthyLastVerifiedCount: number
  attentionCount: number
  items: DashboardStackSummary[]
  truncated: boolean
}

export type DashboardRecoverySummary = {
  state:
    | "not_applicable"
    | "no_recovery_point"
    | "partial_coverage"
    | "covered"
    | "attention"
    | "unknown"
  managedStackCount: number
  stacksWithValidRecoveryPointCount: number
  catalogEntryCount: number
  availableCatalogEntryCount: number
  validCatalogEntryCount: number
  warningCatalogEntryCount: number
  invalidCatalogEntryCount: number
  latestCapturedAtUtc: string | null
  totalPayloadBytes: number | null
  activeRestoreCount: number
  attentionRestoreCount: number
  priorityRestoreSessionId: string | null
}

export type DashboardDiskUsage = {
  usedBytes: number
  totalBytes: number
  scope: string
}

export type DashboardHostSummary = {
  state: "available" | "unavailable"
  observedAtUtc: string | null
  unavailableReasonCode: string | null
  operatingSystem: string | null
  architecture: string | null
  cpuCount: number | null
  memoryTotalBytes: number | null
  dockerServerVersion: string | null
  containerCount: number | null
  imageCount: number | null
  disk: DashboardDiskUsage | null
  storageUnavailableReasonCode: string | null
}

export type DashboardActivityItem = {
  id: string
  kind: string
  severity: "success" | "info" | "warning" | "danger"
  occurredAtUtc: string
  titleCode: string
  detailCode: string | null
  stackSlug: string | null
  restoreSessionId: string | null
}

export type DashboardActivitySummary = {
  state: "available" | "empty"
  items: DashboardActivityItem[]
}

export type DashboardNotice = {
  id: string
  severity: "info" | "warning" | "danger"
  code: string
  action: DashboardAction | null
}

export type DashboardOverviewResponse = {
  source: "control-plane"
  generatedAtUtc: string
  suggestedRefreshSeconds: number
  capabilities: DashboardCapabilities
  hero: DashboardHero
  onboarding: DashboardOnboarding
  platform: DashboardPlatformSummary
  publicAccess: DashboardPublicAccessSummary
  stacks: DashboardStacksSummary
  recovery: DashboardRecoverySummary
  host: DashboardHostSummary
  activity: DashboardActivitySummary
  notices: DashboardNotice[]
}
