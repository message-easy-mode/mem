import type { ReactNode } from "react"

import { AlertTriangle, Archive, CheckCircle2, Clock3, ExternalLink, Stethoscope } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

import type {
  RuntimeStackDoctorResponse,
  RuntimeStackInspectResponse,
  RuntimeStackOperationResponse,
} from "../api/stacks.types"
import { StackStatusPill } from "./stack-status-pill"
import { formatOptionalStackDateTime, formatStackOperationName } from "./stack-workspace-formatting"

type StackOverviewProps = {
  stack: RuntimeStackInspectResponse
  doctor: RuntimeStackDoctorResponse | null
  latestBackupOperation: RuntimeStackOperationResponse | null
  operations: RuntimeStackOperationResponse[]
}

export function StackOverview({
  stack,
  doctor,
  latestBackupOperation,
  operations,
}: StackOverviewProps) {
  const { language, t } = useI18n()
  const recentOperations = operations.slice(0, 3)
  const notRecorded = t("stacks.common.notRecorded")

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <CardTitle>{t("stacks.overview.title")}</CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">
                {t("stacks.overview.description")}
              </p>
            </div>

            <div className="flex flex-wrap gap-2">
              {stack.matrix?.publicBaseUrl && (
                <Button variant="outline" size="sm" asChild>
                  <a href={stack.matrix.publicBaseUrl} target="_blank" rel="noreferrer">
                    <ExternalLink className="mr-2 h-4 w-4" />
                    Matrix
                  </a>
                </Button>
              )}

              {stack.element?.publicBaseUrl && (
                <Button variant="outline" size="sm" asChild>
                  <a href={stack.element.publicBaseUrl} target="_blank" rel="noreferrer">
                    <ExternalLink className="mr-2 h-4 w-4" />
                    Element
                  </a>
                </Button>
              )}
            </div>
          </div>
        </CardHeader>

        <CardContent className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <OverviewFact label={t("stacks.overview.publicReadiness")}>
            <StackStatusPill status={stack.health ?? stack.status} />
          </OverviewFact>
          <OverviewFact
            label={t("stacks.overview.lastVerified")}
            value={formatOptionalStackDateTime(stack.lastVerifiedAtUtc, language, notRecorded)}
          />
          <OverviewFact
            label={t("stacks.overview.matrixPublicHost")}
            value={stack.matrix?.publicHost ?? notRecorded}
          />
          <OverviewFact
            label={t("stacks.overview.elementPublicHost")}
            value={stack.element?.publicHost ?? notRecorded}
          />
        </CardContent>
      </Card>

      <div className="grid gap-6 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Stethoscope className="h-5 w-5" />
              {t("stacks.overview.doctorTitle")}
            </CardTitle>
            <p className="text-sm text-muted-foreground">
              {t("stacks.overview.doctorDescription")}
            </p>
          </CardHeader>
          <CardContent>
            {doctor ? (
              <div className="flex flex-col gap-3 rounded-xl border border-border bg-background/40 p-4 sm:flex-row sm:items-start sm:justify-between">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <div className="font-medium">
                      {doctor.allPassed
                        ? t("stacks.overview.doctorPassed")
                        : t("stacks.overview.doctorFailed")}
                    </div>
                    <StackStatusPill status={doctor.allPassed ? "passed" : "failed"} />
                  </div>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {t("stacks.overview.doctorChecksRun", {
                      count: doctor.checks.length,
                      date: formatOptionalStackDateTime(doctor.checkedAtUtc, language, notRecorded),
                    })}
                  </p>
                  {doctor.detail ? <p className="mt-2 text-sm text-muted-foreground">{doctor.detail}</p> : null}
                </div>
                {doctor.allPassed ? (
                  <CheckCircle2 className="h-5 w-5 text-emerald-400" aria-hidden="true" />
                ) : (
                  <AlertTriangle className="h-5 w-5 text-red-400" aria-hidden="true" />
                )}
              </div>
            ) : (
              <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
                {t("stacks.overview.doctorEmpty")}
              </div>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Archive className="h-5 w-5" />
              {t("stacks.overview.latestBackupTitle")}
            </CardTitle>
            <p className="text-sm text-muted-foreground">
              {t("stacks.overview.latestBackupDescription")}
            </p>
          </CardHeader>
          <CardContent>
            {latestBackupOperation ? (
              <div className="rounded-xl border border-border bg-background/40 p-4">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="font-medium">
                    {formatStackOperationName(latestBackupOperation.operation, t)}
                  </div>
                  <StackStatusPill status={latestBackupOperation.status} />
                </div>
                <div className="mt-3 grid gap-3 text-sm sm:grid-cols-2">
                  <OverviewFact
                    label={t("stacks.overview.started")}
                    value={formatOptionalStackDateTime(latestBackupOperation.startedAtUtc, language, notRecorded)}
                  />
                  <OverviewFact
                    label={t("stacks.overview.completed")}
                    value={formatOptionalStackDateTime(latestBackupOperation.completedAtUtc, language, notRecorded)}
                  />
                </div>
              </div>
            ) : (
              <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
                {t("stacks.overview.latestBackupEmpty")}
              </div>
            )}
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Clock3 className="h-5 w-5" />
            {t("stacks.overview.recentOperationsTitle")}
          </CardTitle>
          <p className="text-sm text-muted-foreground">
            {t("stacks.overview.recentOperationsDescription")}
          </p>
        </CardHeader>
        <CardContent>
          {recentOperations.length === 0 ? (
            <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
              {t("stacks.overview.recentOperationsEmpty")}
            </div>
          ) : (
            <div className="divide-y divide-border overflow-hidden rounded-xl border border-border bg-background/40">
              {recentOperations.map((operation) => (
                <div
                  key={operation.id}
                  className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between"
                >
                  <div className="min-w-0">
                    <div className="truncate font-medium">
                      {formatStackOperationName(operation.operation, t)}
                    </div>
                    <div className="mt-1 text-xs text-muted-foreground">
                      {t("stacks.overview.requestedAt", {
                        date: formatOptionalStackDateTime(operation.requestedAtUtc, language, notRecorded),
                      })}
                    </div>
                  </div>
                  <div className="flex items-center gap-3">
                    <span className="text-xs text-muted-foreground">
                      {operation.completedAtUtc
                        ? t("stacks.overview.completedAt", {
                            date: formatOptionalStackDateTime(operation.completedAtUtc, language, notRecorded),
                          })
                        : t("stacks.overview.inProgress")}
                    </span>
                    <StackStatusPill status={operation.status} />
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  )
}

function OverviewFact({
  label,
  value,
  children,
}: {
  label: string
  value?: string
  children?: ReactNode
}) {
  return (
    <div className="rounded-xl border border-border bg-background/40 p-3">
      <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-2 break-words text-sm text-foreground">{children ?? value}</div>
    </div>
  )
}
