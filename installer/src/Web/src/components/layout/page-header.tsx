import { Menu } from "lucide-react"
import { Link, useLocation } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { LanguageSelect } from "@/components/layout/language-select"
import { ThemeToggle } from "@/components/layout/theme-toggle"
import { OperatorAccountMenu } from "@/features/auth/operator-account-menu"
import { DiagnosticsAttentionMenu } from "@/features/operator/diagnostics/components/diagnostics-attention-menu"
import { useOptionalOperatorSession } from "@/features/auth/operator-session-context"
import { RuntimeContextIndicator } from "@/features/runtime-context/components/runtime-context-indicator"

function isPath(pathname: string, root: string) {
  return pathname === root || pathname.startsWith(`${root}/`)
}

function isSetupMode(pathname: string) {
  return (
    isPath(pathname, "/setup") ||
    isPath(pathname, "/start") ||
    isPath(pathname, "/preflight") ||
    isPath(pathname, "/install") ||
    isPath(pathname, "/activity")
  )
}

export function AppHeader({ onMenuOpen }: { onMenuOpen?: () => void }) {
  const location = useLocation()
  const { t } = useI18n()
  const operatorSession = useOptionalOperatorSession()
  const setupMode = isSetupMode(location.pathname)

  return (
    <header className="flex h-16 w-full items-center justify-between gap-3 py-1.5 pl-3 pr-3 sm:pl-3 sm:pr-6 lg:pl-3 lg:pr-8">
      <div className="flex min-w-0 items-center gap-0">
        {onMenuOpen ? (
          <Button
            type="button"
            variant="outline"
            size="icon-lg"
            className="mr-2 shrink-0 cursor-pointer border-border bg-muted/70 text-foreground shadow-sm hover:bg-muted hover:text-foreground lg:hidden dark:bg-muted/30 dark:hover:bg-muted/50"
            aria-label={t("navigation.openMenu")}
            onClick={onMenuOpen}
          >
            <Menu className="h-5 w-5" aria-hidden="true" />
          </Button>
        ) : null}

        <Link
          to="/"
          aria-label={t("header.homeAria")}
          className="-ml-2 flex min-w-0 cursor-pointer items-center gap-1 rounded-xl px-1.5 py-0.5 transition-colors hover:bg-muted/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          <div className="flex h-11 w-11 shrink-0 translate-x-[4px] items-center justify-center">
            <img
              src="/mem-logo-mark.png"
              alt={t("header.logoAlt")}
              className="h-9 w-9 object-contain"
            />
          </div>

          <div className="-ml-[2px] flex h-11 min-w-0 items-center">
            <div className="translate-y-px truncate text-base font-semibold leading-[1.2] tracking-tight text-foreground sm:text-lg">
              {t("header.operatorTitle")}
            </div>
          </div>
        </Link>

      </div>

      <div
        className="flex shrink-0 items-center gap-1.5 sm:gap-2"
        data-testid="global-header-utilities"
      >
        {operatorSession && !setupMode ? <RuntimeContextIndicator /> : null}
        <LanguageSelect />
        <ThemeToggle />

        {operatorSession && !setupMode ? (
          <>
            <DiagnosticsAttentionMenu />
            <OperatorAccountMenu />
          </>
        ) : (
          <Badge variant="secondary" className="hidden sm:inline-flex">
            {setupMode ? t("header.installerBadge") : t("header.operatorBadge")}
          </Badge>
        )}
      </div>
    </header>
  )
}
