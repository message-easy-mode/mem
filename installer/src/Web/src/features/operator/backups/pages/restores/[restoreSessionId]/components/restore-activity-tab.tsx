import { useMemo, useState } from "react"

import { useI18n, type I18nContextValue } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import {
  CheckCircle2,
  ChevronDown,
  ChevronRight,
  Clipboard,
  Download,
  FileText,
  Info,
  ListFilter,
  TriangleAlert,
} from "lucide-react"

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Select } from "@/components/ui/select"
import { cn } from "@/lib/utils"
import type { RestoreLogEvent } from "@/features/operator/backups/api/types/restore-logs.types"
import type {
  RestoreWorkspaceEvidenceItem,
  RestoreWorkspaceResponse,
} from "@/features/operator/backups/api/types/restore-workspace.types"
import { useRestoreLogs } from "@/features/operator/backups/hooks/use-restore-workspace"

import {
  getRestoreWorkspaceTechnicalDetail,
  RestoreWorkspaceTechnicalDetails,
} from "./restore-workspace-problems"

type ActivityFilter = "all" | "attention" | "successful"
type ActivityGrouping = "timeline" | "stage"
type ActivityStatus = "information" | "success" | "warning" | "error"
type ActivityOrigin = "log" | "evidence"

type ActivityEntry = {
  key: string
  occurredAtUtc: string
  title: string
  description: string
  stage: string | null
  eventCode: string | null
  operationId: string | null
  status: ActivityStatus
  origin: ActivityOrigin
}

const ACTIVITY_LOG_PAGE_SIZE = 100

/**
 * Curated, browser-owned labels for stable restore event codes.
 *
 * Event codes remain the machine/audit contract. Do not derive prose from an
 * unknown code: a future HostAgent code must stay literal until this explicit
 * feature-local map has a reviewed English/German presentation.
 */
const ACTIVITY_TITLE_KEY_BY_EVENT_CODE: Readonly<Record<string, TranslationKey>> = {
  "restore.attempt.created": "restoreWorkspace.activity.event.attemptCreated",
  "restore.source.preparation.started": "restoreWorkspace.activity.event.sourcePreparationStarted",
  "restore.source.validation.passed": "restoreWorkspace.activity.event.sourceValidationPassed",
  "restore.source.validation.failed": "restoreWorkspace.activity.event.sourceValidationFailed",
  "restore.standard-recreate.targets-reserved":
    "restoreWorkspace.activity.event.standardRecreateTargetsReserved",
  "restore.standard-recreate.started": "restoreWorkspace.activity.event.standardRecreateStarted",
  "restore.standard-recreate.completed": "restoreWorkspace.activity.event.standardRecreateCompleted",
  "restore.standard-recreate.completed-needs-verification":
    "restoreWorkspace.activity.event.standardRecreateCompletedNeedsVerification",
  "restore.standard-recreate.failed": "restoreWorkspace.activity.event.standardRecreateFailed",
  "restore.standard-recreate.cleanup.completed":
    "restoreWorkspace.activity.event.standardRecreateCleanupCompleted",
  "restore.standard-recreate.cleanup.failed":
    "restoreWorkspace.activity.event.standardRecreateCleanupFailed",
  "restore.private-test.requested": "restoreWorkspace.activity.event.privateTestRequested",
  "restore.private-test.started": "restoreWorkspace.activity.event.privateTestStarted",
  "restore.private-test.passed": "restoreWorkspace.activity.event.privateTestPassed",
  "restore.private-test.failed": "restoreWorkspace.activity.event.privateTestFailed",
  "restore.private-test.cancelled": "restoreWorkspace.activity.event.privateTestCancelled",
  "restore.cancelled": "restoreWorkspace.activity.event.cancelled",
  "restore.handover.completed": "restoreWorkspace.activity.event.handoverCompleted",
  "restore.handover.failed": "restoreWorkspace.activity.event.handoverFailed",
}

