import { RefreshCw } from "lucide-react"
import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import type { ManagedServiceListItem } from "../api/runtime-ui.types"
import { ManagedServiceSection } from "./managed-service-section"
import { ServicesFilterTabs, type ServicesFilter } from "./services-filter-tabs"

type Props = {
  inventoryNotice?: string
  items?: ManagedServiceListItem[]
  onReviewRetirement?: (target: NonNullable<ManagedServiceListItem["retirementReview"]>) => void
  activeFilter?: ServicesFilter
  onFilterChange?: (filter: ServicesFilter) => void
  refreshing?: boolean
  onRefresh?: () => void
}

export function ManagedServicesBoard({
  inventoryNotice,
  onReviewRetirement,
  items = [],
  activeFilter = "all",
  onFilterChange = () => {},
  refreshing = false,
  onRefresh = () => {},
}: Props) {
  const { t } = useI18n()

  const platform = items.filter((item) => item.category === "platform")
  const support = items.filter((item) => item.category === "support")
  const stacks = items.filter((item) => item.category === "stack")

  return (
    <div className="space-y-8">
      <header className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            {t("services.title")}
          </h1>
          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
            {t("services.description")}
          </p>
        </div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={onRefresh}
          disabled={refreshing}
        >
          <RefreshCw className={`mr-2 h-4 w-4 ${refreshing ? "animate-spin" : ""}`} />
          {t("services.activity.refresh")}
        </Button>
      </header>

      {inventoryNotice ? (
        <p role="status" className="rounded-xl border border-border bg-muted/40 p-4 text-sm text-muted-foreground">
          {inventoryNotice}
        </p>
      ) : null}

      <ServicesFilterTabs
        value={activeFilter}
        onChange={onFilterChange}
        counts={{
          all: items.length,
          platform: platform.length,
          support: support.length,
          stacks: stacks.length,
        }}
      />

      {(activeFilter === "all" || activeFilter === "platform") && (
        <ManagedServiceSection
          title={t("services.section.platform.title")}
          description={t("services.section.platform.description")}
          items={platform}
        />
      )}

      {(activeFilter === "all" || activeFilter === "support") && (
        <ManagedServiceSection
          title={t("services.section.support.title")}
          description={t("services.section.support.description")}
          items={support}
        />
      )}

      {(activeFilter === "all" || activeFilter === "stacks") && (
        <ManagedServiceSection
          title={t("services.section.stacks.title")}
          description={t("services.section.stacks.description")}
          items={stacks}
          onReviewRetirement={onReviewRetirement}
          emptyTitle={t("services.section.stacks.emptyTitle")}
          emptyDescription={t("services.section.stacks.emptyDescription")}
        />
      )}
    </div>
  )
}
