/* eslint-disable react-hooks/incompatible-library -- TanStack Table intentionally owns mutable table instance methods. */
import { useMemo } from "react"
import {
  flexRender,
  functionalUpdate,
  getCoreRowModel,
  useReactTable,
  type PaginationState,
  type SortingState,
  type Table as TanStackTable,
} from "@tanstack/react-table"
import { ChevronLeft, ChevronRight, ClipboardList } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
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
import { cn } from "@/lib/utils"
import type { RestoreAttemptListResponse } from "@/features/operator/backups/api/types/backups.types"

import {
  createRestoreSessionColumns,
  RESTORE_SESSION_ACTIONS_COLUMN_ID,
} from "./restore-sessions-columns"
import {
  PAGE_SIZES,
  type RestoreSessionsQueryState,
  type RestoreSessionsUrlUpdater,
} from "./restore-sessions-query"
import { RestoreSessionsToolbar } from "./restore-sessions-toolbar"

type RestoreSessionsTableProps = {
  response: RestoreAttemptListResponse | undefined
  isLoading: boolean
  isFetching: boolean
  query: RestoreSessionsQueryState
  searchInput: string
  onSearchInputChange: (value: string) => void
  updateUrl: RestoreSessionsUrlUpdater
}

export function RestoreSessionsTable({
  response,
  isLoading,
  isFetching,
  query,
  searchInput,
  onSearchInputChange,
  updateUrl,
}: RestoreSessionsTableProps) {
  const { t, language } = useI18n()
  const sessions = response?.sessions ?? []
  const hasFilters = Boolean(
    query.search || query.targetStack || query.status !== "all",
  )

  const pagination = useMemo<PaginationState>(
    () => ({ pageIndex: Math.max(0, query.page - 1), pageSize: query.pageSize }),
    [query.page, query.pageSize],
  )

  const sorting = useMemo<SortingState>(
    () => [{ id: query.sortBy, desc: query.sortDirection === "desc" }],
    [query.sortBy, query.sortDirection],
  )

  const columns = useMemo(() => createRestoreSessionColumns(t, language), [language, t])

  const table = useReactTable({
    data: sessions,
    columns,
    getCoreRowModel: getCoreRowModel(),
    manualPagination: true,
    manualSorting: true,
    manualFiltering: true,
    pageCount: response?.totalPages ?? 1,
    rowCount: response?.totalSessions ?? 0,
    autoResetPageIndex: false,
    getRowId: (session) => session.restoreSessionId,
    state: {
      pagination,
      sorting,
    },
    onPaginationChange: (updater) => {
      const next = functionalUpdate(updater, pagination)
      updateUrl(
        {
          page: (next.pageIndex + 1).toString(),
          pageSize: next.pageSize.toString(),
        },
        false,
      )
    },
    onSortingChange: (updater) => {
      const next = functionalUpdate(updater, sorting)
      const nextSort = next[0]

      updateUrl({
        sort: nextSort?.id ?? "updated",
        direction: nextSort?.desc ? "desc" : "asc",
      })
    },
  })

  return (
    <Card>
      <CardHeader className="gap-4 pb-3">
        <div>
          <CardTitle className="flex items-center gap-2">
            <ClipboardList className="h-5 w-5 text-sky-400" aria-hidden="true" />
            {t("restoreSessions.activity.title")}
          </CardTitle>
          <p className="mt-2 max-w-4xl text-sm text-muted-foreground">
            {t("restoreSessions.activity.description")}
          </p>
        </div>

        <RestoreSessionsToolbar
          searchInput={searchInput}
          status={query.status}
          targetStack={query.targetStack}
          targetStacks={response?.targetStacks ?? []}
          sortBy={query.sortBy}
          sortDirection={query.sortDirection}
          hasFilters={hasFilters}
          onSearchInputChange={onSearchInputChange}
          onStatusChange={(status) => updateUrl({ status })}
          onTargetStackChange={(targetStack) =>
            updateUrl({ target: targetStack || null })
          }
          onSortByChange={(sortBy) => updateUrl({ sort: sortBy }, false)}
          onSortDirectionToggle={() =>
            updateUrl(
              { direction: query.sortDirection === "desc" ? "asc" : "desc" },
              false,
            )
          }
          onClearFilters={() => {
            onSearchInputChange("")
            updateUrl({ q: null, status: null, target: null })
          }}
        />
      </CardHeader>

      <CardContent className="px-0 sm:px-0">
        {isLoading ? (
          <div className="mx-4 rounded-lg border border-border bg-background/40 p-4 text-sm text-muted-foreground">
            {t("restoreSessions.activity.loading")}
          </div>
        ) : sessions.length === 0 ? (
          <div className="mx-4 rounded-lg border border-dashed border-border bg-background/40 p-5 text-sm text-muted-foreground">
            {t("restoreSessions.activity.empty")}
          </div>
        ) : (
          <div className={isFetching ? "opacity-65 transition-opacity" : "transition-opacity"}>
            <div className="border-y border-border">
              <Table className="min-w-[1160px]">
                <TableHeader>
                  {table.getHeaderGroups().map((headerGroup) => (
                    <TableRow key={headerGroup.id} className="hover:bg-transparent">
                      {headerGroup.headers.map((header) => {
                        const isActionsColumn =
                          header.column.id === RESTORE_SESSION_ACTIONS_COLUMN_ID

                        return (
                          <TableHead
                            key={header.id}
                            className={cn(
                              header.column.columnDef.meta?.align === "right" &&
                                "text-right",
                              isActionsColumn &&
                                "sticky right-0 z-30 min-w-40 border-l border-border bg-card text-right shadow-[-8px_0_12px_-10px_rgba(0,0,0,0.9)]",
                            )}
                          >
                            {header.isPlaceholder
                              ? null
                              : flexRender(
                                  header.column.columnDef.header,
                                  header.getContext(),
                                )}
                          </TableHead>
                        )
                      })}
                    </TableRow>
                  ))}
                </TableHeader>
                <TableBody>
                  {table.getRowModel().rows.map((row) => (
                    <TableRow key={row.id}>
                      {row.getVisibleCells().map((cell) => {
                        const isActionsColumn =
                          cell.column.id === RESTORE_SESSION_ACTIONS_COLUMN_ID

                        return (
                          <TableCell
                            key={cell.id}
                            className={cn(
                              cell.column.columnDef.meta?.align === "right" &&
                                "text-right",
                              isActionsColumn &&
                                "sticky right-0 z-20 min-w-40 border-l border-border bg-card shadow-[-8px_0_12px_-10px_rgba(0,0,0,0.9)]",
                            )}
                          >
                            {flexRender(
                              cell.column.columnDef.cell,
                              cell.getContext(),
                            )}
                          </TableCell>
                        )
                      })}
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>

            <RestoreSessionsPagination
              table={table}
              rowCount={response?.totalSessions ?? 0}
              pageSizes={PAGE_SIZES}
              disabled={isFetching}
            />
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function RestoreSessionsPagination<TData>({
  table,
  rowCount,
  pageSizes,
  disabled,
}: {
  table: TanStackTable<TData>
  rowCount: number
  pageSizes: readonly number[]
  disabled: boolean
}) {
  const { t } = useI18n()
  const { pageIndex, pageSize } = table.getState().pagination
  const pageCount = Math.max(1, table.getPageCount())
  const start = rowCount === 0 ? 0 : pageIndex * pageSize + 1
  const end = Math.min((pageIndex + 1) * pageSize, rowCount)
  const pageItems = buildPageItems(pageIndex + 1, pageCount)

  return (
    <div className="flex flex-col gap-4 px-4 py-4 text-sm text-muted-foreground lg:flex-row lg:items-center lg:justify-between">
      <p>{t("dataTable.showing", { start, end, count: rowCount })}</p>

      <div className="flex flex-wrap items-center gap-3 lg:justify-end">
        <nav aria-label={t("dataTable.pageOf", { page: pageIndex + 1, count: pageCount })} className="flex items-center gap-1">
          <Button
            type="button"
            variant="outline"
            size="icon-sm"
            aria-label={t("dataTable.previous")}
            disabled={disabled || !table.getCanPreviousPage()}
            onClick={() => table.previousPage()}
          >
            <ChevronLeft />
          </Button>

          {pageItems.map((pageItem, index) => pageItem === "ellipsis" ? (
            <span key={`ellipsis-${index}`} className="px-1 text-muted-foreground" aria-hidden="true">…</span>
          ) : (
            <Button
              key={pageItem}
              type="button"
              variant={pageItem === pageIndex + 1 ? "default" : "outline"}
              size="icon-sm"
              aria-current={pageItem === pageIndex + 1 ? "page" : undefined}
              disabled={disabled}
              onClick={() => table.setPageIndex(pageItem - 1)}
            >
              {pageItem}
            </Button>
          ))}

          <Button
            type="button"
            variant="outline"
            size="icon-sm"
            aria-label={t("dataTable.next")}
            disabled={disabled || !table.getCanNextPage()}
            onClick={() => table.nextPage()}
          >
            <ChevronRight />
          </Button>
        </nav>

        <label className="flex items-center gap-2">
          <span className="text-xs uppercase tracking-wide">{t("dataTable.rows")}</span>
          <Select
            aria-label={t("dataTable.rowsPerPageAria")}
            className="h-9 w-20"
            value={pageSize.toString()}
            disabled={disabled}
            onChange={(event) => {
              table.setPagination({
                pageIndex: 0,
                pageSize: Number(event.target.value),
              })
            }}
          >
            {pageSizes.map((size) => (
              <option key={size} value={size}>{size}</option>
            ))}
          </Select>
        </label>
      </div>
    </div>
  )
}

function buildPageItems(currentPage: number, pageCount: number): Array<number | "ellipsis"> {
  if (pageCount <= 7) {
    return Array.from({ length: pageCount }, (_, index) => index + 1)
  }

  if (currentPage <= 4) {
    return [1, 2, 3, 4, 5, "ellipsis", pageCount]
  }

  if (currentPage >= pageCount - 3) {
    return [1, "ellipsis", pageCount - 4, pageCount - 3, pageCount - 2, pageCount - 1, pageCount]
  }

  return [1, "ellipsis", currentPage - 1, currentPage, currentPage + 1, "ellipsis", pageCount]
}
