import type {
  RuntimeStackListRequest,
  RuntimeStackStatusFilter,
} from "../api/stacks.types"

export const STACKS_DEFAULT_PAGE_SIZE = 10
export const STACKS_PAGE_SIZES = [10, 25, 50] as const

export type RuntimeStackInventorySort =
  | "name-asc"
  | "name-desc"
  | "last-checked-desc"
  | "last-checked-asc"

export type RuntimeStackInventoryQueryState = {
  page: number
  pageSize: (typeof STACKS_PAGE_SIZES)[number]
  search: string
  status: RuntimeStackStatusFilter
  category: string
  sort: RuntimeStackInventorySort
}

export type RuntimeStackInventoryUrlUpdater = (
  changes: Record<string, string | null | undefined>,
  resetPage?: boolean,
) => void

export function readRuntimeStackInventoryQuery(
  searchParams: URLSearchParams,
): RuntimeStackInventoryQueryState {
  return {
    page: readPositiveInteger(searchParams.get("page"), 1),
    pageSize: readPageSize(searchParams.get("pageSize")),
    search: normalizeRuntimeStackInventorySearch(searchParams.get("search") ?? ""),
    status: readStatus(searchParams.get("status")),
    category: normalizeRuntimeStackInventoryCategory(searchParams.get("category") ?? ""),
    sort: readSort(searchParams.get("sort")),
  }
}

export function toRuntimeStackListRequest(
  query: RuntimeStackInventoryQueryState,
): RuntimeStackListRequest {
  const { sortBy, sortDirection } = splitSort(query.sort)

  return {
    page: query.page,
    pageSize: query.pageSize,
    search: query.search || null,
    status: query.status,
    category: query.category || null,
    sortBy,
    sortDirection,
  }
}

export function hasRuntimeStackInventoryFilters(
  query: RuntimeStackInventoryQueryState,
) {
  return Boolean(query.search || query.status !== "all" || query.category)
}

export function normalizeRuntimeStackInventorySearch(value: string) {
  return value.trim().slice(0, 200)
}

export function normalizeRuntimeStackInventoryCategory(value: string) {
  return value.trim().slice(0, 40)
}

function splitSort(sort: RuntimeStackInventorySort): Pick<
  RuntimeStackListRequest,
  "sortBy" | "sortDirection"
> {
  switch (sort) {
    case "name-desc":
      return { sortBy: "name", sortDirection: "desc" }
    case "last-checked-desc":
      return { sortBy: "lastChecked", sortDirection: "desc" }
    case "last-checked-asc":
      return { sortBy: "lastChecked", sortDirection: "asc" }
    default:
      return { sortBy: "name", sortDirection: "asc" }
  }
}

function readPositiveInteger(value: string | null, fallback: number) {
  const parsed = Number.parseInt(value ?? "", 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

function readPageSize(value: string | null): (typeof STACKS_PAGE_SIZES)[number] {
  const parsed = Number.parseInt(value ?? "", 10)
  return STACKS_PAGE_SIZES.includes(parsed as (typeof STACKS_PAGE_SIZES)[number])
    ? (parsed as (typeof STACKS_PAGE_SIZES)[number])
    : STACKS_DEFAULT_PAGE_SIZE
}

function readStatus(value: string | null): RuntimeStackStatusFilter {
  const normalized = value?.trim().toLowerCase().replaceAll("-", "_")
  const allowed: readonly RuntimeStackStatusFilter[] = [
    "all",
    "healthy",
    "needs_attention",
    "offline",
    "setting_up",
    "unknown",
  ]

  return allowed.includes(normalized as RuntimeStackStatusFilter)
    ? (normalized as RuntimeStackStatusFilter)
    : "all"
}

function readSort(value: string | null): RuntimeStackInventorySort {
  const allowed: readonly RuntimeStackInventorySort[] = [
    "name-asc",
    "name-desc",
    "last-checked-desc",
    "last-checked-asc",
  ]

  return allowed.includes(value as RuntimeStackInventorySort)
    ? (value as RuntimeStackInventorySort)
    : "name-asc"
}
