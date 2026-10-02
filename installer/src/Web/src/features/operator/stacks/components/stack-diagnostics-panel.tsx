import { AlertTriangle, CheckCircle2, LoaderCircle, Stethoscope } from "lucide-react"

import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

import type { RuntimeStackDoctorResponse } from "../api/stacks.types"
import { StackStatusPill } from "./stack-status-pill"

type StackDiagnosticsPanelProps = {
  doctor: RuntimeStackDoctorResponse | null
  isRunning: boolean
}

/**
 * Presents the latest durable Doctor report exposed by the Control Plane,
 * replacing it immediately with the result of a newly completed Doctor run.
 */
export function StackDiagnosticsPanel({ doctor, isRunning }: StackDiagnosticsPanelProps) {
  const { t } = useI18n()

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Stethoscope className="h-5 w-5" />
            {t("stacks.diagnostics.title")}
          </CardTitle>
          <p className="text-sm text-muted-foreground">
            {t("stacks.diagnostics.description")}
          </p>
        </CardHeader>
        <CardContent className="text-sm text-muted-foreground">
          {t("stacks.diagnostics.sessionScope")}
        </CardContent>
      </Card>

      {isRunning ? (
        <Card aria-live="polite">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <LoaderCircle className="h-5 w-5 animate-spin" />
              {t("stacks.diagnostics.runningTitle")}
            </CardTitle>
          </CardHeader>
          <CardContent className="text-sm text-muted-foreground">
            {t("stacks.diagnostics.runningDescription")}
          </CardContent>
        </Card>
      ) : null}

      {!doctor && !isRunning ? (
        <Card>
          <CardHeader>
            <CardTitle>{t("stacks.diagnostics.reportTitle")}</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
              {t("stacks.diagnostics.empty")}
            </div>
          </CardContent>
        </Card>
      ) : doctor ? (
        <StackDoctorReportEvidence
          doctor={doctor}
          title={t(isRunning
            ? "stacks.diagnostics.previousReportTitle"
            : "stacks.diagnostics.reportTitle")}
          description={doctor.allPassed
            ? t("stacks.diagnostics.passedDescription")
            : t("stacks.diagnostics.failedDescription")}
        />
      ) : null}
    </div>
  )
}

export function StackDoctorReportEvidence({
  doctor,
  title,
  description,
}: {
  doctor: RuntimeStackDoctorResponse
  title: string
  description: string
}) {
  const { language, t } = useI18n()
  const status = doctor.allPassed ? "passed" : "failed"
  const notRecorded = t("stacks.common.notRecorded")
  const notReported = t("stacks.diagnostics.notReported")

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <CardTitle>{title}</CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">{description}</p>
            </div>
            <StackStatusPill status={status} />
          </div>
        </CardHeader>
        <CardContent>
          <div className="grid gap-4 text-sm sm:grid-cols-2 xl:grid-cols-4">
            <DiagnosticFact
              label={t("stacks.diagnostics.checked")}
              value={formatDoctorDate(doctor.checkedAtUtc, language, notRecorded)}
            />
            <DiagnosticFact label={t("stacks.diagnostics.checks")}
              value={String(doctor.checks.length)} />
            <DiagnosticFact label={t("stacks.diagnostics.operation")}
              value={doctor.operationId ?? notRecorded} />
            <DiagnosticFact label={t("stacks.diagnostics.report")}
              value={doctor.reportId ?? notRecorded} />
          </div>

          <div className="mt-4 grid gap-4 text-sm sm:grid-cols-2">
            <DiagnosticFact
              label={t("stacks.diagnostics.lastVerified")}
              value={formatDoctorDate(doctor.lastVerifiedAtUtc, language, notRecorded)}
            />
            <DiagnosticFact
              label={t("stacks.diagnostics.knownRuntimeStatus")}
              value={doctor.lastVerifiedStatus ?? notRecorded}
            />
          </div>

          {doctor.detail ? (
            <div
              className={
                doctor.allPassed
                  ? "mt-4 rounded-xl border border-emerald-500/20 bg-emerald-500/10 p-4 text-sm"
                  : "mt-4 rounded-xl border border-red-500/20 bg-red-500/10 p-4 text-sm"
              }
            >
              {doctor.detail}
            </div>
          ) : null}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{t("stacks.diagnostics.checkResultsTitle")}</CardTitle>
          <p className="text-sm text-muted-foreground">
            {t("stacks.diagnostics.checkResultsDescription")}
          </p>
        </CardHeader>
        <CardContent>
          {doctor.checks.length === 0 ? (
            <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
              {t("stacks.diagnostics.noChecks")}
            </div>
          ) : (
            <div className="space-y-3">
              {doctor.checks.map((check) => (
                <div key={check.code} className="rounded-xl border border-border bg-background/40 p-4">
                  <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <div className="font-medium">{check.name}</div>
                        {check.success ? (
                          <CheckCircle2
                            aria-label={t("stacks.diagnostics.checkPassed")}
                            className="h-4 w-4 text-emerald-400"
                          />
                        ) : (
                          <AlertTriangle
                            aria-label={t("stacks.diagnostics.checkFailed")}
                            className="h-4 w-4 text-red-400"
                          />
                        )}
                      </div>
                      <div className="mt-1 break-all font-mono text-xs text-muted-foreground">
                        {check.code}
                      </div>
                    </div>
                    <StackStatusPill status={check.success ? "passed" : "failed"} />
                  </div>

                  <div className="mt-4 grid gap-3 text-sm md:grid-cols-2">
                    <DiagnosticFact label={t("stacks.diagnostics.endpoint")} value={check.url} />
                    <DiagnosticFact
                      label={t("stacks.diagnostics.httpResult")}
                      value={check.statusCode === null ? notReported : `HTTP ${check.statusCode}`}
                    />
                  </div>

                  {check.detail ? (
                    <p className="mt-4 text-sm text-muted-foreground">{check.detail}</p>
                  ) : null}

                  {check.bodyPreview ? (
                    <details className="mt-4 rounded-lg border border-border bg-muted/20">
                      <summary className="cursor-pointer px-3 py-2 text-sm font-medium">
                        {t("stacks.diagnostics.responsePreview")}
                      </summary>
                      <pre className="max-h-64 overflow-auto border-t border-border px-3 py-3 text-xs whitespace-pre-wrap break-words">
                        {check.bodyPreview}
                      </pre>
                    </details>
                  ) : null}
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  )
}

function DiagnosticFact({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-1 break-words text-foreground">{value}</div>
    </div>
  )
}

function formatDoctorDate(
  value: string | null,
  language: ReturnType<typeof useI18n>["language"],
  fallback: string,
) {
  return value ? formatDateTime(value, language) : fallback
}
