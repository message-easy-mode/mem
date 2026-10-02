import { useEffect, useId, useState } from "react"
import { ArrowDownAZ, ArrowUpAZ, FilterX, Search, SlidersHorizontal } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import { Select } from "@/components/ui/select"
import { stageTitleKey } from "@/features/operator/migrations/components/migration-workspace-ui"

import {
  MIGRATION_STAGE_CODES,
  type MigrationSessionActionFilter,
  type MigrationSessionLifecycle,
  type MigrationSessionSortBy,
} from "./migration-sessions-query"

type MigrationSessionsToolbarProps = {
  searchInput: string
  lifecycle: MigrationSessionLifecycle
  action: MigrationSessionActionFilter
  stage: string
  targetStack: string
  targetStacks: string[]
  sortBy: MigrationSessionSortBy
  sortDirection: "asc" | "desc"
  includeArchived: boolean
  hasFilters: boolean
  onSearchInputChange: (value: string) => void
  onLifecycleChange: (value: MigrationSessionLifecycle) => void
  onQuickFilterChange: (
    lifecycle: MigrationSessionLifecycle,
    action: MigrationSessionActionFilter,
    includeArchived: boolean,
  ) => void
  onStageChange: (value: string) => void
  onTargetStackChange: (value: string) => void
  onSortByChange: (value: MigrationSessionSortBy) => void
  onSortDirectionToggle: () => void
  onIncludeArchivedChange: (value: boolean) => void
  onClearFilters: () => void
}

const lifecycleOptions: readonly MigrationSessionLifecycle[] = [
  "all",
  "active",
  "completed",
  "closed",
  "cancelled",
  "archived",
]

const sortOptions: readonly MigrationSessionSortBy[] = [
  "updated",
  "created",
  "name",
  "stage",
]

const toolbarSelectClassName =
  "h-10 w-full focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500/30 focus-visible:border-emerald-500 focus-visible:ring-2 focus-visible:ring-emerald-500/30"

