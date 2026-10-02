import { getJson, postJson } from "@/lib/api"

export type MigrationConversionAttempt = {
  conversionAttemptId: string
  migrationId: string
  status: string
  currentStep: string
  createdAtUtc: string
  updatedAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  resultCode: string | null
  failureCode: string | null
  failureSummary: string | null
  sourceStackId: string | null
  candidateArtifactId: string | null
  candidateArtifactKind: string | null
  candidateSourcePackageSha256: string | null
  candidateArtifactSha256: string | null
  candidateManifestSha256: string | null
  candidateChecksumsSha256: string | null
  candidateVerificationStatus: string | null
  candidateRetentionState: string | null
  candidateCreatedAtUtc: string | null
  candidateVerifiedAtUtc: string | null
}

export type MigrationConversionSourceStack = {
  sourceStackId: string
  slug: string
  matrixServerName: string
}

export type MigrationConversionOptions = {
  packageRevisionId: string
  boundSourceStack: MigrationConversionSourceStack
}

export type StartMigrationConversionRequest = {
  retryOfConversionAttemptId?: string | null
}

const path = (migrationId: string) =>
  `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/conversion-attempts`

export const listMigrationConversionAttempts = (migrationId: string) =>
  getJson<MigrationConversionAttempt[]>(path(migrationId))

export const getMigrationConversionOptions = (migrationId: string) =>
  getJson<MigrationConversionOptions>(`${path(migrationId)}/options`)

export const startMigrationConversion = (
  migrationId: string,
  request: StartMigrationConversionRequest = {},
) => postJson<StartMigrationConversionRequest, MigrationConversionAttempt>(path(migrationId), request)
