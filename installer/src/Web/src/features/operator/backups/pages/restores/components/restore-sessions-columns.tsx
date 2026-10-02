/* eslint-disable react-refresh/only-export-components -- TanStack column definitions intentionally compose local cell renderers. */
import type { ColumnDef } from "@tanstack/react-table"
import { Link } from "react-router-dom"
import {
  Ban,
  CheckCircle2,
  Circle,
  CircleAlert,
  Clock,
  ExternalLink,
  XCircle,
} from "lucide-react"

import type { UiLanguage } from "@/app/i18n/messages"
import { useI18n } from "@/app/i18n/i18n-context"
import { DataTableColumnHeader } from "@/components/data-table/data-table-column-header"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import type { RestoreAttemptListItem } from "@/features/operator/backups/api/types/backups.types"
import { formatDate } from "@/features/operator/backups/shared/components/backup-formatting"
import { cn } from "@/lib/utils"

export const RESTORE_SESSION_ACTIONS_COLUMN_ID = "actions"

type Translator = ReturnType<typeof useI18n>["t"]

export function createRestoreSessionColumns(
  t: Translator,
  language: UiLanguage,
): ColumnDef<RestoreAttemptListItem>[] {
  return [
    {
      id: "state",
      enableSorting: false,
      header: () => <span className="sr-only">{t("restoreSessions.table.state")}</span>,
      cell: ({ row }) => <StateIcon session={row.original} t={t} />,
      meta: { label: t("restoreSessions.table.state") },
    },
    {
      id: "source",
      accessorFn: (session) => `${session.restoreSessionId} ${session.sourceLabel}`,
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title={t("restoreSessions.table.source")} />
      ),
      cell: ({ row }) => <SourceCell session={row.original} t={t} />,
      meta: { label: t("restoreSessions.table.source") },
    },
    {
      id: "target",
      accessorFn: (session) => session.targetStackSlug ?? "",
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title={t("restoreSessions.table.target")} />
      ),
      cell: ({ row }) => <TargetStackCell session={row.original} t={t} />,
      meta: { label: t("restoreSessions.table.target") },
    },
    {
      id: "started",
      accessorFn: (session) => session.startedAtUtc,
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title={t("restoreSessions.table.started")} />
      ),
      cell: ({ row }) => <DateCell value={row.original.startedAtUtc} language={language} />,
      meta: { label: t("restoreSessions.table.started") },
    },
    {
      id: "status",
      accessorFn: (session) => session.status,
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title={t("restoreSessions.table.status")} />
      ),
      cell: ({ row }) => <StatusCell session={row.original} t={t} />,
      meta: { label: t("restoreSessions.table.status") },
    },
    {
      id: "progress",
      enableSorting: false,
      header: () => <span>{t("restoreSessions.table.progress")}</span>,
      cell: ({ row }) => <ProgressCell session={row.original} t={t} />,
      meta: { label: t("restoreSessions.table.progress") },
    },
    {
      id: "updated",
      accessorFn: (session) => session.lastUpdatedAtUtc,
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title={t("restoreSessions.table.updated")} />
      ),
      cell: ({ row }) => <DateCell value={row.original.lastUpdatedAtUtc} language={language} />,
      meta: { label: t("restoreSessions.table.updated") },
    },
    {
      id: RESTORE_SESSION_ACTIONS_COLUMN_ID,
      enableSorting: false,
      enableHiding: false,
      header: () => <span>{t("restoreSessions.table.actions")}</span>,
      cell: ({ row }) => <SessionActions session={row.original} t={t} />,
    },
  ]
}

function StateIcon({
  session,
  t,
}: {
  session: RestoreAttemptListItem
  t: Translator
}) {
  const presentation = getStatusPresentation(session)
  const statusLabel = formatRestoreStatus(session.status, session.statusLabel, t)
  const Icon = presentation.Icon

  return (
    <span
      className={cn("flex size-8 items-center justify-center rounded-full", presentation.iconClassName)}
      role="img"
      aria-label={t("restoreSessions.table.stateAria", { status: statusLabel })}
      title={statusLabel}
    >
      <Icon className="size-4" aria-hidden="true" />
    </span>
  )
}

