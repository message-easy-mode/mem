import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from "react"
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { AlertTriangle, ExternalLink, FileText, HeartPulse, ListFilter, RefreshCw } from "lucide-react"
import { Link, useSearchParams } from "react-router-dom"

import { formatBytes, formatNumber } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import { ApiProblemAlert } from "@/components/operator/api-problem-alert"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Checkbox } from "@/components/ui/checkbox"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select } from "@/components/ui/select"
import {
  generateDiagnosticSupportReport,
  getDiagnosticIncident,
  getDiagnosticsLoggingHealth,
  getDiagnosticsOverview,
  listDiagnosticEvents,
  listDiagnosticIncidents,
  resolveDiagnosticIncident,
} from "./api/diagnostics.api"
import type {
  DiagnosticsEvent,
  DiagnosticsIncidentLifecycleFilter,
  DiagnosticsIncidentSummary,
  DiagnosticsLoggingHealthResponse,
  DiagnosticsSupportReport,
} from "./api/diagnostics.types"
import { DiagnosticEventDisclosure, DiagnosticIncidentDetail } from "./components/diagnostic-incident-detail"
import {
  DiagnosticIncidentLifecycleBadge,
  diagnosticsIncidentLifecycleFilters,
  diagnosticsIncidentResolutionCodes,
  diagnosticsIncidentResolutionLabel,
  lifecycleFilterLabel,
} from "./components/diagnostic-incident-lifecycle"
import { DiagnosticSeverityBadge } from "./components/diagnostic-severity-badge"
import { DiagnosticDockerEvidence } from "./components/diagnostic-docker-evidence"
import { DiagnosticsSectionErrorBoundary } from "./components/diagnostics-error-boundary"
import { DiagnosticsPipelineSelfTestCard } from "./components/diagnostics-pipeline-self-test-card"
import { DiagnosticsSummary } from "./components/diagnostics-summary"
import { diagnosticsUtcIso, formatDiagnosticsLocalDateTime } from "./diagnostics-time"

type DiagnosticsTab = "incidents" | "events" | "health"
type Translate = ReturnType<typeof useI18n>["t"]

export function DiagnosticsLogsPage() {
  const { language, t } = useI18n()
  const [searchParams, setSearchParams] = useSearchParams()
  const requestedTab = searchParams.get("tab")
  const tab: DiagnosticsTab = requestedTab === "events" || requestedTab === "health"
    ? requestedTab
    : "incidents"
  const incidentId = searchParams.get("incident")?.trim() || null

  const overview = useQuery({
    queryKey: ["diagnostics", "overview"],
    queryFn: getDiagnosticsOverview,
    staleTime: 10_000,
    refetchInterval: 30_000,
    refetchIntervalInBackground: false,
    retry: 0,
  })

  const selectTab = (nextTab: DiagnosticsTab) => {
    const next = new URLSearchParams(searchParams)
    next.set("tab", nextTab)
    if (nextTab !== "incidents") next.delete("incident")
    setSearchParams(next, { replace: true })
  }

  const canReadEvents = overview.data?.capabilities.canReadTechnicalEvents ?? false

  return (
    <div className="space-y-6">
      <div>
        <PageBreadcrumbs items={[
          { label: t("diagnostics.title"), to: "/diagnostics" },
          { label: t("diagnostics.workspace.title") },
        ]} />
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight">{t("diagnostics.workspace.title")}</h1>
            <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
              {t("diagnostics.workspace.description")}
            </p>
          </div>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => void overview.refetch()}
            disabled={overview.isFetching}
          >
            <RefreshCw className={overview.isFetching ? "mr-2 h-4 w-4 animate-spin" : "mr-2 h-4 w-4"} />
            {t("diagnostics.refresh")}
          </Button>
        </div>
      </div>

      <DiagnosticsSectionErrorBoundary
        resetKey={`summary:${language}:${overview.dataUpdatedAt}`}
      >
        <DiagnosticsSummary
          data={overview.data}
          error={overview.error}
          loading={overview.isLoading}
          fetching={overview.isFetching}
          onRefresh={() => void overview.refetch()}
        />
      </DiagnosticsSectionErrorBoundary>

      <nav aria-label={t("diagnostics.workspace.tabsLabel")} className="flex flex-wrap gap-2 border-b pb-2">
        <TabButton active={tab === "incidents"} onClick={() => selectTab("incidents")}>
          <AlertTriangle className="h-4 w-4" />
          {t("diagnostics.tabs.incidents")}
        </TabButton>
        {canReadEvents ? (
          <TabButton active={tab === "events"} onClick={() => selectTab("events")}>
            <FileText className="h-4 w-4" />
            {t("diagnostics.tabs.events")}
          </TabButton>
        ) : null}
        <TabButton active={tab === "health"} onClick={() => selectTab("health")}>
          <HeartPulse className="h-4 w-4" />
          {t("diagnostics.tabs.health")}
        </TabButton>
      </nav>

      <DiagnosticsSectionErrorBoundary
        resetKey={`panel:${language}:${tab}:${incidentId ?? ""}`}
      >
        {tab === "incidents" ? (
          <IncidentsPanel
            incidentId={incidentId}
            incidentCount={overview.data?.counts.incidentCount ?? 0}
            technicalEventCount={overview.data?.counts.eventCount ?? 0}
            canReadEvents={canReadEvents}
            canManageIncidentLifecycle={overview.data?.capabilities.canManageIncidentLifecycle === true}
          />
        ) : null}
        {tab === "events" && canReadEvents ? <EventsPanel /> : null}
        {tab === "health" ? <LoggingHealthPanel /> : null}
      </DiagnosticsSectionErrorBoundary>
    </div>
  )
}

