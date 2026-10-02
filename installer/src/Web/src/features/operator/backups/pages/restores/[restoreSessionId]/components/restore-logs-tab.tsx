import { useEffect, useMemo, useState } from "react"
import {
  CheckCircle2,
  ChevronRight,
  CircleAlert,
  Clipboard,
  Download,
  FileText,
  LoaderCircle,
  Search,
  TriangleAlert,
  X,
} from "lucide-react"

import { useI18n, type I18nContextValue } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import {
  Pagination,
  PaginationContent,
  PaginationEllipsis,
  PaginationItem,
  PaginationLink,
  PaginationNext,
  PaginationPrevious,
} from "@/components/ui/pagination"
import { Select } from "@/components/ui/select"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { cn } from "@/lib/utils"
import type {
  RestoreLogEvent,
  RestoreLogQuery,
  RestoreLogSummary,
} from "@/features/operator/backups/api/types/restore-logs.types"
import type { RestoreWorkspaceResponse } from "@/features/operator/backups/api/types/restore-workspace.types"
import {
  useGenerateRestoreSupportReport,
  useRestoreLogs,
} from "@/features/operator/backups/hooks/use-restore-workspace"

import {
  getRestoreWorkspaceTechnicalDetail,
  RestoreWorkspaceTechnicalDetails,
} from "./restore-workspace-problems"

const PAGE_SIZES = [20, 50, 100] as const
const SECRET_KEY_PATTERN = /password|secret|token|private.?key|access.?key|connection.?string|authorization|cookie/i

