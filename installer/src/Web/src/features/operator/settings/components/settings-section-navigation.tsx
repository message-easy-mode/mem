import { Link, useLocation } from "react-router-dom"
import { KeyRound, Network, ShieldCheck } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { cn } from "@/lib/utils"

const sections = [
  {
    to: "/settings/security-access",
    labelKey: "settings.navigation.security" as const,
    descriptionKey: "settings.navigation.securityDescription" as const,
    icon: ShieldCheck,
  },
  {
    to: "/settings/network-security",
    labelKey: "settings.navigation.network" as const,
    descriptionKey: "settings.navigation.networkDescription" as const,
    icon: Network,
  },
  {
    to: "/settings/nginx-proxy-manager",
    labelKey: "settings.navigation.npm" as const,
    descriptionKey: "settings.navigation.npmDescription" as const,
    icon: KeyRound,
  },
]

export function SettingsSectionNavigation() {
  const { pathname } = useLocation()
  const { t } = useI18n()

  return (
    <nav aria-label={t("settings.navigation.aria")} className="grid gap-3 md:grid-cols-2 lg:grid-cols-3">
      {sections.map((section) => {
        const active = pathname === section.to || pathname.startsWith(`${section.to}/`)
        const Icon = section.icon
        return (
          <Link
            key={section.to}
            to={section.to}
            aria-current={active ? "page" : undefined}
            className={cn(
              "rounded-xl border p-4 transition",
              active
                ? "border-primary/50 bg-primary/10"
                : "border-border bg-card hover:border-primary/30 hover:bg-muted/30",
            )}
          >
            <div className="flex items-start gap-3">
              <Icon className="mt-0.5 h-5 w-5 shrink-0" aria-hidden="true" />
              <div className="space-y-1">
                <div className="font-medium">{t(section.labelKey)}</div>
                <p className="text-sm text-muted-foreground">
                  {t(section.descriptionKey)}
                </p>
              </div>
            </div>
          </Link>
        )
      })}
    </nav>
  )
}