function IncidentsPanel({
  incidentId,
  incidentCount,
  technicalEventCount,
  canReadEvents,
  canManageIncidentLifecycle,
}: {
  incidentId: string | null
  incidentCount: number
  technicalEventCount: number
  canReadEvents: boolean
  canManageIncidentLifecycle: boolean
}) {
  const { t } = useI18n()
  const queryClient = useQueryClient()
  const [searchParams, setSearchParams] = useSearchParams()
  const [searchInput, setSearchInput] = useState("")
  const [search, setSearch] = useState("")
  const [feature, setFeature] = useState("")
  const lifecycle = normalizeIncidentLifecycleFilter(searchParams.get("lifecycle"))
  const [cursor, setCursor] = useState<string | null>(null)
  const [cursorHistory, setCursorHistory] = useState<(string | null)[]>([])
  const [selectedIncidentIds, setSelectedIncidentIds] = useState<Set<string>>(() => new Set())
  const [bulkResolveOpen, setBulkResolveOpen] = useState(false)
  const [bulkResolutionCode, setBulkResolutionCode] = useState("")
  const [bulkResult, setBulkResult] = useState<{ resolved: number; failed: number } | null>(null)

  const incidents = useQuery({
    queryKey: ["diagnostics", "incidents", search, feature, lifecycle, cursor],
    queryFn: () => listDiagnosticIncidents({
      search: search || undefined,
      feature: feature || undefined,
      lifecycle,
      cursor: cursor || undefined,
      pageSize: 50,
    }),
    staleTime: 10_000,
    refetchInterval: 30_000,
    refetchIntervalInBackground: false,
    retry: 0,
    enabled: !incidentId,
  })

  const detail = useQuery({
    queryKey: ["diagnostics", "incident", incidentId],
    queryFn: () => getDiagnosticIncident(incidentId!),
    enabled: Boolean(incidentId),
    retry: 0,
  })

  const visibleResolvableIncidentIds = useMemo(
    () => (incidents.data?.incidents ?? [])
      .filter((incident) => (incident.lifecycle?.state ?? "open") !== "resolved")
      .map((incident) => incident.incidentId),
    [incidents.data],
  )

  useEffect(() => {
    if (!incidents.data) return
    const visible = new Set(visibleResolvableIncidentIds)
    setSelectedIncidentIds((current) => new Set([...current].filter((id) => visible.has(id))))
  }, [incidents.data, visibleResolvableIncidentIds])

  const bulkResolve = useMutation({
    mutationFn: async ({ incidentIds, resolutionCode }: { incidentIds: readonly string[]; resolutionCode: string }) => {
      const failedIds: string[] = []
      let resolved = 0

      for (const selectedIncidentId of incidentIds) {
        try {
          await resolveDiagnosticIncident(selectedIncidentId, resolutionCode)
          resolved += 1
        } catch {
          failedIds.push(selectedIncidentId)
        }
      }

      return { resolved, failedIds }
    },
    onSuccess: async ({ resolved, failedIds }) => {
      setSelectedIncidentIds(new Set(failedIds))
      setBulkResult({ resolved, failed: failedIds.length })
      setBulkResolveOpen(false)
      if (failedIds.length === 0) setBulkResolutionCode("")
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["diagnostics", "incidents"] }),
        queryClient.invalidateQueries({ queryKey: ["diagnostics", "overview"] }),
        queryClient.invalidateQueries({ queryKey: ["diagnostics", "attention"] }),
      ])
    },
  })

  const [copying, setCopying] = useState(false)
  const [downloading, setDownloading] = useState(false)
  const [reportError, setReportError] = useState<unknown>(null)
  const [includeDockerEvidence, setIncludeDockerEvidence] = useState(false)

  const setIncident = (value: string | null) => {
    const next = new URLSearchParams(searchParams)
    next.set("tab", "incidents")
    if (value) next.set("incident", value)
    else next.delete("incident")
    setSearchParams(next, { replace: true })
  }

  const onSearch = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setSearch(searchInput.trim())
    setCursor(null)
    setCursorHistory([])
    setSelectedIncidentIds(new Set())
    setBulkResult(null)
  }

  const setLifecycle = (value: DiagnosticsIncidentLifecycleFilter) => {
    const next = new URLSearchParams(searchParams)
    next.set("tab", "incidents")
    if (value === "open") next.delete("lifecycle")
    else next.set("lifecycle", value)
    next.delete("incident")
    setSearchParams(next, { replace: true })
    setCursor(null)
    setCursorHistory([])
    setSelectedIncidentIds(new Set())
    setBulkResult(null)
  }

  const onCopy = async () => {
    if (!incidentId) return
    setCopying(true)
    setReportError(null)
    try {
      const report = await generateDiagnosticSupportReport(incidentId, includeDockerEvidence)
      await navigator.clipboard.writeText(JSON.stringify(report, null, 2))
    } catch (error) {
      setReportError(error)
    } finally {
      setCopying(false)
    }
  }

  const onDownload = async () => {
    if (!incidentId) return
    setDownloading(true)
    setReportError(null)
    try {
      const report = await generateDiagnosticSupportReport(incidentId, includeDockerEvidence)
      downloadReport(report)
    } catch (error) {
      setReportError(error)
    } finally {
      setDownloading(false)
    }
  }

  return (
    <div className="space-y-5">
      {incidentId ? (
        <div className="space-y-3">
          <Button type="button" variant="outline" size="sm" onClick={() => setIncident(null)}>
            {t("diagnostics.incidents.backToList")}
          </Button>
          {detail.isLoading ? <LoadingCard text={t("diagnostics.incident.loadingDescription")} /> : null}
          {detail.error ? (
            <ApiProblemAlert
              error={detail.error}
              title={t("diagnostics.incident.loadErrorTitle")}
              fallbackDescription={t("diagnostics.incident.loadErrorDescription")}
              onRetry={() => void detail.refetch()}
              retrying={detail.isFetching}
              showDiagnosticsLink={false}
            />
          ) : null}
          {detail.data ? (
            <>
              <DiagnosticIncidentDetail
                detail={detail.data}
                copying={copying}
                downloading={downloading}
                onCopy={() => void onCopy()}
                onDownload={() => void onDownload()}
                canIncludeDockerEvidence={detail.data.capabilities.canReadDockerEvidence}
                includeDockerEvidence={includeDockerEvidence}
                onIncludeDockerEvidenceChange={setIncludeDockerEvidence}
              />
              {detail.data.capabilities.canReadDockerEvidence ? (
                <DiagnosticDockerEvidence
                  incidentId={incidentId}
                  canOpenPortainer={detail.data.capabilities.canOpenPortainer}
                />
              ) : null}
            </>
          ) : null}
          {reportError ? (
            <ApiProblemAlert
              error={reportError}
              title={t("diagnostics.report.errorTitle")}
              fallbackDescription={t("diagnostics.report.errorDescription")}
              showDiagnosticsLink={false}
            />
          ) : null}
        </div>
      ) : (
        <>
          <Card>
            <CardHeader>
              <CardTitle className="text-base">{t("diagnostics.incidents.title")}</CardTitle>
              <CardDescription>{t("diagnostics.incidents.description")}</CardDescription>
            </CardHeader>
            <CardContent>
              <form className="flex flex-col gap-2 sm:flex-row" onSubmit={onSearch}>
                <Input
                  value={searchInput}
                  onChange={(event) => setSearchInput(event.target.value)}
                  placeholder={t("diagnostics.filters.searchPlaceholder")}
                  aria-label={t("diagnostics.filters.search")}
                />
                <Select
                  value={feature}
                  onChange={(event) => {
                    setFeature(event.target.value)
                    setCursor(null)
                    setCursorHistory([])
                    setSelectedIncidentIds(new Set())
                    setBulkResult(null)
                  }}
                  aria-label={t("diagnostics.filters.feature")}
                  wrapperClassName="sm:w-56"
                  className="w-full"
                >
                  <option value="">{t("diagnostics.filters.allFeatures")}</option>
                  <option value="api">API</option>
                  <option value="migration">Migration</option>
                  <option value="restore">Restore</option>
                  <option value="setup">Setup</option>
                  <option value="federation">Federation</option>
                  <option value="turn">TURN</option>
                </Select>
                <Button type="submit" variant="outline">
                  <ListFilter className="mr-2 h-4 w-4" />
                  {t("diagnostics.filters.apply")}
                </Button>
              </form>
              <div
                className="mt-4 flex flex-wrap gap-2 border-t pt-4"
                role="group"
                aria-label={t("diagnostics.lifecycle.filtersLabel")}
              >
                {diagnosticsIncidentLifecycleFilters.map((filter) => (
                  <Button
                    key={filter}
                    type="button"
                    size="sm"
                    variant={lifecycle === filter ? "secondary" : "outline"}
                    aria-pressed={lifecycle === filter}
                    onClick={() => setLifecycle(filter)}
                  >
                    {lifecycleFilterLabel(filter, t)}
                  </Button>
                ))}
              </div>
            </CardContent>
          </Card>

          {incidents.isLoading && !incidents.data ? <LoadingCard text={t("diagnostics.incidents.loading")} /> : null}
          {incidents.error && !incidents.data ? (
            <ApiProblemAlert
              error={incidents.error}
              title={t("diagnostics.incidents.loadErrorTitle")}
              fallbackDescription={t("diagnostics.incidents.loadErrorDescription")}
              onRetry={() => void incidents.refetch()}
              retrying={incidents.isFetching}
              showDiagnosticsLink={false}
            />
          ) : null}
          {incidents.error && incidents.data ? <StaleAlert /> : null}
          {incidents.data?.partial ? <PartialAlert warnings={incidents.data.warnings} /> : null}
          {incidents.data && incidents.data.incidents.length === 0 ? (
            lifecycle === "open" && incidentCount === 0 && !search && !feature ? (
              <Card>
                <CardHeader>
                  <CardTitle className="text-base">{t("diagnostics.incidents.noAttentionTitle")}</CardTitle>
                  <CardDescription>
                    {canReadEvents && technicalEventCount > 0
                      ? t("diagnostics.incidents.eventsAvailable", { count: technicalEventCount })
                      : t("diagnostics.incidents.noAttentionDescription")}
                  </CardDescription>
                </CardHeader>
                {canReadEvents && technicalEventCount > 0 ? (
                  <CardContent>
                    <Button asChild variant="outline" size="sm">
                      <Link to="/diagnostics/logs?tab=events">{t("diagnostics.incidents.viewEvents")}</Link>
                    </Button>
                  </CardContent>
                ) : null}
              </Card>
            ) : (
              <EmptyCard title={t("diagnostics.incidents.emptyTitle")} description={t("diagnostics.incidents.emptyDescription")} />
            )
          ) : null}
          {incidents.data && incidents.data.incidents.length > 0 ? (
            <div className="space-y-3">
              {canManageIncidentLifecycle && visibleResolvableIncidentIds.length > 0 ? (
                <div className="flex flex-col gap-3 rounded-lg border bg-muted/15 p-3 sm:flex-row sm:items-center sm:justify-between">
                  <label className="flex items-center gap-2 text-sm font-medium">
                    <Checkbox
                      checked={visibleResolvableIncidentIds.every((id) => selectedIncidentIds.has(id))}
                      onCheckedChange={(checked) => {
                        setBulkResult(null)
                        setSelectedIncidentIds(checked === true
                          ? new Set(visibleResolvableIncidentIds)
                          : new Set())
                      }}
                      aria-label={t("diagnostics.incidents.bulk.selectAllVisible")}
                    />
                    <span>{t("diagnostics.incidents.bulk.selectAllVisible")}</span>
                  </label>
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="text-sm text-muted-foreground">
                      {t("diagnostics.incidents.bulk.selected", { count: selectedIncidentIds.size })}
                    </span>
                    {selectedIncidentIds.size > 0 ? (
                      <>
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          disabled={bulkResolve.isPending}
                          onClick={() => {
                            setSelectedIncidentIds(new Set())
                            setBulkResult(null)
                          }}
                        >
                          {t("diagnostics.incidents.bulk.clearSelection")}
                        </Button>
                        <Button
                          type="button"
                          size="sm"
                          disabled={bulkResolve.isPending}
                          onClick={() => {
                            setBulkResult(null)
                            setBulkResolveOpen(true)
                          }}
                        >
                          {t("diagnostics.incidents.bulk.resolveSelected")}
                        </Button>
                      </>
                    ) : null}
                  </div>
                </div>
              ) : null}

              {bulkResult ? (
                <Alert variant={bulkResult.failed > 0 ? "destructive" : "default"}>
                  <AlertTitle>{t("diagnostics.incidents.bulk.resultTitle")}</AlertTitle>
                  <AlertDescription>
                    {bulkResult.failed > 0
                      ? t("diagnostics.incidents.bulk.resultPartial", {
                          resolved: bulkResult.resolved,
                          failed: bulkResult.failed,
                        })
                      : t("diagnostics.incidents.bulk.resultSuccess", { count: bulkResult.resolved })}
                  </AlertDescription>
                </Alert>
              ) : null}

              {incidents.data.incidents.map((incident) => (
                <IncidentCard
                  key={incident.incidentId}
                  incident={incident}
                  onOpen={() => setIncident(incident.incidentId)}
                  selectable={canManageIncidentLifecycle && (incident.lifecycle?.state ?? "open") !== "resolved"}
                  selected={selectedIncidentIds.has(incident.incidentId)}
                  onSelectedChange={(selected) => {
                    setBulkResult(null)
                    setSelectedIncidentIds((current) => {
                      const next = new Set(current)
                      if (selected) next.add(incident.incidentId)
                      else next.delete(incident.incidentId)
                      return next
                    })
                  }}
                />
              ))}
              <CursorControls
                canPrevious={cursorHistory.length > 0}
                canNext={Boolean(incidents.data.nextCursor)}
                onPrevious={() => {
                  const history = [...cursorHistory]
                  const previous = history.pop() ?? null
                  setCursorHistory(history)
                  setCursor(previous)
                  setSelectedIncidentIds(new Set())
                  setBulkResult(null)
                }}
                onNext={() => {
                  if (!incidents.data?.nextCursor) return
                  setCursorHistory((history) => [...history, cursor])
                  setCursor(incidents.data.nextCursor)
                  setSelectedIncidentIds(new Set())
                  setBulkResult(null)
                }}
              />

              <ConfirmationDialog
                open={bulkResolveOpen}
                onOpenChange={setBulkResolveOpen}
                title={t("diagnostics.incidents.bulk.resolveTitle")}
                description={t("diagnostics.incidents.bulk.resolveDescription", { count: selectedIncidentIds.size })}
                confirmLabel={t("diagnostics.incidents.bulk.resolveSelected")}
                confirmingLabel={t("diagnostics.incidents.bulk.working")}
                cancelLabel={t("common.cancel")}
                onConfirm={() => {
                  if (!bulkResolutionCode || selectedIncidentIds.size === 0) return
                  bulkResolve.mutate({
                    incidentIds: [...selectedIncidentIds],
                    resolutionCode: bulkResolutionCode,
                  })
                }}
                isConfirming={bulkResolve.isPending}
                showProgress={bulkResolve.isPending}
                confirmDisabled={!bulkResolutionCode || selectedIncidentIds.size === 0}
              >
                <div className="space-y-2">
                  <Label htmlFor="diagnostics-bulk-resolution-code">
                    {t("diagnostics.lifecycle.resolutionReason")}
                  </Label>
                  <Select
                    id="diagnostics-bulk-resolution-code"
                    value={bulkResolutionCode}
                    onChange={(event) => setBulkResolutionCode(event.target.value)}
                  >
                    <option value="">{t("diagnostics.lifecycle.selectReason")}</option>
                    {diagnosticsIncidentResolutionCodes.map((code) => (
                      <option key={code} value={code}>
                        {diagnosticsIncidentResolutionLabel(code, t)}
                      </option>
                    ))}
                  </Select>
                </div>
              </ConfirmationDialog>
            </div>
          ) : null}
        </>
      )}
    </div>
  )
}

