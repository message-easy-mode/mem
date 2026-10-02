import type {
  RuntimeStackBackupDeleteResponse,
  RuntimeStackBackupDetailResponse,
  RuntimeStackBackupEntryListResponse,
  RuntimeStackBackupHistoryResponse,
  RuntimeStackBackupStackHistoryResponse,
} from "../types/backups.types";
import {
  backupsApiRoutes,
  controlPlaneDelete,
  controlPlaneGet,
} from "../transport/host-agent";

const LOCAL_BACKUPS_BASE = backupsApiRoutes.artifacts.localBackups;

export function listBackupHistory() {
  return controlPlaneGet<RuntimeStackBackupHistoryResponse>(LOCAL_BACKUPS_BASE);
}

export type LocalBackupEntriesRequest = {
  page?: number
  pageSize?: number
  search?: string | null
  stackSlug?: string | null
  sortBy?: "backup" | "stack" | "created" | "size" | string | null
  sortDirection?: "asc" | "desc" | null
}

/**
 * Paged flat local-backup inventory for the "By date" operator table.
 * The root catalog endpoint remains intentionally separate for the grouped
 * "By stack" card projection.
 */
export function listBackupEntries(
  request: LocalBackupEntriesRequest = {},
) {
  const query = new URLSearchParams()

  if (request.page && request.page > 0) {
    query.set("page", request.page.toString())
  }

  if (request.pageSize && request.pageSize > 0) {
    query.set("pageSize", request.pageSize.toString())
  }

  if (request.search?.trim()) {
    query.set("search", request.search.trim())
  }

  if (request.stackSlug?.trim()) {
    query.set("stackSlug", request.stackSlug.trim())
  }

  if (request.sortBy?.trim()) {
    query.set("sortBy", request.sortBy.trim())
  }

  if (request.sortDirection) {
    query.set("sortDirection", request.sortDirection)
  }

  const suffix = query.size > 0 ? `?${query.toString()}` : ""

  return controlPlaneGet<RuntimeStackBackupEntryListResponse>(
    `${LOCAL_BACKUPS_BASE}/entries${suffix}`,
  )
}

export function listStackBackupHistory(stackSlug: string) {
  return controlPlaneGet<RuntimeStackBackupStackHistoryResponse>(
    `${LOCAL_BACKUPS_BASE}/stacks/${encodeURIComponent(stackSlug)}`,
  );
}

export function inspectBackup(stackSlug: string, backupId: string) {
  return controlPlaneGet<RuntimeStackBackupDetailResponse>(
    `${LOCAL_BACKUPS_BASE}/stacks/${encodeURIComponent(stackSlug)}/${encodeURIComponent(backupId)}`,
  );
}

/**
 * Permanently removes one local backup artifact. The HostAgent validates its
 * own acknowledgement, safe path, and reparse-point protections before it
 * touches the filesystem.
 */
export function deleteLocalBackup(stackSlug: string, backupId: string) {
  const path = `${LOCAL_BACKUPS_BASE}/stacks/${encodeURIComponent(
    stackSlug,
  )}/${encodeURIComponent(backupId)}?acknowledgeDelete=true`

  return controlPlaneDelete<RuntimeStackBackupDeleteResponse>(path)
}
