import type { MigrationSessionInventoryRequest } from "@/features/operator/migrations/api/migration-sessions"

export const DEFAULT_PAGE_SIZE = 10
export const PAGE_SIZES = [10, 25, 50] as const

export const MIGRATION_STAGE_CODES = [
  "create-and-upload-package",
  "review-old-server",
  "prepare-and-test",
  "create-new-server",
  "make-new-server-live",
  "finish-migration",
] as const

export type MigrationSessionLifecycle =
  | "all"
  | "active"
  | "completed"
  | "closed"
  | "cancelled"
  | "archived"

export type MigrationSessionActionFilter =
  | "all"
  | "continue"
  | "review"
  | "view"

export type MigrationSessionSortBy = "updated" | "created" | "name" | "stage"

export type MigrationSessionsQueryState = {
  page: number
  pageSize: number
  search: string
  lifecycle: MigrationSessionLifecycle
  action: MigrationSessionActionFilter
  stage: string
  targetStack: string
  sortBy: MigrationSessionSortBy
  sortDirection: "asc" | "desc"
  includeArchived: boolean
}

export type MigrationSessionsUrlUpdater = (
  changes: Record<string, string | null | undefined>,
  resetPage?: boolean,
) => void

export function readMigrationSessionsQuery(
  searchParams: URLSearchParams,
): MigrationSessionsQueryState {
  const lifecycle = readLifecycle(searchParams.get("lifecycle"))
  const includeArchived =
    lifecycle === "archived" || searchParams.get("includeArchived") === "true"

  return {
    page: readPositiveInteger(searchParams.get("page"), 1),
    pageSize: readPageSize(searchParams.get("pageSize")),
    search: searchParams.get("search")?.trim() ?? "",
    lifecycle,
    action: readAction(searchParams.get("action")),
    stage: readStage(searchParams.get("stage")),
    targetStack: searchParams.get("targetStack")?.trim() ?? "",
    sortBy: readSortBy(searchParams.get("sortBy")),
    sortDirection: searchParams.get("sortDirection") === "asc" ? "asc" : "desc",
    includeArchived,
  }
}

export function toMigrationSessionInventoryRequest(
  query: MigrationSessionsQueryState,
): MigrationSessionInventoryRequest {
  return {
    page: query.page,
    pageSize: query.pageSize,
    search: query.search || null,
    lifecycle: query.lifecycle,
    action: query.action,
    stage: query.stage || null,
    targetStack: query.targetStack || null,
    sortBy: query.sortBy,
    sortDirection: query.sortDirection,
    includeArchived: query.includeArchived,
  }
}

export function hasMigrationSessionFilters(query: MigrationSessionsQueryState) {
  return Boolean(
    query.search ||
      query.lifecycle !== "all" ||
      query.action !== "all" ||
      query.stage ||
      query.targetStack ||
      query.includeArchived,
  )
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

function readLifecycle(value: string | null): MigrationSessionLifecycle {
  const values: readonly MigrationSessionLifecycle[] = [
    "all",
    "active",
    "completed",
    "closed",
    "cancelled",
    "archived",
  ]
  return values.includes(value as MigrationSessionLifecycle)
    ? (value as MigrationSessionLifecycle)
    : "all"
}

function readAction(value: string | null): MigrationSessionActionFilter {
  const values: readonly MigrationSessionActionFilter[] = [
    "all",
    "continue",
    "review",
    "view",
  ]
  return values.includes(value as MigrationSessionActionFilter)
    ? (value as MigrationSessionActionFilter)
    : "all"
}

function readStage(value: string | null) {
  return MIGRATION_STAGE_CODES.includes(value as (typeof MIGRATION_STAGE_CODES)[number])
    ? value ?? ""
    : ""
}

function readSortBy(value: string | null): MigrationSessionSortBy {
  const values: readonly MigrationSessionSortBy[] = [
    "updated",
    "created",
    "name",
    "stage",
  ]
  return values.includes(value as MigrationSessionSortBy)
    ? (value as MigrationSessionSortBy)
    : "updated"
}
