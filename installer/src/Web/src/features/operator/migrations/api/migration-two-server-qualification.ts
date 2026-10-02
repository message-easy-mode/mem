import type { MigrationAssuranceSummary } from "@/features/operator/migrations/api/migration-assurance"

export type MigrationTwoServerHostIdentity = {
  machineIdSha256: string
  machineName: string
  operatingSystem: string
  architecture: string
  dockerEngineIdSha256: string
  dockerName: string
  dockerServerVersion: string
}

export type MigrationTwoServerSourceContainerEvidence = {
  role: string
  containerId: string
  containerName: string
  imageId: string
  writerContainer: boolean
  wasRunningBeforeFreeze: boolean
  currentState: string
  currentlyRunning: boolean
  currentRestartPolicy: string
  identityMatched: boolean
  frozenStatePreserved: boolean
}

export type MigrationTwoServerSourceEvidencePayload = {
  qualificationAttemptId: string
  generatedAtUtc: string
  migrationId: string
  intakeId: string
  packageRevisionId: string
  encryptedPackageSha256: string
  encryptedPackageBytes: number
  sourceArchiveSha256: string
  freezeAttemptId: string
  freezePlanId: string
  freezePlanSha256: string
  sourceFingerprint: string
  sourceStackSlug: string
  matrixServerName: string
  sourceFrozen: boolean
  publicRoutingMutationOccurred: boolean
  developmentExternalControlPlane: boolean
  sourceHost: MigrationTwoServerHostIdentity
  containers: MigrationTwoServerSourceContainerEvidence[]
}

export type MigrationTwoServerSourceEvidenceEnvelope = {
  schemaVersion: string
  payloadSha256: string
  payload: MigrationTwoServerSourceEvidencePayload
}

export type MigrationTwoServerQualification = {
  qualificationId: string
  status: string
  importedAtUtc: string
  qualifiedAtUtc: string
  sourceEvidenceAttemptId: string
  sourceEvidenceSha256: string
  qualificationEvidenceSha256: string
  sourceMigrationId: string
  packageRevisionId: string
  sourceStackSlug: string
  matrixServerName: string
  adoptionPlanId: string
  runtimeStackId: string
  productionVerificationId: string
  acceptanceId: string
  baselineBackupHandoffId: string
  baselineCatalogEntryId: string
  sourceHost: MigrationTwoServerHostIdentity
  targetHost: MigrationTwoServerHostIdentity
  distinctMachineIdentity: boolean
  distinctDockerEngineIdentity: boolean
  sourceContainers: MigrationTwoServerSourceContainerEvidence[]
}

export type MigrationTwoServerQualificationState = {
  source: string
  status: string
  migrationId: string
  qualificationEligible: boolean
  qualified: boolean
  assurance: MigrationAssuranceSummary | null
  qualification: MigrationTwoServerQualification | null
  blockers: string[]
  detail: string
}


export type MigrationTwoServerQualificationClosureRequest = {
  qualificationEvidenceReviewed: boolean
  distinctHostEvidenceReviewed: boolean
  normalLifecycleReviewed: boolean
  sourceRetentionReviewed: boolean
  releaseHandoffAcknowledged: boolean
  note: string | null
}

export type MigrationTwoServerQualificationClosureRoute = {
  serviceKey: string
  publicHost: string
  publicBaseUrl: string | null
  provider: string
  providerRouteId: string
  forwardScheme: string
  forwardHost: string
  forwardPort: number
  isPublic: boolean
  sslExpected: boolean
  sslConfigured: boolean
  forceSsl: boolean
  status: string
  lastVerifiedAtUtc: string | null
}

