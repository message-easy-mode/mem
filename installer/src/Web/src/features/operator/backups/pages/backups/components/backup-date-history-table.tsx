/* eslint-disable react-hooks/incompatible-library -- TanStack Table intentionally owns mutable table instance methods. */
import { Fragment, useMemo, useState } from "react"
import {
  flexRender,
  functionalUpdate,
  getCoreRowModel,
  getExpandedRowModel,
  useReactTable,
  type ExpandedState,
  type PaginationState,
  type SortingState,
  type VisibilityState,
} from "@tanstack/react-table"

import { DataTablePagination } from "@/components/data-table/data-table-pagination"
import { DataTableViewOptions } from "@/components/data-table/data-table-view-options"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { cn } from "@/lib/utils"
import type { RuntimeStackBackupEntryListResponse } from "@/features/operator/backups/api/types/backups.types"

import "@/components/data-table/data-table-types"

import {
  BACKUP_DATE_ACTIONS_COLUMN_ID,
  BackupDateHistoryDetails,
  createBackupDateHistoryColumns,
} from "./backup-date-history-columns"
import {
  BACKUP_PAGE_SIZES,
  type BackupDateHistoryQueryState,
  type BackupDateHistoryUrlUpdater,
} from "./backup-date-history-query"
import { BackupDateHistoryToolbar } from "./backup-date-history-toolbar"

type BackupDateHistoryTableProps = {
  response: RuntimeStackBackupEntryListResponse | undefined
  isLoading: boolean
  isFetching: boolean
  query: BackupDateHistoryQueryState
  searchInput: string
  onSearchInputChange: (value: string) => void
  updateUrl: BackupDateHistoryUrlUpdater
}

export function BackupDateHistoryTable({
  response,
  isLoading,
  isFetching,
  query,
  searchInput,
  onSearchInputChange,
  updateUrl,
}: BackupDateHistoryTableProps) {
  const [columnVisibility, setColumnVisibility] = useState<VisibilityState>({})
  const [expanded, setExpanded] = useState<ExpandedState>({})
  const backups = response?.backups ?? []
  const hasFilters = Boolean(query.search || query.stackSlug)

  const pagination = useMemo<PaginationState>(
    () => ({ pageIndex: Math.max(0, query.page - 1), pageSize: query.pageSize }),
    [query.page, query.pageSize],
  )

  const sorting = useMemo<SortingState>(
    () => [{ id: query.sortBy, desc: query.sortDirection === "desc" }],
    [query.sortBy, query.sortDirection],
  )

  const columns = useMemo(() => createBackupDateHistoryColumns(), [])

  const table = useReactTable({
    data: backups,
    columns,
    getCoreRowModel: getCoreRowModel(),
    getExpandedRowModel: getExpandedRowModel(),
    manualPagination: true,
    manualSorting: true,
    manualFiltering: true,
    pageCount: response?.totalPages ?? 1,
    rowCount: response?.totalBackups ?? 0,
    autoResetPageIndex: false,
    getRowId: (backup) => `${backup.stackSlug}:${backup.backupId}`,
    getRowCanExpand: () => true,
    state: {
      pagination,
      sorting,
      columnVisibility,
      expanded,
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
        sort: nextSort?.id ?? "created",
        direction: nextSort?.desc ? "desc" : "asc",
      })
    },
    onColumnVisibilityChange: setColumnVisibility,
    onExpandedChange: setExpanded,
  })

  const toolbar = (
    <BackupDateHistoryToolbar
      searchInput={searchInput}
      stackSlug={query.stackSlug}
      availableStacks={response?.availableStacks ?? []}
      hasFilters={hasFilters}
      onSearchInputChange={onSearchInputChange}
      onStackSlugChange={(stackSlug) => updateUrl({ stack: stackSlug || null })}
      onClearFilters={() => {
        onSearchInputChange("")
        updateUrl({ q: null, stack: null })
      }}
    />
  )

  // Keep the toolbar mounted for every request state. Search is a controlled input;
  // returning a separate loading view here would unmount it and make the browser drop focus.
  const isInitialLoad = isLoading && !response
  const isEmpty = !isInitialLoad && (response?.totalBackups ?? 0) === 0

  return (
    <div className={isFetching ? "space-y-4 opacity-65 transition-opacity" : "space-y-4 transition-opacity"}>
      <div className="flex justify-end">
        <DataTableViewOptions table={table} />
      </div>

      {toolbar}

      {isInitialLoad ? (
        <div className="rounded-lg border border-border bg-background/40 p-4 text-sm text-muted-foreground">
          Loading backup history...
        </div>
      ) : isEmpty ? (
        <div className="rounded-lg border border-dashed border-border bg-background/40 p-5 text-sm text-muted-foreground">
          {hasFilters
            ? "No local backups match the current search or stack filter. Clear filters to see the full inventory."
            : "No stack backup history was found yet. Create a backup from an active stack to populate this page."}
        </div>
      ) : (
        <>
          <div className="rounded-xl border border-border">
            <div className="overflow-x-auto">
              <Table className="min-w-[900px]">
                <TableHeader>
                  {table.getHeaderGroups().map((headerGroup) => (
                    <TableRow key={headerGroup.id}>
                      {headerGroup.headers.map((header) => {
                        const isActionsColumn =
                          header.column.id === BACKUP_DATE_ACTIONS_COLUMN_ID

                        return (
                          <TableHead
                            key={header.id}
                            className={cn(
                              header.column.columnDef.meta?.align === "right" &&
                                "text-right",
                              isActionsColumn &&
                                "sticky right-0 z-30 min-w-44 border-l border-border bg-card text-right shadow-[-8px_0_12px_-10px_rgba(0,0,0,0.9)]",
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
                    <Fragment key={row.id}>
                      <TableRow data-state={row.getIsSelected() ? "selected" : undefined}>
                        {row.getVisibleCells().map((cell) => {
                          const isActionsColumn =
                            cell.column.id === BACKUP_DATE_ACTIONS_COLUMN_ID

                          return (
                            <TableCell
                              key={cell.id}
                              className={cn(
                                cell.column.columnDef.meta?.align === "right" &&
                                  "text-right",
                                isActionsColumn &&
                                  "sticky right-0 z-20 min-w-44 border-l border-border bg-card shadow-[-8px_0_12px_-10px_rgba(0,0,0,0.9)]",
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

                      {row.getIsExpanded() ? (
                        <TableRow className="hover:bg-transparent">
                          <TableCell
                            colSpan={row.getVisibleCells().length}
                            className="border-t border-border/70 p-4"
                          >
                            <BackupDateHistoryDetails backup={row.original} />
                          </TableCell>
                        </TableRow>
                      ) : null}
                    </Fragment>
                  ))}
                </TableBody>
              </Table>
            </div>
          </div>

          <DataTablePagination
            table={table}
            rowCount={response?.totalBackups ?? 0}
            pageSizes={BACKUP_PAGE_SIZES}
            disabled={isFetching}
          />
        </>
      )}
    </div>
  )

}