export function RestoreLogsTab({
  restoreSessionId,
  workspace,
}: {
  restoreSessionId: string
  workspace: RestoreWorkspaceResponse
}) {
  const { intlLocale, t } = useI18n()
  const [query, setQuery] = useState<RestoreLogQuery>({ page: 1, pageSize: 20 })
  const [searchInput, setSearchInput] = useState("")
  const [selectedEventKey, setSelectedEventKey] = useState<string | null>(null)
  const logsQuery = useRestoreLogs(restoreSessionId, query)
  const supportReportMutation = useGenerateRestoreSupportReport()
  const logs = logsQuery.data

  useEffect(() => {
    const timeout = window.setTimeout(() => {
      setQuery((current) =>
        current.search === (searchInput.trim() || undefined)
          ? current
          : { ...current, page: 1, search: searchInput.trim() || undefined },
      )
    }, 350)

    return () => window.clearTimeout(timeout)
  }, [searchInput])

  const stages = useMemo(() => {
    const current = new Set(logs?.events.map((event) => event.stage).filter(Boolean) ?? [])
    if (query.stage) current.add(query.stage)
    return [...current].sort() as string[]
  }, [logs?.events, query.stage])

  const selectedEvent = useMemo(
    () => logs?.events.find((event) => getEventKey(event) === selectedEventKey) ?? null,
    [logs?.events, selectedEventKey],
  )

  const summary = logs?.summary ?? workspace.logs
  const hasActiveFilters = hasFilters(query, searchInput)
  const logsTechnicalDetail = logsQuery.isError
    ? getRestoreWorkspaceTechnicalDetail(logsQuery.error)
    : undefined
  const supportReportTechnicalDetail = supportReportMutation.isError
    ? getRestoreWorkspaceTechnicalDetail(supportReportMutation.error)
    : undefined

  const resetFilters = () => {
    setSearchInput("")
    setQuery({ page: 1, pageSize: query.pageSize })
  }

  const updateFilter = (field: "severity" | "stage", value: string) => {
    setQuery((current) => ({
      ...current,
      page: 1,
      [field]: value || undefined,
    }))
  }

  const setSeverity = (severity: string | undefined) => {
    setQuery((current) => ({ ...current, page: 1, severity }))
  }

  const copyVisible = async () => {
    const content = logs?.events.map(eventToText).join("\n\n") ?? ""
    await copyText(content)
  }

  const downloadVisible = () => {
    const content = logs?.events.map(eventToText).join("\n\n") ?? ""
    downloadFile(
      `mem-restore-${restoreSessionId}-logs-page-${query.page}.txt`,
      content,
      "text/plain;charset=utf-8",
    )
  }

  const generateSupportReport = async () => {
    const result = await supportReportMutation.mutateAsync(restoreSessionId)
    downloadFile(
      `mem-restore-${restoreSessionId}-support-report.json`,
      JSON.stringify(result.report, null, 2),
      "application/json;charset=utf-8",
    )
  }

  return (
    <div className="space-y-4">
      <RestoreLogsStatus workspace={workspace} logs={summary} t={t} />

      <Card
        size="sm"
        className="border-border/80 bg-card/95 shadow-[0_10px_28px_rgba(0,0,0,0.08)]"
      >
        <CardContent className="space-y-3 pt-3">
          <div className="flex flex-col gap-2 xl:flex-row xl:items-center">
            <div className="relative min-w-0 flex-1">
              <Search className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                value={searchInput}
                onChange={(event) => setSearchInput(event.target.value)}
                placeholder={t("restoreWorkspace.logs.searchPlaceholder")}
                className="bg-background/60 pl-9"
                aria-label={t("restoreWorkspace.logs.searchAria")}
              />
            </div>
            <Select
              value={query.severity ?? ""}
              onChange={(event) => updateFilter("severity", event.target.value)}
              aria-label={t("restoreWorkspace.logs.severityFilterAria")}
              className="w-full sm:w-[150px]"
            >
              <option value="">{t("restoreWorkspace.logs.allLevels")}</option>
              <option value="information">{t("restoreWorkspace.logs.severity.information")}</option>
              <option value="warning">{t("restoreWorkspace.logs.severity.warning")}</option>
              <option value="error">{t("restoreWorkspace.logs.severity.error")}</option>
            </Select>
            <Select
              value={query.stage ?? ""}
              onChange={(event) => updateFilter("stage", event.target.value)}
              aria-label={t("restoreWorkspace.logs.stageFilterAria")}
              className="w-full sm:w-[170px]"
            >
              <option value="">{t("restoreWorkspace.logs.allStages")}</option>
              {stages.map((stage) => (
                <option key={stage} value={stage}>
                  {stage}
                </option>
              ))}
            </Select>
            <Button
              variant="outline"
              size="sm"
              onClick={resetFilters}
              disabled={!hasActiveFilters}
            >
              {t("restoreWorkspace.logs.clearFilters")}
            </Button>
          </div>

          <div className="flex flex-col gap-3 border-t border-border/70 pt-3 lg:flex-row lg:items-center lg:justify-between">
            <div className="flex min-w-0 flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center">
              <p className="text-xs text-muted-foreground">
                {t("restoreWorkspace.logs.scopeDescription")}
              </p>
              <div className="flex flex-wrap gap-1.5">
                <SeverityFilterChip
                  label={t("restoreWorkspace.logs.filter.all")}
                  count={summary.totalEvents}
                  active={!query.severity}
                  onClick={() => setSeverity(undefined)}
                />
                <SeverityFilterChip
                  label={t("restoreWorkspace.logs.filter.warnings")}
                  count={summary.warningCount}
                  tone="warning"
                  active={isSeverity(query.severity, "warning")}
                  onClick={() => setSeverity("warning")}
                />
                <SeverityFilterChip
                  label={t("restoreWorkspace.logs.filter.errors")}
                  count={summary.errorCount}
                  tone="error"
                  active={isSeverity(query.severity, "error")}
                  onClick={() => setSeverity("error")}
                />
              </div>
            </div>

            <div className="flex flex-wrap gap-2 lg:justify-end">
              <Button
                variant="outline"
                size="sm"
                onClick={() => void copyVisible()}
                disabled={!logs?.events.length}
              >
                <Clipboard className="mr-2 h-3.5 w-3.5" />
                {t("restoreWorkspace.logs.copyVisible")}
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={downloadVisible}
                disabled={!logs?.events.length}
              >
                <Download className="mr-2 h-3.5 w-3.5" />
                {t("restoreWorkspace.logs.downloadVisible")}
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() => void generateSupportReport()}
                disabled={supportReportMutation.isPending}
              >
                {supportReportMutation.isPending ? (
                  <LoaderCircle className="mr-2 h-3.5 w-3.5 animate-spin" />
                ) : (
                  <FileText className="mr-2 h-3.5 w-3.5" />
                )}
                {workspace.logs.supportReportAvailable
                  ? t("restoreWorkspace.logs.regenerateSupportReport")
                  : t("restoreWorkspace.logs.generateSupportReport")}
              </Button>
            </div>
          </div>

          {supportReportMutation.isError ? (
            <Alert variant="destructive">
              <TriangleAlert className="h-4 w-4" />
              <AlertTitle>{t("restoreWorkspace.logs.supportReportErrorTitle")}</AlertTitle>
              {supportReportTechnicalDetail ? (
                <AlertDescription>
                  <RestoreWorkspaceTechnicalDetails detail={supportReportTechnicalDetail} />
                </AlertDescription>
              ) : null}
            </Alert>
          ) : null}
        </CardContent>
      </Card>

      <div
        className={cn(
          "grid gap-4",
          selectedEvent ? "2xl:grid-cols-[minmax(0,1fr)_320px]" : "grid-cols-1",
        )}
      >
        <Card className="min-w-0 border-border/80 bg-card/95 shadow-[0_10px_28px_rgba(0,0,0,0.08)]">
          <CardContent className="p-0">
            {logsQuery.isLoading ? (
              <LogsLoadingState t={t} />
            ) : logsQuery.isError ? (
              <Alert variant="destructive" className="m-5">
                <TriangleAlert className="h-4 w-4" />
                <AlertTitle>{t("restoreWorkspace.logs.loadErrorTitle")}</AlertTitle>
                {logsTechnicalDetail ? (
                  <AlertDescription>
                    <RestoreWorkspaceTechnicalDetails detail={logsTechnicalDetail} />
                  </AlertDescription>
                ) : null}
              </Alert>
            ) : !logs ? null : logs.events.length === 0 ? (
              <EmptyLogsState filtered={hasActiveFilters} onClear={resetFilters} t={t} />
            ) : (
              <>
                <Table>
                  <TableHeader className="bg-muted/35">
                    <TableRow className="hover:bg-transparent">
                      <TableHead className="w-[168px] whitespace-nowrap">
                        {t("restoreWorkspace.logs.table.timeUtc")}
                      </TableHead>
                      <TableHead className="w-[102px]">
                        {t("restoreWorkspace.logs.table.level")}
                      </TableHead>
                      <TableHead className="w-[148px]">
                        {t("restoreWorkspace.logs.table.stage")}
                      </TableHead>
                      <TableHead className="w-[236px]">
                        {t("restoreWorkspace.logs.table.eventCode")}
                      </TableHead>
                      <TableHead>{t("restoreWorkspace.logs.table.message")}</TableHead>
                      <TableHead className="w-10">
                        <span className="sr-only">{t("restoreWorkspace.logs.table.inspect")}</span>
                      </TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {logs.events.map((event) => {
                      const isSelected = selectedEvent
                        ? getEventKey(selectedEvent) === getEventKey(event)
                        : false

                      return (
                        <TableRow
                          key={getEventKey(event)}
                          data-state={isSelected ? "selected" : undefined}
                          aria-selected={isSelected}
                          tabIndex={0}
                          className="cursor-pointer outline-none hover:bg-muted/45 data-[state=selected]:bg-primary/10 focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset"
                          onClick={() => setSelectedEventKey(getEventKey(event))}
                          onKeyDown={(keyboardEvent) => {
                            if (keyboardEvent.key === "Enter" || keyboardEvent.key === " ") {
                              keyboardEvent.preventDefault()
                              setSelectedEventKey(getEventKey(event))
                            }
                          }}
                        >
                          <TableCell className="whitespace-nowrap font-mono text-[11px] text-muted-foreground">
                            {formatUtc(event.timestampUtc, intlLocale)}
                          </TableCell>
                          <TableCell>
                            <SeverityBadge severity={event.severity} t={t} />
                          </TableCell>
                          <TableCell className="font-mono text-[11px] leading-4 text-muted-foreground">
                            <span className="line-clamp-2">{event.stage ?? "—"}</span>
                          </TableCell>
                          <TableCell className="font-mono text-[11px] leading-4 text-foreground/90">
                            <span className="line-clamp-2">{event.eventCode ?? "—"}</span>
                          </TableCell>
                          <TableCell className="max-w-[520px] text-sm leading-5">
                            <span className="line-clamp-2">
                              {event.message ?? t("restoreWorkspace.logs.noMessageProvided")}
                            </span>
                          </TableCell>
                          <TableCell>
                            <ChevronRight className="h-4 w-4 text-muted-foreground" />
                          </TableCell>
                        </TableRow>
                      )
                    })}
                  </TableBody>
                </Table>
                <LogsPagination
                  page={logs.page}
                  pageSize={logs.pageSize}
                  totalEvents={logs.totalEvents}
                  totalPages={logs.totalPages}
                  isFetching={logsQuery.isFetching}
                  onPageChange={(page) => setQuery((current) => ({ ...current, page }))}
                  onPageSizeChange={(pageSize) =>
                    setQuery((current) => ({ ...current, page: 1, pageSize }))
                  }
                  t={t}
                />
              </>
            )}
          </CardContent>
        </Card>

        {selectedEvent ? (
          <EventDetails
            event={selectedEvent}
            onClose={() => setSelectedEventKey(null)}
            intlLocale={intlLocale}
            t={t}
          />
        ) : null}
      </div>
    </div>
  )
}

