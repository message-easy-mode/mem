import type { ReactNode } from "react"
import type { Table } from "@tanstack/react-table"
import { X } from "lucide-react"

import { Button } from "@/components/ui/button"

type DataTableSelectionToolbarProps<TData> = {
  table: Table<TData>
  children?: ReactNode
}

/**
 * Reusable bulk-action shell. A feature opts into row selection only when it has a safe operation
 * to offer; this component keeps the selected-row state and clear action consistent across grids.
 */
export function DataTableSelectionToolbar<TData>({
  table,
  children,
}: DataTableSelectionToolbarProps<TData>) {
  const selectedCount = table.getFilteredSelectedRowModel().rows.length

  if (selectedCount === 0) {
    return null
  }

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-border bg-muted/30 px-3 py-2 text-sm">
      <span>
        {selectedCount} row{selectedCount === 1 ? "" : "s"} selected
      </span>
      <div className="flex flex-wrap items-center gap-2">
        {children}
        <Button variant="ghost" size="sm" onClick={() => table.resetRowSelection()}>
          <X className="mr-1 size-3.5" />
          Clear
        </Button>
      </div>
    </div>
  )
}
