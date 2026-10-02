import { BACKUPS_BASE, controlPlaneGet, controlPlanePost } from "../transport/host-agent"
import type {
  RestoreLogPage,
  RestoreLogQuery,
  RestoreSupportReportGenerationResult,
} from "../types/restore-logs.types"

export const restoreLogsApiRoutes = {
  logs: (restoreSessionId: string, query: RestoreLogQuery) => {
    const params = new URLSearchParams({
      page: String(query.page),
      pageSize: String(query.pageSize),
    })

    if (query.severity?.trim()) params.set("severity", query.severity.trim())
    if (query.stage?.trim()) params.set("stage", query.stage.trim())
    if (query.search?.trim()) params.set("search", query.search.trim())

    return `${BACKUPS_BASE}/restores/${encodeURIComponent(restoreSessionId)}/logs?${params.toString()}`
  },
  supportReport: (restoreSessionId: string) =>
    `${BACKUPS_BASE}/restores/${encodeURIComponent(restoreSessionId)}/support-report`,
} as const

/** Safe, paged, restore-scoped structured event stream. */
export function getRestoreLogs(restoreSessionId: string, query: RestoreLogQuery) {
  return controlPlaneGet<RestoreLogPage>(restoreLogsApiRoutes.logs(restoreSessionId, query))
}

/** Generates the existing redacted support report. This is not a ZIP bundle. */
export function generateRestoreSupportReport(restoreSessionId: string) {
  return controlPlanePost<undefined, RestoreSupportReportGenerationResult>(
    restoreLogsApiRoutes.supportReport(restoreSessionId),
  )
}
