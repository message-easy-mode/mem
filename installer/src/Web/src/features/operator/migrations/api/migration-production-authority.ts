export const migrationProductionAuthorityKeys = {
  all: ["migration-production-authority"] as const,
  state: (migrationId: string) => [...migrationProductionAuthorityKeys.all, migrationId] as const,
}

export type CreateOperatorAttestedSnapshotAuthorityRequest = {
  acknowledgeUsersWereInstructedNotToUseSource: boolean
  acknowledgePostCaptureWritesWillNotMigrate: boolean
  acknowledgeSelectedSnapshotBecomesAuthoritative: boolean
  acknowledgeSourceWillBeRetainedUntilVerification: boolean
  acknowledgeNoFormalSourceFreezeEvidence: boolean
  acknowledgeReducedRollbackAssurance: boolean
}

export type MigrationProductionAuthority = {
  productionAuthorityId: string
  authorityType: string
  status: string
  packageRevisionId: string
  candidateArtifactId: string
  stagingRunId: string
  encryptedPackageSha256: string
  decryptedArchiveSha256: string
  sourceMigrationId: string
  sourceFingerprint: string
  matrixServerName: string
  signingKeyIdentitySha256: string
  captureKind: string
  sourceFrozen: boolean
  rehearsalOnly: boolean
  capturedAtUtc: string | null
  usersCount: number | null
  roomsCount: number | null
  eventsCount: number | null
  evidenceSchemaVersion: string
  evidenceSha256: string
  acknowledgementsSchemaVersion: string
  acknowledgementsSha256: string
  createdAtUtc: string
}

export type MigrationProductionAuthorityState = {
  status: string
  migrationId: string
  authorizesProduction: boolean
  authority: MigrationProductionAuthority | null
  detail: string
}

export class MigrationProductionAuthorityProblemError extends Error {
  readonly status: number
  readonly code: string | null

  constructor(message: string, status: number, code: string | null = null) {
    super(message)
    this.name = "MigrationProductionAuthorityProblemError"
    this.status = status
    this.code = code
  }
}

export function isMigrationProductionAuthorityStepUpRequired(value: unknown): boolean {
  return value instanceof MigrationProductionAuthorityProblemError &&
    value.status === 403 &&
    value.code === "step_up_required"
}

const path = (migrationId: string) =>
  `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/production-authority`

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

    throw new MigrationProductionAuthorityProblemError(
      detail ?? `${method} ${requestPath} failed with status ${response.status}`,
      response.status,
      code,
    )
  }

  return await response.json() as T
}

export const getMigrationProductionAuthorityState = (migrationId: string) =>
  request<MigrationProductionAuthorityState>("GET", path(migrationId))

export const createOperatorAttestedSnapshotAuthority = (
  migrationId: string,
  requestBody: CreateOperatorAttestedSnapshotAuthorityRequest,
) => request<MigrationProductionAuthorityState>(
  "POST",
  `${path(migrationId)}/operator-attested-snapshot`,
  requestBody,
)
