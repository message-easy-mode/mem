import { Link } from "react-router-dom"
import { RefreshCw, RotateCcw } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import { Button } from "@/components/ui/button"

type RestoreSessionsPageHeaderProps = {
  isRefreshing: boolean
  onRefresh: () => void
}

export function RestoreSessionsPageHeader({
  isRefreshing,
  onRefresh,
}: RestoreSessionsPageHeaderProps) {
  const { t } = useI18n()

  return (
    <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
      <div>
        <PageBreadcrumbs
          items={[
            { label: t("restoreSessions.header.backups"), to: "/backups" },
            { label: t("restoreSessions.header.restores") },
          ]}
        />
        <h1 className="text-2xl font-semibold tracking-tight">{t("restoreSessions.header.title")}</h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
          {t("restoreSessions.header.description")}
        </p>
      </div>

      <div className="flex flex-wrap gap-2">
        <Button
          variant="outline"
          size="sm"
          onClick={onRefresh}
          disabled={isRefreshing}
        >
          <RefreshCw className="mr-2 h-4 w-4" />
          {t("common.refresh")}
        </Button>
        <Button size="sm" asChild>
          <Link to="/backups">
            <RotateCcw className="mr-2 h-4 w-4" />
            {t("restoreSessions.header.restoreFromBackup")}
          </Link>
        </Button>
      </div>
    </div>
  )
}