function EventsPanel() {
  const { t } = useI18n()
  const [searchParams, setSearchParams] = useSearchParams()
  const search = searchParams.get("search")?.trim() ?? ""
  const feature = searchParams.get("feature")?.trim() ?? ""
  const severity = normalizeEventLevel(searchParams.get("level"))
  const [searchInput, setSearchInput] = useState(search)

  useEffect(() => {
    setSearchInput(search)
  }, [search])

  const updateFilters = (updates: Readonly<Record<string, string>>) => {
    const next = new URLSearchParams(searchParams)
    next.set("tab", "events")
    next.delete("incident")
    for (const [name, value] of Object.entries(updates)) {
      if (value) next.set(name, value)
      else next.delete(name)
    }
    setSearchParams(next, { replace: true })
  }

  const events = useInfiniteQuery({
    queryKey: ["diagnostics", "events", search, feature, severity],
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) => listDiagnosticEvents({
      search: search || undefined,
      feature: feature || undefined,
      severity: severity || undefined,
      cursor: pageParam || undefined,
      pageSize: 50,
    }),
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
    staleTime: 10_000,
    refetchInterval: 30_000,
    refetchIntervalInBackground: false,
    retry: 0,
  })

  const loadedEvents = useMemo(() => {
    const byId = new Map<string, DiagnosticsEvent>()
    for (const page of events.data?.pages ?? []) {
      for (const event of page.events) byId.set(event.eventId, event)
    }
    return Array.from(byId.values()).sort((left, right) =>
      right.timestampUtc.localeCompare(left.timestampUtc) || right.eventId.localeCompare(left.eventId),
    )
  }, [events.data?.pages])
  const warnings = Array.from(new Set(
    (events.data?.pages ?? []).flatMap((page) => page.warnings),
  ))
  const filtersActive = Boolean(search || feature || severity)

  return (
    <div className="space-y-5">
      <Card>
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <CardTitle className="text-base">{t("diagnostics.events.title")}</CardTitle>
              <CardDescription>{t("diagnostics.events.description")}</CardDescription>
            </div>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => void events.refetch()}
              disabled={events.isFetching}
            >
              <RefreshCw className={events.isFetching ? "mr-2 h-4 w-4 animate-spin" : "mr-2 h-4 w-4"} />
              {t("diagnostics.events.refreshNewest")}
            </Button>
          </div>
        </CardHeader>
        <CardContent className="space-y-3">
          <form
            className="grid gap-2 lg:grid-cols-[minmax(0,1fr)_12rem_12rem_auto]"
            onSubmit={(event) => {
              event.preventDefault()
              updateFilters({ search: searchInput.trim() })
            }}
          >
            <Input
              value={searchInput}
              onChange={(event) => setSearchInput(event.target.value)}
              placeholder={t("diagnostics.filters.searchPlaceholder")}
              aria-label={t("diagnostics.filters.search")}
            />
            <Select
              value={severity}
              onChange={(event) => updateFilters({ level: event.target.value })}
              aria-label={t("diagnostics.filters.severity")}
              className="w-full"
            >
              <option value="">{t("diagnostics.filters.allSeverities")}</option>
              <option value="warning">{t("diagnostics.severity.warning")}</option>
              <option value="error">{t("diagnostics.severity.error")}</option>
              <option value="critical">{t("diagnostics.severity.critical")}</option>
              <option value="information">{t("diagnostics.severity.information")}</option>
            </Select>
            <Select
              value={feature}
              onChange={(event) => updateFilters({ feature: event.target.value })}
              aria-label={t("diagnostics.filters.feature")}
              className="w-full"
            >
              <option value="">{t("diagnostics.filters.allFeatures")}</option>
              <option value="api">API</option>
              <option value="migration">Migration</option>
              <option value="restore">Restore</option>
              <option value="setup">Setup</option>
              <option value="federation">Federation</option>
              <option value="turn">TURN</option>
              <option value="logging">Logging</option>
            </Select>
            <Button type="submit" variant="outline">
              <ListFilter className="mr-2 h-4 w-4" />
              {t("diagnostics.filters.apply")}
            </Button>
          </form>
          {filtersActive ? (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => {
                setSearchInput("")
                updateFilters({ search: "", feature: "", level: "" })
              }}
            >
              {t("diagnostics.filters.clear")}
            </Button>
          ) : null}
        </CardContent>
      </Card>

      {events.isLoading && !events.data ? <LoadingCard text={t("diagnostics.events.loading")} /> : null}
      {events.error && !events.data ? (
        <ApiProblemAlert
          error={events.error}
          title={t("diagnostics.events.loadErrorTitle")}
          fallbackDescription={t("diagnostics.events.loadErrorDescription")}
          onRetry={() => void events.refetch()}
          retrying={events.isFetching}
          showDiagnosticsLink={false}
        />
      ) : null}
      {events.error && events.data ? <StaleAlert /> : null}
      {warnings.length > 0 ? <PartialAlert warnings={warnings} /> : null}
      {events.data && loadedEvents.length === 0 ? (
        <EmptyCard title={t("diagnostics.events.emptyTitle")} description={t("diagnostics.events.emptyDescription")} />
      ) : null}
      {events.data && loadedEvents.length > 0 ? (
        <div className="space-y-3">
          <div className="flex flex-wrap items-center justify-between gap-2 text-sm text-muted-foreground">
            <span>{t("diagnostics.events.loadedCount", { count: loadedEvents.length })}</span>
            <span>{severity ? t("diagnostics.events.filteredNewest") : t("diagnostics.events.newestFirst")}</span>
          </div>
          {loadedEvents.map((event) => <DiagnosticEventDisclosure key={event.eventId} event={event} />)}
          {events.hasNextPage ? (
            <div className="flex justify-center pt-2">
              <Button
                type="button"
                variant="outline"
                onClick={() => void events.fetchNextPage()}
                disabled={events.isFetchingNextPage}
              >
                {events.isFetchingNextPage
                  ? t("diagnostics.events.loadingMore")
                  : t("diagnostics.events.loadMore")}
              </Button>
            </div>
          ) : (
            <p className="text-center text-xs text-muted-foreground">
              {t("diagnostics.events.allLoaded")}
            </p>
          )}
        </div>
      ) : null}
    </div>
  )
}

