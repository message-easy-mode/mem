export type MigrationCutoverCandidate = {
  candidateId: string
  status: string
  targetStackSlug: string
  matrixServerName: string | null
  privateRuntimeStatus: string
  safety: {
    privateOnly: boolean
    publicRoutesCreated: boolean
    productionExecutionLocked: boolean
  }
  database: { importSucceeded: boolean }
  runtime: { synapseHealthPassed: boolean; elementHealthPassed: boolean }
  warnings: string[]
  errors: string[]
  detail: string | null
}

export type MigrationCutoverPreview = {
  previewId: string
  status: string
  productionExecutionLocked: boolean
  blockers: string[]
  warnings: string[]
  errors: string[]
  detail: string | null
}

export type MigrationCutoverConfirmation = {
  confirmationId: string
  status: string
  productionExecutionLocked: boolean
  executionAvailable: boolean
  blockers: string[]
  warnings: string[]
  errors: string[]
  detail: string | null
}

export type MigrationCutoverExecution = {
  executionId: string
  status: string
  finishedAtUtc: string | null
  npmRouteExecutionPerformed: boolean
  cutoverIngressNetworkExecutionPerformed: boolean
  routes: {
    allRequestedRoutesSucceeded: boolean
    anyRouteMutated: boolean
  }
  cutoverIngress: {
    ready: boolean
    dockerNetworksChanged: boolean
  }
  candidate: {
    privateOnly: boolean
    databaseImportSucceeded: boolean
    synapseHealthPassed: boolean
    elementHealthPassed: boolean
  }
  blockers: string[]
  warnings: string[]
  errors: string[]
  detail: string | null
}

export type MigrationCutoverState = {
  source: string
  status: string
  migrationId: string
  capture: {
    kind: string
    sourceFrozen: boolean
    rehearsalOnly: boolean
    finalCutoverEligible: boolean
    sourceMigrationId: string
    sourceStackSlug: string
    matrixServerName: string
    matrixPublicUrl: string | null
    elementPublicUrl: string | null
  }
  candidateArtifactId: string
  staging: {
    stagingRunId: string
    privateRuntimeStagingId: string
    status: string
    privateOnly: boolean
    publicRoutesCreated: boolean
    databaseImportSucceeded: boolean
    synapseHealthPassed: boolean
    elementConfigPresent: boolean
    usersCount: number | null
    roomsCount: number | null
    eventsCount: number | null
  }
  productionCandidate: MigrationCutoverCandidate | null
  preview: MigrationCutoverPreview | null
  confirmation: MigrationCutoverConfirmation | null
  readiness: {
    status: string
    executionReady: boolean
    finalCaptureRequired: boolean
    candidateReady: boolean
    previewReady: boolean
    confirmationReady: boolean
    routesReady: boolean
    blockers: string[]
    warnings: string[]
    detail: string
  }
  routeSnapshot: {
    capturedAtUtc: string | null
    status: string
  }
  latestExecution: MigrationCutoverExecution | null
  rollback: {
    routeMutationOccurred: boolean
    automaticRollbackAvailable: boolean
    automaticRollbackAttempted: boolean
    automaticRollbackCompleted: boolean
    status: string
    detail: string
  }
  backupCatalogItemCreated: boolean
  restoreSessionCreated: boolean
  detail: string
}

export type ConfirmMigrationCutoverRequest = {
  operator?: string | null
  note?: string | null
  acknowledgePreviewReviewed: boolean
  acknowledgeCandidateIsPrivateAndHealthy: boolean
  acknowledgePublicRouteExposureRisk: boolean
  acknowledgeNoAutomaticRollback: boolean
  acknowledgeFinalBackupRequired: boolean
  acknowledgeExecutionStillLocked: boolean
}

export type ExecuteMigrationCutoverRequest = {
  operator?: string | null
  note?: string | null
  executeNpmRouteMutation: boolean
  executeCutoverIngressNetworkMutation: boolean
  acknowledgeConfirmationReviewed: boolean
  acknowledgeDockerNetworkMutation: boolean
  acknowledgeNpmMustNotJoinPrivateRestoreNetwork: boolean
  acknowledgeCreatesPublicRoutes: boolean
  acknowledgeNpmRoutesWillChange: boolean
  acknowledgeMatrixFederationExposureMayChange: boolean
  acknowledgeNoDnsMutation: boolean
  acknowledgeNoCertificateMutation: boolean
  acknowledgeNoRuntimePromotion: boolean
  acknowledgeNoAutomaticRollback: boolean
  acknowledgePostCutoverVerificationRequired: boolean
}

export class MigrationCutoverProblemError extends Error {
  readonly status: number
  readonly code: string | null

  constructor(message: string, status: number, code: string | null) {
    super(message)
    this.name = "MigrationCutoverProblemError"
    this.status = status
    this.code = code
  }
}

export function isMigrationCutoverStepUpRequired(value: unknown): boolean {
  return value instanceof MigrationCutoverProblemError &&
    value.status === 403 &&
    value.code === "step_up_required"
}

const base = (migrationId: string) =>
  `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/cutover`

async function request<T>(method: "GET" | "POST", path: string, body?: unknown): Promise<T> {
  const response = await fetch(path, {
    method,
    credentials: "include",
    cache: "no-store",
    headers: {
      Accept: "application/json",
      ...(body === undefined ? {} : { "Content-Type": "application/json" }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  if (!response.ok) {
    let code: string | null = null
    let detail: string | null = null
    try {
      const problem = await response.json() as {
        status?: unknown
        code?: unknown
        error?: unknown
        detail?: unknown
        message?: unknown
      }
      code = typeof problem.code === "string"
        ? problem.code
        : typeof problem.status === "string"
          ? problem.status
          : typeof problem.error === "string"
            ? problem.error
            : null
      detail = typeof problem.detail === "string"
        ? problem.detail
        : typeof problem.message === "string"
          ? problem.message
          : null
    } catch {
      code = null
      detail = null
    }

    throw new MigrationCutoverProblemError(
      detail ?? `${method} ${path} failed with status ${response.status}`,
      response.status,
      code,
    )
  }

  return await response.json() as T
}

export const getMigrationCutoverState = (migrationId: string) =>
  request<MigrationCutoverState>("GET", base(migrationId))

export const prepareMigrationCutoverCandidate = (migrationId: string) =>
  request("POST", `${base(migrationId)}/candidate`, { targetStackSlug: null })

export const createMigrationCutoverPreview = (migrationId: string) =>
  request("POST", `${base(migrationId)}/preview`, {
    targetStackSlug: null,
    restoreMode: null,
    intendedMatrixHost: null,
    intendedElementHost: null,
  })

export const confirmMigrationCutover = (
  migrationId: string,
  body: ConfirmMigrationCutoverRequest,
) => request("POST", `${base(migrationId)}/confirmation`, body)

export const getMigrationCutoverReadiness = (migrationId: string) =>
  request<MigrationCutoverState>("GET", `${base(migrationId)}/readiness`)

export const executeMigrationCutover = (
  migrationId: string,
  body: ExecuteMigrationCutoverRequest,
) => request("POST", `${base(migrationId)}/execution`, body)

export const getMigrationPostCutover = (migrationId: string) =>
  request<MigrationCutoverState>("GET", `${base(migrationId)}/post-cutover`)
