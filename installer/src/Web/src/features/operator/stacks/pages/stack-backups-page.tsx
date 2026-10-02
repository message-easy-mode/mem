import { useMemo } from "react"
import { Link, useParams } from "react-router-dom"
import {
  Archive,
  ArrowRight,
  CheckCircle2,
  Clock3,
  Database,
  ExternalLink,
  History,
  type LucideIcon,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type {
  BackupCatalogListItem,
  RestoreAttemptListItem,
} from "@/features/operator/backups/api"
import { useBackupCatalog } from "@/features/operator/backups/hooks/use-backup-catalog"
import { useRestoreAttempts } from "@/features/operator/backups/hooks/use-backups"
import {
  BackupCatalogIntegrityBadge,
  BackupCatalogOriginBadge,
  BackupCatalogPayloadBadge,
} from "@/features/operator/backups/pages/backups/components/backup-catalog-status-badge"
import {
  formatBytes,
  formatDate,
} from "@/features/operator/backups/shared/components/backup-formatting"

import { StackWorkspaceHeader } from "../components/stack-workspace-header"
import { StackWorkspaceNavigation } from "../components/stack-workspace-navigation"
import {
  useBackupRuntimeStack,
  useRuntimeStack,
} from "../hooks/use-runtime-stacks"
import { useRunDoctorInStackDiagnostics } from "../hooks/use-stack-diagnostics-navigation"

const RESTORE_SESSIONS_PAGE_SIZE = 10

/**
 * Stack-scoped recovery view. The Backup Catalog remains the only recovery
 * source of truth; this route filters catalog projections by source stack and
 * shows durable restore sessions targeting this stack. It does not introduce
 * a second backup store, retention policy, or delete workflow.
 */
export function StackBackupsPage() {
  const { slugOrId } = useParams()
  const { language, t } = useI18n()

  const stackQuery = useRuntimeStack(slugOrId)
  const catalogQuery = useBackupCatalog()
  const runDoctorInDiagnostics = useRunDoctorInStackDiagnostics(slugOrId)
  const backup = useBackupRuntimeStack(slugOrId ?? "")

  const stack = stackQuery.data
  const stackSlug = stack?.slug ?? slugOrId ?? ""
  const restoreAttemptsQuery = useRestoreAttempts({
    page: 1,
    pageSize: RESTORE_SESSIONS_PAGE_SIZE,
    targetStack: stackSlug || null,
    sortBy: "updated",
    sortDirection: "desc",
  })

  const recoverySources = useMemo(
    () =>
      (catalogQuery.data?.entries ?? [])
        .filter((entry) => entry.sourceStackSlug === stackSlug)
        .sort((left, right) => recoverySourceTime(right) - recoverySourceTime(left)),
    [catalogQuery.data?.entries, stackSlug],
  )

  const latestRecoverySource = recoverySources[0] ?? null
  const readyRecoverySourceCount = recoverySources.filter(
    (entry) => entry.payloadState === "available" && entry.integrityStatus === "valid",
  ).length

  function refreshWorkspace() {
    void stackQuery.refetch()
    void catalogQuery.refetch()
    void restoreAttemptsQuery.refetch()
  }

  function createBackup() {
    backup.mutate(undefined, {
      onSuccess: () => {
        void catalogQuery.refetch()
      },
    })
  }

  return (
    <div className="space-y-6">
      <StackWorkspaceHeader
        slugOrId={slugOrId}
        displayName={stack?.displayName?.trim() || stack?.slug || slugOrId || t("stacks.common.stack")}
        status={stack?.health ?? stack?.status}
        verificationFreshness={stack?.verificationFreshness}
        matrixPublicBaseUrl={stack?.matrix?.publicBaseUrl}
        elementPublicBaseUrl={stack?.element?.publicBaseUrl}
        onRefresh={refreshWorkspace}
        refreshing={
          stackQuery.isFetching || catalogQuery.isFetching || restoreAttemptsQuery.isFetching
        }
        onRunDoctor={runDoctorInDiagnostics}
        doctorPending={false}
        onCreateBackup={createBackup}
        backupPending={backup.isPending}
      />

      {slugOrId ? <StackWorkspaceNavigation slugOrId={slugOrId} activeArea="recovery" /> : null}

      {stackQuery.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.backups.loadErrorTitle")}</AlertTitle>
          <AlertDescription>{stackQuery.error.message}</AlertDescription>
        </Alert>
      ) : null}

      {catalogQuery.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.backups.recoverySourcesLoadErrorTitle")}</AlertTitle>
          <AlertDescription>{catalogQuery.error.message}</AlertDescription>
        </Alert>
      ) : null}

      {restoreAttemptsQuery.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.backups.restoreSessionsLoadErrorTitle")}</AlertTitle>
          <AlertDescription>{restoreAttemptsQuery.error.message}</AlertDescription>
        </Alert>
      ) : null}

      {backup.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.workspace.backupFailedTitle")}</AlertTitle>
          <AlertDescription>{backup.error.message}</AlertDescription>
        </Alert>
      ) : null}

      {backup.data ? (
        <Alert className="border-emerald-500/20 bg-emerald-500/10">
          <Archive className="h-4 w-4" />
          <AlertTitle>{t("stacks.backups.localBackupCapturedTitle")}</AlertTitle>
          <AlertDescription>
            {t("stacks.backups.localBackupCapturedDescription", {
              backupId: backup.data.backupId,
              date: formatDate(backup.data.createdAtUtc, language),
              size: formatBytes(backup.data.stats.totalBytes, language),
              files: t("stacks.backups.files", { count: backup.data.stats.totalFiles }),
            })}
          </AlertDescription>
        </Alert>
      ) : null}

      {!stack && stackQuery.isLoading ? (
        <Card>
          <CardContent className="py-6 text-sm text-muted-foreground">
            {t("stacks.backups.loading")}
          </CardContent>
        </Card>
      ) : stack ? (
        <>
          <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4" aria-label={t("stacks.backups.summaryAriaLabel")}>
            <BackupSummaryCard
              icon={Database}
              label={t("stacks.backups.recoverySources")}
              value={catalogQuery.isLoading ? t("stacks.common.loading") : recoverySources.length.toString()}
              detail={t("stacks.backups.recoverySourcesDetail")}
            />
            <BackupSummaryCard
              icon={CheckCircle2}
              label={t("stacks.backups.readyToRestore")}
              value={catalogQuery.isLoading ? t("stacks.common.loading") : readyRecoverySourceCount.toString()}
              detail={t("stacks.backups.readyToRestoreDetail")}
            />
            <BackupSummaryCard
              icon={Clock3}
              label={t("stacks.backups.latestRecoverySource")}
              value={
                latestRecoverySource
                  ? formatDate(recoverySourceDate(latestRecoverySource), language)
                  : t("stacks.common.noneYet")
              }
              detail={latestRecoverySource?.sourceBackupId ?? t("stacks.backups.noCataloguedCaptureYet")}
              compactValue
            />
            <BackupSummaryCard
              icon={Archive}
              label={t("stacks.backups.targetedRestoreSessions")}
              value={
                restoreAttemptsQuery.isLoading
                  ? t("stacks.common.loading")
                  : (restoreAttemptsQuery.data?.totalSessions ?? 0).toString()
              }
              detail={t("stacks.backups.targetedRestoreSessionsDetail")}
            />
          </section>

          <Card>
            <CardHeader>
              <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div>
                  <CardTitle className="flex items-center gap-2">
                    <Database className="h-5 w-5" />
                    {t("stacks.backups.recoverySources")}
                  </CardTitle>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {t("stacks.backups.recoverySourcesDescription", { stack: stack.slug })}
                  </p>
                </div>
                <Button variant="outline" size="sm" asChild>
                  <Link to="/backups">
                    {t("stacks.backups.openBackupCatalog")}
                    <ArrowRight className="ml-2 h-4 w-4" />
                  </Link>
                </Button>
              </div>
            </CardHeader>
            <CardContent>
              <RecoverySourcesList
                entries={recoverySources}
                isLoading={catalogQuery.isLoading}
                language={language}
                t={t}
              />
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div>
                  <CardTitle className="flex items-center gap-2">
                    <History className="h-5 w-5" />
                    {t("stacks.backups.restoreSessionsTitle")}
                  </CardTitle>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {t("stacks.backups.restoreSessionsDescription", { stack: stack.slug })}
                  </p>
                </div>
                <Button variant="outline" size="sm" asChild>
                  <Link to={`/restores?target=${encodeURIComponent(stack.slug)}`}>
                    {t("stacks.backups.openAllRestoreSessions")}
                    <ArrowRight className="ml-2 h-4 w-4" />
                  </Link>
                </Button>
              </div>
            </CardHeader>
            <CardContent>
              <TargetRestoreSessionsList
                sessions={restoreAttemptsQuery.data?.sessions ?? []}
                totalSessions={restoreAttemptsQuery.data?.totalSessions ?? 0}
                isLoading={restoreAttemptsQuery.isLoading}
                language={language}
                t={t}
              />
            </CardContent>
          </Card>
        </>
      ) : null}
    </div>
  )
}