function normalizeEventLevel(value: string | null): string {
  switch (value?.toLowerCase()) {
    case "warning":
    case "error":
    case "critical":
    case "information":
      return value.toLowerCase()
    default:
      return ""
  }
}

function LoggingHealthPanel() {
  const { t } = useI18n()
  const health = useQuery({
    queryKey: ["diagnostics", "logging-health"],
    queryFn: getDiagnosticsLoggingHealth,
    staleTime: 10_000,
    refetchInterval: 30_000,
    refetchIntervalInBackground: false,
    retry: 0,
  })

  if (health.isLoading && !health.data) return <LoadingCard text={t("diagnostics.health.loading")} />
  if (health.error && !health.data) {
    return (
      <ApiProblemAlert
        error={health.error}
        title={t("diagnostics.health.loadErrorTitle")}
        fallbackDescription={t("diagnostics.health.loadErrorDescription")}
        onRetry={() => void health.refetch()}
        retrying={health.isFetching}
        showDiagnosticsLink={false}
      />
    )
  }
  if (!health.data) return null

  return (
    <div className="space-y-4">
      {health.error ? <StaleAlert /> : null}
      <LoggingHealthCards health={health.data} />
    </div>
  )
}

function LoggingHealthCards({ health }: { health: DiagnosticsLoggingHealthResponse }) {
  const { language, t } = useI18n()
  const seqLabel = loggingHealthSeqLabel(health.seq.status, t)
  const seqDescription = loggingHealthSeqDescription(health.seq.status, t)
  const seqNeutral = isNeutralLoggingHealthSeqStatus(health.seq.status)
  const hasEverRecordedEvent = health.safeEventStore.hasEverRecordedEvent ||
    Boolean(health.safeEventStore.lastWriteAtUtc) ||
    health.safeEventStore.storedEventCount > 0

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle className="text-base">{t("diagnostics.health.layersTitle")}</CardTitle>
          <CardDescription>{t("diagnostics.health.layersDescription")}</CardDescription>
        </CardHeader>
        <CardContent>
          <ul className="list-disc space-y-2 pl-5 text-sm text-muted-foreground">
            <li>{t("diagnostics.health.layersIncidents")}</li>
            <li>{t("diagnostics.health.layersSafeEvents")}</li>
            <li>{t("diagnostics.health.layersClef")}</li>
          </ul>
        </CardContent>
      </Card>

      <div className="grid gap-4 lg:grid-cols-3">
        <HealthCard
          title={t("diagnostics.health.localRecorder")}
          status={health.localRecorder.status}
          description={localRecorderDescription(health, t)}
          facts={[
            fact(t("diagnostics.health.enabled"), yesNo(health.localRecorder.enabled, t)),
            fact(t("diagnostics.health.files"), formatNumber(health.localRecorder.retainedFileCount, language)),
            fact(t("diagnostics.health.bytes"), formatBytes(health.localRecorder.retainedBytes, language)),
            fact(t("diagnostics.health.lastWrite"), health.localRecorder.lastFileWriteAtUtc ? formatDiagnosticsLocalDateTime(health.localRecorder.lastFileWriteAtUtc, language, true) : t("diagnostics.health.never")),
            ...(health.localRecorder.persistentFilePath ? [fact(t("diagnostics.health.path"), health.localRecorder.persistentFilePath)] : []),
            ...storageFacts(health.localRecorder.storage, language, t),
          ]}
        />
        <HealthCard
          title={t("diagnostics.health.safeStore")}
          status={health.safeEventStore.status}
          description={safeStoreDescription(health, hasEverRecordedEvent, t)}
          facts={[
            fact(t("diagnostics.health.eventsStoredSinceStart"), formatNumber(health.safeEventStore.storedEventCount, language)),
            fact(t("diagnostics.health.eventsDropped"), formatNumber(health.safeEventStore.droppedEventCount, language)),
            fact(t("diagnostics.health.malformed"), formatNumber(health.safeEventStore.malformedLineCount, language)),
            fact(t("diagnostics.health.lastWrite"), health.safeEventStore.lastWriteAtUtc ? formatDiagnosticsLocalDateTime(health.safeEventStore.lastWriteAtUtc, language, true) : t("diagnostics.health.never")),
            ...storageFacts(health.safeEventStore.storage, language, t),
          ]}
        />
        <HealthCard
          title="Seq"
          status={seqLabel}
          neutral={seqNeutral}
          description={seqDescription}
          facts={[
            fact(t("diagnostics.health.sinkEnabled"), yesNo(health.seq.sinkEnabled, t)),
            fact(t("diagnostics.health.managementEnabled"), yesNo(health.seq.managementEnabled, t)),
            fact(t("diagnostics.health.reachable"), yesNo(health.seq.reachable, t)),
            fact(t("diagnostics.health.lastChecked"), health.seq.lastCheckedAtUtc ? formatDiagnosticsLocalDateTime(health.seq.lastCheckedAtUtc, language, true) : t("diagnostics.health.never")),
            fact(t("diagnostics.health.lastSuccess"), health.seq.lastSuccessAtUtc ? formatDiagnosticsLocalDateTime(health.seq.lastSuccessAtUtc, language, true) : t("diagnostics.health.never")),
            ...(health.seq.warningCode ? [fact(t("diagnostics.health.warningCode"), health.seq.warningCode)] : []),
          ]}
          action={health.seq.serverUrl ? (
            <Button asChild variant="outline" size="sm">
              <a href={health.seq.serverUrl} target="_blank" rel="noopener noreferrer">
                <ExternalLink className="mr-2 h-4 w-4" />
                {t("diagnostics.health.openSeq")}
              </a>
            </Button>
          ) : null}
        />
        {health.warnings.length > 0 ? (
          <Alert className="lg:col-span-3">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("diagnostics.health.warnings")}</AlertTitle>
            <AlertDescription>
              <ul className="list-disc space-y-1 pl-5">
                {health.warnings.map((warning) => <li key={warning}>{warning}</li>)}
              </ul>
            </AlertDescription>
          </Alert>
        ) : null}
      </div>

      <DiagnosticsPipelineSelfTestCard enabled={health.capabilities.canVerifyPipeline} />
    </div>
  )
}

