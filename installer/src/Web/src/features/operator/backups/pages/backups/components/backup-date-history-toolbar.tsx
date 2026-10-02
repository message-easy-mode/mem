import { FilterX, Search } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Select } from "@/components/ui/select"

type BackupDateHistoryToolbarProps = {
  searchInput: string
  stackSlug: string
  availableStacks: string[]
  hasFilters: boolean
  onSearchInputChange: (value: string) => void
  onStackSlugChange: (value: string) => void
  onClearFilters: () => void
}

export function BackupDateHistoryToolbar({
  searchInput,
  stackSlug,
  availableStacks,
  hasFilters,
  onSearchInputChange,
  onStackSlugChange,
  onClearFilters,
}: BackupDateHistoryToolbarProps) {
  return (
    <div className="flex flex-col gap-3 rounded-lg border border-border bg-muted/20 p-3 lg:flex-row lg:items-center">
      <label className="relative min-w-0 flex-1">
        <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          value={searchInput}
          onChange={(event) => onSearchInputChange(event.target.value)}
          placeholder="Search backup ID or stack"
          className="pl-9"
          aria-label="Search local backups"
        />
      </label>

      <Select
        value={stackSlug}
        onChange={(event) => onStackSlugChange(event.target.value)}
        aria-label="Filter backups by stack"
      >
        <option value="">All stacks</option>
        {availableStacks.map((stack) => (
          <option key={stack} value={stack}>
            {stack}
          </option>
        ))}
      </Select>

      {hasFilters ? (
        <Button variant="ghost" size="sm" onClick={onClearFilters}>
          <FilterX className="mr-2 size-4" />
          Clear
        </Button>
      ) : null}
    </div>
  )
}