function BackupSummaryCard({
  icon: Icon,
  label,
  value,
  detail,
  compactValue = false,
}: {
  icon: LucideIcon
  label: string
  value: string
  detail: string
  compactValue?: boolean
}) {
  return (
    <Card>
      <CardContent className="flex items-start gap-3 p-4">
        <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-emerald-500/10 text-emerald-300">
          <Icon className="size-5" aria-hidden="true" />
        </span>
        <div className="min-w-0">
          <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</div>
          <div className={compactValue ? "mt-1 truncate text-base font-semibold" : "mt-1 text-2xl font-semibold"}>
            {value}
          </div>
          <div className="mt-1 text-xs text-muted-foreground">{detail}</div>
        </div>
      </CardContent>
    </Card>
  )
}

function RecoverySourcesList({
  entries,
  isLoading,
  language,
  t,
}: {
  entries: BackupCatalogListItem[]
  isLoading: boolean
  language: ReturnType<typeof useI18n>["language"]
  t: ReturnType<typeof useI18n>["t"]
}) {
  if (isLoading) {
    return <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">{t("stacks.backups.loadingRecoverySources")}</div>
  }

  if (entries.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
        {t("stacks.backups.emptyRecoverySources")}
      </div>
    )
  }

  return (
    <div className="divide-y divide-border overflow-hidden rounded-xl border border-border bg-background/40">
      {entries.map((entry) => {
        const detailPath = `/backups/catalog/${encodeURIComponent(entry.catalogEntryId)}`
        const capturedAt = recoverySourceDate(entry)
        const primaryLabel = entry.sourceBackupId ?? entry.displayName

        return (
          <div key={entry.catalogEntryId} className="flex flex-col gap-4 p-4 lg:flex-row lg:items-start lg:justify-between">
            <div className="min-w-0 space-y-2">
              <Link
                to={detailPath}
                className="block break-words font-mono text-sm font-semibold text-emerald-400 transition-colors hover:text-emerald-300 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              >
                {primaryLabel}
              </Link>
              <div className="text-sm text-muted-foreground">{entry.displayName}</div>
              <div className="flex flex-wrap gap-2">
                <BackupCatalogOriginBadge originKind={entry.originKind} />
                <BackupCatalogIntegrityBadge integrityStatus={entry.integrityStatus} />
                <BackupCatalogPayloadBadge payloadState={entry.payloadState} />
              </div>
              <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
                <span>{t("stacks.backups.captured", { date: formatDate(capturedAt, language) })}</span>
                <span>{formatBytes(entry.payloadBytes, language)}</span>
                {entry.warningCount > 0 ? <span>{t("stacks.backups.integrityWarnings", { count: entry.warningCount })}</span> : null}
                {entry.advisoryCount ? <span>{t("stacks.backups.importAdvisories", { count: entry.advisoryCount })}</span> : null}
              </div>
            </div>
            <Button variant="outline" size="sm" asChild>
              <Link to={detailPath}>
                {t("stacks.backups.openCatalogEntry")}
                <ArrowRight className="ml-2 h-4 w-4" />
              </Link>
            </Button>
          </div>
        )
      })}
    </div>
  )
}