function RestoreLogsStatus({
  workspace,
  logs,
  t,
}: {
  workspace: RestoreWorkspaceResponse
  logs: RestoreLogSummary | RestoreWorkspaceResponse["logs"]
  t: I18nContextValue["t"]
}) {
  const status =
    logs.latestWarningOrError ??
    workspace.logs.latestWarningOrError ??
    logs.latestEvent ??
    workspace.logs.latestEvent

  return (
    <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_292px]">
      <Card className="border-border/80 bg-card/95 shadow-[0_10px_28px_rgba(0,0,0,0.08)]">
        <CardHeader className="pb-0">
          <CardTitle className="text-sm">{t("restoreWorkspace.logs.latestStatus")}</CardTitle>
        </CardHeader>
        <CardContent className="pt-1">
          <div className="flex items-start gap-3">
            <StatusGlyph severity={status?.severity ?? workspace.overallStatus.severity} />
            <div className="min-w-0">
              <div className="font-medium">
                {status?.message ?? workspace.overallStatus.title}
              </div>
              <p className="mt-1 text-sm text-muted-foreground">
                {status
                  ? `${status.stage ?? t("restoreWorkspace.logs.defaultStage")} · ${status.eventCode ?? t("restoreWorkspace.logs.defaultEventCode")}`
                  : workspace.overallStatus.description}
              </p>
            </div>
          </div>
        </CardContent>
      </Card>

      <Card className="border-border/80 bg-card/95 shadow-[0_10px_28px_rgba(0,0,0,0.08)]">
        <CardHeader className="pb-0">
          <CardTitle className="text-sm">{t("restoreWorkspace.logs.severitySummary")}</CardTitle>
        </CardHeader>
        <CardContent className="space-y-2 pt-1 text-sm">
          <SummaryRow label={t("restoreWorkspace.logs.summary.events")} value={logs.totalEvents} />
          <SummaryRow
            label={t("restoreWorkspace.logs.summary.warnings")}
            value={logs.warningCount}
            tone="warning"
          />
          <SummaryRow
            label={t("restoreWorkspace.logs.summary.errors")}
            value={logs.errorCount}
            tone="error"
          />
        </CardContent>
      </Card>
    </div>
  )
}