function isNeutralLoggingHealthSeqStatus(status: string) {
  return status === "optional-disabled" ||
    status === "stopped-intentionally" ||
    status === "runtime-absent"
}

function loggingHealthSeqLabel(status: string, t: Translate) {
  switch (status) {
    case "optional-disabled":
      return t("diagnostics.health.notConfigured")
    case "stopped-intentionally":
      return t("diagnostics.seq.healthStatus.stopped-intentionally")
    case "runtime-absent":
      return t("diagnostics.seq.healthStatus.runtime-absent")
    default:
      return status
  }
}

function loggingHealthSeqDescription(status: string, t: Translate) {
  switch (status) {
    case "optional-disabled":
      return t("diagnostics.health.seqOptional")
    case "stopped-intentionally":
      return t("diagnostics.seq.runtime.stopped")
    case "runtime-absent":
      return t("diagnostics.seq.runtime.absent")
    default:
      return t("diagnostics.health.seqExpected")
  }
}

function localRecorderDescription(
  health: DiagnosticsLoggingHealthResponse,
  t: ReturnType<typeof useI18n>["t"],
): string {
  if (!health.localRecorder.enabled || !health.localRecorder.persistentRecorderConfigured) {
    return t("diagnostics.health.recorderDisabled")
  }

  if (!health.localRecorder.persistentRecorderActive || health.localRecorder.status !== "ready") {
    return t("diagnostics.health.recorderUnavailable")
  }

  return health.localRecorder.lastFileWriteAtUtc
    ? t("diagnostics.health.recorderActive")
    : t("diagnostics.health.recorderNeverWritten")
}

