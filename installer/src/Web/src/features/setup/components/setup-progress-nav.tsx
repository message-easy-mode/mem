import { Link, useLocation } from "react-router-dom"

import { cn } from "@/lib/utils"

const steps = [
  { label: "Start", to: "/setup/start" },
  { label: "Check server", to: "/setup/check-server" },
  { label: "Domain", to: "/setup/domain" },
  { label: "Review", to: "/setup/review" },
]

export function SetupStepNav() {
  const location = useLocation()

  return (
    <nav className="flex flex-wrap gap-2">
      {steps.map((step) => {
        const active =
          location.pathname === step.to || location.pathname.startsWith(`${step.to}/`)

        return (
          <Link
            key={step.to}
            to={step.to}
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