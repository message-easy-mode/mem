import { useI18n } from "@/app/i18n/i18n-context"
import type { ManagedServiceListItem } from "@/features/operator/services/api/runtime-ui.types"

import { ManagedServiceRow } from "./managed-service-row"

type Props = {
  title: string
  description: string
  items: ManagedServiceListItem[]
  onReviewRetirement?: (target: NonNullable<ManagedServiceListItem["retirementReview"]>) => void
  emptyTitle?: string
  emptyDescription?: string
}

export function ManagedServiceSection({
  title,
  description,
  items,
  emptyTitle,
  emptyDescription,
  onReviewRetirement,
}: Props) {
  const { t } = useI18n()

  return (
    <section className="space-y-4">
      <div>
        <h2 className="text-xl font-semibold tracking-tight">{title}</h2>
        <p className="mt-1 max-w-4xl text-sm text-muted-foreground">
          {description}
        </p>
      </div>

      {items.length > 0 ? (
        <div className="space-y-3">
          {items.map((item) => (
            <ManagedServiceRow key={item.serviceName} item={item} onReviewRetirement={onReviewRetirement} />
          ))}
        </div>
      ) : (
        <div className="rounded-2xl border border-dashed border-border bg-card/60 p-6">
          <div className="text-sm font-medium text-foreground">
            {emptyTitle ?? t("services.section.emptyTitle")}
          </div>
          <p className="mt-1 text-sm text-muted-foreground">
            {emptyDescription ?? t("services.section.emptyDescription")}
          </p>
        </div>
      )}
    </section>
  )
}
