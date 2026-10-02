import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { cn } from "@/lib/utils"

type StackWorkspaceArea =
  | "overview"
  | "services"
  | "storage-media"
  | "users"
  | "backups"
  | "recovery"
  | "network-domains"
  | "voice-video"
  | "federation"
  | "diagnostics"

type StackWorkspaceNavigationProps = {
  slugOrId: string
  activeArea?: StackWorkspaceArea
}

type ImplementedArea = {
  key: StackWorkspaceArea
  labelKey: TranslationKey
  to: string
}

/**
 * Compact, route-based navigation for one stack workspace.
 *
 * The top-level surface intentionally stays small: runtime evidence lives under
 * Services, catalog/restore evidence lives under Recovery, and sensitive user
 * and diagnostic workflows stay separate. Retired deep links redirect to these
 * canonical areas from the app router.
 */
export function StackWorkspaceNavigation({
  slugOrId,
  activeArea = "overview",
}: StackWorkspaceNavigationProps) {
  const { t } = useI18n()
  const overviewPath = `/stacks/${encodeURIComponent(slugOrId)}`
  const normalisedActiveArea = normaliseActiveArea(activeArea)
  const implementedAreas: ImplementedArea[] = [
    { key: "overview", labelKey: "stacks.workspace.navigation.overview", to: overviewPath },
    { key: "services", labelKey: "stacks.workspace.navigation.services", to: `${overviewPath}/services` },
    { key: "users", labelKey: "stacks.workspace.navigation.users", to: `${overviewPath}/users` },
    { key: "recovery", labelKey: "stacks.workspace.navigation.recovery", to: `${overviewPath}/recovery` },
    {
      key: "federation",
      labelKey: "stacks.workspace.navigation.federation",
      to: `${overviewPath}/federation`,
    },
    {
      key: "diagnostics",
      labelKey: "stacks.workspace.navigation.diagnostics",
      to: `${overviewPath}/diagnostics`,
    },
  ]

  return (
    <nav aria-label={t("stacks.workspace.navigation.ariaLabel")} className="border-b border-border">
      <div className="flex flex-wrap items-end gap-1 px-1">
        {implementedAreas.map((area) => {
          const isActive = area.key === normalisedActiveArea

          return (
            <Link
              key={area.key}
              to={area.to}
              aria-current={isActive ? "page" : undefined}
              className={cn(
                "relative -mb-px inline-flex h-11 shrink-0 items-center border-b-2 px-3 text-sm font-medium whitespace-nowrap transition-colors",
                "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/50",
                isActive
                  ? "border-primary text-primary"
                  : "border-transparent text-muted-foreground hover:border-border hover:text-foreground",
              )}
            >
              {t(area.labelKey)}
            </Link>
          )
        })}
      </div>
    </nav>
  )
}

function normaliseActiveArea(area: StackWorkspaceArea): StackWorkspaceArea {
  switch (area) {
    case "storage-media":
    case "network-domains":
    case "voice-video":
      return "services"
    case "backups":
      return "recovery"
    default:
      return area
  }
}