function TargetRestoreSessionsList({
  sessions,
  totalSessions,
  isLoading,
  language,
  t,
}: {
  sessions: RestoreAttemptListItem[]
  totalSessions: number
  isLoading: boolean
  language: ReturnType<typeof useI18n>["language"]
  t: ReturnType<typeof useI18n>["t"]
}) {
  if (isLoading) {
    return <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">{t("stacks.backups.loadingTargetedRestoreSessions")}</div>
  }

  if (sessions.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
        {t("stacks.backups.emptyTargetedRestoreSessions")}
      </div>
    )
  }

  return (
    <div className="space-y-3">
      {totalSessions > sessions.length ? (
        <div className="text-xs text-muted-foreground">
          {t("stacks.backups.targetedRestoreSessionsSummary", { shown: sessions.length, total: totalSessions })}
        </div>
      ) : null}
      <div className="divide-y divide-border overflow-hidden rounded-xl border border-border bg-background/40">
        {sessions.map((session) => (
          <RestoreSessionRow key={session.restoreSessionId} session={session} language={language} t={t} />
        ))}
      </div>
    </div>
  )
}

function RestoreSessionRow({
  session,
  language,
  t,
}: {
  session: RestoreAttemptListItem
  language: ReturnType<typeof useI18n>["language"]
  t: ReturnType<typeof useI18n>["t"]
}) {
  const catalogHref = session.catalogEntryId && !session.sourceDeleted
    ? `/backups/catalog/${encodeURIComponent(session.catalogEntryId)}`
    : null
  const workspaceHref = session.workspaceAvailable
    ? `/restores/${encodeURIComponent(session.restoreSessionId)}`
    : null
  const presentation = restoreStatusPresentation(session)
  const stageLabel = restoreStageLabel(session, t)

  return (
    <div className="flex flex-col gap-4 p-4 lg:flex-row lg:items-start lg:justify-between">
      <div className="min-w-0 space-y-2">
        <div className="flex flex-wrap items-center gap-2">
          <div className="font-mono text-sm font-semibold">{session.restoreSessionId}</div>
          <Badge variant={presentation.variant} className={presentation.className}>
            {restoreStatusLabel(session, t)}
          </Badge>
        </div>

        {catalogHref ? (
          <Link
            to={catalogHref}
            className="block truncate text-sm text-foreground transition-colors hover:text-emerald-300 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            title={session.sourceLabel}
          >
            {session.sourceLabel}
          </Link>
        ) : (
          <div className="text-sm text-muted-foreground">{t("stacks.backups.sourceCatalogUnavailable")}</div>
        )}

        <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
          <span>{stageLabel}</span>
          <span>{t("stacks.backups.started", { date: formatDate(session.startedAtUtc, language) })}</span>
          <span>{t("stacks.backups.updated", { date: formatDate(session.lastUpdatedAtUtc, language) })}</span>
          {session.warningCount > 0 ? <span>{t("stacks.backups.warnings", { count: session.warningCount })}</span> : null}
          {session.errorCount > 0 ? <span className="text-destructive">{t("stacks.backups.errors", { count: session.errorCount })}</span> : null}
        </div>

        {session.progressPercent !== null ? (
          <div className="max-w-sm space-y-1.5">
            <div className="flex justify-between gap-3 text-xs text-muted-foreground">
              <span className="truncate">{stageLabel}</span>
              <span>{session.progressPercent}%</span>
            </div>
            <div className="h-1.5 overflow-hidden rounded-full bg-muted" role="progressbar" aria-label={t("stacks.backups.restoreProgress", { sessionId: session.restoreSessionId })} aria-valuemin={0} aria-valuemax={100} aria-valuenow={session.progressPercent}>
              <div className={`h-full rounded-full ${presentation.progressClassName}`} style={{ width: `${session.progressPercent}%` }} />
            </div>
          </div>
        ) : null}
      </div>

      {workspaceHref ? (
        <Button variant="outline" size="sm" asChild>
          <Link to={workspaceHref}>
            {t("stacks.backups.openWorkspace")}
            <ExternalLink className="ml-2 h-4 w-4" />
          </Link>
        </Button>
      ) : (
        <div className="text-sm text-muted-foreground">{t("stacks.backups.workspaceUnavailable")}</div>
      )}
    </div>
  )
}

