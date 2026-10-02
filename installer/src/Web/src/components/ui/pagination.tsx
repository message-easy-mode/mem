import * as React from "react"
import { ChevronLeft, ChevronRight, MoreHorizontal } from "lucide-react"

import { cn } from "@/lib/utils"

function Pagination({
  className,
  ...props
}: React.ComponentProps<"nav">) {
  return (
    <nav
      data-slot="pagination"
      role="navigation"
      aria-label="pagination"
      className={cn("mx-auto flex w-full justify-center", className)}
      {...props}
    />
  )
}

function PaginationContent({
  className,
  ...props
}: React.ComponentProps<"ul">) {
  return (
    <ul
      data-slot="pagination-content"
      className={cn("flex flex-row items-center gap-1", className)}
      {...props}
    />
  )
}

function PaginationItem(props: React.ComponentProps<"li">) {
  return <li data-slot="pagination-item" {...props} />
}

function PaginationLink({
  className,
  isActive,
  ...props
}: React.ComponentProps<"button"> & { isActive?: boolean }) {
  return (
    <button
      data-slot="pagination-link"
      data-active={isActive ? "true" : undefined}
      className={cn(
        "inline-flex size-8 items-center justify-center rounded-md border border-transparent text-sm transition-colors hover:bg-muted disabled:pointer-events-none disabled:opacity-50 data-[active=true]:border-border data-[active=true]:bg-muted",
        className,
      )}
      {...props}
    />
  )
}

function PaginationPrevious({
  className,
  children = "Previous",
  ...props
}: React.ComponentProps<"button">) {
  return (
    <button
      data-slot="pagination-previous"
      className={cn(
        "inline-flex h-8 items-center justify-center gap-1 rounded-md px-2 text-sm transition-colors hover:bg-muted disabled:pointer-events-none disabled:opacity-50",
        className,
      )}
      {...props}
    >
      <ChevronLeft className="size-4" />
      <span>{children}</span>
    </button>
  )
}

function PaginationNext({
  className,
  children = "Next",
  ...props
}: React.ComponentProps<"button">) {
  return (
    <button
      data-slot="pagination-next"
      className={cn(
        "inline-flex h-8 items-center justify-center gap-1 rounded-md px-2 text-sm transition-colors hover:bg-muted disabled:pointer-events-none disabled:opacity-50",
        className,
      )}
      {...props}
    >
      <span>{children}</span>
      <ChevronRight className="size-4" />
    </button>
  )
}

function PaginationEllipsis({
  className,
  ...props
}: React.ComponentProps<"span">) {
  return (
    <span
      data-slot="pagination-ellipsis"
      aria-hidden
      className={cn("flex size-8 items-center justify-center", className)}
      {...props}
    >
      <MoreHorizontal className="size-4" />
    </span>
  )
}

export {
  Pagination,
  PaginationContent,
  PaginationEllipsis,
  PaginationItem,
  PaginationLink,
  PaginationNext,
  PaginationPrevious,
}
