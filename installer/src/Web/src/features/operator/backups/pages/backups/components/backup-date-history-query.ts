import type { LocalBackupEntriesRequest } from "@/features/operator/backups/api"

export const DEFAULT_BACKUP_PAGE_SIZE = 10
export const BACKUP_PAGE_SIZES = [10, 25, 50, 100] as const

export type BackupDateHistorySortBy = "backup" | "stack" | "created" | "size"

export type BackupDateHistoryQueryState = {
  page: number
  pageSize: number
  search: string
  stackSlug: string
  sortBy: BackupDateHistorySortBy
  sortDirection: "asc" | "desc"
}

export type BackupDateHistoryUrlUpdater = (
  changes: Record<string, string | null | undefined>,
  resetPage?: boolean,
) => void

export function readBackupDateHistoryQuery(
  searchParams: URLSearchParams,
): BackupDateHistoryQueryState {
  return {
    page: readPositiveInteger(searchParams.get("page"), 1),
    pageSize: readPageSize(searchParams.get("pageSize")),
    search: searchParams.get("q")?.trim() ?? "",
    stackSlug: searchParams.get("stack")?.trim() ?? "",
    sortBy: readSortBy(searchParams.get("sort")),
    sortDirection: searchParams.get("direction") === "asc" ? "asc" : "desc",
  }
}

export function toLocalBackupEntriesRequest(
  query: BackupDateHistoryQueryState,
): LocalBackupEntriesRequest {
  return {
    page: query.page,
    pageSize: query.pageSize,
    search: query.search || null,
    stackSlug: query.stackSlug || null,
    sortBy: query.sortBy,
    sortDirection: query.sortDirection,
  }
}

function readPositiveInteger(value: string | null, fallback: number) {
  const parsed = Number.parseInt(value ?? "", 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

function readPageSize(value: string | null) {
  const parsed = Number.parseInt(value ?? "", 10)
  return BACKUP_PAGE_SIZES.includes(parsed as (typeof BACKUP_PAGE_SIZES)[number])
    ? parsed
    : DEFAULT_BACKUP_PAGE_SIZE
}

function readSortBy(value: string | null): BackupDateHistorySortBy {
  return ["backup", "stack", "created", "size"].includes(value ?? "")
    ? (value as BackupDateHistorySortBy)
    : "created"
}
