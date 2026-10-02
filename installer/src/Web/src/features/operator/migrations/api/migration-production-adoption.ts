export type MigrationProductionAdoptionCollision = {
  code: string
  resourceType: string
  resourceValue: string
  detail: string
}

export type MigrationProductionAdoptionRoutePlan = {
  serviceKey: string
  publicHost: string
  publicBaseUrl: string
  forwardScheme: string
  forwardHost: string
  forwardPort: number
  provider: string
  publicMutationDeferred: boolean
}


export type MigrationProductionMaterialization = {
  materializationId: string | null
  status: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  productionDatabaseImported: boolean
  matrixContainerStarted: boolean
  matrixHealthPassed: boolean
  elementContainerStarted: boolean
  elementHealthPassed: boolean
  runtimeManifestSaved: boolean
  databaseOwnershipSaved: boolean
  runtimeRecordsCreated: boolean
  userInventorySynchronized: boolean
  publicRoutesCreated: boolean
  failureCode: string | null
  failureSummary: string | null
}


export type MigrationProductionCutoverRouteSnapshot = {
  serviceKey: string
  publicHost: string
  desiredForwardHost: string
  desiredForwardPort: number
  existingRouteFound: boolean
  existingRouteId: number | null
  existingForwardScheme: string | null
  existingForwardHost: string | null
  existingForwardPort: number | null
  existingCertificateId: number | null
  existingSslForced: boolean | null
  existingHttp2: boolean | null
  existingEnabled: boolean | null
  existingAdvancedConfigSha256: string
  alreadyTargetsProductionRuntime: boolean
  action: string
  selectedCertificateId?: number | null
  selectedCertificateRecordId?: string | null
  selectedCertificateName?: string | null
}

export type MigrationProductionVerificationCheck = {
  code: string
  name: string
  success: boolean
  detail: string
  url: string | null
  statusCode: number | null
}

export type MigrationProductionVerification = {
  verificationId: string | null
  status: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  validUntilUtc: string | null
  fresh: boolean
  passed: boolean
  checkCount: number
  failedCheckCount: number
  evidenceSha256: string | null
  readinessReportId: string | null
  checks: MigrationProductionVerificationCheck[]
  failureCode: string | null
  failureSummary: string | null
}

export type MigrationProductionCutover = {
  preview: {
    previewId: string | null
    status: string
    createdAtUtc: string | null
    expiresAtUtc: string | null
    snapshotSha256: string | null
    routes: MigrationProductionCutoverRouteSnapshot[]
    blockers: string[]
  }
  execution: {
    executionId: string | null
    status: string
    startedAtUtc: string | null
    completedAtUtc: string | null
    targetPublicAtUtc: string | null
    matrixRouteId: string | null
    elementRouteId: string | null
    runtimePromotionCompleted: boolean
    publicRoutesCreated: boolean
    routeCompensationAttempted: boolean
    routeCompensationCompleted: boolean
    failureCode: string | null
    failureSummary: string | null
  }
}

export type MigrationProductionRollbackRouteState = {
  serviceKey: string
  publicHost: string
  currentRouteId: number | null
  currentForwardScheme: string | null
  currentForwardHost: string | null
  currentForwardPort: number | null
  currentEnabled: boolean | null
  currentSnapshotSha256: string
  matchesExpectedTarget: boolean
  restoreAction: string
}


export type MigrationProductionSourceRestorationCompletionContainer = {
  role: string
  containerId: string
  containerName: string
  imageId: string
  originalRestartPolicy: string
  wasRunning: boolean
  finalState: string
  finalRestartPolicy: string
  finalHealth: string | null
  runningStateRestored: boolean
  restartPolicyRestored: boolean
  serviceVerified: boolean
}

export type MigrationProductionSourceRestorationCompletionEnvelope = {
  schemaVersion: string
  payloadSha256: string
  payload: {
    restorationAttemptId: string
    completedAtUtc: string
    sourceHandoffId: string
    sourceHandoffSha256: string
    migrationId: string
    sourceMigrationId: string
    targetRollbackExecutionId: string
    freezeAttemptId: string
    freezePlanId: string
    freezePlanHash: string
    sourceFingerprint: string
    sourceStackSlug: string
    matrixServerName: string
    sourceRestored: boolean
    restartPoliciesRestored: boolean
    originalRunningStatesRestored: boolean
    matrixVerified: boolean
    elementVerified: boolean
    targetRollbackAuthorityVerified: boolean
    developmentExternalControlPlane: boolean
    containers: MigrationProductionSourceRestorationCompletionContainer[]
    targetRouteEvidence: Array<{
      serviceKey: string
      publicHost: string
      restoredState: string
      routeId: number | null
      forwardScheme: string | null
      forwardHost: string | null
      forwardPort: number | null
      enabled: boolean | null
      snapshotSha256: string
    }>
  }
}

