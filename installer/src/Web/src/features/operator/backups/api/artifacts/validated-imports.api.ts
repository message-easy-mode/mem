import type {
  RuntimeStackBackupUploadValidationResponse,
} from "../types/backups.types"
import type {
  ValidatedImportArtifactDeleteResponse,
  ValidatedImportArtifactDetailResponse,
} from "./validated-imports.types"
import {
  backupsApiRoutes,
  controlPlaneDelete,
  controlPlaneGet,
  controlPlaneUpload,
} from "../transport/host-agent"

const VALIDATED_IMPORTS_BASE = backupsApiRoutes.artifacts.validatedImports

export function validateBackupExportUpload(
  file: File,
): Promise<RuntimeStackBackupUploadValidationResponse> {
  const formData = new FormData()
  formData.append("file", file)

  return controlPlaneUpload<RuntimeStackBackupUploadValidationResponse>(
    VALIDATED_IMPORTS_BASE,
    formData,
  )
}

/** Safe operator-facing source metadata; no host filesystem paths are exposed. */
export function inspectValidatedImportArtifact(validationId: string) {
  return controlPlaneGet<ValidatedImportArtifactDetailResponse>(
    `${VALIDATED_IMPORTS_BASE}/${encodeURIComponent(validationId)}`,
  )
}

/**
 * Deletes only the retained Uploaded ZIP source artifact. The HostAgent retains
 * restore sessions, logs, evidence, support reports, and restored stacks.
 */
export function deleteValidatedImportArtifact(validationId: string) {
  return controlPlaneDelete<ValidatedImportArtifactDeleteResponse>(
    `${VALIDATED_IMPORTS_BASE}/${encodeURIComponent(validationId)}?acknowledgeDelete=true`,
  )
}
