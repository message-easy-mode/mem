import type { RestoreAttemptListRequest } from "@/features/operator/backups/api"

export const DEFAULT_PAGE_SIZE = 10
export const PAGE_SIZES = [10, 25, 50, 100] as const

export type RestoreSessionStatus =
  | "all"
  | "in-progress"
  | "ready"
  | "testing"
  | "planning"
  | "recreating"
  | "verifying"
  | "needs-attention"
  | "completed"
  | "failed"
  | "cancelled"
  | "warnings"

export type RestoreSessionSortBy =
  | "updated"
  | "started"
  | "source"
  | "target"
  | "status"
  | "session"

export type RestoreSessionsQueryState = {
  page: number
  pageSize: number
  search: string
  status: RestoreSessionStatus
  targetStack: string
  sortBy: RestoreSessionSortBy
  sortDirection: "asc" | "desc"
}

export type RestoreSessionsUrlUpdater = (
  changes: Record<string, string | null | undefined>,
  resetPage?: boolean,
) => void

export const statusOptions: ReadonlyArray<{
  value: RestoreSessionStatus
  label: string
}> = [
  { value: "all", label: "All lifecycle states" },
  { value: "in-progress", label: "In progress" },
  { value: "ready", label: "Ready to continue" },
  { value: "testing", label: "Private test running" },
  { value: "planning", label: "Planning restore" },
  { value: "recreating", label: "Creating restored server" },
  { value: "verifying", label: "Verifying restored server" },
  { value: "needs-attention", label: "Needs attention" },
  { value: "completed", label: "Completed" },
  { value: "failed", label: "Failed" },
  { value: "cancelled", label: "Cancelled" },
  { value: "warnings", label: "Has warnings" },
]

export const sortOptions: ReadonlyArray<{
  value: RestoreSessionSortBy
  label: string
}> = [
  { value: "updated", label: "Updated" },
  { value: "started", label: "Started" },
  { value: "source", label: "Backup source" },
  { value: "target", label: "Target stack" },
  { value: "status", label: "Status" },
  { value: "session", label: "Session ID" },
]

export function readRestoreSessionsQuery(
  searchParams: URLSearchParams,
): RestoreSessionsQueryState {
  return {
    page: readPositiveInteger(searchParams.get("page"), 1),
    pageSize: readPageSize(searchParams.get("pageSize")),
    search: searchParams.get("q")?.trim() ?? "",
    status: readStatus(searchParams.get("status")),
    targetStack: searchParams.get("target")?.trim() ?? "",
    sortBy: readSortBy(searchParams.get("sort")),
    sortDirection: searchParams.get("direction") === "asc" ? "asc" : "desc",
  }
}

export function toRestoreAttemptListRequest(
  query: RestoreSessionsQueryState,
): RestoreAttemptListRequest {
  return {
    page: query.page,
    pageSize: query.pageSize,
    search: query.search || null,
    status: query.status === "all" ? null : query.status,
    targetStack: query.targetStack || null,
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
  return PAGE_SIZES.includes(parsed as (typeof PAGE_SIZES)[number])
    ? parsed
    : DEFAULT_PAGE_SIZE
}

function readStatus(value: string | null): RestoreSessionStatus {
  return statusOptions.some((option) => option.value === value)
    ? (value as RestoreSessionStatus)
    : "all"
}

function readSortBy(value: string | null): RestoreSessionSortBy {
  return sortOptions.some((option) => option.value === value)
    ? (value as RestoreSessionSortBy)
    : "updated"
}
