/**
 * Browser-safe uploaded-ZIP provenance models. These belong to archive
 * validation and catalog materialisation, not restore-session routing.
 */
export type ValidatedImportArtifactDetailResponse = {
  source: string
  status: string
  validationId: string
  sourceKind: "uploaded-zip" | string
  uploadedFileName: string | null
  recordedAtUtc: string | null
  archiveBytes: number | null
  archiveState: "retained" | "removed" | string
  validation: {
    status: string
    summary: string
    zipEntryCount: number
    totalUncompressedBytes: number
    manifestPresent: boolean
    checksumsPresent: boolean
    passedChecks: number
    failedChecks: number
    warningCount: number
    errors: string[]
  }
  manifest: {
    manifestVersion: number | null
    memVersion: string | null
    sourceStackSlug: string | null
    sourceStackDisplayName: string | null
    matrixServerName: string | null
    includedFileCount: number
  } | null
  retention: {
    canDelete: boolean
    deleteBlockReason: string | null
    removedAtUtc: string | null
    removedBy: string | null
  }
  warnings: string[]
  detail: string | null
}

export type ValidatedImportArtifactDeleteResponse = {
  source: string
  status: string
  validationId: string
  archiveState: string
  deletedBytes: number
  deletedAtUtc: string | null
  warnings: string[]
  detail: string | null
}
