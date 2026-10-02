import { ChevronLeft, ChevronRight, History, LoaderCircle, X } from "lucide-react"

import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

import type {
  RuntimeStackDoctorHistoryResponse,
  RuntimeStackDoctorResponse,
} from "../api/stacks.types"
import { StackDoctorReportEvidence } from "./stack-diagnostics-panel"
import { StackStatusPill } from "./stack-status-pill"

type StackDoctorHistoryPanelProps = {
  history: RuntimeStackDoctorHistoryResponse | null
  loading: boolean
  fetching: boolean
  selectedReportId: string | null
  selectedReport: RuntimeStackDoctorResponse | null
  selectedReportLoading: boolean
  selectedReportError: string | null
  onSelectReport: (reportId: string) => void
  onCloseReport: () => void
  onPreviousPage: () => void
  onNextPage: () => void
}

/**
 * Projects only previously completed Doctor readiness reports. Cancelled,
 * timed-out, or otherwise incomplete Doctor attempts remain runtime-operation
 * evidence and deliberately do not appear in this history.
 */
export function StackDoctorHistoryPanel({
  history,
  loading,
  fetching,
  selectedReportId,
  selectedReport,
  selectedReportLoading,
  selectedReportError,
  onSelectReport,
  onCloseReport,
  onPreviousPage,
  onNextPage,
}: StackDoctorHistoryPanelProps) {
  const { language, t } = useI18n()

  return (
    <div className="space-y-6">
      <Card aria-busy={fetching}>
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <CardTitle className="flex items-center gap-2">
                <History className="h-5 w-5" />
                {t("stacks.diagnostics.historyTitle")}
              </CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">
                {t("stacks.diagnostics.historyDescription")}
              </p>
            </div>
            {history && history.totalCount > 0 ? (
              <span className="text-sm text-muted-foreground">
                {t("stacks.diagnostics.historyCount", { count: history.totalCount })}
              </span>
            ) : null}
          </div>
        </CardHeader>
        <CardContent>
          {loading && !history ? (
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
              <LoaderCircle className="h-4 w-4 animate-spin" />
              {t("stacks.diagnostics.historyLoading")}
            </div>
          ) : !history || history.reports.length === 0 ? (
            <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
              {t("stacks.diagnostics.historyEmpty")}
            </div>
          ) : (
            <div className="space-y-4">
              <div className="divide-y divide-border overflow-hidden rounded-xl border border-border">
                {history.reports.map((report) => {
                  const selected = report.reportId === selectedReportId

                  return (
                    <div
                      key={report.reportId}
                      className={selected ? "bg-muted/35 p-4" : "bg-background/30 p-4"}
                    >
                      <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                        <div className="min-w-0 space-y-2">
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="font-medium">
                              {formatDateTime(report.checkedAtUtc, language)}
                            </span>
                            <StackStatusPill status={report.allPassed ? "passed" : "failed"} />
                            <span className="text-sm text-muted-foreground">
                              {t("stacks.diagnostics.historyChecks", {
                                count: report.checkCount,
                              })}
                            </span>
                            {report.failedCheckCount > 0 ? (
                              <span className="text-sm text-red-400">
                                {t("stacks.diagnostics.historyFailedChecks", {
                                  count: report.failedCheckCount,
                                })}
                              </span>
                            ) : null}
                          </div>

                          <div className="grid gap-x-6 gap-y-1 text-xs text-muted-foreground md:grid-cols-2">
                            <div className="min-w-0">
                              <span className="font-medium text-foreground/80">
                                {t("stacks.diagnostics.report")}:
                              </span>{" "}
                              <span className="break-all font-mono">{report.reportId}</span>
                            </div>
                            <div className="min-w-0">
                              <span className="font-medium text-foreground/80">
                                {t("stacks.diagnostics.operation")}:
                              </span>{" "}
                              <span className="break-all font-mono">
                                {report.operationId ?? t("stacks.common.notRecorded")}
                              </span>
                            </div>
                          </div>
                        </div>

                        <Button
                          type="button"
                          variant={selected ? "secondary" : "outline"}
                          size="sm"
                          aria-pressed={selected}
                          onClick={() => onSelectReport(report.reportId)}
                        >
                          {selected
                            ? t("stacks.diagnostics.historyViewing")
                            : t("stacks.diagnostics.historyView")}
                        </Button>
                      </div>
                    </div>
                  )
                })}
              </div>

              <div className="flex flex-col gap-3 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
                <span>
                  {t("stacks.diagnostics.historyPage", {
                    page: history.page,
                    totalPages: history.totalPages,
                  })}
                </span>
                <div className="flex items-center gap-2">
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    disabled={!history.hasPreviousPage || fetching}
                    onClick={onPreviousPage}
                  >
                    <ChevronLeft />
                    {t("stacks.diagnostics.historyPreviousPage")}
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    disabled={!history.hasNextPage || fetching}
                    onClick={onNextPage}
                  >
                    {t("stacks.diagnostics.historyNextPage")}
                    <ChevronRight />
                  </Button>
                </div>
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      {selectedReportId ? (
        selectedReportLoading && !selectedReport ? (
          <Card>
            <CardContent className="flex items-center gap-2 py-6 text-sm text-muted-foreground">
              <LoaderCircle className="h-4 w-4 animate-spin" />
              {t("stacks.diagnostics.historyDetailLoading")}
            </CardContent>
          </Card>
        ) : selectedReportError ? (
          <Card>
            <CardContent className="py-6">
              <div className="rounded-xl border border-red-500/20 bg-red-500/10 p-4 text-sm">
                <div className="font-medium">{t("stacks.diagnostics.historyDetailErrorTitle")}</div>
                <div className="mt-1 text-muted-foreground">{selectedReportError}</div>
              </div>
            </CardContent>
          </Card>
        ) : selectedReport ? (
          <div className="space-y-4">
            <div className="flex justify-end">
              <Button type="button" variant="outline" size="sm" onClick={onCloseReport}>
                <X />
                {t("stacks.diagnostics.historyClose")}
              </Button>
            </div>
            <StackDoctorReportEvidence
              doctor={selectedReport}
              title={t("stacks.diagnostics.historyDetailTitle")}
              description={t("stacks.diagnostics.historyDetailDescription")}
            />
          </div>
        ) : null
      ) : null}
    </div>
  )
}
