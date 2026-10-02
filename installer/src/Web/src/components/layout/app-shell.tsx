import { useState, type PropsWithChildren } from "react"
import { useLocation } from "react-router-dom"

import { AppHeader } from "@/components/layout/page-header"
import { SideNav } from "@/components/layout/side-nav"
import { useOptionalOperatorSession } from "@/features/auth/operator-session-context"
import { RuntimeContextWarningBanner } from "@/features/runtime-context/components/runtime-context-banner"
import { RuntimeContextDetailsProvider } from "@/features/runtime-context/components/runtime-context-details"
import { DiagnosticsSetupFinishBanner } from "@/features/operator/diagnostics/components/diagnostics-setup-finish-banner"
import { cn } from "@/lib/utils"

function getCanvasClass(pathname: string) {
  const isRestoreSurface = /^\/restores(?:\/|$)/.test(pathname)
  const isStackWorkspace = /^\/stacks\/(?!new(?:\/|$))[^/]+(?:\/|$)/.test(pathname)
  const isDenseOperatorSurface =
    /^\/diagnostics(?:\/|$)/.test(pathname) ||
    /^\/chat-servers\/[^/]+$/.test(pathname) ||
    /^\/backups(?:\/|$)/.test(pathname) ||
    /^\/docs(?:\/|$)/.test(pathname) ||
    /^\/setup\/docs(?:\/|$)/.test(pathname) ||
    isRestoreSurface ||
    isStackWorkspace

  if (isDenseOperatorSurface) {
    return "max-w-[1760px]"
  }

  return "max-w-7xl"
}

export function AppShell({ children }: PropsWithChildren) {
  const { pathname } = useLocation()
  const operatorSession = useOptionalOperatorSession()
  const [mobileNavOpen, setMobileNavOpen] = useState(false)
  const isDiagnosticsSurface = /^\/diagnostics(?:\/|$)/.test(pathname)

  const shell = (
    <div className="min-h-screen bg-background text-foreground">
      <div className="sticky top-0 z-40 border-b border-border bg-card/95 backdrop-blur dark:bg-background/85">
        <AppHeader onMenuOpen={() => setMobileNavOpen(true)} />
      </div>

      {operatorSession ? <RuntimeContextWarningBanner /> : null}

      <div className="flex min-h-[calc(100vh-4rem)]">
        <SideNav mobileOpen={mobileNavOpen} onMobileOpenChange={setMobileNavOpen} />

        <main className="min-w-0 flex-1 px-4 py-6 sm:px-6 sm:py-8 lg:px-8 2xl:px-10">
          <div className={cn("mx-auto w-full", getCanvasClass(pathname))}>
            {operatorSession && isDiagnosticsSurface ? <DiagnosticsSetupFinishBanner /> : null}
            {children}
          </div>
        </main>
      </div>
    </div>
  )

  return operatorSession ? (
    <RuntimeContextDetailsProvider>{shell}</RuntimeContextDetailsProvider>
  ) : shell
}
