import type { PropsWithChildren, ReactNode } from "react"

import { SetupStepNav } from "./setup-step-nav"

type Props = PropsWithChildren<{
  title: string
  description?: string
  actions?: ReactNode
  showStepNav?: boolean
}>

export function SetupStepLayout({
  title,
  description,
  actions,
  showStepNav = true,
  children,
}: Props) {
  return (
    <div className="space-y-6">
      <div className="space-y-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
          {description && (
            <p className="mt-1 text-sm text-muted-foreground">{description}</p>
          )}
        </div>

        {showStepNav ? <SetupStepNav /> : null}
      </div>

      {children}

      {actions && <div className="flex flex-wrap gap-3">{actions}</div>}
    </div>
  )
}