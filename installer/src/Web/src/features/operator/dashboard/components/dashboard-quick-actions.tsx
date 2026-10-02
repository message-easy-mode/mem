import { Activity, DatabaseBackup, Globe2, MessageSquarePlus, RadioTower } from "lucide-react"
import type { LucideIcon } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type { DashboardCapabilities } from "../api/dashboard.types"

type QuickAction = {
  labelKey:
    | "dashboard.action.createChatServer"
    | "dashboard.action.viewChatServers"
    | "dashboard.action.manageDomains"
    | "dashboard.action.viewBackups"
    | "dashboard.action.viewRestores"
    | "services.coturn.title"
  route: string
  icon: LucideIcon
}

type Props = {
  capabilities: DashboardCapabilities
}

export function DashboardQuickActions({ capabilities }: Props) {
  const { t } = useI18n()
  const actions = getQuickActions(capabilities)

  return (
    <Card size="sm" role="region" aria-labelledby="dashboard-quick-actions-title">
      <CardHeader className="pb-2">
        <CardTitle id="dashboard-quick-actions-title" className="text-base">
          {t("dashboard.quickActions.title")}
        </CardTitle>
        <p className="mt-1 text-sm text-muted-foreground">
          {t("dashboard.quickActions.description")}
        </p>
      </CardHeader>
      <CardContent>
        <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-2">
          {actions.map((action) => {
            const Icon = action.icon
            return (
              <Link
                key={action.route}
                to={action.route}
                className="flex min-h-14 items-center gap-2.5 rounded-xl border border-border bg-background/40 px-3 py-2 text-sm font-medium leading-tight transition-colors hover:bg-muted"
              >
                <span className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-lg border border-border bg-card text-muted-foreground">
                  <Icon className="h-3.5 w-3.5" />
                </span>
                <span>{t(action.labelKey)}</span>
              </Link>
            )
          })}
        </div>
      </CardContent>
    </Card>
  )
}

function getQuickActions(capabilities: DashboardCapabilities): QuickAction[] {
  const actions: QuickAction[] = [
    {
      labelKey: "dashboard.action.viewChatServers",
      route: "/stacks",
      icon: MessageSquarePlus,
    },
    {
      labelKey: "dashboard.action.viewBackups",
      route: "/backups",
      icon: DatabaseBackup,
    },
    {
      labelKey: "dashboard.action.viewRestores",
      route: "/restores",
      icon: Activity,
    },
    {
      labelKey: "services.coturn.title",
      route: "/services/coturn",
      icon: RadioTower,
    },
  ]

  if (capabilities.canOperate) {
    actions.unshift({
      labelKey: "dashboard.action.createChatServer",
      route: "/stacks/new",
      icon: MessageSquarePlus,
    })
  }

  if (capabilities.canManagePlatform) {
    actions.splice(capabilities.canOperate ? 1 : 0, 0, {
      labelKey: "dashboard.action.manageDomains",
      route: "/domains",
      icon: Globe2,
    })
  }

  return actions
}
