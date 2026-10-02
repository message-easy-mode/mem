import { getJson } from "@/lib/api"

export type MigrationSessionHistoricalCompatibility = {
  usesLegacyNeutralImportContract: boolean
  legacyContractVersion: string | null
  legacyContractStatus: string | null
  legacyManifestSha256: string | null
  usesCatalogRestorePath: boolean
  catalogEntryCount: number
  restoreSessionCount: number
}

export type MigrationSessionSummary = {
  migrationId: string
  displayName: string
  sourceAdapter: string
  sourceDisplay: string
  phase: string
  status: string
  nextAction: string
  createdAtUtc: string
  updatedAtUtc: string
  blockerCount: number
  warningCount: number
  advisoryCount: number
  needsAttention: boolean
  sourceCount: number
  stackCount: number
  historicalCompatibility: MigrationSessionHistoricalCompatibility
}

export type MigrationSessionPackage = {
  transferMode: string
  status: string
  fileName: string | null
  sizeBytes: number | null
  encryptedSha256: string | null
  decryptedSha256: string | null
  uploadedAtUtc: string | null
  validatedAtUtc: string | null
  expiresAtUtc: string | null
  ageRecipient: string | null
  recipientFingerprint: string | null
  archiveMigrationId: string | null
  archiveSourceProduct: string | null
  archiveSourceVersion: string | null
  archiveStackCount: number | null
}

export type MigrationSessionPackageRevision = {
  packageRevisionId: string
  revisionNumber: number
  purpose: string
  status: string
  retentionState: string
  active: boolean
  transferMode: string
  fileName: string | null
  sizeBytes: number | null
  encryptedSha256: string | null
  decryptedSha256: string | null
  createdAtUtc: string
  uploadedAtUtc: string | null
  validatedAtUtc: string | null
  expiresAtUtc: string | null
  supersededAtUtc: string | null
  retiredAtUtc: string | null
  ageRecipient: string | null
  recipientFingerprint: string | null
  archiveMigrationId: string | null
  archiveSourceProduct: string | null
  archiveSourceVersion: string | null
  archiveStackCount: number | null
  captureKind: string | null
  sourceFrozen: boolean | null
  rehearsalOnly: boolean | null
  verifiedFileCount: number | null
  verifiedExpandedBytes: number | null
  validationCode: string | null
  validationSummary: string | null
}

export type MigrationSessionSource = {
  sourceId: string
  kind: string
  product: string
  productVersion: string | null
  sourceFingerprint: string | null
  capturedAtUtc: string | null
}

export type MigrationSessionFinding = {
  severity: "blocker" | "warning" | "advisory" | string
  code: string
  message: string
  artifactId: string | null
  createdAtUtc: string
}

export type MigrationSessionLinkedObject = {
  kind: string
  id: string
  displayName: string
  status: string
  relationship: string
  historical: boolean
}

export type MigrationSessionDetail = {
  session: MigrationSessionSummary
  package: MigrationSessionPackage | null
  packageRevisions?: MigrationSessionPackageRevision[]
  sources: MigrationSessionSource[]
  findings: MigrationSessionFinding[]
  linkedObjects: MigrationSessionLinkedObject[]
}

export type MigrationSessionLifecycleCapabilities = {
  canArchive: boolean
  canUnarchive: boolean
  canCancel: boolean
  canDelete: boolean
  cancelBlockedCode: string | null
  cancelBlockedReason: string | null
  deleteBlockedCode: string | null
  deleteBlockedReason: string | null
}

export type MigrationSessionLifecycleInspection = {
  migrationId: string
  lifecycleStatus: string
  archived: boolean
  archivedAtUtc: string | null
  archivedBy: string | null
  closedAtUtc: string | null
  closureKind: string | null
  stateVersion: number
  currentOperation: string
  packageState: string
  candidateArtifactState: string
  privateStagingRuntimeState: string
  productionRuntimeState: string
  publicRoutesState: string
  sourceState: string
  capabilities: MigrationSessionLifecycleCapabilities
  sourceUnaffectedNotice: string
}

export type MigrationSessionLifecycleMutationResponse = {
  resultCode: string
  idempotent: boolean
  lifecycle: MigrationSessionLifecycleInspection
}

export type MigrationSessionCancelRequest = {
  expectedStateVersion: number
  acknowledgeSourceUnaffected: boolean
  encryptedPackageRetention: "remove" | "retain-encrypted"
}

export type MigrationSessionDeleteRequest = {
  expectedStateVersion: number
  confirmationMigrationId: string
  acknowledgeSourceUnaffected: boolean
}

export type MigrationSessionDeleteResponse = {
  resultCode: string
  idempotent: boolean
  migrationId: string
  deletedAtUtc: string
}

export class MigrationSessionLifecycleProblemError extends Error {
  readonly status: number
  readonly code: string | null

  constructor(message: string, status: number, code: string | null = null) {
    super(message)
    this.name = "MigrationSessionLifecycleProblemError"
    this.status = status
    this.code = code
  }
}

export function isMigrationSessionLifecycleStepUpRequired(value: unknown): boolean {
  return value instanceof MigrationSessionLifecycleProblemError &&
    value.status === 403 &&
    value.code === "step_up_required"
}