export function MigrationSessionsToolbar({
  searchInput,
  lifecycle,
  action,
  stage,
  targetStack,
  targetStacks,
  sortBy,
  sortDirection,
  includeArchived,
  hasFilters,
  onSearchInputChange,
  onLifecycleChange,
  onQuickFilterChange,
  onStageChange,
  onTargetStackChange,
  onSortByChange,
  onSortDirectionToggle,
  onIncludeArchivedChange,
  onClearFilters,
}: MigrationSessionsToolbarProps) {
  const { t } = useI18n()
  const filtersId = useId()
  const hasDetailedSelection = Boolean(
    stage || targetStack || lifecycle === "cancelled" ||
    (includeArchived && lifecycle !== "archived") || sortBy !== "updated" || sortDirection !== "desc",
  )
  const [filtersOpen, setFiltersOpen] = useState(hasDetailedSelection)
  const detailCount =
    Number(Boolean(stage)) + Number(Boolean(targetStack)) +
    Number(lifecycle !== "all") + Number(includeArchived) +
    Number(sortBy !== "updated" || sortDirection !== "desc")

  // A deep link/back navigation must not conceal a non-default detailed filter.
  // Toggling the panel itself never changes the URL or the inventory request.
  useEffect(() => {
    if (hasDetailedSelection) setFiltersOpen(true)
  }, [hasDetailedSelection, stage, targetStack, lifecycle, includeArchived, sortBy, sortDirection])

  const sortDirectionLabel = sortDirection === "desc"
    ? t("migrationWorkspace.inventory.toolbar.sortDescending")
    : t("migrationWorkspace.inventory.toolbar.sortAscending")

  const quickFilters: ReadonlyArray<{
    key: string
    lifecycle: MigrationSessionLifecycle
    action: MigrationSessionActionFilter
    includeArchived: boolean
    label: string
  }> = [
    { key: "all", lifecycle: "all", action: "all", includeArchived: false, label: t("migrationWorkspace.inventory.quick.all") },
    { key: "active", lifecycle: "active", action: "all", includeArchived: false, label: t("migrationWorkspace.inventory.quick.active") },
    { key: "ready", lifecycle: "active", action: "continue", includeArchived: false, label: t("migrationWorkspace.inventory.quick.ready") },
    { key: "attention", lifecycle: "all", action: "review", includeArchived: false, label: t("migrationWorkspace.inventory.quick.attention") },
    { key: "completed", lifecycle: "completed", action: "all", includeArchived: false, label: t("migrationWorkspace.inventory.quick.completed") },
    { key: "closed", lifecycle: "closed", action: "all", includeArchived: false, label: t("migrationWorkspace.inventory.quick.closed") },
    { key: "archived", lifecycle: "archived", action: "all", includeArchived: true, label: t("migrationWorkspace.inventory.quick.archived") },
  ]

  return (
    <div className="space-y-3">
      <div className="flex min-w-0 flex-wrap items-center gap-2">
        <label className="relative min-w-0 flex-1 basis-52">
          <Search aria-hidden="true" className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={searchInput}
            onChange={(event) => onSearchInputChange(event.target.value)}
            placeholder={t("migrationWorkspace.inventory.toolbar.searchPlaceholder")}
            className="h-10 pl-9"
            aria-label={t("migrationWorkspace.inventory.toolbar.searchAria")}
          />
        </label>
        <Button
          type="button"
          variant="outline"
          className="h-10"
          aria-expanded={filtersOpen}
          aria-controls={filtersId}
          onClick={() => setFiltersOpen((open) => !open)}
        >
          <SlidersHorizontal className="mr-2 size-4" aria-hidden="true" />
          {t("migrationWorkspace.inventory.toolbar.filters")}
          {detailCount > 0 ? <span className="ml-1 rounded bg-muted px-1.5 text-xs">{detailCount}</span> : null}
        </Button>
        {hasFilters ? (
          <Button type="button" variant="ghost" size="sm" onClick={onClearFilters}>
            <FilterX className="mr-2 size-4" aria-hidden="true" />
            {t("migrationWorkspace.inventory.toolbar.clear")}
          </Button>
        ) : null}
      </div>

      <div
        className="flex flex-wrap items-center gap-2"
        role="group"
        aria-label={t("migrationWorkspace.inventory.toolbar.quickFiltersAria")}
      >
        {quickFilters.map((filter) => {
          const selected =
            lifecycle === filter.lifecycle &&
            action === filter.action &&
            includeArchived === filter.includeArchived
          return (
            <Button
              key={filter.key}
              type="button"
              size="sm"
              variant={selected ? "default" : "outline"}
              aria-pressed={selected}
              onClick={() => onQuickFilterChange(filter.lifecycle, filter.action, filter.includeArchived)}
            >
              {filter.label}
            </Button>
          )
        })}
      </div>

      <div id={filtersId} hidden={!filtersOpen} className="space-y-3 rounded-xl border bg-muted/15 p-3">
        <div className="grid min-w-0 gap-3 sm:grid-cols-2">
          <Select
            value={lifecycle}
            onChange={(event) => onLifecycleChange(event.target.value as MigrationSessionLifecycle)}
            aria-label={t("migrationWorkspace.inventory.toolbar.lifecycleAria")}
            wrapperClassName="min-w-0 w-full"
            className={toolbarSelectClassName}
          >
            {lifecycleOptions.map((value) => (
              <option key={value} value={value}>{lifecycleLabel(value, t)}</option>
            ))}
          </Select>

          <Select
            value={stage}
            onChange={(event) => onStageChange(event.target.value)}
            aria-label={t("migrationWorkspace.inventory.toolbar.stageAria")}
            wrapperClassName="min-w-0 w-full"
            className={toolbarSelectClassName}
          >
            <option value="">{t("migrationWorkspace.inventory.toolbar.allStages")}</option>
            {MIGRATION_STAGE_CODES.map((code) => (
              <option key={code} value={code}>{t(stageTitleKey(code))}</option>
            ))}
          </Select>

          <Select
            value={targetStack}
            onChange={(event) => onTargetStackChange(event.target.value)}
            aria-label={t("migrationWorkspace.inventory.toolbar.targetAria")}
            wrapperClassName="min-w-0 w-full"
            className={toolbarSelectClassName}
          >
            <option value="">{t("migrationWorkspace.inventory.toolbar.allTargets")}</option>
            {targetStacks.map((stack) => <option key={stack} value={stack}>{stack}</option>)}
          </Select>

          <div className="flex min-w-0 items-center gap-3 sm:col-span-2">
            <Select
              value={sortBy}
              onChange={(event) => onSortByChange(event.target.value as MigrationSessionSortBy)}
              aria-label={t("migrationWorkspace.inventory.toolbar.sortAria")}
              wrapperClassName="min-w-0 flex-1"
              className={toolbarSelectClassName}
            >
              {sortOptions.map((value) => (
                <option key={value} value={value}>
                  {t("migrationWorkspace.inventory.toolbar.sortPrefix", { label: sortLabel(value, t) })}
                </option>
              ))}
            </Select>
            <Button
              type="button"
              variant="outline"
              size="icon"
              className="size-10 shrink-0"
              onClick={onSortDirectionToggle}
              aria-label={sortDirectionLabel}
              title={sortDirectionLabel}
            >
              {sortDirection === "desc" ? <ArrowDownAZ /> : <ArrowUpAZ />}
            </Button>
          </div>
        </div>
        <label className="flex items-center gap-2 text-sm text-muted-foreground">
          <Checkbox
            checked={includeArchived}
            onCheckedChange={(checked) => onIncludeArchivedChange(checked === true)}
            aria-label={t("migrationWorkspace.inventory.toolbar.includeArchived")}
          />
          <span>{t("migrationWorkspace.inventory.toolbar.includeArchived")}</span>
        </label>
      </div>
    </div>
  )
}

function lifecycleLabel(
  lifecycle: MigrationSessionLifecycle,
  t: ReturnType<typeof useI18n>["t"],
) {
  switch (lifecycle) {
    case "all": return t("migrationWorkspace.inventory.lifecycle.all")
    case "active": return t("migrationWorkspace.inventory.lifecycle.active")
    case "completed": return t("migrationWorkspace.inventory.lifecycle.completed")
    case "closed": return t("migrationWorkspace.inventory.lifecycle.closed")
    case "cancelled": return t("migrationWorkspace.inventory.lifecycle.cancelled")
    case "archived": return t("migrationWorkspace.inventory.lifecycle.archived")
  }
}

function sortLabel(
  sortBy: MigrationSessionSortBy,
  t: ReturnType<typeof useI18n>["t"],
) {
  switch (sortBy) {
    case "updated": return t("migrationWorkspace.inventory.sort.updated")
    case "created": return t("migrationWorkspace.inventory.sort.created")
    case "name": return t("migrationWorkspace.inventory.sort.name")
    case "stage": return t("migrationWorkspace.inventory.sort.stage")
  }
}
