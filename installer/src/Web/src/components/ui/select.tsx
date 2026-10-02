import * as React from "react"
import { ChevronDown } from "lucide-react"

import { cn } from "@/lib/utils"

type SelectProps = React.ComponentProps<"select"> & {
  /**
   * Styles the layout wrapper around the native select. Use this when the
   * select must participate in a grid or flex layout at a specific width.
   */
  wrapperClassName?: string
}

function Select({
  className,
  wrapperClassName,
  children,
  ...props
}: SelectProps) {
  return (
    <div className={cn("relative inline-flex min-w-0", wrapperClassName)}>
      <select
        data-slot="select"
        className={cn(
          "h-8 cursor-pointer appearance-none rounded-md border border-input bg-background py-1 pr-8 pl-2 text-sm shadow-xs outline-none transition-colors focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50",
          className,
        )}
        {...props}
      >
        {children}
      </select>
      <ChevronDown
        aria-hidden
        className="pointer-events-none absolute top-1/2 right-2 size-4 -translate-y-1/2 text-muted-foreground"
      />
    </div>
  )
}

export { Select }
