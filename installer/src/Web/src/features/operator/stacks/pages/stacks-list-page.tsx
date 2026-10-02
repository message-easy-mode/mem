import { useCallback, useEffect, useMemo, useRef, useState, type RefObject } from "react"
import { Link, useSearchParams } from "react-router-dom"
import {
  ChevronLeft,
  ChevronRight,
  ExternalLink,
  FilterX,
  MessageSquarePlus,
  RefreshCw,
  Search,
} from "lucide-react"

import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Select } from "@/components/ui/select"
import { cn } from "@/lib/utils"
import { useRuntimeStacks } from "../hooks/use-runtime-stacks"
import { StackLogo } from "../components/stack-logo"
import { StackDestroyControl } from "../components/stack-destroy-control"
import { StackStatusPill, stackStatusLabel } from "../components/stack-status-pill"
import type {
  RuntimeStackHealth,
  RuntimeStackInventoryCategoryFacet,
  RuntimeStackInventorySummary,
  RuntimeStackStatusFilter,
  RuntimeStackSummaryResponse,
} from "../api/stacks.types"
import {
  STACKS_DEFAULT_PAGE_SIZE,
  STACKS_PAGE_SIZES,
  hasRuntimeStackInventoryFilters,
  normalizeRuntimeStackInventorySearch,
  readRuntimeStackInventoryQuery,
  toRuntimeStackListRequest,
  type RuntimeStackInventorySort,
  type RuntimeStackInventoryUrlUpdater,
} from "../lib/stack-inventory-query"

