import { Link, useLocation, useParams } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { cn } from "@/lib/utils"

export function SetupStepNav() {
  const location = useLocation()
  const { installationId } = useParams()
  const { t } = useI18n()

  const steps = [
    {
      label: t("navigation.setup.start"),
      to: "/setup/start",
      match: "/setup/start",
    },
    {
      label: t("navigation.setup.checkServer"),
      to: "/setup/check-server",
      match: "/setup/check-server",
    },
    {
      label: t("navigation.setup.domain"),
      to: "/setup/domain",
      match: "/setup/domain",
    },
    {
      label: t("navigation.setup.review"),
      to: "/setup/review",
      match: "/setup/review",
    },
    {
      label: t("navigation.setup.installMem"),
      to: installationId ? `/setup/install/${installationId}` : "/setup/install",
      match: "/setup/install",
    },
    {
      label: t("navigation.setup.verify"),
      to: installationId ? `/setup/verify/${installationId}` : "/setup/install",
      match: "/setup/verify",
    },
    {
      label: t("navigation.setup.finish"),
      to: installationId ? `/setup/handoff/${installationId}` : "/setup/install",
      match: "/setup/handoff",
    },
  ]

  return (
    <nav aria-label={t("setup.progress.ariaLabel")} className="flex flex-wrap gap-2">
      {steps.map((step) => {
        const active =
          location.pathname === step.match ||
          location.pathname.startsWith(`${step.match}/`)

        return (
          <Link
            key={step.match}
            to={step.to}
            aria-current={active ? "step" : undefined}
            className={cn(
              "rounded-full border px-3 py-1.5 text-sm transition",
              active
                ? "border-primary/30 bg-primary text-primary-foreground"
                : "border-border bg-card text-muted-foreground hover:bg-accent hover:text-accent-foreground",
            )}
          >
            {step.label}
          </Link>
        )
      })}
    </nav>
  )
}
