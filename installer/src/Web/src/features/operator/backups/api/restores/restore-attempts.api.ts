import type { RestoreAttemptListResponse } from "../types/backups.types"
import { backupsApiRoutes, controlPlaneGet } from "../transport/host-agent"

/**
 * Browser query for the durable catalog-backed Restore Attempt inventory.
 * Page numbering remains one-based to match the URL and HostAgent contract.
 */
export type RestoreAttemptListRequest = {
  page?: number
  pageSize?: number
  search?: string | null
  status?: string | null
  targetStack?: string | null
  sortBy?: string | null
  sortDirection?: "asc" | "desc" | null
}

export function listRestoreAttempts(
  request: RestoreAttemptListRequest = {},
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

  if (request.status?.trim() && request.status !== "all") {
    query.set("status", request.status.trim())
  }

  if (request.targetStack?.trim()) {
    query.set("targetStack", request.targetStack.trim())
  }

  if (request.sortBy?.trim()) {
    query.set("sortBy", request.sortBy.trim())
  }

  if (request.sortDirection) {
    query.set("sortDirection", request.sortDirection)
  }

  const suffix = query.size > 0 ? `?${query.toString()}` : ""

  return controlPlaneGet<RestoreAttemptListResponse>(
    `${backupsApiRoutes.restores}${suffix}`,
  )
}
