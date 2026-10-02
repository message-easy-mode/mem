import { AlertTriangle, FileQuestion, RefreshCw } from "lucide-react"
import { isRouteErrorResponse, Link, useRouteError } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader } from "@/components/ui/card"

type RouteFailurePanelProps = {
  readonly kind: "not-found" | "unexpected"
  readonly standalone?: boolean
}

function RouteFailurePanel({ kind, standalone = false }: RouteFailurePanelProps) {
  const { t } = useI18n()
  const notFound = kind === "not-found"

  const panel = (
    <div className="mx-auto w-full max-w-2xl space-y-6">
      {standalone ? (
        <div>
          <div className="text-xl font-semibold tracking-tight">MEM</div>
          <div className="text-xs font-medium uppercase tracking-[0.2em] text-muted-foreground">
            Control Plane
          </div>
        </div>
      ) : null}

      <Card className="border-amber-500/30 bg-amber-500/5">
        <CardHeader>
          <div className="flex items-start gap-3">
            <div className="mt-0.5 rounded-full border border-amber-500/30 bg-amber-500/10 p-2 text-amber-300">
              {notFound ? (
                <FileQuestion className="h-5 w-5" aria-hidden="true" />
              ) : (
                <AlertTriangle className="h-5 w-5" aria-hidden="true" />
              )}
            </div>
            <div>
              <h1 className="text-base leading-snug font-medium">
                {notFound
                  ? t("app.routeError.notFoundTitle")
                  : t("app.routeError.unexpectedTitle")}
              </h1>
              <CardDescription>
                {notFound
                  ? t("app.routeError.notFoundDescription")
                  : t("app.routeError.unexpectedDescription")}
              </CardDescription>
            </div>
          </div>
        </CardHeader>
        <CardContent className="flex flex-wrap gap-2">
          <Button asChild>
            <Link to="/dashboard">{t("app.routeError.openDashboard")}</Link>
          </Button>
          <Button asChild variant="outline">
            <Link to="/docs">{t("app.routeError.openDocumentation")}</Link>
          </Button>
          {!notFound ? (
            <Button type="button" variant="outline" onClick={() => window.location.reload()}>
              <RefreshCw className="mr-2 h-4 w-4" aria-hidden="true" />
              {t("app.routeError.reload")}
            </Button>
          ) : null}
        </CardContent>
      </Card>
    </div>
  )

  if (!standalone) {
    return panel
  }

  return (
    <main className="min-h-screen bg-background px-6 py-16 text-foreground">
      {panel}
    </main>
  )
}

export function RouteNotFoundPage() {
  return <RouteFailurePanel kind="not-found" />
}

export function GlobalRouteErrorPage() {
  const error = useRouteError()
  const notFound = isRouteErrorResponse(error) && error.status === 404

  return <RouteFailurePanel kind={notFound ? "not-found" : "unexpected"} standalone />
}