function safeStoreDescription(
  health: DiagnosticsLoggingHealthResponse,
  hasEverRecordedEvent: boolean,
  t: ReturnType<typeof useI18n>["t"],
): string {
  if (!health.safeEventStore.enabled) {
    return t("diagnostics.health.safeStoreDisabled")
  }

  if (health.safeEventStore.status !== "ready") {
    return t("diagnostics.health.safeStoreUnavailable")
  }

  return hasEverRecordedEvent
    ? t("diagnostics.health.safeStoreActive")
    : t("diagnostics.health.safeStoreNeverRecorded")
}

function IncidentCard({
  incident,
  onOpen,
  selectable,
  selected,
  onSelectedChange,
}: {
  incident: DiagnosticsIncidentSummary
  onOpen: () => void
  selectable: boolean
  selected: boolean
  onSelectedChange: (selected: boolean) => void
}) {
  const { language, t } = useI18n()
  return (
    <Card>
      <CardContent className="flex flex-col gap-4 py-4 md:flex-row md:items-start md:justify-between">
        <div className="flex min-w-0 flex-1 items-start gap-3">
          {selectable ? (
            <Checkbox
              className="mt-0.5"
              checked={selected}
              onCheckedChange={(checked) => onSelectedChange(checked === true)}
              aria-label={t("diagnostics.incidents.bulk.selectIncident", { incidentId: incident.incidentId })}
            />
          ) : null}
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2">
              <DiagnosticIncidentLifecycleBadge lifecycle={incident.lifecycle} />
              <DiagnosticSeverityBadge severity={incident.severity} />
              <span className="font-mono text-xs text-muted-foreground">{incident.eventCode}</span>
            </div>
            <button
              type="button"
              className="mt-2 block text-left font-medium hover:underline focus-visible:rounded-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              onClick={onOpen}
            >
              {incident.message}
            </button>
            <button
              type="button"
              className="mt-1 block break-all text-left font-mono text-xs text-muted-foreground hover:text-foreground hover:underline focus-visible:rounded-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              onClick={onOpen}
            >
              {incident.incidentId}
            </button>
            <p className="mt-2 text-xs text-muted-foreground">
              {t("diagnostics.incident.lastSeen")}: {" "}
              <span title={`${t("diagnostics.time.utcEvidence")}: ${diagnosticsUtcIso(incident.lastSeenAtUtc)}`}>
                {formatDiagnosticsLocalDateTime(incident.lastSeenAtUtc, language, true)}
              </span>
              {" · "}{t("diagnostics.incident.occurrences")}: {incident.occurrenceCount}
            </p>
          </div>
        </div>
        <Button type="button" variant="outline" size="sm" onClick={onOpen}>
          {t("diagnostics.incidents.open")}
        </Button>
      </CardContent>
    </Card>
  )
}

