import { useI18n } from "@/app/i18n/i18n-context"
import { cn } from "@/lib/utils"

export type ServicesFilter = "all" | "platform" | "support" | "stacks"

type Props = {
  value: ServicesFilter
  onChange: (value: ServicesFilter) => void
  counts: Record<ServicesFilter, number>
}

export function ServicesFilterTabs({ value, onChange, counts }: Props) {
  const { t } = useI18n()

  const filters: Array<{
    value: ServicesFilter
    label: string
    description: string
  }> = [
    {
      value: "all",
      label: t("services.filter.all"),
      description: t("services.filter.allDescription"),
    },
    {
      value: "platform",
      label: t("services.filter.platform"),
      description: t("services.filter.platformDescription"),
    },
    {
      value: "support",
      label: t("services.filter.support"),
      description: t("services.filter.supportDescription"),
    },
    {
      value: "stacks",
      label: t("services.filter.stacks"),
      description: t("services.filter.stacksDescription"),
    },
  ]

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-2">
        {filters.map((filter) => {
          const active = value === filter.value

          return (
            <button
              key={filter.value}
              type="button"
              onClick={() => onChange(filter.value)}
              className={cn(
                "inline-flex items-center gap-2 rounded-full border px-3 py-1.5 text-sm transition",
                active
                  ? "border-primary/30 bg-primary text-primary-foreground"
                  : "border-border bg-card text-muted-foreground hover:bg-accent hover:text-accent-foreground",
              )}
            >
              <span>{filter.label}</span>
              <span
                className={cn(
                  "rounded-full px-1.5 py-0.5 text-xs",
                  active
                    ? "bg-primary-foreground/20"
                    : "bg-muted text-muted-foreground",
                )}
              >
                {counts[filter.value]}
              </span>
            </button>
          )
        })}
      </div>

      <p className="max-w-3xl text-sm text-muted-foreground">
        {filters.find((filter) => filter.value === value)?.description}
      </p>
    </div>
  )
}
