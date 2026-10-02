export type ManagedServiceName = string

export type ManagedServiceCategory = "platform" | "support" | "stack"

export type ManagedServiceStatusTone = "positive" | "warning" | "danger" | "neutral"

export type ManagedServiceLink = {
  label: string
  href: string
}

export type ManagedServiceActionLink = {
  label: string
  href: string
  description?: string
}

export type ManagedServiceListItem = {
  serviceName: ManagedServiceName
  displayName: string
  category: ManagedServiceCategory
  supported: boolean
  bulkManageable?: boolean
  exists: boolean
  running: boolean
  state: string
  statusLabel?: string
  statusTone?: ManagedServiceStatusTone
  image: string | null
  containerName: string | null
  desiredPorts: string[]
  actualPorts: string[]
  hostPaths: string[]
  warnings: string[]
  serviceHref: string | null
  description?: string | null
  inventoryNote?: string
  workspaceLink?: ManagedServiceLink
  retirementReview?: { migrationId: string; stagingRunId: string; status: string | null; label: string }
  openUiHref: string | null
  urls: ManagedServiceLink[]
  actions?: ManagedServiceActionLink[]
  notes: string[]
  stackName?: string | null
  stackSlug?: string | null
  serviceKind?: "platform" | "support" | "matrix" | "element" | "unknown"
}