function SeverityFilterChip({
  label,
  count,
  active,
  tone = "neutral",
  onClick,
}: {
  label: string
  count: number
  active: boolean
  tone?: "neutral" | "warning" | "error"
  onClick: () => void
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={cn(
        "inline-flex h-7 items-center gap-1.5 rounded-md border px-2 text-xs font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
        active && tone === "neutral" && "border-primary bg-primary text-primary-foreground",
        active && tone === "warning" && "border-amber-500/70 bg-amber-500/15 text-amber-700 dark:text-amber-300",
        active && tone === "error" && "border-destructive/60 bg-destructive/15 text-destructive",
        !active && tone === "neutral" && "border-border bg-background/50 text-muted-foreground hover:bg-muted hover:text-foreground",
        !active && tone === "warning" && "border-amber-500/30 bg-amber-500/5 text-amber-700 hover:bg-amber-500/10 dark:text-amber-300",
        !active && tone === "error" && "border-destructive/30 bg-destructive/5 text-destructive hover:bg-destructive/10",
      )}
    >
      <span>{label}</span>
      <span className="rounded-sm bg-black/10 px-1 py-0.5 font-mono text-[10px] leading-none dark:bg-white/10">
        {count}
      </span>
    </button>
  )
}

function EventDetails({
  event,
  onClose,
  intlLocale,
  t,
}: {
  event: RestoreLogEvent
  onClose: () => void
  intlLocale: string
  t: I18nContextValue["t"]
}) {
  const safeDetails = Object.entries(event.details ?? {}).map(([key, value]) => [
    key,
    redactDetail(key, value),
  ] as const)

  return (
    <Card className="h-fit min-w-0 border-primary/25 bg-card/95 shadow-[0_10px_28px_rgba(0,0,0,0.12)] 2xl:sticky 2xl:top-6">
      <CardHeader className="flex-row items-center justify-between space-y-0 border-b border-border/70 pb-3">
        <CardTitle className="text-sm">{t("restoreWorkspace.logs.eventDetails")}</CardTitle>
        <Button
          variant="ghost"
          size="icon-sm"
          onClick={onClose}
          aria-label={t("restoreWorkspace.logs.closeEventDetails")}
        >
          <X className="h-4 w-4" />
        </Button>
      </CardHeader>
      <CardContent className="space-y-4 pt-4">
        <div className="space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <SeverityBadge severity={event.severity} t={t} />
            <code className="break-all text-[11px] text-muted-foreground">
              {event.eventCode ?? t("restoreWorkspace.logs.defaultEventCode")}
            </code>
          </div>
          <p className="text-sm leading-5">
            {event.message ?? t("restoreWorkspace.logs.noMessageProvided")}
          </p>
        </div>

        <dl className="space-y-2.5 border-y border-border/70 py-3 text-sm">
          <DetailRow label={t("restoreWorkspace.logs.detail.timestamp")} value={formatUtc(event.timestampUtc, intlLocale)} />
          <DetailRow label={t("restoreWorkspace.logs.detail.stage")} value={event.stage} />
          <CopyableDetail
            label={t("restoreWorkspace.logs.detail.operationId")}
            value={event.operationId}
            t={t}
          />
          <CopyableDetail
            label={t("restoreWorkspace.logs.detail.eventId")}
            value={event.eventId}
            t={t}
          />
          <CopyableDetail
            label={t("restoreWorkspace.logs.detail.restoreSessionId")}
            value={event.restoreSessionId}
            t={t}
          />
        </dl>

        <div>
          <div className="mb-2 flex items-center justify-between gap-3">
            <div>
              <div className="text-sm font-medium">{t("restoreWorkspace.logs.safeContext")}</div>
              <p className="mt-0.5 text-xs text-muted-foreground">
                {t("restoreWorkspace.logs.safeContextDescription")}
              </p>
            </div>
          </div>
          {safeDetails.length ? (
            <dl className="space-y-2 rounded-lg border border-border/70 bg-muted/25 p-3 text-sm">
              {safeDetails.map(([key, value]) => (
                <CopyableDetail key={key} label={key} value={value} compact t={t} />
              ))}
            </dl>
          ) : (
            <p className="rounded-lg border border-dashed border-border p-3 text-sm text-muted-foreground">
              {t("restoreWorkspace.logs.noStructuredContext")}
            </p>
          )}
        </div>

        <div className="flex flex-wrap gap-2 border-t border-border/70 pt-3">
          <Button
            variant="outline"
            size="sm"
            onClick={() => void copyText(JSON.stringify(toSafeEventJson(event), null, 2))}
          >
            <Clipboard className="mr-2 h-3.5 w-3.5" />
            {t("restoreWorkspace.logs.copyDetails")}
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() =>
              downloadFile(
                `mem-restore-event-${event.eventId ?? "unknown"}.json`,
                JSON.stringify(toSafeEventJson(event), null, 2),
                "application/json;charset=utf-8",
              )
            }
          >
            <Download className="mr-2 h-3.5 w-3.5" />
            {t("restoreWorkspace.logs.downloadJson")}
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}

