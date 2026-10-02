import type { MigrationAssuranceSummary } from "@/features/operator/migrations/api/migration-assurance"

export type MigrationAcceptanceState = {
  source: string
  status: string
  migrationId: string
  acceptanceEligible: boolean
  accepted: boolean
  publicVerificationRequired: boolean
  assurance: MigrationAssuranceSummary | null
  acceptance: {
    acceptanceId: string
    executionId: string
    candidateArtifactId: string
    stagingRunId: string
    publicCutoverAtUtc: string
    acceptedAtUtc: string
    acceptedBy: string
    note: string | null
    publicVerification: {
      status: string
      attempted: boolean
      passed: boolean
      checkCount: number
      failedCheckCount: number
      evidenceSha256: string | null
      detail: string
    }
    freshPublicVerificationAcknowledged: boolean
    targetWriteDivergenceAcknowledged: boolean
    rollbackBoundaryAcknowledged: boolean
    legacyRetentionAcknowledged: boolean
    noAutomaticLegacyDeletionAcknowledged: boolean
  } | null
  legacyRetention: {
    retentionRecordId: string
    status: string
    createdAtUtc: string
    retainUntilUtc: string
    cleanupEligibleAtUtc: string
    sourceMigrationId: string
    sourceProduct: string
    sourceVersion: string | null
    sourcePackageRetained: boolean
    candidateArtifactRetained: boolean
    privateStagingEvidenceRetained: boolean
    legacySourceResourcesRetained: boolean
    automaticDeletionAllowed: boolean
    summary: string
  } | null
  baselineBackup: {
    handoffId: string
    status: string
    attemptCount: number
    createdAtUtc: string
    updatedAtUtc: string
    startedAtUtc: string | null
    completedAtUtc: string | null
    targetStackSlug: string
    candidateId: string
    privateRuntimeId: string
    backupId: string | null
    catalogEntryId: string | null
    backupCreatedAtUtc: string | null
    backupTotalBytes: number | null
    backupTotalFiles: number | null
    backupWarningCount: number | null
    failureCode: string | null
    failureSummary: string | null
    detail: string
  } | null
  blockers: string[]
  warnings: string[]
  detail: string
}

export type MigrationAcceptanceCompletionEvidenceEnvelope = {
  schemaVersion: string
  payloadSha256: string
  payload: {
    reportId: string
    generatedAtUtc: string
    memVersion: string
    migrationId: string
    assurance: MigrationAssuranceSummary
    adoptionPlanId: string
    runtimeStackId: string
    targetStackSlug: string
    productionVerificationId: string
    productionVerificationEvidenceSha256: string
    acceptance: NonNullable<MigrationAcceptanceState["acceptance"]>
    legacyRetention: NonNullable<MigrationAcceptanceState["legacyRetention"]>
    baselineBackup: NonNullable<MigrationAcceptanceState["baselineBackup"]>
  }
}

export type MigrationAcceptanceCompletionDownload = {
  blob: Blob
  fileName: string
}

export type FinishMigrationRequest = {
  retentionDays: number
  confirmVerifiedServerIsAuthoritative: boolean
  confirmRecoveryBoundaryChanges: boolean
  confirmRetainOldServerAndNoAutomaticDeletion: boolean
}

export type AcceptMigrationRequest = {
  note?: string | null
  retentionDays: number
  acknowledgeFreshPublicVerification: boolean
  acknowledgeTargetWriteDivergence: boolean
  acknowledgeRollbackBoundaryChanges: boolean
  acknowledgeLegacySourceResourcesRetained: boolean
  acknowledgeNoAutomaticLegacyDeletion: boolean
}

export class MigrationAcceptanceProblemError extends Error {
  readonly status: number
  readonly code: string | null

  constructor(message: string, status: number, code: string | null) {
    super(message)
    this.name = "MigrationAcceptanceProblemError"
    this.status = status
    this.code = code
  }
}

export function isMigrationAcceptanceStepUpRequired(value: unknown): boolean {
  return value instanceof MigrationAcceptanceProblemError &&
    value.status === 403 &&
    value.code === "step_up_required"
}

const base = (migrationId: string) =>
  `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/acceptance`

const baselineBase = (migrationId: string) =>
  `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/baseline-backup`

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
      const problem = await response.json() as { code?: unknown; status?: unknown; error?: unknown; detail?: unknown; message?: unknown }
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

    throw new MigrationAcceptanceProblemError(
      detail ?? `${method} ${path} failed with status ${response.status}`,
      response.status,
      code,
    )
  }

  return await response.json() as T
}

export const getMigrationAcceptanceState = (migrationId: string) =>
  request<MigrationAcceptanceState>("GET", base(migrationId))

export const acceptMigration = (migrationId: string, body: AcceptMigrationRequest) =>
  request<MigrationAcceptanceState>("POST", base(migrationId), body)

export const finishMigration = (migrationId: string, body: FinishMigrationRequest) =>
  request<MigrationAcceptanceState>("POST", `${base(migrationId)}/finish`, body)

export const retryMigrationBaselineBackup = (migrationId: string) =>
  request<MigrationAcceptanceState>("POST", `${baselineBase(migrationId)}/retry`)

function completionReportFileName(response: Response): string {
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
    "mem-migration-completion.json"
}

export async function downloadMigrationAcceptanceCompletionReport(
  migrationId: string,
): Promise<MigrationAcceptanceCompletionDownload> {
  const path = `${base(migrationId)}/report`
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

    throw new MigrationAcceptanceProblemError(
      detail ?? `GET ${path} failed with status ${response.status}`,
      response.status,
      code,
    )
  }

  return {
    blob: await response.blob(),
    fileName: completionReportFileName(response),
  }
}