function SourceCell({
  session,
  t,
}: {
  session: RestoreAttemptListItem
  t: Translator
}) {
  const workspaceHref = getRestoreWorkspaceHref(session)
  const catalogHref = session.catalogEntryId
    ? `/backups/catalog/${encodeURIComponent(session.catalogEntryId)}`
    : null

  return (
    <div className="min-w-60 space-y-1">
      <Link
        to={workspaceHref}
        className="block font-mono text-sm font-semibold text-emerald-400 transition-colors hover:text-emerald-300 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
      >
        {session.restoreSessionId}
      </Link>

      {catalogHref ? (
        <Link
          to={catalogHref}
          className="block truncate text-sm text-foreground transition-colors hover:text-emerald-300 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
          title={session.sourceLabel}
        >
          {session.sourceLabel}
        </Link>
      ) : (
        <div className="text-sm text-muted-foreground">{t("restoreSessions.table.deletedBackup")}</div>
      )}

      <div className="flex flex-wrap gap-x-2 gap-y-1 text-xs text-muted-foreground">
        {session.sourceStackSlug ? <span>{session.sourceStackSlug}</span> : null}
        {session.sourceBackupId ? <span className="font-mono">{session.sourceBackupId}</span> : null}
        <span>{formatSourceKind(session.sourceKind, t)}</span>
      </div>
    </div>
  )
}

function TargetStackCell({
  session,
  t,
}: {
  session: RestoreAttemptListItem
  t: Translator
}) {
  const stackHref = session.targetStackSlug
    ? `/stacks/${encodeURIComponent(session.targetStackSlug)}`
    : null
  const stageLabel = formatRestoreStage(session.currentStage, session.currentStageLabel, t)

  return (
    <div className="min-w-36">
      {stackHref ? (
        <Link
          to={stackHref}
          className="text-sm transition-colors hover:text-emerald-300 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
        >
          {session.targetStackSlug}
        </Link>
      ) : (
        <span className="text-sm text-muted-foreground">{t("restoreSessions.table.notSelected")}</span>
      )}
      <div className="mt-1 text-xs text-muted-foreground">{stageLabel}</div>
    </div>
  )
}

function DateCell({
  value,
  language,
}: {
  value: string
  language: UiLanguage
}) {
  return (
    <span className="whitespace-nowrap text-sm text-muted-foreground">
      {formatDate(value, language)}
    </span>
  )
}

function StatusCell({
  session,
  t,
}: {
  session: RestoreAttemptListItem
  t: Translator
}) {
  const presentation = getStatusPresentation(session)
  const statusLabel = formatRestoreStatus(session.status, session.statusLabel, t)
  const nextActionLabel = formatRestoreNextAction(
    session.nextActionCode,
    session.nextActionTitle,
    t,
  )

  return (
    <div className="min-w-36 space-y-2">
      <Badge variant={presentation.badgeVariant} className={presentation.badgeClassName}>
        {statusLabel}
      </Badge>
      <div className="flex flex-wrap gap-1.5 text-xs text-muted-foreground">
        {session.warningCount > 0 ? (
          <span>{t("restoreSessions.table.warningCount", { count: session.warningCount })}</span>
        ) : null}
        {session.errorCount > 0 ? (
          <span className="text-destructive">{t("restoreSessions.table.errorCount", { count: session.errorCount })}</span>
        ) : null}
        {session.warningCount === 0 && session.errorCount === 0 ? (
          <span>{nextActionLabel}</span>
        ) : null}
      </div>
    </div>
  )
}

function ProgressCell({
  session,
  t,
}: {
  session: RestoreAttemptListItem
  t: Translator
}) {
  if (session.progressPercent === null) {
    return <span className="text-sm text-muted-foreground">—</span>
  }

  const presentation = getStatusPresentation(session)
  const stageLabel = formatRestoreStage(session.currentStage, session.currentStageLabel, t)

  return (
    <div className="min-w-32 space-y-1.5">
      <div className="flex justify-between gap-3 text-xs text-muted-foreground">
        <span className="truncate">{stageLabel}</span>
        <span>{session.progressPercent}%</span>
      </div>
      <div
        className="h-1.5 overflow-hidden rounded-full bg-muted"
        role="progressbar"
        aria-label={t("restoreSessions.table.progressAria", { sessionId: session.restoreSessionId })}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={session.progressPercent}
      >
        <div
          className={cn("h-full rounded-full transition-[width]", presentation.progressClassName)}
          style={{ width: `${session.progressPercent}%` }}
        />
      </div>
    </div>
  )
}

function SessionActions({
  session,
  t,
}: {
  session: RestoreAttemptListItem
  t: Translator
}) {
  const workspaceHref = getRestoreWorkspaceHref(session)

  return (
    <div className="flex justify-end whitespace-nowrap">
      <Button variant="outline" size="sm" asChild>
        <Link to={workspaceHref}>
          {t("restoreSessions.table.open")}
          <ExternalLink className="ml-2 h-4 w-4" />
        </Link>
      </Button>
    </div>
  )
}

function getRestoreWorkspaceHref(session: RestoreAttemptListItem) {
  return `/restores/${encodeURIComponent(session.restoreSessionId)}`
}

