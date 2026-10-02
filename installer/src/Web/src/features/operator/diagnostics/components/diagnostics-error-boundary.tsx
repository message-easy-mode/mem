import { Component, type PropsWithChildren, type ReactNode } from "react"
import { AlertTriangle, RefreshCw } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"

type RenderErrorBoundaryProps = PropsWithChildren<{
  fallback: (reset: () => void) => ReactNode
  resetKey?: string
}>

type RenderErrorBoundaryState = {
  hasError: boolean
}

class RenderErrorBoundary extends Component<
  RenderErrorBoundaryProps,
  RenderErrorBoundaryState
> {
  state: RenderErrorBoundaryState = { hasError: false }

  static getDerivedStateFromError(): RenderErrorBoundaryState {
    return { hasError: true }
  }

  componentDidUpdate(previousProps: RenderErrorBoundaryProps) {
    if (
      this.state.hasError &&
      previousProps.resetKey !== this.props.resetKey
    ) {
      this.setState({ hasError: false })
    }
  }

  private readonly reset = () => {
    this.setState({ hasError: false })
  }

  render() {
    if (this.state.hasError) {
      return this.props.fallback(this.reset)
    }

    return this.props.children
  }
}

type DiagnosticsSectionErrorBoundaryProps = PropsWithChildren<{
  resetKey?: string
}>

export function DiagnosticsSectionErrorBoundary({
  children,
  resetKey,
}: DiagnosticsSectionErrorBoundaryProps) {
  const { t } = useI18n()

  return (
    <RenderErrorBoundary
      resetKey={resetKey}
      fallback={(reset) => (
        <Alert className="border-amber-500/30" role="alert">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("diagnostics.errorBoundary.sectionTitle")}</AlertTitle>
          <AlertDescription className="space-y-3">
            <p>{t("diagnostics.errorBoundary.sectionDescription")}</p>
            <Button type="button" variant="outline" size="sm" onClick={reset}>
              <RefreshCw className="mr-2 h-4 w-4" />
              {t("diagnostics.errorBoundary.retry")}
            </Button>
          </AlertDescription>
        </Alert>
      )}
    >
      {children}
    </RenderErrorBoundary>
  )
}

export function DiagnosticsRouteErrorPage() {
  const { t } = useI18n()

  return (
    <div className="space-y-6">
      <div className="space-y-2">
        <h1 className="text-2xl font-semibold tracking-tight">
          {t("diagnostics.title")}
        </h1>
        <p className="max-w-3xl text-sm text-muted-foreground">
          {t("diagnostics.description")}
        </p>
      </div>

      <Alert className="border-amber-500/30" role="alert">
        <AlertTriangle className="h-4 w-4" />
        <AlertTitle>{t("diagnostics.errorBoundary.routeTitle")}</AlertTitle>
        <AlertDescription className="space-y-3">
          <p>{t("diagnostics.errorBoundary.routeDescription")}</p>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => window.location.reload()}
          >
            <RefreshCw className="mr-2 h-4 w-4" />
            {t("diagnostics.errorBoundary.reload")}
          </Button>
        </AlertDescription>
      </Alert>
    </div>
  )
}