function recoverySourceDate(entry: BackupCatalogListItem) {
  return entry.capturedAtUtc ?? entry.importedAtUtc ?? entry.createdAtUtc
}

function recoverySourceTime(entry: BackupCatalogListItem) {
  return new Date(recoverySourceDate(entry)).getTime()
}


function restoreStageLabel(session: RestoreAttemptListItem, t: ReturnType<typeof useI18n>["t"]) {
  switch (session.currentStage.trim().toLowerCase()) {
    case "creating":
      return t("restoreSessions.stage.creating")
    case "preparing-source":
      return t("restoreSessions.stage.preparingSource")
    case "backup-ready":
      return t("restoreSessions.stage.backupReady")
    case "private-test":
      return t("restoreSessions.stage.privateTest")
    case "create-restored-chat-server":
    case "production-recreate":
      return t("restoreSessions.stage.createRestoredChatServer")
    case "public-verification":
      return t("restoreSessions.stage.publicVerification")
    case "needs-attention":
      return t("restoreSessions.stage.needsAttention")
    case "cancelled":
      return t("restoreSessions.stage.cancelled")
    default:
      return session.currentStageLabel
  }
}

function restoreStatusLabel(session: RestoreAttemptListItem, t: ReturnType<typeof useI18n>["t"]) {
  switch (session.status.trim().toLowerCase()) {
    case "creating":
      return t("stacks.backups.restoreStatus.creating")
    case "in-progress":
      return t("stacks.backups.restoreStatus.inProgress")
    case "ready":
      return t("stacks.backups.restoreStatus.ready")
    case "testing":
      return t("stacks.backups.restoreStatus.testing")
    case "planning":
      return t("stacks.backups.restoreStatus.planning")
    case "recreating":
      return t("stacks.backups.restoreStatus.recreating")
    case "verifying":
      return t("stacks.backups.restoreStatus.verifying")
    case "needs-attention":
      return t("stacks.backups.restoreStatus.needsAttention")
    case "completed":
      return t("stacks.backups.restoreStatus.completed")
    case "cancelled":
      return t("stacks.backups.restoreStatus.cancelled")
    case "abandoned":
    case "failed":
      return t("stacks.backups.restoreStatus.failed")
    case "superseded":
      return t("stacks.backups.restoreStatus.superseded")
    default:
      return session.statusLabel
  }
}

function restoreStatusPresentation(session: RestoreAttemptListItem) {
  const status = session.status.toLowerCase()

  if (status === "completed") {
    return {
      variant: "secondary" as const,
      className: "bg-emerald-500/10 text-emerald-400",
      progressClassName: "bg-emerald-400",
    }
  }

  if (status === "needs-attention" || status === "abandoned" || status === "failed" || session.errorCount > 0) {
    return {
      variant: "destructive" as const,
      className: undefined,
      progressClassName: "bg-destructive",
    }
  }

  if (status === "cancelled" || status === "superseded") {
    return {
      variant: "outline" as const,
      className: "border-amber-500/30 bg-amber-500/10 text-amber-400",
      progressClassName: "bg-amber-400",
    }
  }

  return {
    variant: "outline" as const,
    className: "border-sky-500/30 bg-sky-500/10 text-sky-300",
    progressClassName: "bg-sky-400",
  }
}
