import { ArrowDownAZ, ArrowUpAZ, FilterX, Search } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Select } from "@/components/ui/select"

import {
  sortOptions,
  statusOptions,
  type RestoreSessionSortBy,
  type RestoreSessionStatus,
} from "./restore-sessions-query"

type RestoreSessionsToolbarProps = {
  searchInput: string
  status: RestoreSessionStatus
  targetStack: string
  targetStacks: string[]
  sortBy: RestoreSessionSortBy
  sortDirection: "asc" | "desc"
  hasFilters: boolean
  onSearchInputChange: (value: string) => void
  onStatusChange: (value: RestoreSessionStatus) => void
  onTargetStackChange: (value: string) => void
  onSortByChange: (value: RestoreSessionSortBy) => void
  onSortDirectionToggle: () => void
  onClearFilters: () => void
}

const quickFilterStatuses: RestoreSessionStatus[] = [
  "all",
  "in-progress",
  "completed",
  "failed",
  "cancelled",
  "warnings",
]

const toolbarSelectClassName =
  "h-10 w-full focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500/30 focus-visible:border-emerald-500 focus-visible:ring-2 focus-visible:ring-emerald-500/30"

export function RestoreSessionsToolbar({
  searchInput,
  status,
  targetStack,
  targetStacks,
  sortBy,
  sortDirection,
  hasFilters,
  onSearchInputChange,
  onStatusChange,
  onTargetStackChange,
  onSortByChange,
  onSortDirectionToggle,
  onClearFilters,
}: RestoreSessionsToolbarProps) {
  const { t } = useI18n()

  const sortDirectionLabel =
    sortDirection === "desc"
      ? t("restoreSessions.toolbar.desc")
      : t("restoreSessions.toolbar.asc")

  return (
    <div className="space-y-3 rounded-xl border border-border bg-muted/20 p-3">
      <div className="grid gap-3 min-[860px]:grid-cols-2 min-[1280px]:grid-cols-3 min-[1750px]:grid-cols-[minmax(22rem,1fr)_minmax(11rem,0.34fr)_minmax(12rem,0.36fr)_minmax(14rem,0.42fr)] min-[1750px]:items-center">
        <label className="relative min-w-0 min-[860px]:col-span-2 min-[1280px]:col-span-3 min-[1750px]:col-span-1">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={searchInput}
            onChange={(event) => onSearchInputChange(event.target.value)}
            placeholder={t("restoreSessions.toolbar.searchPlaceholder")}
            className="h-10 pl-9"
            aria-label={t("restoreSessions.toolbar.searchAria")}
          />
        </label>

        <Select
          value={status}
          onChange={(event) => onStatusChange(event.target.value as RestoreSessionStatus)}
          aria-label={t("restoreSessions.toolbar.lifecycleAria")}
          wrapperClassName="min-w-0 w-full"
          className={toolbarSelectClassName}
        >
          {statusOptions.map((option) => (
            <option key={option.value} value={option.value}>
              {statusOptionLabel(option.value, t)}
            </option>
          ))}
        </Select>

        <Select
          value={targetStack}
          onChange={(event) => onTargetStackChange(event.target.value)}
          aria-label={t("restoreSessions.toolbar.targetAria")}
          wrapperClassName="min-w-0 w-full"
          className={toolbarSelectClassName}
        >
          <option value="">{t("restoreSessions.toolbar.allTargetStacks")}</option>
          {targetStacks.map((stack) => (
            <option key={stack} value={stack}>
              {stack}
            </option>
          ))}
        </Select>

        <div className="flex min-w-0 items-center gap-3 min-[860px]:col-span-2 min-[1280px]:col-span-1">
          <Select
            value={sortBy}
            onChange={(event) => onSortByChange(event.target.value as RestoreSessionSortBy)}
            aria-label={t("restoreSessions.toolbar.sortAria")}
            wrapperClassName="min-w-0 flex-1"
            className={toolbarSelectClassName}
          >
            {sortOptions.map((option) => (
              <option key={option.value} value={option.value}>
                {t("restoreSessions.toolbar.sortPrefix", {
                  label: sortOptionLabel(option.value, t),
                })}
              </option>
            ))}
          </Select>

          <Button
            type="button"
            variant="outline"
            size="icon"
            className="size-10 shrink-0 focus-visible:border-emerald-500 focus-visible:ring-2 focus-visible:ring-emerald-500/30"
            onClick={onSortDirectionToggle}
            aria-label={sortDirectionLabel}
            title={sortDirectionLabel}
          >
            {sortDirection === "desc" ? <ArrowDownAZ /> : <ArrowUpAZ />}
          </Button>
        </div>
      </div>

      <div className="flex flex-col gap-3 border-t border-border/70 pt-3 sm:flex-row sm:items-center sm:justify-between">
        <div
          className="flex flex-wrap items-center gap-2"
          role="group"
          aria-label={t("restoreSessions.toolbar.quickFiltersAria")}
        >
          {quickFilterStatuses.map((quickStatus) => (
            <Button
              key={quickStatus}
              type="button"
              size="sm"
              variant={status === quickStatus ? "default" : "outline"}
              aria-pressed={status === quickStatus}
              onClick={() => onStatusChange(quickStatus)}
            >
              {statusOptionLabel(quickStatus, t)}
            </Button>
          ))}
        </div>

        {hasFilters ? (
          <Button
            variant="ghost"
            size="sm"
            onClick={onClearFilters}
            className="self-start sm:self-auto"
          >
            <FilterX className="mr-2 size-4" />
            {t("restoreSessions.toolbar.clear")}
          </Button>
        ) : null}
      </div>
    </div>
  )
}

function statusOptionLabel(
  status: RestoreSessionStatus,
  t: ReturnType<typeof useI18n>["t"],
) {
  switch (status) {
    case "all":
      return t("restoreSessions.status.all")
    case "in-progress":
      return t("restoreSessions.status.inProgress")
    case "ready":
      return t("restoreSessions.status.ready")
    case "testing":
      return t("restoreSessions.status.testing")
    case "planning":
      return t("restoreSessions.status.planning")
    case "recreating":
      return t("restoreSessions.status.recreating")
    case "verifying":
      return t("restoreSessions.status.verifying")
    case "needs-attention":
      return t("restoreSessions.status.needsAttention")
    case "completed":
      return t("restoreSessions.status.completed")
    case "failed":
      return t("restoreSessions.status.failed")
    case "cancelled":
      return t("restoreSessions.status.cancelled")
    case "warnings":
      return t("restoreSessions.status.warnings")
  }
}

function sortOptionLabel(
  sort: RestoreSessionSortBy,
  t: ReturnType<typeof useI18n>["t"],
) {
  switch (sort) {
    case "updated":
      return t("restoreSessions.sort.updated")
    case "started":
      return t("restoreSessions.sort.started")
    case "source":
      return t("restoreSessions.sort.backupSource")
    case "target":
      return t("restoreSessions.sort.target")
    case "status":
      return t("restoreSessions.sort.status")
    case "session":
      return t("restoreSessions.sort.session")
  }
}