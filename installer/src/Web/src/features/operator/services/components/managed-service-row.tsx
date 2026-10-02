import { Link } from "react-router-dom"
import { Button } from "@/components/ui/button"

import type { ManagedServiceListItem } from "@/features/operator/services/api/runtime-ui.types"

import { ServiceActions } from "./service-actions"
import { ServiceIcon } from "./service-icon"
import { ServiceStatusPill } from "./service-status-pill"

type Props = {
  item: ManagedServiceListItem
  onReviewRetirement?: (target: NonNullable<ManagedServiceListItem["retirementReview"]>) => void
}

export function ManagedServiceRow({ item, onReviewRetirement }: Props) {
  return (
    <div
      data-service-name={item.serviceName}
      className="rounded-2xl border border-border bg-card/95 shadow-sm"
    >
      <div className="grid gap-4 p-4 md:grid-cols-[minmax(0,1fr)_auto] md:items-center">
        {item.serviceHref ? (
          <Link
            to={item.serviceHref}
            className="group flex min-w-0 items-center gap-3 rounded-lg outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-card"
          >
            <ServiceIcon serviceName={item.serviceName} />

            <div className="min-w-0">
              <div className="text-base font-medium text-foreground group-hover:underline group-focus-visible:underline">
                {item.displayName}
              </div>
              {item.description ? (
                <p className="mt-0.5 break-words text-sm text-muted-foreground">{item.description}</p>
              ) : null}
            </div>
          </Link>
        ) : (
          <div className="flex min-w-0 items-center gap-3">
            <ServiceIcon serviceName={item.serviceName} />

            <div className="min-w-0">
              <div className="text-base font-medium text-foreground">{item.displayName}</div>
              {item.description ? (
                <p className="mt-0.5 break-words text-sm text-muted-foreground">{item.description}</p>
              ) : null}
            </div>
          </div>
        )}

        <div
          data-service-state-actions
          className="flex min-h-8 min-w-0 flex-wrap items-center justify-end gap-2 md:justify-self-end md:flex-nowrap"
        >
          <div data-service-action-column className="flex min-h-8 min-w-0 shrink-0 flex-wrap items-center justify-end gap-2">
            {item.retirementReview && onReviewRetirement ? (
              <Button type="button" variant="outline" size="sm" onClick={() => onReviewRetirement(item.retirementReview!)}>
                {item.retirementReview.label}
              </Button>
            ) : null}
            {item.workspaceLink ? (
              <Button asChild variant="outline" size="sm">
                <Link to={item.workspaceLink.href}>{item.workspaceLink.label}</Link>
              </Button>
            ) : null}
            <ServiceActions
              supported={item.supported}
              openUiHref={item.openUiHref}
            />
          </div>

          <div data-service-status-column className="shrink-0">
            <ServiceStatusPill
              exists={item.exists}
              running={item.running}
              state={item.state}
              supported={item.supported}
              statusLabel={item.statusLabel}
              statusTone={item.statusTone}
            />
          </div>
        </div>
      </div>
      {item.inventoryNote ? (
        <p className="px-4 pb-4 text-sm text-muted-foreground">{item.inventoryNote}</p>
      ) : null}
    </div>
  )
}