function LogsPagination({
  page,
  pageSize,
  totalEvents,
  totalPages,
  isFetching,
  onPageChange,
  onPageSizeChange,
  t,
}: {
  page: number
  pageSize: number
  totalEvents: number
  totalPages: number
  isFetching: boolean
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
  t: I18nContextValue["t"]
}) {
  const first = totalEvents === 0 ? 0 : (page - 1) * pageSize + 1
  const last = Math.min(page * pageSize, totalEvents)
  const paginationItems = getPaginationItems(page, Math.max(totalPages, 1))

  return (
    <div className="flex flex-col gap-3 border-t border-border/70 p-4 md:flex-row md:items-center md:justify-between">
      <p className="text-sm text-muted-foreground">
        {t("restoreWorkspace.logs.pagination.showing", {
          start: first,
          end: last,
          count: totalEvents,
        })}
      </p>
      <div className="flex flex-wrap items-center gap-3">
        <Pagination className="mx-0 w-auto" aria-label={t("restoreWorkspace.logs.pagination.aria")}>
          <PaginationContent>
            <PaginationItem>
              <PaginationPrevious
                onClick={() => onPageChange(page - 1)}
                disabled={page <= 1 || isFetching}
              >
                {t("restoreWorkspace.logs.pagination.previous")}
              </PaginationPrevious>
            </PaginationItem>
            {paginationItems.map((item, index) =>
              item === "ellipsis" ? (
                <PaginationItem key={`ellipsis-${index}`}>
                  <PaginationEllipsis />
                </PaginationItem>
              ) : (
                <PaginationItem key={item}>
                  <PaginationLink
                    isActive={item === page}
                    onClick={() => onPageChange(item)}
                    disabled={item === page || isFetching}
                  >
                    {item}
                  </PaginationLink>
                </PaginationItem>
              ),
            )}
            <PaginationItem>
              <PaginationNext
                onClick={() => onPageChange(page + 1)}
                disabled={page >= totalPages || isFetching}
              >
                {t("restoreWorkspace.logs.pagination.next")}
              </PaginationNext>
            </PaginationItem>
          </PaginationContent>
        </Pagination>
        <Select
          value={String(pageSize)}
          onChange={(event) => onPageSizeChange(Number(event.target.value))}
          aria-label={t("restoreWorkspace.logs.pagination.perPageAria")}
        >
          {PAGE_SIZES.map((size) => (
            <option key={size} value={size}>
              {t("restoreWorkspace.logs.pagination.perPage", { count: size })}
            </option>
          ))}
        </Select>
      </div>
    </div>
  )
}