export type MigrationSessionInventoryRequest = {
  page: number
  pageSize: number
  search: string | null
  lifecycle: string
  action: string
  stage: string | null
  targetStack: string | null
  sortBy: string
  sortDirection: "asc" | "desc"
  includeArchived: boolean
}

export type MigrationSessionInventoryQuery = {
  search: string | null
  lifecycle: string
  action: string
  stage: string | null
  targetStack: string | null
  sortBy: string
  sortDirection: "asc" | "desc"
  includeArchived: boolean
}

export type MigrationSessionInventorySummary = {
  totalSessions: number
  activeCount: number
  needsActionCount: number
  completedCount: number
  closedCount: number
  cancelledCount: number
  archivedCount: number
}

export type MigrationSessionInventoryPrimaryAction = {
  kind: "continue" | "review" | "view" | string
  code: string
}

export type MigrationSessionInventoryCapabilities = {
  canOpen: boolean
  canArchive: boolean
  canUnarchive: boolean
  canCancel: boolean
  canDelete: boolean
  deleteBlockedReason: string | null
}

export type MigrationSessionInventoryRow = {
  migrationId: string
  displayName: string
  sourceAdapter: string
  sourceDisplay: string
  sourceProduct: string | null
  sourceVersion: string | null
  targetStackSlug: string | null
  lifecycleStatus: string
  archived: boolean
  currentStageCode: string
  currentStageState: string
  currentStatusCode: string
  needsAttention: boolean
  warningCount: number
  errorCount: number
  createdAtUtc: string
  updatedAtUtc: string
  stateVersion: number
  primaryAction: MigrationSessionInventoryPrimaryAction
  capabilities: MigrationSessionInventoryCapabilities
}

export type MigrationSessionInventoryWarning = {
  code: string
  message: string
}

export type MigrationSessionInventoryResponse = {
  schemaVersion: number
  query: MigrationSessionInventoryQuery
  summary: MigrationSessionInventorySummary
  totalSessions: number
  page: number
  pageSize: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
  targetStacks: string[]
  sessions: MigrationSessionInventoryRow[]
  warnings: MigrationSessionInventoryWarning[]
}

const base = "/api/operator/migrations/sessions"

export function listMigrationSessions(request: MigrationSessionInventoryRequest) {
  const query = new URLSearchParams()

  query.set("page", request.page.toString())
  query.set("pageSize", request.pageSize.toString())

  if (request.search?.trim()) query.set("search", request.search.trim())
  if (request.lifecycle !== "all") query.set("lifecycle", request.lifecycle)
  if (request.action !== "all") query.set("action", request.action)
  if (request.stage) query.set("stage", request.stage)
  if (request.targetStack?.trim()) query.set("targetStack", request.targetStack.trim())
  if (request.sortBy !== "updated") query.set("sortBy", request.sortBy)
  if (request.sortDirection !== "desc") query.set("sortDirection", request.sortDirection)
  if (request.includeArchived) query.set("includeArchived", "true")

  return getJson<MigrationSessionInventoryResponse>(`${base}/inventory?${query.toString()}`)
}

export const getMigrationSession = (migrationId: string) =>
  getJson<MigrationSessionDetail>(`${base}/${encodeURIComponent(migrationId)}`)

async function lifecycleMutation<TResponse = MigrationSessionLifecycleMutationResponse>(
  migrationId: string,
  action: "archive" | "unarchive" | "cancel" | "delete",
  body: unknown,
): Promise<TResponse> {
  const path = `${base}/${encodeURIComponent(migrationId)}/lifecycle/${action}`
  const response = await fetch(path, {
    method: "POST",
    credentials: "include",
    cache: "no-store",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
  })

  if (!response.ok) {
    let code: string | null = null
    let message = `POST ${path} failed with status ${response.status}`

    try {
      const problem = await response.json() as {
        status?: unknown
        code?: unknown
        message?: unknown
      }
      code = typeof problem.status === "string"
        ? problem.status
        : typeof problem.code === "string"
          ? problem.code
          : null
      if (typeof problem.message === "string") message = problem.message
    } catch {
      // Preserve the stable status-based fallback.
    }

    throw new MigrationSessionLifecycleProblemError(message, response.status, code)
  }

  return await response.json() as TResponse
}

export const getMigrationSessionLifecycle = (migrationId: string) =>
  getJson<MigrationSessionLifecycleInspection>(
    `${base}/${encodeURIComponent(migrationId)}/lifecycle`,
  )

export const archiveMigrationSession = (
  migrationId: string,
  expectedStateVersion: number,
) => lifecycleMutation(migrationId, "archive", { expectedStateVersion })

export const unarchiveMigrationSession = (
  migrationId: string,
  expectedStateVersion: number,
) => lifecycleMutation(migrationId, "unarchive", { expectedStateVersion })

export const cancelMigrationSession = (
  migrationId: string,
  request: MigrationSessionCancelRequest,
) => lifecycleMutation(migrationId, "cancel", request)

export const deleteMigrationSession = (
  migrationId: string,
  request: MigrationSessionDeleteRequest,
) => lifecycleMutation<MigrationSessionDeleteResponse>(migrationId, "delete", request)
