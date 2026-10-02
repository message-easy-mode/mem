export type RestoreLogEvent = {
  schemaVersion: number
  eventId: string | null
  timestampUtc: string
  restoreSessionId: string | null
  operationId: string | null
  stage: string | null
  severity: string | null
  eventCode: string | null
  message: string | null
  details: Record<string, string> | null
}

export type RestoreLogSummary = {
  totalEvents: number
  warningCount: number
  errorCount: number
  latestEvent: RestoreLogEvent | null
  latestWarningOrError: RestoreLogEvent | null
}

export type RestoreLogPage = {
  restoreSessionId: string | null
  page: number
  pageSize: number
  totalEvents: number
  totalPages: number
  summary: RestoreLogSummary
  events: RestoreLogEvent[]
  warnings: string[]
}

export type RestoreLogQuery = {
  page: number
  pageSize: number
  severity?: string | null
  stage?: string | null
  search?: string | null
}

export type RestoreSupportReportGenerationResult = {
  restoreSessionId: string | null
  reportPath: string | null
  report: unknown
}
