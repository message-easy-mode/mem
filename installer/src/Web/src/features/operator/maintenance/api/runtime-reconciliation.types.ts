export type RuntimeReconciliationSummary = {
  activeStackCount: number
  destroyedStackHistoryCount: number
  activeRouteCount: number
  npmProxyHostCount: number
  memManagedNpmProxyHostCount: number
  orphanedNpmProxyHostCount: number
  manifestCount: number
  activeDatabaseRowsWithoutManifestCount: number
  manifestWithoutActiveDatabaseRowCount: number
  removableManifestlessStackCount: number
}

export type RuntimeReconciliationActiveStack = {
  stackId: string
  slug: string
  status: string
  lastVerifiedStatus: string | null
  lastVerifiedAtUtc: string | null
  matrixPublicBaseUrl: string | null
  elementPublicBaseUrl: string | null
  hasManifest: boolean
  recordedServiceCount: number
  recordedRouteCount: number
  matchingNpmRouteCount: number
  missingNpmRouteCount: number
  mismatchedNpmRouteCount: number
  reconciliationState: string
  canRemoveFromReconciliation: boolean
  removalActionCode: string | null
  reason: string | null
}

export type RuntimeReconciliationRoute = {
  routeId: string
  runtimeStackId: string
  stackSlug: string
  serviceKey: string
  provider: string
  publicHost: string
  forwardHost: string
  forwardPort: number
  npmCertificateId: number | null
  providerRouteId: string | null
  status: string
}

export type RuntimeReconciliationNpmProxyHost = {
  proxyHostId: number
  domainNames: string[]
  forwardHost: string | null
  forwardPort: number | null
  certificateId: number | null
  enabled: boolean
  nginxOnline: boolean
  looksLikeMemStackRoute: boolean
  matchesActiveRoute: boolean
  matchedActiveRouteHosts: string[]
  reason: string | null
}

export type RuntimeReconciliationReport = {
  source: string
  status: string
  checkedAtUtc: string
  summary: RuntimeReconciliationSummary
  activeStacks: RuntimeReconciliationActiveStack[]
  destroyedStacks: Array<{
    stackId: string
    slug: string
    status: string
    lastVerifiedStatus: string | null
    lastVerifiedAtUtc: string | null
    updatedAtUtc: string
  }>
  activeRoutes: RuntimeReconciliationRoute[]
  npmProxyHosts: RuntimeReconciliationNpmProxyHost[]
  orphanedNpmProxyHosts: RuntimeReconciliationNpmProxyHost[]
  warnings: string[]
  detail: string | null
}

export type RuntimeReconciliationNpmProxyHostCleanupRequest = {
  proxyHostIds: number[]
  confirmationText: string
}

export type RuntimeReconciliationNpmProxyHostCleanupResult = {
  proxyHostId: number
  domainNames: string[]
  status: string
  reason: string | null
}

export type RuntimeReconciliationNpmProxyHostCleanupResponse = {
  source: string
  status: string
  checkedAtUtc: string
  requestedProxyHostIds: number[]
  deletedCount: number
  alreadyMissingCount: number
  skippedCount: number
  failedCount: number
  results: RuntimeReconciliationNpmProxyHostCleanupResult[]
  detail: string | null
}