export type MigrationProductionRollback = {
  preview: {
    previewId: string | null
    status: string
    createdAtUtc: string | null
    expiresAtUtc: string | null
    snapshotSha256: string | null
    routes: MigrationProductionRollbackRouteState[]
    blockers: string[]
  }
  execution: {
    executionId: string | null
    status: string
    startedAtUtc: string | null
    completedAtUtc: string | null
    routesRestored: boolean
    runtimeRoutesRemoved: boolean
    targetContainersStopped: boolean
    targetRouteCompensationAttempted: boolean
    targetRouteCompensationCompleted: boolean
    sourceHandoffId: string | null
    sourceHandoffSha256: string | null
    failureCode: string | null
    failureSummary: string | null
  }
  completion: {
    status: string
    restorationAttemptId: string | null
    completionSha256: string | null
    importedAtUtc: string | null
    completedAtUtc: string | null
    sourceRestored: boolean
    restartPoliciesRestored: boolean
    originalRunningStatesRestored: boolean
    matrixVerified: boolean
    elementVerified: boolean
    targetRollbackAuthorityVerified: boolean
    targetRollbackStillIntact: boolean
    developmentExternalControlPlane: boolean
  }
}

export type MigrationProductionAdoptionPlan = {
  adoptionPlanId: string
  migrationId: string
  packageRevisionId: string
  candidateArtifactId: string
  stagingRunId: string
  status: string
  revisionNumber: number
  planSha256: string
  createdAtUtc: string
  updatedAtUtc: string
  preparedAtUtc: string
  runtimeStackId: string
  targetStackSlug: string
  targetDisplayName: string
  matrixInstanceId: string
  elementInstanceId: string
  matrixServerName: string
  matrixPublicHost: string
  matrixPublicBaseUrl: string
  elementPublicHost: string
  elementPublicBaseUrl: string
  runtimeNetworkName: string
  matrixContainerName: string
  elementContainerName: string
  matrixImageReference: string
  matrixImageId: string
  elementImageReference: string
  elementImageId: string
  databaseEngine: string
  databaseHost: string
  databasePort: number
  databaseName: string
  databaseUsername: string
  databasePasswordSecretKind: string
  expectedUsersCount: number | null
  expectedRoomsCount: number | null
  expectedEventsCount: number | null
  routes: MigrationProductionAdoptionRoutePlan[]
  collisions: MigrationProductionAdoptionCollision[]
  collisionFree: boolean
  runtimeRecordsCreated: boolean
  publicRoutesCreated: boolean
  materialization: MigrationProductionMaterialization
  cutover: MigrationProductionCutover
  productionVerification: MigrationProductionVerification
  rollback: MigrationProductionRollback
  blockerSummary: string | null
}

export type MigrationProductionAdoptionState = {
  source: string
  status: string
  migrationId: string
  planPrepared: boolean
  plan: MigrationProductionAdoptionPlan | null
  detail: string
}

export type PrepareMigrationProductionAdoptionRequest = {
  targetStackSlug?: string | null
  elementPublicHost?: string | null
}

export type CreateMigrationPrivateServerRequest = {
  targetStackSlug?: string | null
  elementPublicHost?: string | null
  confirmVerifiedSnapshotIsAuthoritative: boolean
  confirmLaterSourceWritesAreNotIncluded: boolean
  confirmCreatePrivateServer: boolean
}

export type ReviewMigrationPrivateServerTargetRequest = {
  targetStackSlug?: string | null
  elementPublicHost?: string | null
}

export type MigrationPrivateServerTargetCollision = {
  field: "stack-name" | "matrix-address" | "element-address"
  code: string
  resourceType: string
  resourceValue: string
  owner: string | null
  detail: string
}

