import { ExternalLink, MessageSquareText } from "lucide-react"
import { Link } from "react-router-dom"

import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type { DashboardStacksSummary } from "../api/dashboard.types"
import { getDashboardStateLabel, getDashboardTone } from "./dashboard-state"
import { DashboardStatusPill } from "./dashboard-status-pill"

type Props = {
  stacks: DashboardStacksSummary
}

export function DashboardManagedStacksCard({ stacks }: Props) {
  const { language, t } = useI18n()

  return (
    <Card size="sm">
      <CardHeader className="pb-2">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex items-center gap-3">
            <div className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-cyan-500/20 bg-cyan-500/10 text-cyan-300">
              <MessageSquareText className="h-4 w-4" />
            </div>
            <div>
              <CardTitle className="text-base">{t("dashboard.managedStacks.title")}</CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">
                {t("dashboard.managedStacks.description", { count: stacks.total })}
              </p>
            </div>
          </div>
          <Button variant="outline" size="sm" asChild>
            <Link to="/stacks">{t("dashboard.managedStacks.view")}</Link>
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        {stacks.items.length > 0 ? (
          <div className="divide-y divide-border overflow-hidden rounded-xl border border-border">
            {stacks.items.map((stack) => (
              <article key={stack.stackId} className="space-y-2 px-3 py-3 sm:flex sm:items-center sm:justify-between sm:gap-4 sm:space-y-0">
                <div className="min-w-0">
                  <Link
                    to={`/stacks/${encodeURIComponent(stack.slug)}`}
                    className="inline-flex items-center gap-1 font-medium hover:underline"
                  >
                    {stack.slug}
                    <ExternalLink className="h-3.5 w-3.5 text-muted-foreground" />
                  </Link>
                  <div className="mt-1 flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted-foreground">
                    <PublicEndpoint label={t("dashboard.stacks.matrix")} value={stack.matrixPublicBaseUrl} />
                    <PublicEndpoint label={t("dashboard.stacks.element")} value={stack.elementPublicBaseUrl} />
                  </div>
                </div>
                <div className="flex shrink-0 flex-wrap items-center gap-2 sm:flex-col sm:items-end">
                  <DashboardStatusPill tone={getDashboardTone(stack.lastVerification.state)}>
                    {getDashboardStateLabel(stack.lastVerification.state, t)}
                  </DashboardStatusPill>
                  <span className="text-xs text-muted-foreground">
                    {stack.lastVerification.verifiedAtUtc
                      ? t("dashboard.stacks.verifiedAt", {
                          value: formatDateTime(stack.lastVerification.verifiedAtUtc, language),
                        })
                      : t("dashboard.stacks.notYetVerified")}
                  </span>
                </div>
              </article>
            ))}
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">{t("dashboard.managedStacks.noSummaries")}</p>
        )}

        {stacks.truncated ? (
          <p className="text-sm text-muted-foreground">
            {t("dashboard.managedStacks.truncated", { shown: stacks.items.length, total: stacks.total })}
          </p>
        ) : null}
      </CardContent>
    </Card>
  )
}

function PublicEndpoint({ label, value }: { label: string; value: string | null }) {
  const displayValue = getPublicEndpointLabel(value)

  if (!displayValue) {
    return null
  }

  return (
    <span>
      {label}: <span className="font-mono">{displayValue}</span>
    </span>
  )
}

function getPublicEndpointLabel(value: string | null): string | null {
  if (!value) {
    return null
  }

  try {
    return new URL(value).host
  } catch {
    return null
  }
}
