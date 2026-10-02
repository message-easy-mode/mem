export type MemValidationErrors = Readonly<Record<string, readonly string[]>>

export type MemApiProblem = Readonly<{
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  code?: string
  error?: string
  traceId?: string
  spanId?: string
  requestId?: string
  correlationId?: string
  incidentId?: string
  operationId?: string
  retryable?: boolean
  suggestedAction?: string
  validationErrors?: MemValidationErrors
  diagnosticCapture?: string
  diagnosticWarningCode?: string
}>

type MemApiProblemErrorOptions = {
  method: string
  path: string
  status: number
  problem?: MemApiProblem
}

export class MemApiProblemError extends Error {
  readonly method: string
  readonly path: string
  readonly status: number
  readonly problem?: MemApiProblem

  constructor({ method, path, status, problem }: MemApiProblemErrorOptions) {
    super(buildSafeErrorMessage(method, path, status, problem))
    this.name = "MemApiProblemError"
    this.method = method
    this.path = path
    this.status = status
    this.problem = problem
  }
}

export function isMemApiProblemError(value: unknown): value is MemApiProblemError {
  return value instanceof MemApiProblemError
}

export function parseMemApiProblemText(text: string): MemApiProblem | undefined {
  if (!text.trim()) {
    return undefined
  }

  try {
    return parseMemApiProblemValue(JSON.parse(text))
  } catch {
    return undefined
  }
}

export function parseMemApiProblemValue(value: unknown): MemApiProblem | undefined {
  if (!isRecord(value)) {
    return undefined
  }

  const type = getBoundedString(value.type)
  const title = getBoundedString(value.title)
  const status = getStatus(value.status)
  const detail = getBoundedString(value.detail)
  const instance = getBoundedString(value.instance)
  const code = getIdentifier(value.code)
  const error = getIdentifier(value.error)
  const traceId = getIdentifier(value.traceId)
  const spanId = getIdentifier(value.spanId)
  const requestId = getIdentifier(value.requestId)
  const correlationId = getIdentifier(value.correlationId)
  const incidentId = getIdentifier(value.incidentId)
  const operationId = getIdentifier(value.operationId)
  const retryable = typeof value.retryable === "boolean" ? value.retryable : undefined
  const suggestedAction = getBoundedString(value.suggestedAction)
  const validationErrors = parseValidationErrors(value.validationErrors)
  const diagnosticCapture = getIdentifier(value.diagnosticCapture)
  const diagnosticWarningCode = getIdentifier(value.diagnosticWarningCode)

  if (
    !type &&
    !title &&
    status === undefined &&
    !detail &&
    !code &&
    !error &&
    !traceId &&
    !incidentId
  ) {
    return undefined
  }

  return {
    ...(type ? { type } : {}),
    ...(title ? { title } : {}),
    ...(status !== undefined ? { status } : {}),
    ...(detail ? { detail } : {}),
    ...(instance ? { instance } : {}),
    ...(code ? { code } : {}),
    ...(error ? { error } : {}),
    ...(traceId ? { traceId } : {}),
    ...(spanId ? { spanId } : {}),
    ...(requestId ? { requestId } : {}),
    ...(correlationId ? { correlationId } : {}),
    ...(incidentId ? { incidentId } : {}),
    ...(operationId ? { operationId } : {}),
    ...(retryable !== undefined ? { retryable } : {}),
    ...(suggestedAction ? { suggestedAction } : {}),
    ...(validationErrors ? { validationErrors } : {}),
    ...(diagnosticCapture ? { diagnosticCapture } : {}),
    ...(diagnosticWarningCode ? { diagnosticWarningCode } : {}),
  }
}

export function getMemApiProblem(value: unknown): MemApiProblem | undefined {
  if (value instanceof MemApiProblemError) {
    return value.problem
  }

  if (!isRecord(value)) {
    return undefined
  }

  return parseMemApiProblemValue(value.problem)
}

export function getMemApiProblemCode(value: unknown): string | undefined {
  const problem = getMemApiProblem(value)
  return problem?.code ?? problem?.error
}

export function getMemApiIncidentId(value: unknown): string | undefined {
  return getMemApiProblem(value)?.incidentId
}

export function getMemApiProblemDetail(
  value: unknown,
  fallback: string,
): string {
  const problem = getMemApiProblem(value)

  if (problem?.detail) {
    return problem.detail
  }

  if (problem?.title) {
    return problem.title
  }

  if (value instanceof MemApiProblemError) {
    return fallback
  }

  if (value instanceof Error && value.message.trim()) {
    return value.message
  }

  return fallback
}

export function buildDiagnosticsIncidentHref(value: unknown): string | undefined {
  const incidentId = getMemApiIncidentId(value)

  if (!incidentId) {
    return undefined
  }

  const params = new URLSearchParams({ incident: incidentId })
  return `/diagnostics/logs?${params.toString()}`
}

function buildSafeErrorMessage(
  method: string,
  path: string,
  status: number,
  problem?: MemApiProblem,
): string {
  const summary = problem?.detail ?? problem?.title

  if (summary) {
    return summary
  }

  const code = problem?.code ?? problem?.error
  return `${method} ${path} failed with status ${status}${code ? ` (${code})` : ""}`
}

function parseValidationErrors(value: unknown): MemValidationErrors | undefined {
  if (!isRecord(value)) {
    return undefined
  }

  const result: Record<string, readonly string[]> = {}

  for (const [key, messages] of Object.entries(value)) {
    if (!Array.isArray(messages)) {
      continue
    }

    const safeMessages = messages
      .map((message) => getBoundedString(message))
      .filter((message): message is string => Boolean(message))
      .slice(0, 20)

    if (safeMessages.length > 0) {
      result[key.slice(0, 160)] = safeMessages
    }
  }

  return Object.keys(result).length > 0 ? result : undefined
}

function getStatus(value: unknown): number | undefined {
  return typeof value === "number" && Number.isInteger(value) && value >= 100 && value <= 599
    ? value
    : undefined
}

function getIdentifier(value: unknown): string | undefined {
  return getBoundedString(value, 256)
}

function getBoundedString(value: unknown, maximumLength = 4_096): string | undefined {
  if (typeof value !== "string") {
    return undefined
  }

  const trimmed = value.trim()
  return trimmed.length > 0 ? trimmed.slice(0, maximumLength) : undefined
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value)
}