export function StacksListPage() {
  const { language, t } = useI18n()
  const [searchParams, setSearchParams] = useSearchParams()
  const inventoryQuery = readRuntimeStackInventoryQuery(searchParams)
  const [searchInput, setSearchInput] = useState(() => inventoryQuery.search)
  const searchInputRef = useRef<HTMLInputElement | null>(null)

  const updateUrl = useCallback<RuntimeStackInventoryUrlUpdater>(
    (changes, resetPage = true) => {
      setSearchParams((current) => {
        const next = new URLSearchParams(current)

        for (const [key, value] of Object.entries(changes)) {
          if (value === undefined) continue
          if (value === null || value === "") next.delete(key)
          else next.set(key, value)
        }

        if (resetPage && !Object.hasOwn(changes, "page")) next.delete("page")
        if (next.get("page") === "1") next.delete("page")
        if (next.get("pageSize") === String(STACKS_DEFAULT_PAGE_SIZE)) next.delete("pageSize")
        if (next.get("status") === "all") next.delete("status")
        if (next.get("sort") === "name-asc") next.delete("sort")

        return next
      })
    },
    [setSearchParams],
  )

  useEffect(() => {
    setSearchInput(inventoryQuery.search)
  }, [inventoryQuery.search])

  useEffect(() => {
    const timer = window.setTimeout(() => {
      const nextSearch = normalizeRuntimeStackInventorySearch(searchInput)
      if (nextSearch !== inventoryQuery.search) {
        updateUrl({ search: nextSearch || null })
      }
    }, 300)

    return () => window.clearTimeout(timer)
  }, [inventoryQuery.search, searchInput, updateUrl])

  useEffect(() => {
    const handleShortcut = (event: KeyboardEvent) => {
      if (event.key !== "/" || event.metaKey || event.ctrlKey || event.altKey) return
      const target = event.target as HTMLElement | null
      if (target?.closest("input, textarea, select, [contenteditable='true']")) return
      event.preventDefault()
      searchInputRef.current?.focus()
    }

    window.addEventListener("keydown", handleShortcut)
    return () => window.removeEventListener("keydown", handleShortcut)
  }, [])

  const inventoryRequest = useMemo(
    () => toRuntimeStackListRequest(inventoryQuery),
    [
      inventoryQuery.page,
      inventoryQuery.pageSize,
      inventoryQuery.search,
      inventoryQuery.sort,
      inventoryQuery.status,
      inventoryQuery.category,
    ],
  )
  const stacksQuery = useRuntimeStacks(inventoryRequest)
  const data = stacksQuery.data

  useEffect(() => {
    if (
      data?.isPaged &&
      data.page !== undefined &&
      !stacksQuery.isPlaceholderData &&
      data.page !== inventoryQuery.page
    ) {
      updateUrl({ page: String(data.page) }, false)
    }
  }, [data, inventoryQuery.page, stacksQuery.isPlaceholderData, updateUrl])

  const clearInventoryControls = useCallback(() => {
    setSearchInput("")
    updateUrl({ search: null, status: null, category: null, sort: null })
  }, [updateUrl])

  const totalInventoryStacks = data?.summary?.totalStacks ?? data?.stacks.length ?? 0
  const hasInventoryFilters = hasRuntimeStackInventoryFilters(inventoryQuery)
  const hasInventoryControlChanges = hasInventoryFilters || inventoryQuery.sort !== "name-asc"
  const categoryFacets = resolveCategoryFacets(
    data?.categories,
    data?.stacks ?? [],
    inventoryQuery.category,
  )

  return (
    <div className="space-y-6">
      <PageHeader
        onRefresh={() => void stacksQuery.refetch()}
        refreshing={stacksQuery.isFetching}
      />

      <StackDestroyControl />

      {stacksQuery.error && (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.list.loadErrorTitle")}</AlertTitle>
          <AlertDescription>{stacksQuery.error.message}</AlertDescription>
        </Alert>
      )}

      {stacksQuery.isLoading && !data ? (
        <div className="rounded-xl border border-border bg-background/40 p-4 text-sm text-muted-foreground">
          {t("stacks.list.loading")}
        </div>
      ) : !data || totalInventoryStacks === 0 ? (
        <EmptyState />
      ) : (
        <div className="space-y-4">
          <InventoryToolbar
            searchInput={searchInput}
            searchInputRef={searchInputRef}
            status={inventoryQuery.status}
            category={inventoryQuery.category}
            categories={categoryFacets}
            sort={inventoryQuery.sort}
            hasControlChanges={hasInventoryControlChanges}
            onSearchInputChange={setSearchInput}
            onStatusChange={(status) => updateUrl({ status: status === "all" ? null : status })}
            onCategoryChange={(category) => updateUrl({ category: category || null })}
            onSortChange={(sort) => updateUrl({ sort: sort === "name-asc" ? null : sort })}
            onClear={clearInventoryControls}
          />

          <InventorySummary stacks={data.stacks} summary={data.summary} />

          {data.stacks.length === 0 ? (
            <FilteredEmptyState onClear={clearInventoryControls} />
          ) : (
            <div
              className={stacksQuery.isFetching ? "opacity-70 transition-opacity" : "transition-opacity"}
              aria-busy={stacksQuery.isFetching}
            >
              <div className="overflow-hidden rounded-2xl border border-border bg-card/60 ring-1 ring-foreground/5">
                <div className="overflow-x-auto">
                  <div className="hidden min-w-[1020px] border-b border-border bg-muted/20 px-4 py-3 text-xs font-medium uppercase tracking-wide text-muted-foreground min-[1400px]:grid min-[1400px]:grid-cols-[minmax(210px,1.2fr)_minmax(150px,.9fr)_minmax(150px,.9fr)_minmax(125px,.7fr)_minmax(120px,.7fr)_minmax(100px,auto)] min-[1400px]:gap-4">
                    <span>{t("stacks.list.columns.stack")}</span>
                    <span>{t("stacks.list.columns.matrixHost")}</span>
                    <span>{t("stacks.list.columns.elementHost")}</span>
                    <span>{t("stacks.list.columns.status")}</span>
                    <span>{t("stacks.list.columns.lastChecked")}</span>
                    <span>{t("stacks.list.columns.actions")}</span>
                  </div>

                  <div className="divide-y divide-border">
                    {data.stacks.map((stack) => {
                      const displayName = stackDisplayName(stack)
                      const hasFriendlyName = displayName !== stack.slug
                      const health = stack.health ?? classifyStackHealth(stack.lastVerifiedStatus)
                      const healthLabel = healthLabelKey(health)
                      const matrixHost = formatHost(stack.matrixPublicBaseUrl, t("common.unknown"))
                      const elementHost = formatHost(stack.elementPublicBaseUrl, t("common.unknown"))

                      return (
                        <div
                          key={stack.stackId}
                          className="px-4 py-4 transition-colors hover:bg-muted/10"
                        >
                          <div className="grid gap-4 md:grid-cols-2 md:gap-x-6 md:gap-y-5 min-[1400px]:min-w-[1020px] min-[1400px]:grid-cols-[minmax(210px,1.2fr)_minmax(150px,.9fr)_minmax(150px,.9fr)_minmax(125px,.7fr)_minmax(120px,.7fr)_minmax(100px,auto)] min-[1400px]:items-center min-[1400px]:gap-4">
                            <div className="flex min-w-0 items-center gap-3">
                              <Link
                                to={`/stacks/${encodeURIComponent(stack.slug)}`}
                                aria-label={t("stacks.list.manageNamed", { slug: displayName })}
                                className="shrink-0 rounded-xl transition-colors hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                              >
                                <StackLogo
                                  displayName={displayName}
                                  logoUrl={stack.logoUrl}
                                  className="h-12 w-12 rounded-xl text-sm transition-colors hover:border-primary/40"
                                />
                              </Link>

                              <div className="min-w-0">
                                <Link
                                  to={`/stacks/${encodeURIComponent(stack.slug)}`}
                                  className="block truncate font-semibold tracking-tight hover:underline"
                                >
                                  {displayName}
                                </Link>
                                <div className="mt-1 flex min-w-0 flex-wrap items-center gap-2 text-xs text-muted-foreground">
                                  <span className="truncate">
                                    {hasFriendlyName ? stack.slug : t("stacks.list.managedStack")}
                                  </span>
                                  {stack.category ? (
                                    <span className="inline-flex shrink-0 items-center rounded-full border border-primary/25 bg-primary/10 px-2 py-0.5 text-[11px] font-medium text-primary">
                                      {stack.category}
                                    </span>
                                  ) : null}
                                </div>
                              </div>
                            </div>

                            <InventoryField
                              label={t("stacks.list.columns.matrixHost")}
                              value={matrixHost}
                              href={safePublicUrl(stack.matrixPublicBaseUrl)}
                              className="md:col-start-1 md:row-start-2 min-[1400px]:col-start-2 min-[1400px]:row-start-1"
                            />
                            <InventoryField
                              label={t("stacks.list.columns.elementHost")}
                              value={elementHost}
                              href={safePublicUrl(stack.elementPublicBaseUrl)}
                              className="md:col-start-2 md:row-start-2 min-[1400px]:col-start-3 min-[1400px]:row-start-1"
                            />

                            <div className="min-w-0 md:col-start-1 md:row-start-3 min-[1400px]:col-start-4 min-[1400px]:row-start-1">
                              <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground min-[1400px]:hidden">
                                {t("stacks.list.columns.status")}
                              </div>
                              <div className="mt-1 min-[1400px]:mt-0">
                                <StackStatusPill status={healthLabel} />
                              </div>
                              <div className="mt-1 truncate text-xs text-muted-foreground" title={stack.lastVerifiedStatus}>
                                {stack.verificationFreshness === "stale"
                                  ? t("stacks.list.verificationStale")
                                  : stackStatusLabel(stack.lastVerifiedStatus, t)}
                              </div>
                            </div>

                            <InventoryField
                              label={t("stacks.list.columns.lastChecked")}
                              value={formatStackDate(
                                stack.lastVerifiedAtUtc,
                                language,
                                t("common.unknown"),
                              )}
                              className="md:col-start-2 md:row-start-3 min-[1400px]:col-start-5 min-[1400px]:row-start-1"
                            />

                            <div className="flex min-w-0 flex-wrap items-center gap-2 md:col-start-2 md:row-start-1 md:justify-end min-[1400px]:col-start-6 min-[1400px]:row-start-1">
                              <Button size="sm" asChild>
                                <Link to={`/stacks/${encodeURIComponent(stack.slug)}`}>
                                  {t("stacks.list.manage")}
                                </Link>
                              </Button>
                            </div>
                          </div>
                        </div>
                      )
                    })}
                  </div>
                </div>

                <InventoryPagination
                  page={data.page ?? inventoryQuery.page}
                  pageSize={(data.pageSize ?? inventoryQuery.pageSize) as (typeof STACKS_PAGE_SIZES)[number]}
                  totalPages={data.totalPages ?? 1}
                  totalMatchingStacks={data.totalMatchingStacks ?? data.stacks.length}
                  hasPreviousPage={data.hasPreviousPage ?? false}
                  hasNextPage={data.hasNextPage ?? false}
                  disabled={stacksQuery.isFetching}
                  updateUrl={updateUrl}
                />
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  )
}

function PageHeader({
  onRefresh,
  refreshing,
}: {
  onRefresh: () => void
  refreshing: boolean
}) {
  const { t } = useI18n()

  return (
    <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">{t("stacks.list.title")}</h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
          {t("stacks.list.description")}
        </p>
      </div>

      <div className="flex flex-wrap gap-2">
        <Button variant="outline" size="sm" onClick={onRefresh} disabled={refreshing}>
          <RefreshCw className="mr-2 h-4 w-4" />
          {t("stacks.list.refresh")}
        </Button>

        <Button size="sm" asChild>
          <Link to="/stacks/new">
            <MessageSquarePlus className="mr-2 h-4 w-4" />
            {t("stacks.list.create")}
          </Link>
        </Button>
      </div>
    </div>
  )
}

function EmptyState() {
  const { t } = useI18n()

  return (
    <div className="rounded-2xl border border-dashed border-border bg-background/40 p-8 text-center">
      <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-primary/10 text-primary">
        <MessageSquarePlus className="h-6 w-6" />
      </div>
      <h2 className="mt-4 text-lg font-semibold">{t("stacks.list.emptyTitle")}</h2>
      <p className="mx-auto mt-2 max-w-xl text-sm text-muted-foreground">
        {t("stacks.list.emptyDescription")}
      </p>
      <div className="mt-5">
        <Button asChild>
          <Link to="/stacks/new">{t("stacks.list.createFirst")}</Link>
        </Button>
      </div>
    </div>
  )
}

function InventoryToolbar({
  searchInput,
  searchInputRef,
  status,
  category,
  categories,
  sort,
  hasControlChanges,
  onSearchInputChange,
  onStatusChange,
  onCategoryChange,
  onSortChange,
  onClear,
}: {
  searchInput: string
  searchInputRef: RefObject<HTMLInputElement | null>
  status: RuntimeStackStatusFilter
  category: string
  categories: RuntimeStackInventoryCategoryFacet[]
  sort: RuntimeStackInventorySort
  hasControlChanges: boolean
  onSearchInputChange: (value: string) => void
  onStatusChange: (value: RuntimeStackStatusFilter) => void
  onCategoryChange: (value: string) => void
  onSortChange: (value: RuntimeStackInventorySort) => void
  onClear: () => void
}) {
  const { t } = useI18n()

  return (
    <div className="rounded-2xl border border-border bg-card/40 p-3 ring-1 ring-foreground/5">
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-[minmax(11rem,.7fr)_minmax(11rem,.7fr)_minmax(15rem,1fr)_auto] min-[1650px]:grid-cols-[minmax(22rem,1fr)_minmax(11rem,.3fr)_minmax(11rem,.3fr)_minmax(15rem,.45fr)_auto] min-[1650px]:items-center">
        <label className="relative min-w-0 md:col-span-2 xl:col-span-4 min-[1650px]:col-span-1">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
          <Input
            ref={searchInputRef}
            value={searchInput}
            maxLength={200}
            onChange={(event) => onSearchInputChange(event.target.value)}
            placeholder={t("stacks.list.filters.searchPlaceholder")}
            aria-label={t("stacks.list.filters.searchAria")}
            className="h-10 pr-10 pl-9"
          />
          <kbd className="pointer-events-none absolute top-1/2 right-3 hidden -translate-y-1/2 rounded border border-border bg-muted/40 px-1.5 py-0.5 text-[10px] text-muted-foreground sm:inline">
            /
          </kbd>
        </label>

        <Select
          value={status}
          onChange={(event) => onStatusChange(event.target.value as RuntimeStackStatusFilter)}
          aria-label={t("stacks.list.filters.statusAria")}
          wrapperClassName="w-full"
          className="h-10 w-full"
        >
          <option value="all">{t("stacks.list.filters.statusAll")}</option>
          <option value="healthy">{t("stacks.list.filters.statusHealthy")}</option>
          <option value="needs_attention">{t("stacks.list.filters.statusNeedsAttention")}</option>
          <option value="offline">{t("stacks.list.filters.statusOffline")}</option>
          <option value="setting_up">{t("stacks.list.filters.statusSettingUp")}</option>
          <option value="unknown">{t("stacks.list.filters.statusUnknown")}</option>
        </Select>

        <Select
          value={category}
          onChange={(event) => onCategoryChange(event.target.value)}
          aria-label={t("stacks.list.filters.categoryAria")}
          wrapperClassName="w-full"
          className="h-10 w-full"
          disabled={categories.length === 0 && !category}
        >
          <option value="">{t("stacks.list.filters.categoryAll")}</option>
          {categories.map((facet) => (
            <option key={facet.category} value={facet.category}>
              {facet.category} ({facet.count})
            </option>
          ))}
        </Select>

        <Select
          value={sort}
          onChange={(event) => onSortChange(event.target.value as RuntimeStackInventorySort)}
          aria-label={t("stacks.list.filters.sortAria")}
          wrapperClassName="w-full"
          className="h-10 w-full"
        >
          <option value="name-asc">{t("stacks.list.filters.sortNameAsc")}</option>
          <option value="name-desc">{t("stacks.list.filters.sortNameDesc")}</option>
          <option value="last-checked-desc">{t("stacks.list.filters.sortLastCheckedDesc")}</option>
          <option value="last-checked-asc">{t("stacks.list.filters.sortLastCheckedAsc")}</option>
        </Select>

        <Button
          type="button"
          variant="outline"
          className="h-10 md:col-span-1 xl:col-span-1 min-[1650px]:col-span-1"
          disabled={!hasControlChanges}
          onClick={onClear}
        >
          <FilterX className="mr-2 size-4" aria-hidden="true" />
          {t("stacks.list.filters.clear")}
        </Button>
      </div>
    </div>
  )
}

function FilteredEmptyState({ onClear }: { onClear: () => void }) {
  const { t } = useI18n()

  return (
    <div className="rounded-2xl border border-dashed border-border bg-background/40 p-8 text-center">
      <Search className="mx-auto h-8 w-8 text-muted-foreground" aria-hidden="true" />
      <h2 className="mt-3 text-lg font-semibold">{t("stacks.list.filteredEmptyTitle")}</h2>
      <p className="mx-auto mt-2 max-w-xl text-sm text-muted-foreground">
        {t("stacks.list.filteredEmptyDescription")}
      </p>
      <Button className="mt-5" variant="outline" onClick={onClear}>
        {t("stacks.list.filters.clear")}
      </Button>
    </div>
  )
}

function InventorySummary({
  stacks,
  summary,
}: {
  stacks: RuntimeStackSummaryResponse[]
  summary?: RuntimeStackInventorySummary
}) {
  const { t } = useI18n()
  const fallback = stacks.reduce(
    (result, stack) => {
      const health = stack.health ?? classifyStackHealth(stack.lastVerifiedStatus)
      result[health] += 1
      return result
    },
    {
      healthy: 0,
      needs_attention: 0,
      offline: 0,
      setting_up: 0,
      unknown: 0,
    } satisfies Record<RuntimeStackHealth, number>,
  )
  const counts = summary ?? {
    totalStacks: stacks.length,
    healthy: fallback.healthy,
    needsAttention: fallback.needs_attention,
    offline: fallback.offline,
    settingUp: fallback.setting_up,
    unknown: fallback.unknown,
  }

  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-sm text-muted-foreground">
      <span className="font-medium text-foreground">
        {t("stacks.list.summary.total", { count: counts.totalStacks })}
      </span>
      <span aria-hidden="true" className="hidden h-4 w-px bg-border sm:block" />
      <span>{t("stacks.list.summary.healthy", { count: counts.healthy })}</span>
      <span>{t("stacks.list.summary.needsAttention", { count: counts.needsAttention })}</span>
      <span>{t("stacks.list.summary.offline", { count: counts.offline })}</span>
      {(counts.settingUp > 0 || counts.unknown > 0) && (
        <span>
          {t("stacks.list.summary.other", {
            count: counts.settingUp + counts.unknown,
          })}
        </span>
      )}
    </div>
  )
}

function InventoryPagination({
  page,
  pageSize,
  totalPages,
  totalMatchingStacks,
  hasPreviousPage,
  hasNextPage,
  disabled,
  updateUrl,
}: {
  page: number
  pageSize: (typeof STACKS_PAGE_SIZES)[number]
  totalPages: number
  totalMatchingStacks: number
  hasPreviousPage: boolean
  hasNextPage: boolean
  disabled: boolean
  updateUrl: RuntimeStackInventoryUrlUpdater
}) {
  const { t } = useI18n()
  const safeTotalPages = Math.max(1, totalPages)
  const start = totalMatchingStacks === 0 ? 0 : (page - 1) * pageSize + 1
  const end = Math.min(page * pageSize, totalMatchingStacks)

  return (
    <div className="flex flex-col gap-4 border-t border-border px-4 py-4 text-sm text-muted-foreground lg:flex-row lg:items-center lg:justify-between">
      <p>{t("stacks.list.pagination.showing", { start, end, count: totalMatchingStacks })}</p>
      <div className="flex flex-wrap items-center gap-3 lg:justify-end">
        <label className="flex items-center gap-2">
          <span>{t("stacks.list.pagination.rowsPerPage")}</span>
          <Select
            value={String(pageSize)}
            disabled={disabled}
            onChange={(event) => updateUrl({ page: null, pageSize: event.target.value }, false)}
            aria-label={t("stacks.list.pagination.rowsPerPage")}
            className="h-9 w-20"
          >
            {STACKS_PAGE_SIZES.map((size) => (
              <option key={size} value={size}>{size}</option>
            ))}
          </Select>
        </label>

        <nav
          className="flex items-center gap-2"
          aria-label={t("stacks.list.pagination.pageOf", { page, count: safeTotalPages })}
        >
          <Button
            type="button"
            variant="outline"
            size="icon-sm"
            disabled={disabled || !hasPreviousPage}
            onClick={() => updateUrl({ page: String(page - 1) }, false)}
            aria-label={t("dataTable.previous")}
          >
            <ChevronLeft aria-hidden="true" />
          </Button>
          <Button type="button" size="icon-sm" aria-current="page" disabled>
            {page}
          </Button>
          <Button
            type="button"
            variant="outline"
            size="icon-sm"
            disabled={disabled || !hasNextPage}
            onClick={() => updateUrl({ page: String(page + 1) }, false)}
            aria-label={t("dataTable.next")}
          >
            <ChevronRight aria-hidden="true" />
          </Button>
        </nav>
      </div>
    </div>
  )
}

function InventoryField({
  label,
  value,
  href,
  className,
}: {
  label: string
  value: string
  href?: string | null
  className?: string
}) {
  return (
    <div className={cn("min-w-0", className)}>
      <div className="min-[1400px]:hidden text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      {href ? (
        <a
          href={href}
          target="_blank"
          rel="noopener noreferrer"
          className="mt-1 inline-flex max-w-full items-center gap-1.5 text-sm text-foreground underline decoration-border underline-offset-4 transition-colors hover:text-primary hover:decoration-primary min-[1400px]:mt-0"
          title={value}
          aria-label={`${label}: ${value}`}
        >
          <span className="truncate">{value}</span>
          <ExternalLink className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
        </a>
      ) : (
        <div className="mt-1 truncate text-sm text-foreground min-[1400px]:mt-0" title={value}>
          {value}
        </div>
      )}
    </div>
  )
}

function classifyStackHealth(status: string | null | undefined): RuntimeStackHealth {
  const normalized = (status ?? "")
    .trim()
    .toLowerCase()
    .replaceAll("_", " ")
    .replaceAll("-", " ")
    .replace(/\s+/g, " ")

  if (!normalized) return "unknown"
  if (normalized.includes("offline") || normalized.includes("unreachable")) return "offline"
  if (
    normalized.includes("failed") ||
    normalized.includes("error") ||
    normalized.includes("degraded")
  ) {
    return "needs_attention"
  }
  if (
    normalized === "created" ||
    normalized === "started" ||
    normalized === "running" ||
    normalized === "in progress" ||
    normalized.includes("starting")
  ) {
    return "setting_up"
  }
  if (
    normalized === "ready" ||
    normalized === "passed" ||
    normalized === "completed" ||
    normalized === "verified" ||
    normalized.includes("verified")
  ) {
    return "healthy"
  }

  return "unknown"
}

function healthLabelKey(health: RuntimeStackHealth): string {
  switch (health) {
    case "healthy":
      return "Healthy"
    case "needs_attention":
      return "Needs attention"
    case "offline":
      return "Offline"
    case "setting_up":
      return "Setting up"
    default:
      return "Unknown"
  }
}

function safePublicUrl(value: string | null | undefined) {
  const candidate = value?.trim()
  if (!candidate) return null

  try {
    const parsed = new URL(candidate)
    return parsed.protocol === "https:" || parsed.protocol === "http:" ? candidate : null
  } catch {
    return null
  }
}

function formatHost(value: string | null, fallback: string) {
  if (!value) return fallback

  try {
    return new URL(value).host || fallback
  } catch {
    return value
  }
}

function stackDisplayName(stack: RuntimeStackSummaryResponse) {
  const candidate = stack.displayName?.trim()
  return candidate || stack.slug
}


function resolveCategoryFacets(
  serverFacets: RuntimeStackInventoryCategoryFacet[] | undefined,
  stacks: RuntimeStackSummaryResponse[],
  selectedCategory: string,
): RuntimeStackInventoryCategoryFacet[] {
  const source = serverFacets ?? Array.from(
    stacks.reduce((counts, stack) => {
      const category = stack.category?.trim()
      if (!category) return counts
      const key = category.toLocaleLowerCase()
      const existing = counts.get(key)
      counts.set(key, {
        category: existing?.category ?? category,
        count: (existing?.count ?? 0) + 1,
      })
      return counts
    }, new Map<string, RuntimeStackInventoryCategoryFacet>()).values(),
  )

  const facets = [...source]
  if (
    selectedCategory &&
    !facets.some(
      (facet) => facet.category.toLocaleLowerCase() === selectedCategory.toLocaleLowerCase(),
    )
  ) {
    facets.push({ category: selectedCategory, count: 0 })
  }

  return facets.sort((left, right) =>
    left.category.localeCompare(right.category, undefined, { sensitivity: "base" }),
  )
}

function formatStackDate(value: string | null, language: "en" | "de", fallback: string) {
  return value ? formatDateTime(value, language) : fallback
}