export type MigrationTwoServerQualificationClosurePayload = {
  closureId: string
  closedAtUtc: string
  memVersion: string
  migrationId: string
  assurance: MigrationAssuranceSummary
  sourceMigrationId: string
  packageRevisionId: string
  encryptedPackageSha256: string
  sourceFingerprint: string
  sourceStackSlug: string
  matrixServerName: string
  qualificationId: string
  qualificationEvidenceSha256: string
  sourceEvidenceAttemptId: string
  sourceEvidenceSha256: string
  adoptionPlanId: string
  runtimeStackId: string
  runtimeStackSlug: string
  productionVerificationId: string
  productionVerificationEvidenceSha256: string
  productionVerificationCompletedAtUtc: string
  acceptanceId: string
  acceptanceEvidenceSha256: string
  acceptedAtUtc: string
  baselineBackupHandoffId: string
  baselineBackupId: string
  baselineCatalogEntryId: string
  baselineCatalogPayloadState: string
  baselineCatalogIntegrityStatus: string
  baselineCompletedAtUtc: string
  baselineBackupBytes: number | null
  baselineBackupFiles: number | null
  baselineBackupWarnings: number | null
  sourceHost: MigrationTwoServerHostIdentity
  targetHost: MigrationTwoServerHostIdentity
  distinctMachineIdentity: boolean
  distinctDockerEngineIdentity: boolean
  sourceContainers: MigrationTwoServerSourceContainerEvidence[]
  publicRoutes: MigrationTwoServerQualificationClosureRoute[]
  qualificationEvidenceReviewed: boolean
  distinctHostEvidenceReviewed: boolean
  normalLifecycleReviewed: boolean
  sourceRetentionReviewed: boolean
  releaseHandoffAcknowledged: boolean
  note: string | null
}

export type MigrationTwoServerQualificationClosureEnvelope = {
  schemaVersion: string
  payloadSha256: string
  payload: MigrationTwoServerQualificationClosurePayload
}

export type MigrationTwoServerQualificationClosureState = {
  source: string
  status: string
  migrationId: string
  closureEligible: boolean
  closed: boolean
  assurance: MigrationAssuranceSummary | null
  closure: MigrationTwoServerQualificationClosureEnvelope | null
  blockers: string[]
  detail: string
}

export type MigrationTwoServerQualificationClosureDownload = {
  blob: Blob
  fileName: string
}

export class MigrationTwoServerQualificationProblemError extends Error {
  readonly status: number
  readonly code: string | null

  constructor(message: string, status: number, code: string | null) {
    super(message)
    this.name = "MigrationTwoServerQualificationProblemError"
    this.status = status
    this.code = code
  }
}

export function isMigrationTwoServerQualificationStepUpRequired(value: unknown): boolean {
  return value instanceof MigrationTwoServerQualificationProblemError &&
    value.status === 403 &&
    value.code === "step_up_required"
}

const base = (migrationId: string) =>
  `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/two-server-qualification`

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
        code?: unknown
        status?: unknown
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

    throw new MigrationTwoServerQualificationProblemError(
      detail ?? `${method} ${path} failed with status ${response.status}`,
      response.status,
      code,
    )
  }

  return await response.json() as T
}

export const getMigrationTwoServerQualificationState = (migrationId: string) =>
  request<MigrationTwoServerQualificationState>("GET", base(migrationId))

export const importMigrationTwoServerSourceEvidence = (
  migrationId: string,
  envelope: MigrationTwoServerSourceEvidenceEnvelope,
) => request<MigrationTwoServerQualificationState>(
  "POST",
  `${base(migrationId)}/source-evidence`,
  envelope,
)

export const getMigrationTwoServerQualificationClosureState = (migrationId: string) =>
  request<MigrationTwoServerQualificationClosureState>("GET", `${base(migrationId)}/closure`)

export const closeMigrationTwoServerQualification = (
  migrationId: string,
  closure: MigrationTwoServerQualificationClosureRequest,
) => request<MigrationTwoServerQualificationClosureState>(
  "POST",
  `${base(migrationId)}/closure`,
  closure,
)

function closureReportFileName(response: Response): string {
  const disposition = response.headers.get("content-disposition")
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition ?? "")?.[1]
  if (encoded) {
    try {
      return decodeURIComponent(encoded)
    } catch {
      return encoded
    }
  }

  return /filename="?([^";]+)"?/i.exec(disposition ?? "")?.[1]?.trim() ||
    "mem-production-qualification-closure.json"
}

export async function downloadMigrationTwoServerQualificationClosureReport(
  migrationId: string,
): Promise<MigrationTwoServerQualificationClosureDownload> {
  const path = `${base(migrationId)}/closure/report`
  const response = await fetch(path, {
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
        code?: unknown
        status?: unknown
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

    throw new MigrationTwoServerQualificationProblemError(
      detail ?? `GET ${path} failed with status ${response.status}`,
      response.status,
      code,
    )
  }

  return {
    blob: await response.blob(),
    fileName: closureReportFileName(response),
  }
}