function HealthCard({
  title,
  status,
  facts,
  description,
  neutral = false,
  action = null,
}: {
  title: string
  status: string
  facts: readonly (readonly [string, string])[]
  description?: string
  neutral?: boolean
  action?: ReactNode
}) {
  return (
    <Card>
      <CardHeader>
        <div className="flex items-center justify-between gap-2">
          <CardTitle className="text-base">{title}</CardTitle>
          <Badge variant={neutral || status === "ready" ? "outline" : "secondary"}>{status}</Badge>
        </div>
        {description ? <CardDescription>{description}</CardDescription> : null}
      </CardHeader>
      <CardContent>
        <dl className="space-y-3">
          {facts.map(([label, value]) => (
            <div key={label}>
              <dt className="text-xs text-muted-foreground">{label}</dt>
              <dd className="mt-1 break-all text-sm font-medium">{value}</dd>
            </div>
          ))}
        </dl>
        {action ? <div className="pt-1">{action}</div> : null}
      </CardContent>
    </Card>
  )
}

function TabButton({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-current={active ? "page" : undefined}
      className={active
        ? "inline-flex items-center gap-2 border-b-2 border-primary px-3 py-2 text-sm font-medium"
        : "inline-flex items-center gap-2 border-b-2 border-transparent px-3 py-2 text-sm text-muted-foreground hover:text-foreground"}
    >
      {children}
    </button>
  )
}