export function RestoreActivityTab({
  restoreSessionId,
  workspace,
  onViewLogs,
}: {
  restoreSessionId: string
  workspace: RestoreWorkspaceResponse
  onViewLogs: () => void
}) {
  const { t } = useI18n()
  const [filter, setFilter] = useState<ActivityFilter>("all")
  const [grouping, setGrouping] = useState<ActivityGrouping>("timeline")
  const [expandedEntryKey, setExpandedEntryKey] = useState<string | null>(null)

  const activityLogsQuery = useRestoreLogs(restoreSessionId, {
    page: 1,
    pageSize: ACTIVITY_LOG_PAGE_SIZE,
  })

  const allEntries = useMemo(
    () => buildActivityEntries(workspace, activityLogsQuery.data?.events ?? [], t),
    [activityLogsQuery.data?.events, t, workspace],
  )

  const visibleEntries = useMemo(
    () => allEntries.filter((entry) => matchesActivityFilter(entry, filter)),
    [allEntries, filter],
  )

  const groupedEntries = useMemo(
    () => groupActivityEntries(visibleEntries, grouping, t),
    [grouping, t, visibleEntries],
  )

  const timelineEndUtc = workspace.attempt.terminalAtUtc
    ?? workspace.attempt.lastEventAtUtc
    ?? workspace.attempt.updatedAtUtc
  const latestSuccess = workspace.evidence.latestSuccess
  const logsSummary = activityLogsQuery.data?.summary ?? null
  const containsMoreLogsThanActivityWindow = Boolean(
    activityLogsQuery.data
    && activityLogsQuery.data.totalEvents > activityLogsQuery.data.events.length,
  )
  const activityLogsTechnicalDetail = activityLogsQuery.isError
    ? getRestoreWorkspaceTechnicalDetail(activityLogsQuery.error)
    : undefined

  const exportActivity = () => {
    downloadJsonFile(
      `mem-restore-${restoreSessionId}-activity.json`,
      {
        restoreSessionId,
        generatedAtUtc: new Date().toISOString(),
        source: "curated restore-scoped timeline",
        filter,
        grouping,
        entries: visibleEntries,
      },
    )
  }

  const copyActivity = async () => {
    await copyText(
      visibleEntries
        .map((entry) => [
          entry.occurredAtUtc,
          entry.title,
          entry.description,
          `Stage: ${entry.stage ?? "—"}`,
          `Event: ${entry.eventCode ?? "—"}`,
          `Status: ${entry.status}`,
        ].join("\n"))
        .join("\n\n"),
    )
  }

  return (
    <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_280px]">
      <section className="min-w-0 space-y-4">
        <Card>
          <CardHeader className="border-b">
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div>
                <CardTitle>{t("restoreWorkspace.activity.title")}</CardTitle>
                <p className="mt-1 text-sm text-muted-foreground">
                  {t("restoreWorkspace.activity.description")}
                </p>
              </div>

              <div className="flex flex-wrap items-center gap-2">
                <div className="flex items-center gap-2 rounded-lg border bg-background/70 px-2 py-1">
                  <ListFilter className="h-3.5 w-3.5 text-muted-foreground" aria-hidden="true" />
                  <Select
                    value={filter}
                    onChange={(event) => setFilter(event.target.value as ActivityFilter)}
                    className="h-7 border-0 bg-transparent py-0 pl-0 shadow-none focus-visible:ring-0"
                    aria-label={t("restoreWorkspace.activity.filterAria")}
                  >
                    <option value="all">{t("restoreWorkspace.activity.filter.all")}</option>
                    <option value="attention">{t("restoreWorkspace.activity.filter.attention")}</option>
                    <option value="successful">{t("restoreWorkspace.activity.filter.successful")}</option>
                  </Select>
                </div>

                <Select
                  value={grouping}
                  onChange={(event) => setGrouping(event.target.value as ActivityGrouping)}
                  aria-label={t("restoreWorkspace.activity.groupingAria")}
                >
                  <option value="timeline">{t("restoreWorkspace.activity.grouping.timeline")}</option>
                  <option value="stage">{t("restoreWorkspace.activity.grouping.stage")}</option>
                </Select>

                <Button variant="outline" size="sm" onClick={exportActivity} disabled={visibleEntries.length === 0}>
                  <Download className="mr-2 h-3.5 w-3.5" />
                  {t("restoreWorkspace.activity.downloadJson")}
                </Button>
              </div>
            </div>
          </CardHeader>

          <CardContent className="p-0">
            {activityLogsQuery.isLoading && allEntries.length === 0 ? (
              <ActivityLoadingState />
            ) : visibleEntries.length === 0 ? (
              <EmptyActivityState filter={filter} onReset={() => setFilter("all")} />
            ) : grouping === "timeline" ? (
              <ActivityTimeline
                entries={visibleEntries}
                expandedEntryKey={expandedEntryKey}
                onToggleEntry={setExpandedEntryKey}
                onViewLogs={onViewLogs}
              />
            ) : (
              <ActivityStageGroups
                groups={groupedEntries}
                expandedEntryKey={expandedEntryKey}
                onToggleEntry={setExpandedEntryKey}
                onViewLogs={onViewLogs}
              />
            )}
          </CardContent>

          <div className="border-t bg-muted/20 px-4 py-3 text-xs text-muted-foreground">
            <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
              <span>
                {activityLogsQuery.data
                  ? t("restoreWorkspace.activity.shown", { count: visibleEntries.length })
                  : t("restoreWorkspace.activity.derivedFromEvidence")}
              </span>
              <Button variant="link" size="sm" className="h-auto px-0" onClick={onViewLogs}>
                {t("restoreWorkspace.activity.openFullLogs")}
              </Button>
            </div>
          </div>
        </Card>

        {activityLogsQuery.isError ? (
          <Alert variant="destructive">
            <TriangleAlert className="h-4 w-4" />
            <AlertTitle>{t("restoreWorkspace.activity.loadErrorTitle")}</AlertTitle>
            <AlertDescription>
              {t("restoreWorkspace.activity.loadErrorDescription")}
              <RestoreWorkspaceTechnicalDetails detail={activityLogsTechnicalDetail} />
            </AlertDescription>
          </Alert>
        ) : null}

        <Alert>
          <Info className="h-4 w-4" />
          <AlertTitle>{t("restoreWorkspace.activity.aboutTitle")}</AlertTitle>
          <AlertDescription>
            {t("restoreWorkspace.activity.aboutDescription")}
            {containsMoreLogsThanActivityWindow
              ? ` ${t("restoreWorkspace.activity.aboutExtendedDescription")}`
              : ""}
          </AlertDescription>
        </Alert>
      </section>

      <aside className="space-y-4">
        <Card>
          <CardHeader>
            <CardTitle>{t("restoreWorkspace.activity.summary.title")}</CardTitle>
          </CardHeader>
          <CardContent className="space-y-2 text-sm">
            <SummaryRow label={t("restoreWorkspace.activity.summary.milestonesShown")} value={String(visibleEntries.length)} />
            <SummaryRow label={t("restoreWorkspace.activity.summary.allLogEvents")} value={String(logsSummary?.totalEvents ?? workspace.logs.totalEvents)} />
            <SummaryRow label={t("restoreWorkspace.activity.summary.warnings")} value={String(logsSummary?.warningCount ?? workspace.logs.warningCount)} tone="warning" />
            <SummaryRow label={t("restoreWorkspace.activity.summary.errors")} value={String(logsSummary?.errorCount ?? workspace.logs.errorCount)} tone="error" />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>{t("restoreWorkspace.activity.range.title")}</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3 text-sm">
            <TimelinePoint label={t("restoreWorkspace.activity.range.started")} value={workspace.attempt.createdAtUtc} />
            <TimelinePoint label={t("restoreWorkspace.activity.range.completed")} value={timelineEndUtc} />
            <div className="border-t pt-3">
              <div className="text-xs text-muted-foreground">{t("restoreWorkspace.activity.range.duration")}</div>
              <div className="mt-1 font-medium">{formatDuration(workspace.attempt.createdAtUtc, timelineEndUtc, t)}</div>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>{t("restoreWorkspace.activity.highlights.title")}</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3 text-sm">
            <Highlight
              state={workspace.source.validationStatus}
              successText={t("restoreWorkspace.activity.highlights.backupValidationPassed")}
              fallbackText={t("restoreWorkspace.activity.highlights.backupValidationAttention")}
            />
            <Highlight
              state={workspace.verification.status}
              successText={t("restoreWorkspace.activity.highlights.publicChecksPassed")}
              fallbackText={t("restoreWorkspace.activity.highlights.publicVerificationAttention")}
            />
            <Highlight
              state={workspace.overallStatus.severity}
              successText={latestSuccess ? latestSuccess.title : workspace.overallStatus.title}
              fallbackText={workspace.overallStatus.title}
            />
            {(logsSummary?.warningCount ?? workspace.logs.warningCount) > 0 ? (
              <Highlight
                state="warning"
                successText=""
                fallbackText={t("restoreWorkspace.activity.highlights.warningsRecorded", {
                  count: logsSummary?.warningCount ?? workspace.logs.warningCount,
                })}
              />
            ) : (
              <Highlight
                state="success"
                successText={t("restoreWorkspace.activity.highlights.noWarnings")}
                fallbackText={t("restoreWorkspace.activity.highlights.noWarnings")}
              />
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>{t("restoreWorkspace.activity.actions.title")}</CardTitle>
          </CardHeader>
          <CardContent className="space-y-2">
            <Button variant="outline" size="sm" className="w-full justify-start" onClick={exportActivity} disabled={visibleEntries.length === 0}>
              <Download className="mr-2 h-3.5 w-3.5" />
              {t("restoreWorkspace.activity.downloadJson")}
            </Button>
            <Button variant="outline" size="sm" className="w-full justify-start" onClick={() => void copyActivity()} disabled={visibleEntries.length === 0}>
              <Clipboard className="mr-2 h-3.5 w-3.5" />
              {t("restoreWorkspace.activity.copyDisplayed")}
            </Button>
            <Button variant="outline" size="sm" className="w-full justify-start" onClick={onViewLogs}>
              <FileText className="mr-2 h-3.5 w-3.5" />
              {t("restoreWorkspace.activity.openLogs")}
            </Button>
          </CardContent>
        </Card>
      </aside>
    </div>
  )
}

function ActivityTimeline({
  entries,
  expandedEntryKey,
  onToggleEntry,
  onViewLogs,
}: {
  entries: ActivityEntry[]
  expandedEntryKey: string | null
  onToggleEntry: (entryKey: string | null) => void
  onViewLogs: () => void
}) {
  return (
    <ol className="relative divide-y before:absolute before:bottom-7 before:left-[29px] before:top-7 before:w-px before:bg-border">
      {entries.map((entry) => (
        <ActivityTimelineItem
          key={entry.key}
          entry={entry}
          expanded={expandedEntryKey === entry.key}
          onToggle={() => onToggleEntry(expandedEntryKey === entry.key ? null : entry.key)}
          onViewLogs={onViewLogs}
        />
      ))}
    </ol>
  )
}

function ActivityStageGroups({
  groups,
  expandedEntryKey,
  onToggleEntry,
  onViewLogs,
}: {
  groups: Array<{ label: string; entries: ActivityEntry[] }>
  expandedEntryKey: string | null
  onToggleEntry: (entryKey: string | null) => void
  onViewLogs: () => void
}) {
  return (
    <div className="divide-y">
      {groups.map((group) => (
        <section key={group.label}>
          <div className="bg-muted/30 px-4 py-2 text-xs font-medium tracking-wide text-muted-foreground uppercase">
            {group.label}
          </div>
          <ol className="relative before:absolute before:bottom-7 before:left-[29px] before:top-7 before:w-px before:bg-border">
            {group.entries.map((entry) => (
              <ActivityTimelineItem
                key={entry.key}
                entry={entry}
                expanded={expandedEntryKey === entry.key}
                onToggle={() => onToggleEntry(expandedEntryKey === entry.key ? null : entry.key)}
                onViewLogs={onViewLogs}
              />
            ))}
          </ol>
        </section>
      ))}
    </div>
  )
}

function ActivityTimelineItem({
  entry,
  expanded,
  onToggle,
  onViewLogs,
}: {
  entry: ActivityEntry
  expanded: boolean
  onToggle: () => void
  onViewLogs: () => void
}) {
  const { intlLocale, t } = useI18n()

  return (
    <li className="relative pl-14">
      <div
        className={cn(
          "absolute left-4 top-5 z-10 flex h-7 w-7 items-center justify-center rounded-full border border-background",
          activityStatusClasses(entry.status).marker,
        )}
        aria-hidden="true"
      >
        <ActivityStatusIcon status={entry.status} className="h-3.5 w-3.5" />
      </div>

      <button
        type="button"
        className="grid w-full gap-3 px-4 py-3 text-left transition-colors hover:bg-muted/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset md:grid-cols-[155px_minmax(0,1fr)_auto] md:items-center"
        onClick={onToggle}
        aria-expanded={expanded}
      >
        <time className="font-mono text-xs text-muted-foreground">
          {formatUtc(entry.occurredAtUtc, intlLocale)} {t("restoreWorkspace.activity.utc")}
        </time>

        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-medium">{entry.title}</span>
            <ActivityStatusBadge status={entry.status} />
          </div>
          <p className="mt-1 line-clamp-1 text-sm text-muted-foreground">
            {entry.description}
          </p>
        </div>

        <div className="flex items-center gap-2 md:justify-self-end">
          {entry.stage ? <StageBadge stage={entry.stage} /> : null}
          {expanded ? <ChevronDown className="h-4 w-4 text-muted-foreground" /> : <ChevronRight className="h-4 w-4 text-muted-foreground" />}
        </div>
      </button>

      {expanded ? (
        <div className="border-t bg-muted/20 px-4 py-3 md:pl-[calc(1rem+155px)]">
          <div className="grid gap-3 text-sm sm:grid-cols-2">
            <ActivityDetail label={t("restoreWorkspace.activity.detail.eventCode")} value={entry.eventCode ?? "—"} />
            <ActivityDetail label={t("restoreWorkspace.activity.detail.operationId")} value={entry.operationId ?? "—"} copyable={Boolean(entry.operationId)} />
            <ActivityDetail label={t("restoreWorkspace.activity.detail.stage")} value={entry.stage ?? "—"} />
            <ActivityDetail
              label={t("restoreWorkspace.activity.detail.recordSource")}
              value={entry.origin === "log"
                ? t("restoreWorkspace.activity.detail.recordSourceLog")
                : t("restoreWorkspace.activity.detail.recordSourceEvidence")}
            />
          </div>
          <div className="mt-3 flex justify-end">
            <Button variant="outline" size="sm" onClick={onViewLogs}>
              <FileText className="mr-2 h-3.5 w-3.5" />
              {t("restoreWorkspace.activity.openLogs")}
            </Button>
          </div>
        </div>
      ) : null}
    </li>
  )
}

function ActivityDetail({
  label,
  value,
  copyable = false,
}: {
  label: string
  value: string
  copyable?: boolean
}) {
  const { t } = useI18n()

  return (
    <div className="min-w-0">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="mt-1 flex min-w-0 items-start gap-1">
        <span className="min-w-0 break-all font-mono text-xs">{value}</span>
        {copyable && value !== "—" ? (
          <Button
            variant="ghost"
            size="icon-xs"
            className="shrink-0"
            onClick={(event) => {
              event.stopPropagation()
              void copyText(value)
            }}
            aria-label={t("restoreWorkspace.activity.copyValue", { label })}
          >
            <Clipboard className="h-3 w-3" />
          </Button>
        ) : null}
      </div>
    </div>
  )
}

function ActivityStatusBadge({ status }: { status: ActivityStatus }) {
  const { t } = useI18n()
  const classes = activityStatusClasses(status)

  return (
    <Badge variant="outline" className={classes.badge}>
      {formatStatus(status, t)}
    </Badge>
  )
}

function StageBadge({ stage }: { stage: string }) {
  return (
    <Badge variant="secondary" className="max-w-40 truncate font-mono text-[0.7rem] font-normal">
      {stage}
    </Badge>
  )
}

function ActivityStatusIcon({
  status,
  className,
}: {
  status: ActivityStatus
  className?: string
}) {
  if (status === "error") {
    return <TriangleAlert className={cn("text-destructive", className)} aria-hidden="true" />
  }

  if (status === "warning") {
    return <TriangleAlert className={cn("text-amber-500", className)} aria-hidden="true" />
  }

  if (status === "information") {
    return <Info className={cn("text-blue-500", className)} aria-hidden="true" />
  }

  return <CheckCircle2 className={cn("text-primary", className)} aria-hidden="true" />
}

function SummaryRow({
  label,
  value,
  tone,
}: {
  label: string
  value: string
  tone?: "warning" | "error"
}) {
  return (
    <div className="flex items-center justify-between gap-3">
      <span className={cn(
        "text-muted-foreground",
        tone === "warning" && "text-amber-700 dark:text-amber-300",
        tone === "error" && "text-destructive",
      )}>{label}</span>
      <span className="font-medium tabular-nums">{value}</span>
    </div>
  )
}

function TimelinePoint({ label, value }: { label: string; value: string | null }) {
  const { intlLocale, t } = useI18n()

  return (
    <div>
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="mt-1 font-medium">
        {value
          ? `${formatUtc(value, intlLocale)} ${t("restoreWorkspace.activity.utc")}`
          : t("restoreWorkspace.activity.range.notCompleted")}
      </div>
    </div>
  )
}

function Highlight({
  state,
  successText,
  fallbackText,
}: {
  state: string | null | undefined
  successText: string
  fallbackText: string
}) {
  const status = deriveActivityStatus(state, null)
  const success = status === "success" || status === "information"
  const text = success ? successText : fallbackText

  return (
    <div className="flex items-start gap-2">
      <ActivityStatusIcon status={success ? "success" : status} className="mt-0.5 h-4 w-4 shrink-0" />
      <span>{text}</span>
    </div>
  )
}

function ActivityLoadingState() {
  return (
    <div className="space-y-2 p-4">
      {Array.from({ length: 6 }).map((_, index) => (
        <div key={index} className="h-15 animate-pulse rounded-lg bg-muted" />
      ))}
    </div>
  )
}

function EmptyActivityState({
  filter,
  onReset,
}: {
  filter: ActivityFilter
  onReset: () => void
}) {
  const { t } = useI18n()
  const filtered = filter !== "all"

  return (
    <div className="p-10 text-center">
      <FileText className="mx-auto h-9 w-9 text-muted-foreground" />
      <h2 className="mt-3 font-medium">
        {filtered
          ? t("restoreWorkspace.activity.empty.filteredTitle")
          : t("restoreWorkspace.activity.empty.title")}
      </h2>
      <p className="mx-auto mt-1 max-w-lg text-sm text-muted-foreground">
        {filtered
          ? t("restoreWorkspace.activity.empty.filteredDescription")
          : t("restoreWorkspace.activity.empty.description")}
      </p>
      {filtered ? (
        <Button variant="outline" size="sm" className="mt-4" onClick={onReset}>
          {t("restoreWorkspace.activity.empty.reset")}
        </Button>
      ) : null}
    </div>
  )
}

function buildActivityEntries(
  workspace: RestoreWorkspaceResponse,
  logEvents: RestoreLogEvent[],
  t: I18nContextValue["t"],
) {
  const entriesByKey = new Map<string, ActivityEntry>()

  for (const event of logEvents) {
    if (!shouldPromoteLogEvent(event)) continue

    const entry = activityFromLogEvent(event, t)
    addActivityEntry(entriesByKey, entry)
  }

  for (const category of workspace.evidence.categories ?? []) {
    for (const item of category.items ?? []) {
      const entry = activityFromEvidenceItem(item, t)
      addActivityEntry(entriesByKey, entry)
    }
  }

  return [...entriesByKey.values()].sort(
    (left, right) => toTimestamp(left.occurredAtUtc) - toTimestamp(right.occurredAtUtc),
  )
}

function addActivityEntry(
  entriesByKey: Map<string, ActivityEntry>,
  candidate: ActivityEntry,
) {
  const dedupeKey = candidate.eventCode
    ? `event:${candidate.eventCode}`
    : `activity:${candidate.title}:${candidate.occurredAtUtc}`
  const existing = entriesByKey.get(dedupeKey)

  if (!existing || (existing.origin === "evidence" && candidate.origin === "log")) {
    entriesByKey.set(dedupeKey, candidate)
  }
}

function activityFromLogEvent(
  event: RestoreLogEvent,
  t: I18nContextValue["t"],
): ActivityEntry {
  const eventCode = event.eventCode ?? null

  return {
    key: event.eventId ?? `log:${event.timestampUtc}:${eventCode ?? event.message ?? "event"}`,
    occurredAtUtc: event.timestampUtc,
    title: titleForActivity(eventCode, event.message, t),
    description: event.message ?? t("restoreWorkspace.activity.event.recordedDescription"),
    stage: event.stage,
    eventCode,
    operationId: event.operationId,
    status: deriveActivityStatus(event.severity, eventCode),
    origin: "log",
  }
}

function activityFromEvidenceItem(
  item: RestoreWorkspaceEvidenceItem,
  t: I18nContextValue["t"],
): ActivityEntry {
  return {
    key: `evidence:${item.code}`,
    occurredAtUtc: item.occurredAtUtc,
    title: titleForActivity(item.eventCode, item.title, t),
    description: item.description,
    stage: item.stage,
    eventCode: item.eventCode,
    operationId: item.operationId,
    status: deriveActivityStatus(item.status, item.eventCode),
    origin: "evidence",
  }
}

function shouldPromoteLogEvent(event: RestoreLogEvent) {
  const severity = (event.severity ?? "information").toLowerCase()
  const eventCode = (event.eventCode ?? "").toLowerCase()

  if (severity === "error" || severity === "warning" || severity === "warn") {
    return true
  }

  return /started|completed|passed|failed|created|reserved|claimed|configured|provision|ready|generated|handover|cancelled|cleanup/.test(eventCode)
}

function titleForActivity(
  eventCode: string | null,
  fallback: string | null,
  t: I18nContextValue["t"],
) {
  const localisedKey = eventCode
    ? ACTIVITY_TITLE_KEY_BY_EVENT_CODE[eventCode]
    : undefined

  if (localisedKey) {
    return t(localisedKey)
  }

  // Unknown event codes are intentionally literal. They are stable evidence,
  // not a prompt to manufacture English prose from code segments.
  if (eventCode) {
    return eventCode
  }

  return fallback || t("restoreWorkspace.activity.event.recorded")
}

function deriveActivityStatus(
  sourceStatus: string | null | undefined,
  eventCode: string | null,
): ActivityStatus {
  const normalizedStatus = (sourceStatus ?? "information").toLowerCase()
  const normalizedCode = (eventCode ?? "").toLowerCase()

  if (normalizedStatus === "error" || normalizedStatus === "failed" || normalizedCode.includes("failed")) {
    return "error"
  }

  if (normalizedStatus === "warning" || normalizedStatus === "warn" || normalizedCode.includes("warn")) {
    return "warning"
  }

  if (
    normalizedStatus === "success"
    || normalizedStatus === "succeeded"
    || normalizedStatus === "completed"
    || normalizedStatus === "passed"
    || normalizedCode.includes("passed")
    || normalizedCode.includes("completed")
    || normalizedCode.includes("ready")
    || normalizedCode.includes("configured")
    || normalizedCode.includes("handover")
  ) {
    return "success"
  }

  return "information"
}

function matchesActivityFilter(entry: ActivityEntry, filter: ActivityFilter) {
  if (filter === "attention") {
    return entry.status === "warning" || entry.status === "error"
  }

  if (filter === "successful") {
    return entry.status === "success"
  }

  return true
}

function groupActivityEntries(
  entries: ActivityEntry[],
  grouping: ActivityGrouping,
  t: I18nContextValue["t"],
) {
  if (grouping === "timeline") {
    return [{ label: t("restoreWorkspace.activity.group.timeline"), entries }]
  }

  const groups = new Map<string, ActivityEntry[]>()

  for (const entry of entries) {
    const label = entry.stage ?? t("restoreWorkspace.activity.group.generalRestoreActivity")
    const current = groups.get(label) ?? []
    current.push(entry)
    groups.set(label, current)
  }

  return [...groups.entries()].map(([label, groupedEntries]) => ({
    label,
    entries: groupedEntries,
  }))
}

function activityStatusClasses(status: ActivityStatus) {
  if (status === "error") {
    return {
      badge: "border-destructive/40 bg-destructive/10 text-destructive",
      marker: "bg-destructive/15 text-destructive",
    }
  }

  if (status === "warning") {
    return {
      badge: "border-amber-500/40 bg-amber-500/10 text-amber-700 dark:text-amber-300",
      marker: "bg-amber-500/15 text-amber-600 dark:text-amber-300",
    }
  }

  if (status === "information") {
    return {
      badge: "border-blue-500/40 bg-blue-500/10 text-blue-700 dark:text-blue-300",
      marker: "bg-blue-500/15 text-blue-600 dark:text-blue-300",
    }
  }

  return {
    badge: "border-primary/40 bg-primary/10 text-primary",
    marker: "bg-primary/15 text-primary",
  }
}

function formatStatus(status: ActivityStatus, t: I18nContextValue["t"]) {
  if (status === "information") return t("restoreWorkspace.activity.status.information")
  if (status === "success") return t("restoreWorkspace.activity.status.success")
  if (status === "warning") return t("restoreWorkspace.activity.status.warning")
  return t("restoreWorkspace.activity.status.error")
}

function formatUtc(value: string, intlLocale: string) {
  return new Intl.DateTimeFormat(intlLocale, {
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
    .replace(",", "")
}

function formatDuration(
  startedAtUtc: string,
  completedAtUtc: string | null,
  t: I18nContextValue["t"],
) {
  if (!completedAtUtc) return t("restoreWorkspace.activity.range.inProgress")

  const durationMs = toTimestamp(completedAtUtc) - toTimestamp(startedAtUtc)
  if (!Number.isFinite(durationMs) || durationMs < 0) {
    return t("restoreWorkspace.activity.range.durationUnavailable")
  }

  const totalSeconds = Math.floor(durationMs / 1000)
  const hours = Math.floor(totalSeconds / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  const seconds = totalSeconds % 60

  if (hours > 0) return t("restoreWorkspace.activity.range.durationHours", { hours, minutes, seconds })
  if (minutes > 0) return t("restoreWorkspace.activity.range.durationMinutes", { minutes, seconds })
  return t("restoreWorkspace.activity.range.durationSeconds", { seconds })
}

function toTimestamp(value: string) {
  const timestamp = new Date(value).getTime()
  return Number.isNaN(timestamp) ? 0 : timestamp
}

async function copyText(value: string) {
  try {
    await navigator.clipboard.writeText(value)
  } catch {
    // Clipboard access is intentionally best-effort. The timeline remains inspectable.
  }
}

function downloadJsonFile(name: string, value: unknown) {
  const content = JSON.stringify(value, null, 2)
  const blob = new Blob([content], { type: "application/json;charset=utf-8" })
  const url = URL.createObjectURL(blob)
  const link = document.createElement("a")

  link.href = url
  link.download = name
  link.click()
  URL.revokeObjectURL(url)
}
