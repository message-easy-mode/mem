import { ArrowRight, MessageSquarePlus } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type { DashboardCapabilities, DashboardOnboarding } from "../api/dashboard.types"
import { getDashboardActionLabel, getDashboardActionRoute } from "./dashboard-state"

type Props = {
  onboarding: DashboardOnboarding
  capabilities: DashboardCapabilities
}

export function DashboardOnboardingCard({ onboarding, capabilities }: Props) {
  const { t } = useI18n()
  const nextStep = onboarding.nextSteps[0] ?? null
  const route = nextStep?.action
    ? getDashboardActionRoute(nextStep.action, capabilities)
    : null

  return (
    <Card size="sm" className="overflow-hidden">
      <CardHeader className="pb-2">
        <div className="flex items-center gap-3">
          <div className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-teal-500/20 bg-teal-500/10 text-teal-300">
            <MessageSquarePlus className="h-4 w-4" />
          </div>
          <div>
            <CardTitle className="text-base">{t("dashboard.onboarding.title")}</CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("dashboard.onboarding.description")}
            </p>
          </div>
        </div>
      </CardHeader>
      <CardContent>
        <div className="rounded-xl border border-dashed border-border bg-background/40 p-4">
          <div className="text-sm font-medium">{t("dashboard.onboarding.emptyTitle")}</div>
          <p className="mt-2 max-w-xl text-sm text-muted-foreground">
            {t("dashboard.onboarding.emptyDescription")}
          </p>

          {route && nextStep?.action ? (
            <Button className="mt-3" size="sm" asChild>
              <Link to={route}>
                {getDashboardActionLabel(nextStep.action, t)}
                <ArrowRight className="ml-2 h-4 w-4" />
              </Link>
            </Button>
          ) : (
            <p className="mt-3 text-sm text-muted-foreground">
              {t("dashboard.onboarding.blocked")}
            </p>
          )}
        </div>
      </CardContent>
    </Card>
  )
}
