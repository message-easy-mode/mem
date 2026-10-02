import type { Table } from "@tanstack/react-table"
import { ChevronsLeft, ChevronsRight } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import {
  Pagination,
  PaginationContent,
  PaginationItem,
  PaginationLink,
  PaginationNext,
  PaginationPrevious,
} from "@/components/ui/pagination"
import { Select } from "@/components/ui/select"

type DataTablePaginationProps<TData> = {
  table: Table<TData>
  rowCount: number
  pageSizes?: readonly number[]
  disabled?: boolean
}

export function DataTablePagination<TData>({
  table,
  rowCount,
  pageSizes = [10, 25, 50, 100],
  disabled = false,
}: DataTablePaginationProps<TData>) {
  const { t } = useI18n()
  const { pageIndex, pageSize } = table.getState().pagination
  const pageCount = Math.max(1, table.getPageCount())
  const start = rowCount === 0 ? 0 : pageIndex * pageSize + 1
  const end = Math.min((pageIndex + 1) * pageSize, rowCount)

  return (
    <div className="flex flex-col gap-3 border-t border-border pt-4 md:flex-row md:items-center md:justify-between">
      <div className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
        <span>{t("dataTable.showing", { start, end, count: rowCount })}</span>
        <label className="ml-0 flex items-center gap-2 md:ml-3">
          <span className="text-xs uppercase tracking-wide">{t("dataTable.rows")}</span>
          <Select
            aria-label={t("dataTable.rowsPerPageAria")}
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
              <option key={size} value={size}>
                {size}
              </option>
            ))}
          </Select>
        </label>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <span className="text-sm text-muted-foreground">
          {t("dataTable.pageOf", { page: pageIndex + 1, count: pageCount })}
        </span>
        <Pagination className="mx-0 w-auto justify-end">
          <PaginationContent>
            <PaginationItem>
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label={t("dataTable.firstPage")}
                disabled={disabled || !table.getCanPreviousPage()}
                onClick={() => table.setPageIndex(0)}
              >
                <ChevronsLeft />
              </Button>
            </PaginationItem>
            <PaginationItem>
              <PaginationPrevious
                disabled={disabled || !table.getCanPreviousPage()}
                onClick={() => table.previousPage()}
              >
                {t("dataTable.previous")}
              </PaginationPrevious>
            </PaginationItem>
            <PaginationItem>
              <PaginationLink isActive type="button" disabled={disabled}>
                {pageIndex + 1}
              </PaginationLink>
            </PaginationItem>
            <PaginationItem>
              <PaginationNext
                disabled={disabled || !table.getCanNextPage()}
                onClick={() => table.nextPage()}
              >
                {t("dataTable.next")}
              </PaginationNext>
            </PaginationItem>
            <PaginationItem>
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label={t("dataTable.lastPage")}
                disabled={disabled || !table.getCanNextPage()}
                onClick={() => table.setPageIndex(pageCount - 1)}
              >
                <ChevronsRight />
              </Button>
            </PaginationItem>
          </PaginationContent>
        </Pagination>
      </div>
    </div>
  )
}