export type MigrationPrivateServerTargetReview = {
  migrationId: string
  sourceStackSlug: string
  sourceElementPublicHost: string
  targetStackSlug: string
  matrixServerName: string
  elementPublicHost: string
  stackNameStatus: "available" | "conflict"
  matrixAddressStatus: "locked-available" | "locked-conflict"
  elementAddressStatus: "available" | "conflict"
  collisionFree: boolean
  suggestedTargetStackSlug: string | null
  collisions: MigrationPrivateServerTargetCollision[]
  detail: string
}

export type MakeMigrationServerLiveRequest = {
  confirmMovePublicTraffic: boolean
  confirmStopUsingOldServer: boolean
  confirmRunLiveVerification: boolean
}

export type MaterializeMigrationProductionRuntimeRequest = {
  operator?: string | null
  note?: string | null
  executePrivateProductionMaterialization: boolean
  acknowledgeCreatesNormalRuntimeRecords: boolean
  acknowledgeMutatesProductionPostgres: boolean
  acknowledgeStartsProductionContainers: boolean
  acknowledgeNoPublicRoutes: boolean
  acknowledgeNoAutomaticRollback: boolean
}


export type PrepareMigrationProductionCutoverRequest = {
  previewLifetimeMinutes?: number | null
}

export type ExecuteMigrationProductionCutoverRequest = {
  operator?: string | null
  note?: string | null
  previewId: string
  executeNpmRouteMutation: boolean
  acknowledgeSourceFrozen: boolean
  acknowledgeProductionAuthority: boolean
  acknowledgePrivateRuntimeHealthy: boolean
  acknowledgeRouteSnapshotReviewed: boolean
  acknowledgeCreatesPublicRoutes: boolean
  acknowledgeNoDnsMutation: boolean
  acknowledgeNoCertificateMutation: boolean
  acknowledgeRollbackIsNextSlice: boolean
  acknowledgePostCutoverVerificationRequired: boolean
}

export type RunMigrationProductionVerificationRequest = {
  operator?: string | null
  note?: string | null
  freshnessMinutes?: number | null
}

export type PrepareMigrationProductionRollbackRequest = {
  previewLifetimeMinutes?: number | null
}

export type ExecuteMigrationProductionRollbackRequest = {
  operator?: string | null
  note?: string | null
  previewId: string
  executeTargetRollback: boolean
  acknowledgeMigrationNotAccepted: boolean
  acknowledgeRestoresPreCutoverRoutes: boolean
  acknowledgeStopsTargetContainers: boolean
  acknowledgePreservesTargetData: boolean
  acknowledgeSourceRemainsFrozen: boolean
  acknowledgeSourceRestorationRequiresHandoff: boolean
  acknowledgeNoAutomaticSourceHostMutation: boolean
}

export type MigrationProductionSourceHandoffDownload = {
  blob: Blob
  fileName: string
}

export class MigrationProductionAdoptionProblemError extends Error {
  readonly status: number
  readonly code: string | null

  constructor(message: string, status: number, code: string | null = null) {
    super(message)
    this.name = "MigrationProductionAdoptionProblemError"
    this.status = status
    this.code = code
  }
}

const path = (migrationId: string) =>
  `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/production-adoption`

const utcOffsetPattern = /(?:Z|[+-]\d{2}:\d{2})$/i

export function normalizeMigrationUtcTimestamp(value: string | null): string | null {
  if (!value || utcOffsetPattern.test(value)) return value
  return `${value}Z`
}

export function normalizeMigrationProductionAdoptionState(
  state: MigrationProductionAdoptionState,
): MigrationProductionAdoptionState {
  if (!state.plan) return state

  return {
    ...state,
    plan: {
      ...state.plan,
      cutover: {
        ...state.plan.cutover,
        preview: {
          ...state.plan.cutover.preview,
          createdAtUtc: normalizeMigrationUtcTimestamp(state.plan.cutover.preview.createdAtUtc),
          expiresAtUtc: normalizeMigrationUtcTimestamp(state.plan.cutover.preview.expiresAtUtc),
        },
      },
    },
  }
}

