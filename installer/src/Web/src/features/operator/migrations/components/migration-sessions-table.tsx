import {
  AlertTriangle,
  Archive,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  CircleDot,
  CircleOff,
  ClipboardList,
} from "lucide-react"
import { Link } from "react-router-dom"

import "./migration-target.css"
import { formatMigrationDateTime, migrationUtcIso } from "./migration-time"

import { useI18n } from "@/app/i18n/i18n-context"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Select } from "@/components/ui/select"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  actionKey,
  stageTitleKey,
  summaryKey,
} from "@/features/operator/migrations/components/migration-workspace-ui"
import type {
  MigrationSessionInventoryResponse,
  MigrationSessionInventoryRow,
} from "@/features/operator/migrations/api/migration-sessions"
import { cn } from "@/lib/utils"

import {
  PAGE_SIZES,
  hasMigrationSessionFilters,
  type MigrationSessionsQueryState,
  type MigrationSessionsUrlUpdater,
} from "./migration-sessions-query"
import { MigrationSessionsToolbar } from "./migration-sessions-toolbar"
import { MigrationSessionRowActions } from "./migration-session-lifecycle-actions"

type MigrationSessionsTableProps = {
  response: MigrationSessionInventoryResponse | undefined
  isLoading: boolean
  isFetching: boolean
  isError: boolean
  query: MigrationSessionsQueryState
  searchInput: string
  onSearchInputChange: (value: string) => void
  updateUrl: MigrationSessionsUrlUpdater
  onClearFilters: () => void
  onQuickFilterChange: (
    lifecycle: MigrationSessionsQueryState["lifecycle"],
    action: MigrationSessionsQueryState["action"],
    includeArchived: boolean,
  ) => void
}

