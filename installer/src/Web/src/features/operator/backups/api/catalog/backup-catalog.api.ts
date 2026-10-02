import {
  controlPlaneDelete,
  controlPlaneGet,
  controlPlanePost,
} from "../transport/host-agent"
import type {
  BackupCatalogDetailResponse,
  BackupCatalogImportArchiveDeleteRequest,
  BackupCatalogImportArchiveDeleteResponse,
  BackupCatalogImportArchiveResponse,
  BackupCatalogImportedMaterialisationResponse,
  BackupCatalogLifecycleResponse,
  BackupCatalogListResponse,
  BackupCatalogLocalBackfillResponse,
  BackupCatalogPermanentDeleteResponse,
  BackupCatalogPortableExportResponse,
  CatalogRestoreSessionResponse,
} from "./backup-catalog.types"

const CATALOG_BASE = "/internal/host-agent/backups/catalog"

export function listBackupCatalog() {
  return controlPlaneGet<BackupCatalogListResponse>(CATALOG_BASE)
}

export function getBackupCatalogEntry(catalogEntryId: string) {
  return controlPlaneGet<BackupCatalogDetailResponse>(
    `${CATALOG_BASE}/${encodeURIComponent(catalogEntryId)}`,
  )
}

export function getBackupCatalogLifecycle(catalogEntryId: string) {
  return controlPlaneGet<BackupCatalogLifecycleResponse>(
    `${CATALOG_BASE}/${encodeURIComponent(catalogEntryId)}/lifecycle`,
  )
}

export function backfillLocalBackupCatalog() {
  return controlPlanePost<void, BackupCatalogLocalBackfillResponse>(
    `${CATALOG_BASE}/backfill/local`,
  )
}

export function materialiseImportedZip(validationId: string) {
  return controlPlanePost<void, BackupCatalogImportedMaterialisationResponse>(
    `${CATALOG_BASE}/imports/${encodeURIComponent(validationId)}/materialise`,
  )
}

export function prepareCatalogRestoreSession(catalogEntryId: string) {
  return controlPlanePost<void, CatalogRestoreSessionResponse>(
    `${CATALOG_BASE}/${encodeURIComponent(catalogEntryId)}/restore-session`,
  )
}

export function exportCatalogPortableZip(catalogEntryId: string) {
  return controlPlanePost<void, BackupCatalogPortableExportResponse>(
    `${CATALOG_BASE}/${encodeURIComponent(catalogEntryId)}/portable-export`,
  )
}

/**
 * The server derives the deletion actor from the authenticated Identity
 * session. Keep this request bodyless so the browser cannot nominate an
 * arbitrary actor name.
 */
export function deleteBackupCatalogEntry(catalogEntryId: string) {
  return controlPlaneDelete<BackupCatalogPermanentDeleteResponse>(
    `${CATALOG_BASE}/${encodeURIComponent(catalogEntryId)}`,
  )
}

export function getImportedZipArchive(validationId: string) {
  return controlPlaneGet<BackupCatalogImportArchiveResponse>(
    `${CATALOG_BASE}/imports/${encodeURIComponent(validationId)}/archive`,
  )
}

export function deleteImportedZipArchive(
  validationId: string,
  request: BackupCatalogImportArchiveDeleteRequest = {},
) {
  return controlPlaneDelete<
    BackupCatalogImportArchiveDeleteResponse,
    BackupCatalogImportArchiveDeleteRequest
  >(`${CATALOG_BASE}/imports/${encodeURIComponent(validationId)}/archive`, request)
}
