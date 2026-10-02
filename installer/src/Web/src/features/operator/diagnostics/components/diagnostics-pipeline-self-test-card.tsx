import { useMutation, useQueryClient } from "@tanstack/react-query"
import { CheckCircle2, CircleAlert, RefreshCw, ShieldCheck } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { ApiProblemAlert } from "@/components/operator/api-problem-alert"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader } from "@/components/ui/card"
import { runDiagnosticsPipelineSelfTest } from "../api/diagnostics.api"
import type { DiagnosticsPipelineSelfTestResponse } from "../api/diagnostics.types"

export function DiagnosticsPipelineSelfTestCard({
  enabled,
  titleHref,
  commandCentre = false,
}: {
  enabled: boolean
  titleHref?: string
  commandCentre?: boolean
}) {
  const { t } = useI18n()
  const queryClient = useQueryClient()
  const verification = useMutation({
    mutationFn: runDiagnosticsPipelineSelfTest,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["diagnostics", "overview"] }),
        queryClient.invalidateQueries({ queryKey: ["diagnostics", "logging-health"] }),
        queryClient.invalidateQueries({ queryKey: ["diagnostics", "events"] }),
      ])
    },
  })

  if (!enabled) return null

  return (
    <Card
      id="pipeline-verification"
      className={commandCentre ? "h-full transition-shadow hover:ring-foreground/20" : undefined}
    >
      <CardHeader className={commandCentre ? "flex-1" : undefined}>
        {commandCentre ? (
          <div className="flex items-start justify-between gap-3">
            <div className="flex min-w-0 items-start gap-3">
              <div
                className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border border-cyan-500/25 bg-cyan-500/10 text-cyan-400 shadow-sm"
                data-capability-icon="verification"
              >
                <ShieldCheck className="h-5 w-5" />
              </div>
              <div className="min-w-0">
                <h3 className="text-base leading-snug font-semibold">
                  {t("diagnostics.selfTest.title")}
                </h3>
                <CardDescription className="mt-1 leading-5">
                  {t("diagnostics.selfTest.description")}
                </CardDescription>
              </div>
            </div>
            <Badge className="shrink-0" variant="outline">
              {t("diagnostics.command.status.available")}
            </Badge>
          </div>
        ) : (
          <>
            <h3 className="flex items-center gap-2 text-base font-medium">
              <ShieldCheck className="h-4 w-4" />
              {titleHref ? (
                <Link
                  to={titleHref}
                  className="rounded-sm underline-offset-4 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                >
                  {t("diagnostics.selfTest.title")}
                </Link>
              ) : t("diagnostics.selfTest.title")}
            </h3>
            <CardDescription>{t("diagnostics.selfTest.description")}</CardDescription>
          </>
        )}
      </CardHeader>
      <CardContent className={commandCentre ? "mt-auto space-y-4" : "space-y-4"}>
        {!commandCentre ? (
          <p className="text-sm text-muted-foreground">
            {t("diagnostics.selfTest.noIncidentExplanation")}
          </p>
        ) : null}
        <Button
          type="button"
          variant="outline"
          size={commandCentre ? "sm" : "default"}
          onClick={() => verification.mutate()}
          disabled={verification.isPending}
        >
          <RefreshCw className={verification.isPending ? "mr-2 h-4 w-4 animate-spin" : "mr-2 h-4 w-4"} />
          {verification.isPending
            ? t("diagnostics.selfTest.running")
            : t("diagnostics.selfTest.action")}
        </Button>

        {verification.error ? (
          <ApiProblemAlert
            error={verification.error}
            title={t("diagnostics.selfTest.errorTitle")}
            fallbackDescription={t("diagnostics.selfTest.errorDescription")}
            showDiagnosticsLink={false}
          />
        ) : null}

        {verification.data ? <VerificationResult result={verification.data} /> : null}
      </CardContent>
    </Card>
  )
}

function VerificationResult({ result }: { result: DiagnosticsPipelineSelfTestResponse }) {
  const { t } = useI18n()
  const passed = result.status === "passed"

  return (
    <Alert variant={passed ? "default" : "destructive"}>
      {passed ? <CheckCircle2 className="h-4 w-4" /> : <CircleAlert className="h-4 w-4" />}
      <AlertTitle>
        {passed ? t("diagnostics.selfTest.passed") : t("diagnostics.selfTest.failed")}
      </AlertTitle>
      <AlertDescription className="space-y-3">
        <dl className="grid gap-2 text-sm sm:grid-cols-2">
          <div>
            <dt className="text-xs text-muted-foreground">{t("diagnostics.selfTest.verificationId")}</dt>
            <dd className="mt-1 break-all font-mono text-xs">{result.verificationId}</dd>
          </div>
          {result.eventId ? (
            <div>
              <dt className="text-xs text-muted-foreground">{t("diagnostics.selfTest.eventId")}</dt>
              <dd className="mt-1 break-all font-mono text-xs">{result.eventId}</dd>
            </div>
          ) : null}
        </dl>
        <ul className="space-y-2">
          {result.checks.map((check) => (
            <li key={check.code} className="flex flex-wrap items-center justify-between gap-2 rounded-md border px-3 py-2">
              <span>{checkLabel(check.code, t)}</span>
              <span className="flex items-center gap-2">
                <Badge variant={check.status === "failed" ? "destructive" : "outline"}>
                  {checkStatus(check.status, t)}
                </Badge>
                {check.warningCode ? <code className="text-xs text-muted-foreground">{check.warningCode}</code> : null}
              </span>
            </li>
          ))}
        </ul>
      </AlertDescription>
    </Alert>
  )
}

function checkLabel(code: string, t: ReturnType<typeof useI18n>["t"]): string {
  switch (code) {
    case "local_recorder_writable":
      return t("diagnostics.selfTest.check.localRecorder")
    case "safe_event_write":
      return t("diagnostics.selfTest.check.safeWrite")
    case "safe_event_read_back":
      return t("diagnostics.selfTest.check.safeReadBack")
    case "correlation_round_trip":
      return t("diagnostics.selfTest.check.correlation")
    case "seq_delivery":
      return t("diagnostics.selfTest.check.seq")
    default:
      return code
  }
}

function checkStatus(status: string, t: ReturnType<typeof useI18n>["t"]): string {
  switch (status) {
    case "passed":
      return t("diagnostics.selfTest.status.passed")
    case "failed":
      return t("diagnostics.selfTest.status.failed")
    case "disabled":
      return t("diagnostics.selfTest.status.disabled")
    case "not-configured":
      return t("diagnostics.selfTest.status.notConfigured")
    case "not-run":
      return t("diagnostics.selfTest.status.notRun")
    case "restart-pending":
      return t("diagnostics.selfTest.status.restartPending")
    case "verification-required":
      return t("diagnostics.selfTest.status.verificationRequired")
    default:
      return status
  }
}
