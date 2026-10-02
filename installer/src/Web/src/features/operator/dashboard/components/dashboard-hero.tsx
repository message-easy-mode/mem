import { AlertTriangle, CheckCircle2, CircleX } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import type { DashboardCapabilities, DashboardHero as DashboardHeroModel } from "../api/dashboard.types"
import {
  getDashboardActionLabel,
  getDashboardActionRoute,
  getDashboardHeroDescription,
  getDashboardHeroTitle,
  getDashboardStateLabel,
  getDashboardTone,
} from "./dashboard-state"
import { DashboardStatusPill } from "./dashboard-status-pill"

type Props = {
  hero: DashboardHeroModel
  capabilities: DashboardCapabilities
}

export function DashboardHero({ hero, capabilities }: Props) {
  const { t } = useI18n()
  const tone = getDashboardTone(hero.state)
  const route = hero.action ? getDashboardActionRoute(hero.action, capabilities) : null

  return (
    <section className={heroClassName(tone)}>
      <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
        <div className="flex gap-3">
          <div className={heroIconClassName(tone)}>
            <HeroStatusIcon tone={tone} />
          </div>
          <div>
            <div className="flex flex-wrap items-center gap-2.5">
              <h2 className="text-lg font-semibold tracking-tight">
                {getDashboardHeroTitle(hero, t)}
              </h2>
              <DashboardStatusPill tone={tone}>
                {getDashboardStateLabel(hero.state, t)}
              </DashboardStatusPill>
            </div>
            <p className="mt-1.5 max-w-3xl text-sm text-muted-foreground">
              {getDashboardHeroDescription(hero, t)}
            </p>
          </div>
        </div>

        {route && hero.action ? (
          <Button
            size="sm"
            className="w-full sm:w-auto sm:self-end lg:self-auto"
            asChild
          >
            <Link to={route}>{getDashboardActionLabel(hero.action, t)}</Link>
          </Button>
        ) : null}
      </div>
    </section>
  )
}

function HeroStatusIcon({ tone }: { tone: ReturnType<typeof getDashboardTone> }) {
  if (tone === "danger") {
    return <CircleX className="h-5 w-5" />
  }

  if (tone === "warning") {
    return <AlertTriangle className="h-5 w-5" />
  }

  return <CheckCircle2 className="h-5 w-5" />
}

function heroClassName(tone: ReturnType<typeof getDashboardTone>): string {
  switch (tone) {
    case "danger":
      return "rounded-2xl border border-red-500/20 bg-red-500/10 p-4 shadow-sm"
    case "warning":
      return "rounded-2xl border border-amber-500/20 bg-amber-500/10 p-4 shadow-sm"
    case "good":
      return "rounded-2xl border border-emerald-500/20 bg-emerald-500/10 p-4 shadow-sm"
    default:
      return "rounded-2xl border border-border bg-card/95 p-4 shadow-sm"
  }
}

function heroIconClassName(tone: ReturnType<typeof getDashboardTone>): string {
  switch (tone) {
    case "danger":
      return "inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-red-500/20 bg-red-500/10 text-red-300"
    case "warning":
      return "inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-amber-500/20 bg-amber-500/10 text-amber-300"
    case "good":
      return "inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-emerald-500/20 bg-emerald-500/10 text-emerald-300"
    default:
      return "inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-border bg-background/60 text-muted-foreground"
  }
}
