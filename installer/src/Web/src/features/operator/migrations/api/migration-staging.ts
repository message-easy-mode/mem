export type MigrationStagingRun = {
  stagingRunId: string
  migrationId: string
  candidateArtifactId: string
  retryOfStagingRunId: string | null
  status: string
  currentStep: string
  createdAtUtc: string
  updatedAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  destroyedAtUtc: string | null
  privateOnly: boolean
  publicRoutesCreated: boolean
  databaseImportSucceeded: boolean
  synapseHealthPassed: boolean
  elementConfigPresent: boolean
  elementConfigSha256: string | null
  elementContainerStarted: boolean
  elementHealthPassed: boolean
  elementSynapseConnectivityPassed: boolean
  elementNetworkAttached: boolean
  elementImageReference: string | null
  synapseImageReference: string | null
  usersCount: number | null
  roomsCount: number | null
  eventsCount: number | null
  matrixServerName: string | null
  failureCode: string | null
  failureSummary: string | null
  retirementReviewAvailable: boolean
}

export type StartMigrationStagingRequest = {
  retryOfStagingRunId?: string | null
}

export class MigrationStagingProblemError extends Error {
  readonly status: number
  readonly code: string | null

  constructor(message: string, status: number, code: string | null = null) {
    super(message)
    this.name = "MigrationStagingProblemError"
    this.status = status
    this.code = code
  }
}

export function isMigrationStagingStepUpRequired(value: unknown): boolean {
  return value instanceof MigrationStagingProblemError &&
    value.status === 403 &&
    value.code === "step_up_required"
}

const path = (migrationId: string) =>
  `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/staging-runs`

export async function migrationStagingRequest<T>(
  method: "GET" | "POST",
  requestPath: string,
  body?: unknown,
  signal?: AbortSignal,
): Promise<T> {
  const response = await fetch(requestPath, {
    method,
    signal,
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

    throw new MigrationStagingProblemError(
      detail ?? `${method} ${requestPath} failed with status ${response.status}`,
      response.status,
      code,
    )
  }

  return await response.json() as T
}

export const listMigrationStagingRuns = (migrationId: string) =>
  migrationStagingRequest<MigrationStagingRun[]>("GET", path(migrationId))

export const startMigrationStaging = (
  migrationId: string,
  requestBody: StartMigrationStagingRequest = {},
) => migrationStagingRequest<MigrationStagingRun>("POST", path(migrationId), requestBody)