function SeverityBadge({
  severity,
  t,
}: {
  severity: string | null
  t: I18nContextValue["t"]
}) {
  const normalized = normalizeSeverity(severity)
  const label = severityLabel(normalized, severity, t)

  return (
    <Badge
      variant="outline"
      className={cn(
        "h-5 rounded-md border px-1.5 font-mono text-[10px] font-semibold tracking-wide",
        normalized === "information" && "border-sky-500/30 bg-sky-500/10 text-sky-700 dark:text-sky-300",
        normalized === "warning" && "border-amber-500/40 bg-amber-500/10 text-amber-700 dark:text-amber-300",
        normalized === "error" && "border-destructive/45 bg-destructive/10 text-destructive",
        normalized === "other" && "border-border bg-muted text-muted-foreground",
      )}
    >
      {label}
    </Badge>
  )
}

function StatusGlyph({ severity }: { severity: string | null | undefined }) {
  const normalized = normalizeSeverity(severity)

  if (normalized === "error") {
    return <TriangleAlert className="mt-0.5 h-5 w-5 shrink-0 text-destructive" />
  }

  if (normalized === "warning") {
    return <CircleAlert className="mt-0.5 h-5 w-5 shrink-0 text-amber-500" />
  }

  return <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-primary" />
}

function SummaryRow({
  label,
  value,
  tone = "neutral",
}: {
  label: string
  value: number
  tone?: "neutral" | "warning" | "error"
}) {
  return (
    <div className="flex items-center justify-between gap-3">
      <span
        className={cn(
          "text-muted-foreground",
          tone === "warning" && "text-amber-700 dark:text-amber-300",
          tone === "error" && "text-destructive",
        )}
      >
        {label}
      </span>
      <span className="font-mono text-xs font-semibold tabular-nums">{value}</span>
    </div>
  )
}