export function MigrationSessionsTable({
  response,
  isLoading,
  isFetching,
  isError,
  query,
  searchInput,
  onSearchInputChange,
  updateUrl,
  onClearFilters,
  onQuickFilterChange,
}: MigrationSessionsTableProps) {
  const { intlLocale, t } = useI18n()
  const sessions = response?.sessions ?? []
  const hasFilters = hasMigrationSessionFilters(query)

  return (
    <Card className="min-w-0">
      <CardHeader className="gap-3 pb-3">
        <div>
          <CardTitle className="flex items-center gap-2">
            <ClipboardList className="h-5 w-5 text-sky-400" aria-hidden="true" />
            {t("migrationWorkspace.sessions.title")}
          </CardTitle>
        </div>

        <MigrationSessionsToolbar
          searchInput={searchInput}
          lifecycle={query.lifecycle}
          action={query.action}
          stage={query.stage}
          targetStack={query.targetStack}
          targetStacks={response?.targetStacks ?? []}
          sortBy={query.sortBy}
          sortDirection={query.sortDirection}
          includeArchived={query.includeArchived}
          hasFilters={hasFilters}
          onSearchInputChange={onSearchInputChange}
          onLifecycleChange={(lifecycle) =>
            updateUrl({
              lifecycle,
              action: null,
              includeArchived: lifecycle === "archived" ? "true" : null,
            })
          }
          onQuickFilterChange={onQuickFilterChange}
          onStageChange={(stage) => updateUrl({ stage: stage || null })}
          onTargetStackChange={(targetStack) => updateUrl({ targetStack: targetStack || null })}
          onSortByChange={(sortBy) => updateUrl({ sortBy }, false)}
          onSortDirectionToggle={() =>
            updateUrl({ sortDirection: query.sortDirection === "desc" ? "asc" : "desc" }, false)
          }
          onIncludeArchivedChange={(includeArchived) =>
            updateUrl({
              includeArchived: includeArchived ? "true" : null,
              lifecycle: !includeArchived && query.lifecycle === "archived" ? null : undefined,
            })
          }
          onClearFilters={onClearFilters}
        />
      </CardHeader>

      <CardContent className="px-0 sm:px-0">
        {isError ? (
          <MigrationSessionsLoadError />
        ) : isLoading ? (
          <MigrationSessionsLoading />
        ) : sessions.length === 0 ? (
          hasFilters ? (
            <MigrationSessionsFilteredEmpty onClearFilters={onClearFilters} />
          ) : (
            <MigrationSessionsEmpty />
          )
        ) : (
          <div className={isFetching ? "opacity-65 transition-opacity" : "transition-opacity"}>
            <div className="migration-inventory-results border-y border-border">
              <Table className="migration-inventory-table" role="table" aria-label={t("migrationWorkspace.sessions.title")}>
                <TableHeader role="rowgroup">
                  <TableRow role="row" className="hover:bg-transparent">
                    <TableHead role="columnheader" scope="col" className="migration-inventory-identity-head">{t("migrationWorkspace.inventory.table.migration")}</TableHead>
                    <TableHead role="columnheader" scope="col">{t("migrationWorkspace.inventory.table.progress")}</TableHead>
                    <TableHead role="columnheader" scope="col" className="migration-inventory-actions-head text-right">
                      {t("migrationWorkspace.inventory.table.actions")}
                    </TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody role="rowgroup">
                  {sessions.map((session) => (
                    <TableRow role="row" key={session.migrationId} className="align-top">
                      <TableCell role="cell" className="migration-inventory-identity">
                        <div className="flex min-w-0 items-start gap-3">
                          <SessionStateIcon session={session} />
                          <MigrationCell session={session} />
                        </div>
                      </TableCell>
                      <TableCell role="cell" className="migration-inventory-progress">
                        <StageCell session={session} />
                        <StatusActionCell session={session} />
                        <p className="mt-2 text-xs text-muted-foreground">
                          {t("migrationWorkspace.table.updated")}: {" "}
                          <time dateTime={migrationUtcIso(session.updatedAtUtc) ?? undefined} title={session.updatedAtUtc}>
                            {formatMigrationDateTime(session.updatedAtUtc, intlLocale)}
                          </time>
                        </p>
                      </TableCell>
                      <TableCell role="cell" className="migration-inventory-actions text-right">
                        <MigrationSessionRowActions session={session} />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>

            <MigrationSessionsPagination
              page={response?.page ?? query.page}
              pageSize={response?.pageSize ?? query.pageSize}
              totalPages={response?.totalPages ?? 0}
              totalSessions={response?.totalSessions ?? 0}
              hasPreviousPage={response?.hasPreviousPage ?? false}
              hasNextPage={response?.hasNextPage ?? false}
              disabled={isFetching}
              updateUrl={updateUrl}
            />
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function SessionStateIcon({ session }: { session: MigrationSessionInventoryRow }) {
  const { t } = useI18n()
  const lifecycle = session.archived ? "archived" : session.lifecycleStatus
  const label = lifecycleLabel(lifecycle, t)
  const Icon = session.archived
    ? Archive
    : session.needsAttention
      ? AlertTriangle
      : session.lifecycleStatus === "completed"
        ? CheckCircle2
        : session.lifecycleStatus === "closed" || session.lifecycleStatus === "cancelled"
          ? CircleOff
          : CircleDot
  const className = session.needsAttention
    ? "bg-amber-500/10 text-amber-400"
    : session.lifecycleStatus === "completed"
      ? "bg-emerald-500/10 text-emerald-400"
      : session.archived || session.lifecycleStatus === "closed" || session.lifecycleStatus === "cancelled"
        ? "bg-muted text-muted-foreground"
        : "bg-sky-500/10 text-sky-400"

  return (
    <span
      className={cn("flex size-8 shrink-0 items-center justify-center rounded-full", className)}
      role="img"
      aria-label={t("migrationWorkspace.inventory.table.stateAria", { state: label })}
      title={label}
    >
      <Icon className="size-4" aria-hidden="true" />
    </span>
  )
}

function MigrationCell({ session }: { session: MigrationSessionInventoryRow }) {
  const { t } = useI18n()
  return (
    <div className="min-w-0 flex-1 space-y-1">
      <Link
        to={`/migrations/${encodeURIComponent(session.migrationId)}`}
        className="font-medium text-foreground transition-colors hover:text-primary hover:underline"
      >
        {session.displayName}
      </Link>
      <div className="text-sm text-muted-foreground">{session.sourceDisplay}</div>
      <Badge variant="outline" className="mt-1">
        {lifecycleLabel(session.archived ? "archived" : session.lifecycleStatus, t)}
      </Badge>
      <div className="pt-1 text-xs text-muted-foreground">
        {t("migrationWorkspace.inventory.table.target")}: <TargetCell session={session} />
      </div>
    </div>
  )
}

function TargetCell({ session }: { session: MigrationSessionInventoryRow }) {
  const { t } = useI18n()
  return session.targetStackSlug ? (
    <Link
      to={`/stacks/${encodeURIComponent(session.targetStackSlug)}`}
      className="text-sm transition-colors hover:text-emerald-300 hover:underline"
    >
      {session.targetStackSlug}
    </Link>
  ) : (
    <span className="text-sm text-muted-foreground">
      {t("migrationWorkspace.inventory.table.notCreated")}
    </span>
  )
}

function StageCell({ session }: { session: MigrationSessionInventoryRow }) {
  const { t } = useI18n()
  const title = session.lifecycleStatus === "cancelled"
    ? t("migrationWorkspace.inventory.stage.cancelled")
    : t(stageTitleKey(session.currentStageCode))

  return (
    <div className="mb-2 flex min-w-0 flex-wrap items-baseline gap-x-2 gap-y-1">
      <div className="text-sm font-medium">{title}</div>
      <div className="text-xs text-muted-foreground">
        {stageStateLabel(session.currentStageState, t)}
      </div>
    </div>
  )
}

function StatusActionCell({ session }: { session: MigrationSessionInventoryRow }) {
  const { t } = useI18n()
  return (
    <div className="min-w-0 space-y-1.5">
      <div className={cn("text-sm font-medium", session.needsAttention && "text-amber-400")}>
        {t(summaryKey(session.currentStatusCode))}
      </div>
      <div className="text-xs text-muted-foreground">
        {t(session.primaryAction.kind === "view"
          ? "migrationWorkspace.inventory.action.viewDetails"
          : actionKey(session.primaryAction.code))}
      </div>
      <div className="flex flex-wrap gap-2 text-xs text-muted-foreground">
        {session.warningCount > 0 ? (
          <span>{t("migrationWorkspace.inventory.table.warningCount", { count: session.warningCount })}</span>
        ) : null}
        {session.errorCount > 0 ? (
          <span className="text-destructive">{t("migrationWorkspace.inventory.table.errorCount", { count: session.errorCount })}</span>
        ) : null}
      </div>
    </div>
  )
}

function MigrationSessionsLoadError() {
  const { t } = useI18n()
  return (
    <div className="mx-4 flex min-h-48 flex-col items-center justify-center rounded-lg border border-destructive/40 bg-destructive/5 p-6 text-center">
      <AlertTriangle className="mb-3 h-9 w-9 text-destructive" aria-hidden="true" />
      <h2 className="font-medium text-destructive">{t("migrationWorkspace.error.title")}</h2>
      <p className="mt-1 max-w-lg text-sm text-muted-foreground">
        {t("migrationWorkspace.error.description")}
      </p>
    </div>
  )
}

function MigrationSessionsLoading() {
  const { t } = useI18n()
  return (
    <div className="mx-4 space-y-3" aria-label={t("migrationWorkspace.loading")}>
      <div className="h-12 animate-pulse rounded bg-muted" />
      <div className="h-20 animate-pulse rounded bg-muted" />
      <div className="h-20 animate-pulse rounded bg-muted" />
    </div>
  )
}

function MigrationSessionsEmpty() {
  const { t } = useI18n()
  return (
    <div className="mx-4 flex min-h-56 flex-col items-center justify-center rounded-lg border border-dashed p-6 text-center">
      <ClipboardList className="mb-3 h-9 w-9 text-muted-foreground" aria-hidden="true" />
      <h2 className="font-medium">{t("migrationWorkspace.empty.title")}</h2>
      <p className="mt-1 max-w-lg text-sm text-muted-foreground">{t("migrationWorkspace.empty.description")}</p>
      <Button className="mt-4" asChild>
        <Link to="/migrations/new">{t("migrationWorkspace.start")}</Link>
      </Button>
    </div>
  )
}

function MigrationSessionsFilteredEmpty({ onClearFilters }: { onClearFilters: () => void }) {
  const { t } = useI18n()
  return (
    <div className="mx-4 flex min-h-48 flex-col items-center justify-center rounded-lg border border-dashed p-6 text-center">
      <h2 className="font-medium">{t("migrationWorkspace.inventory.filteredEmpty.title")}</h2>
      <p className="mt-1 max-w-lg text-sm text-muted-foreground">
        {t("migrationWorkspace.inventory.filteredEmpty.description")}
      </p>
      <Button className="mt-4" variant="outline" onClick={onClearFilters}>
        {t("migrationWorkspace.inventory.toolbar.clear")}
      </Button>
    </div>
  )
}

function MigrationSessionsPagination({
  page,
  pageSize,
  totalPages,
  totalSessions,
  hasPreviousPage,
  hasNextPage,
  disabled,
  updateUrl,
}: {
  page: number
  pageSize: number
  totalPages: number
  totalSessions: number
  hasPreviousPage: boolean
  hasNextPage: boolean
  disabled: boolean
  updateUrl: MigrationSessionsUrlUpdater
}) {
  const { t } = useI18n()
  const pageCount = Math.max(1, totalPages)
  const start = totalSessions === 0 ? 0 : (page - 1) * pageSize + 1
  const end = Math.min(page * pageSize, totalSessions)
  const pageItems = buildPageItems(page, pageCount)

  return (
    <div className="flex flex-col gap-4 px-4 py-4 text-sm text-muted-foreground lg:flex-row lg:items-center lg:justify-between">
      <p>{t("dataTable.showing", { start, end, count: totalSessions })}</p>
      <div className="flex flex-wrap items-center gap-3 lg:justify-end">
        <nav aria-label={t("dataTable.pageOf", { page, count: pageCount })} className="flex min-w-0 flex-wrap items-center gap-1">
          <Button
            type="button"
            variant="outline"
            size="icon-sm"
            aria-label={t("dataTable.previous")}
            disabled={disabled || !hasPreviousPage}
            onClick={() => updateUrl({ page: String(page - 1) }, false)}
          >
            <ChevronLeft />
          </Button>
          {pageItems.map((item, index) => item === "ellipsis" ? (
            <span key={`ellipsis-${index}`} className="px-1" aria-hidden="true">…</span>
          ) : (
            <Button
              key={item}
              type="button"
              variant={item === page ? "default" : "outline"}
              size="icon-sm"
              aria-current={item === page ? "page" : undefined}
              disabled={disabled}
              onClick={() => updateUrl({ page: String(item) }, false)}
            >
              {item}
            </Button>
          ))}
          <Button
            type="button"
            variant="outline"
            size="icon-sm"
            aria-label={t("dataTable.next")}
            disabled={disabled || !hasNextPage}
            onClick={() => updateUrl({ page: String(page + 1) }, false)}
          >
            <ChevronRight />
          </Button>
        </nav>

        <label className="flex items-center gap-2">
          <span className="text-xs uppercase tracking-wide">{t("dataTable.rows")}</span>
          <Select
            aria-label={t("dataTable.rowsPerPageAria")}
            className="h-9 w-20"
            value={String(pageSize)}
            disabled={disabled}
            onChange={(event) => updateUrl({ page: null, pageSize: event.target.value }, false)}
          >
            {PAGE_SIZES.map((size) => <option key={size} value={size}>{size}</option>)}
          </Select>
        </label>
      </div>
    </div>
  )
}

function lifecycleLabel(lifecycle: string, t: ReturnType<typeof useI18n>["t"]) {
  switch (lifecycle) {
    case "active": return t("migrationWorkspace.inventory.lifecycle.active")
    case "completed": return t("migrationWorkspace.inventory.lifecycle.completed")
    case "closed": return t("migrationWorkspace.inventory.lifecycle.closed")
    case "cancelled": return t("migrationWorkspace.inventory.lifecycle.cancelled")
    case "archived": return t("migrationWorkspace.inventory.lifecycle.archived")
    default: return t("migrationWorkspace.inventory.lifecycle.active")
  }
}

function stageStateLabel(state: string, t: ReturnType<typeof useI18n>["t"]) {
  switch (state) {
    case "ready": return t("migrationWorkspace.inventory.stageState.ready")
    case "running": return t("migrationWorkspace.inventory.stageState.running")
    case "completed": return t("migrationWorkspace.inventory.stageState.completed")
    case "blocked": return t("migrationWorkspace.inventory.stageState.blocked")
    case "failed": return t("migrationWorkspace.inventory.stageState.failed")
    case "action-required": return t("migrationWorkspace.inventory.stageState.actionRequired")
    case "closed": return t("migrationWorkspace.inventory.stageState.closed")
    default: return t("migrationWorkspace.inventory.stageState.notStarted")
  }
}

function buildPageItems(currentPage: number, pageCount: number): Array<number | "ellipsis"> {
  if (pageCount <= 7) return Array.from({ length: pageCount }, (_, index) => index + 1)
  if (currentPage <= 4) return [1, 2, 3, 4, 5, "ellipsis", pageCount]
  if (currentPage >= pageCount - 3) {
    return [1, "ellipsis", pageCount - 4, pageCount - 3, pageCount - 2, pageCount - 1, pageCount]
  }
  return [1, "ellipsis", currentPage - 1, currentPage, currentPage + 1, "ellipsis", pageCount]
}