function LoadingCard({ text }: { text: string }) {
  return <Card><CardContent className="py-6 text-sm text-muted-foreground">{text}</CardContent></Card>
}

function EmptyCard({ title, description }: { title: string; description: string }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">{title}</CardTitle>
        <CardDescription>{description}</CardDescription>
      </CardHeader>
    </Card>
  )
}

function StaleAlert() {
  const { t } = useI18n()
  return (
    <Alert>
      <AlertTriangle className="h-4 w-4" />
      <AlertTitle>{t("diagnostics.stale.title")}</AlertTitle>
      <AlertDescription>{t("diagnostics.stale.description")}</AlertDescription>
    </Alert>
  )
}

function PartialAlert({ warnings }: { warnings: readonly string[] }) {
  const { t } = useI18n()
  return (
    <Alert>
      <AlertTriangle className="h-4 w-4" />
      <AlertTitle>{t("diagnostics.partial.title")}</AlertTitle>
      <AlertDescription>
        <p>{t("diagnostics.partial.description")}</p>
        {warnings.length > 0 ? (
          <ul className="mt-2 list-disc space-y-1 pl-5">
            {warnings.map((warning) => <li key={warning}>{warning}</li>)}
          </ul>
        ) : null}
      </AlertDescription>
    </Alert>
  )
}

function CursorControls({
  canPrevious,
  canNext,
  onPrevious,
  onNext,
}: {
  canPrevious: boolean
  canNext: boolean
  onPrevious: () => void
  onNext: () => void
}) {
  const { t } = useI18n()
  return (
    <div className="flex justify-end gap-2">
      <Button type="button" variant="outline" size="sm" onClick={onPrevious} disabled={!canPrevious}>
        {t("diagnostics.pagination.previous")}
      </Button>
      <Button type="button" variant="outline" size="sm" onClick={onNext} disabled={!canNext}>
        {t("diagnostics.pagination.next")}
      </Button>
    </div>
  )
}

function downloadReport(report: DiagnosticsSupportReport) {
  const safeIncidentId = report.incident.incidentId.replace(/[^A-Za-z0-9_.-]/g, "-")
  const timestamp = report.generatedAtUtc.replace(/[-:]/g, "").replace(/\.\d+Z$/, "Z")
  const filename = `mem-diagnostics-${safeIncidentId}-${timestamp}.json`
  const blob = new Blob([JSON.stringify(report, null, 2)], { type: "application/json" })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement("a")
  anchor.href = url
  anchor.download = filename
  anchor.click()
  URL.revokeObjectURL(url)
}

function normalizeIncidentLifecycleFilter(value: string | null): DiagnosticsIncidentLifecycleFilter {
  switch (value) {
    case "all":
    case "acknowledged":
    case "snoozed":
    case "resolved":
      return value
    default:
      return "open"
  }
}

function storageFacts(
  storage: DiagnosticsLoggingHealthResponse["localRecorder"]["storage"],
  language: Parameters<typeof formatBytes>[1],
  t: ReturnType<typeof useI18n>["t"],
): readonly (readonly [string, string])[] {
  if (!storage) return []

  return [
    fact(t("diagnostics.health.storageStatus"), storage.status),
    ...(storage.availableBytes === null
      ? []
      : [fact(t("diagnostics.health.availableBytes"), formatBytes(storage.availableBytes, language))]),
    ...(storage.totalBytes === null
      ? []
      : [fact(t("diagnostics.health.totalBytes"), formatBytes(storage.totalBytes, language))]),
  ]
}

function fact(label: string, value: string): readonly [string, string] {
  return [label, value] as const
}

function yesNo(value: boolean, t: ReturnType<typeof useI18n>["t"]): string {
  return value ? t("common.yes") : t("common.no")
}