function DetailRow({ label, value }: { label: string; value: string | null }) {
  return (
    <div className="grid grid-cols-[92px_minmax(0,1fr)] gap-3">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="break-all font-mono text-[11px] leading-4">{value ?? "—"}</dd>
    </div>
  )
}

function CopyableDetail({
  label,
  value,
  compact = false,
  t,
}: {
  label: string
  value: string | null
  compact?: boolean
  t: I18nContextValue["t"]
}) {
  return (
    <div
      className={cn(
        "grid gap-2",
        compact ? "grid-cols-[minmax(0,1fr)_auto]" : "grid-cols-[92px_minmax(0,1fr)]",
      )}
    >
      <dt className={cn("min-w-0 text-muted-foreground", compact && "font-mono text-[11px]")}>{label}</dt>
      <dd className="flex min-w-0 items-start gap-1">
        <span className="min-w-0 break-all font-mono text-[11px] leading-4">{value ?? "—"}</span>
        {value ? (
          <Button
            variant="ghost"
            size="icon-xs"
            className="mt-[-2px] shrink-0"
            onClick={() => void copyText(value)}
            aria-label={t("restoreWorkspace.logs.copyValue", { label })}
          >
            <Clipboard className="h-3 w-3" />
          </Button>
        ) : null}
      </dd>
    </div>
  )
}

function EmptyLogsState({
  filtered,
  onClear,
  t,
}: {
  filtered: boolean
  onClear: () => void
  t: I18nContextValue["t"]
}) {
  return (
    <div className="p-10 text-center">
      <FileText className="mx-auto h-8 w-8 text-muted-foreground" />
      <h3 className="mt-3 font-medium">
        {filtered
          ? t("restoreWorkspace.logs.empty.filteredTitle")
          : t("restoreWorkspace.logs.empty.title")}
      </h3>
      <p className="mx-auto mt-1 max-w-md text-sm text-muted-foreground">
        {filtered
          ? t("restoreWorkspace.logs.empty.filteredDescription")
          : t("restoreWorkspace.logs.empty.description")}
      </p>
      {filtered ? (
        <Button variant="outline" className="mt-4" onClick={onClear}>
          {t("restoreWorkspace.logs.clearFilters")}
        </Button>
      ) : null}
    </div>
  )
}

