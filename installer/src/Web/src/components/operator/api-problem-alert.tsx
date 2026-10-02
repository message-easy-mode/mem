import { AlertTriangle, RotateCw, Wrench } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  buildDiagnosticsIncidentHref,
  getMemApiProblem,
  getMemApiProblemCode,
  getMemApiProblemDetail,
} from "@/lib/api-problem"

type ApiProblemAlertProps = {
  error: unknown
  fallbackDescription: string
  title?: string
  onRetry?: () => void
  retrying?: boolean
  showDiagnosticsLink?: boolean
  variant?: "default" | "destructive"
  className?: string
}

export function ApiProblemAlert({
  error,
  fallbackDescription,
  title,
  onRetry,
  retrying = false,
  showDiagnosticsLink = true,
  variant = "destructive",
  className,
}: ApiProblemAlertProps) {
  const { t } = useI18n()
  const problem = getMemApiProblem(error)
  const code = getMemApiProblemCode(error)
  const diagnosticsHref = showDiagnosticsLink
    ? buildDiagnosticsIncidentHref(error)
    : undefined
  const detail = getMemApiProblemDetail(error, fallbackDescription)

  return (
    <Alert variant={variant} className={className}>
      <AlertTriangle className="h-4 w-4" />
      <AlertTitle>{title ?? problem?.title ?? t("apiProblem.defaultTitle")}</AlertTitle>
      <AlertDescription className="space-y-3">
        <p>{detail}</p>

        {problem?.suggestedAction ? (
          <p>
            <span className="font-medium">{t("apiProblem.suggestedAction")}:</span>{" "}
            {problem.suggestedAction}
          </p>
        ) : null}

        {problem?.incidentId || code || problem?.traceId ? (
          <div className="flex flex-wrap gap-2" aria-label={t("apiProblem.references")}>
            {problem?.incidentId ? (
              <Badge variant="outline">
                {t("apiProblem.incidentReference")}: {problem.incidentId}
              </Badge>
            ) : null}
            {code ? (
              <Badge variant="outline">
                {t("apiProblem.problemCode")}: {code}
              </Badge>
            ) : null}
            {problem?.traceId ? (
              <Badge variant="outline">
                {t("apiProblem.traceReference")}: {problem.traceId}
              </Badge>
            ) : null}
          </div>
        ) : null}

        <div className="flex flex-wrap gap-2">
          {diagnosticsHref ? (
            <Button asChild variant="outline" size="sm">
              <Link to={diagnosticsHref}>
                <Wrench className="mr-2 h-4 w-4" />
                {t("apiProblem.openDiagnostics")}
              </Link>
            </Button>
          ) : null}

          {onRetry ? (
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={onRetry}
              disabled={retrying}
            >
              <RotateCw className={retrying ? "mr-2 h-4 w-4 animate-spin" : "mr-2 h-4 w-4"} />
              {t("apiProblem.retry")}
            </Button>
          ) : null}
        </div>
      </AlertDescription>
    </Alert>
  )
}