function formatSourceKind(sourceKind: string, t: Translator) {
  return sourceKind === "backup-catalog"
    ? t("restoreSessions.table.sourceKind.catalog")
    : sourceKind.replaceAll("-", " ")
}

function formatRestoreStatus(
  status: string,
  fallback: string,
  t: Translator,
) {
  switch (status.trim().toLowerCase()) {
    case "creating":
      return t("restoreSessions.rowStatus.creating")
    case "in-progress":
      return t("restoreSessions.status.inProgress")
    case "ready":
      return t("restoreSessions.rowStatus.ready")
    case "testing":
      return t("restoreSessions.rowStatus.testing")
    case "planning":
      return t("restoreSessions.rowStatus.planning")
    case "recreating":
      return t("restoreSessions.rowStatus.recreating")
    case "verifying":
      return t("restoreSessions.rowStatus.verifying")
    case "needs-attention":
      return t("restoreSessions.rowStatus.needsAttention")
    case "completed":
      return t("restoreSessions.rowStatus.completed")
    case "cancelled":
      return t("restoreSessions.rowStatus.cancelled")
    case "abandoned":
    case "failed":
      return t("restoreSessions.rowStatus.failed")
    case "superseded":
      return t("restoreSessions.rowStatus.superseded")
    default:
      return fallback
  }
}

function formatRestoreStage(
  stage: string,
  fallback: string,
  t: Translator,
) {
  switch (stage.trim().toLowerCase()) {
    case "creating":
      return t("restoreSessions.stage.creating")
    case "preparing-source":
      return t("restoreSessions.stage.preparingSource")
    case "backup-ready":
      return t("restoreSessions.stage.backupReady")
    case "private-test":
      return t("restoreSessions.stage.privateTest")
    case "create-restored-chat-server":
      return t("restoreSessions.stage.createRestoredChatServer")
    case "public-verification":
      return t("restoreSessions.stage.publicVerification")
    case "needs-attention":
      return t("restoreSessions.stage.needsAttention")
    case "cancelled":
      return t("restoreSessions.stage.cancelled")
    default:
      return fallback
  }
}

function formatRestoreNextAction(
  action: string,
  fallback: string,
  t: Translator,
) {
  switch (action.trim().toLowerCase()) {
    case "view-restore":
      return t("restoreSessions.table.open")
    case "review-issue":
      return t("restoreSessions.nextAction.reviewIssue")
    case "continue-verification":
      return t("restoreSessions.nextAction.continueVerification")
    case "view-progress":
      return t("restoreSessions.nextAction.viewProgress")
    case "continue-restore":
      return t("restoreSessions.nextAction.continueRestore")
    default:
      return fallback
  }
}

function getStatusPresentation(session: RestoreAttemptListItem) {
  const status = session.status.toLowerCase()

  if (status === "completed") {
    return {
      Icon: CheckCircle2,
      iconClassName: "bg-emerald-500/10 text-emerald-400",
      badgeVariant: "secondary" as const,
      badgeClassName: "bg-emerald-500/10 text-emerald-400",
      progressClassName: "bg-emerald-400",
    }
  }

  if (status === "needs-attention" || status === "abandoned" || session.errorCount > 0) {
    return {
      Icon: CircleAlert,
      iconClassName: "bg-destructive/10 text-destructive",
      badgeVariant: "destructive" as const,
      badgeClassName: undefined,
      progressClassName: "bg-destructive",
    }
  }

  if (status === "cancelled" || status === "superseded") {
    return {
      Icon: Ban,
      iconClassName: "bg-amber-500/10 text-amber-400",
      badgeVariant: "outline" as const,
      badgeClassName: "border-amber-500/30 bg-amber-500/10 text-amber-400",
      progressClassName: "bg-amber-400",
    }
  }

  if (status === "ready") {
    return {
      Icon: Circle,
      iconClassName: "bg-sky-500/10 text-sky-300",
      badgeVariant: "outline" as const,
      badgeClassName: "border-sky-500/30 bg-sky-500/10 text-sky-300",
      progressClassName: "bg-sky-400",
    }
  }

  if (status === "failed") {
    return {
      Icon: XCircle,
      iconClassName: "bg-destructive/10 text-destructive",
      badgeVariant: "destructive" as const,
      badgeClassName: undefined,
      progressClassName: "bg-destructive",
    }
  }

  return {
    Icon: Clock,
    iconClassName: "bg-sky-500/10 text-sky-300",
    badgeVariant: "default" as const,
    badgeClassName: "bg-sky-500/15 text-sky-300",
    progressClassName: "bg-sky-400",
  }
}