async function request<T>(
  method: "GET" | "POST",
  requestPath: string,
  body?: unknown,
): Promise<T> {
  const response = await fetch(requestPath, {
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
      code = typeof problem.status === "string"
        ? problem.status
        : typeof problem.code === "string"
          ? problem.code
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

    throw new MigrationProductionAdoptionProblemError(
      detail ?? `${method} ${requestPath} failed with status ${response.status}`,
      response.status,
      code,
    )
  }

  return await response.json() as T
}

const requestState = (
  method: "GET" | "POST",
  requestPath: string,
  body?: unknown,
) => request<MigrationProductionAdoptionState>(method, requestPath, body)
  .then(normalizeMigrationProductionAdoptionState)

export const getMigrationProductionAdoptionState = (migrationId: string) =>
  requestState("GET", path(migrationId))

export const prepareMigrationProductionAdoptionPlan = (
  migrationId: string,
  requestBody: PrepareMigrationProductionAdoptionRequest = {},
) => requestState(
  "POST",
  `${path(migrationId)}/plan`,
  requestBody,
)

export const reviewMigrationPrivateServerTarget = (
  migrationId: string,
  requestBody: ReviewMigrationPrivateServerTargetRequest = {},
) => request<MigrationPrivateServerTargetReview>(
  "POST",
  `${path(migrationId)}/private-server/review`,
  requestBody,
)

export const createMigrationPrivateServer = (
  migrationId: string,
  requestBody: CreateMigrationPrivateServerRequest,
) => requestState(
  "POST",
  `${path(migrationId)}/private-server`,
  requestBody,
)

export const makeMigrationServerLive = (
  migrationId: string,
  requestBody: MakeMigrationServerLiveRequest,
) => requestState(
  "POST",
  `${path(migrationId)}/go-live`,
  requestBody,
)

export const materializeMigrationProductionRuntime = (
  migrationId: string,
  requestBody: MaterializeMigrationProductionRuntimeRequest,
) => requestState(
  "POST",
  `${path(migrationId)}/materialize`,
  requestBody,
)



export const prepareMigrationProductionCutoverPreview = (
  migrationId: string,
  requestBody: PrepareMigrationProductionCutoverRequest = {},
) => requestState(
  "POST",
  `${path(migrationId)}/cutover/preview`,
  requestBody,
)

export const executeMigrationProductionCutover = (
  migrationId: string,
  requestBody: ExecuteMigrationProductionCutoverRequest,
) => requestState(
  "POST",
  `${path(migrationId)}/cutover/execution`,
  requestBody,
)

export const runMigrationProductionVerification = (
  migrationId: string,
  requestBody: RunMigrationProductionVerificationRequest = {},
) => requestState(
  "POST",
  `${path(migrationId)}/verification`,
  requestBody,
)

export const prepareMigrationProductionRollbackPreview = (
  migrationId: string,
  requestBody: PrepareMigrationProductionRollbackRequest = {},
) => requestState(
  "POST",
  `${path(migrationId)}/rollback/preview`,
  requestBody,
)

export const executeMigrationProductionRollback = (
  migrationId: string,
  requestBody: ExecuteMigrationProductionRollbackRequest,
) => requestState(
  "POST",
  `${path(migrationId)}/rollback/execution`,
  requestBody,
)

export const importMigrationProductionSourceCompletion = (
  migrationId: string,
  envelope: MigrationProductionSourceRestorationCompletionEnvelope,
) => requestState(
  "POST",
  `${path(migrationId)}/rollback/source-completion`,
  envelope,
)

function sourceHandoffFileName(response: Response) {
  const disposition = response.headers.get("Content-Disposition") ?? ""
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1]
  if (encoded) {
    try {
      return decodeURIComponent(encoded)
    } catch {
      // Fall back to the basic filename token below.
    }
  }

  const basic = /filename="?([^";]+)"?/i.exec(disposition)?.[1]?.trim()
  return basic || "mem-source-restoration-handoff.json"
}

export async function downloadMigrationProductionSourceHandoff(
  migrationId: string,
): Promise<MigrationProductionSourceHandoffDownload> {
  const requestPath = `${path(migrationId)}/rollback/source-handoff`
  const response = await fetch(requestPath, {
    method: "GET",
    credentials: "include",
    cache: "no-store",
    headers: { Accept: "application/json" },
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
      code = typeof problem.status === "string"
        ? problem.status
        : typeof problem.code === "string"
          ? problem.code
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

    throw new MigrationProductionAdoptionProblemError(
      detail ?? `GET ${requestPath} failed with status ${response.status}`,
      response.status,
      code,
    )
  }

  return {
    blob: await response.blob(),
    fileName: sourceHandoffFileName(response),
  }
}

export const isMigrationProductionAdoptionStepUpRequired = (error: unknown) =>
  error instanceof MigrationProductionAdoptionProblemError &&
  error.status === 403 &&
  (error.code === "step_up_required" || error.code === "operator_step_up_required")