function LogsLoadingState({ t }: { t: I18nContextValue["t"] }) {
  return (
    <div
      className="space-y-3 p-5"
      role="status"
      aria-label={t("restoreWorkspace.logs.loadingAria")}
    >
      {Array.from({ length: 8 }).map((_, index) => (
        <div key={index} className="h-11 animate-pulse rounded-lg bg-muted" />
      ))}
    </div>
  )
}

function getPaginationItems(page: number, totalPages: number): Array<number | "ellipsis"> {
  if (totalPages <= 7) {
    return Array.from({ length: totalPages }, (_, index) => index + 1)
  }

  const items: Array<number | "ellipsis"> = [1]
  const start = Math.max(2, page - 1)
  const end = Math.min(totalPages - 1, page + 1)

  if (start > 2) items.push("ellipsis")
  for (let current = start; current <= end; current += 1) items.push(current)
  if (end < totalPages - 1) items.push("ellipsis")
  items.push(totalPages)

  return items
}

function getEventKey(event: RestoreLogEvent) {
  return event.eventId ?? `${event.timestampUtc}-${event.eventCode ?? "event"}`
}

function normalizeSeverity(value: string | null | undefined) {
  const normalized = (value ?? "information").trim().toLowerCase()

  if (normalized === "information" || normalized === "info") return "information"
  if (normalized === "warning" || normalized === "warn") return "warning"
  if (normalized === "error" || normalized === "failed" || normalized === "failure") return "error"

  return "other"
}

function severityLabel(
  value: ReturnType<typeof normalizeSeverity>,
  rawValue: string | null | undefined,
  t: I18nContextValue["t"],
) {
  if (value === "information") return t("restoreWorkspace.logs.severity.information")
  if (value === "warning") return t("restoreWorkspace.logs.severity.warning")
  if (value === "error") return t("restoreWorkspace.logs.severity.error")
  return rawValue?.trim() || t("restoreWorkspace.logs.severity.event")
}

function isSeverity(value: string | null | undefined, expected: "warning" | "error") {
  return normalizeSeverity(value) === expected
}

function hasFilters(query: RestoreLogQuery, searchInput: string) {
  return Boolean(query.severity || query.stage || query.search || searchInput.trim())
}

function formatUtc(value: string, intlLocale: string) {
  return `${new Intl.DateTimeFormat(intlLocale, {
    timeZone: "UTC",
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false,
  })
    .format(new Date(value))
    .replace(",", "")} UTC`
}

function redactDetail(key: string, value: string) {
  return SECRET_KEY_PATTERN.test(key) ? "[redacted]" : value
}

function toSafeEventJson(event: RestoreLogEvent) {
  return {
    ...event,
    details: Object.fromEntries(
      Object.entries(event.details ?? {}).map(([key, value]) => [key, redactDetail(key, value)]),
    ),
  }
}

function eventToText(event: RestoreLogEvent) {
  const safe = toSafeEventJson(event)

  return [
    event.timestampUtc,
    `${safe.severity ?? "information"} · ${safe.stage ?? "restore"} · ${safe.eventCode ?? "event"}`,
    safe.message ?? "No message provided.",
    JSON.stringify(safe.details ?? {}, null, 2),
  ].join("\n")
}

async function copyText(value: string) {
  await navigator.clipboard.writeText(value)
}

function downloadFile(name: string, content: string, type: string) {
  const blob = new Blob([content], { type })
  const url = URL.createObjectURL(blob)
  const link = document.createElement("a")

  link.href = url
  link.download = name
  link.click()
  URL.revokeObjectURL(url)
}
