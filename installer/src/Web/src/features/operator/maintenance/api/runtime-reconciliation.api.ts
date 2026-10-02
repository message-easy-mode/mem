import type { RuntimeReconciliationNpmProxyHostCleanupRequest, RuntimeReconciliationNpmProxyHostCleanupResponse, RuntimeReconciliationReport } from "./runtime-reconciliation.types"

const RUNTIME_RECONCILIATION_PATH = "/internal/host-agent/admin/maintenance/runtime-reconciliation"

export async function getRuntimeReconciliationReport(): Promise<RuntimeReconciliationReport> {
  const response = await fetch(RUNTIME_RECONCILIATION_PATH, {
    method: "GET",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
  })

  const contentType = response.headers.get("content-type") ?? ""

  if (!response.ok) {
    const text = await response.text().catch(() => "")
    throw new Error(
      `GET ${RUNTIME_RECONCILIATION_PATH} failed with status ${response.status}${text ? `: ${text}` : ""}`,
    )
  }

  if (!contentType.includes("application/json")) {
    const text = await response.text().catch(() => "")
    throw new Error(
      `GET ${RUNTIME_RECONCILIATION_PATH} returned non-JSON content (${contentType}). Response started with: ${text.slice(0, 160)}`,
    )
  }

  return (await response.json()) as RuntimeReconciliationReport
}


const RUNTIME_RECONCILIATION_DELETE_NPM_HOSTS_PATH = `${RUNTIME_RECONCILIATION_PATH}/npm-proxy-hosts/delete`

export class RuntimeReconciliationProblemError extends Error {
  readonly status: number
  readonly code: string | null

  constructor(message: string, status: number, code: string | null = null) {
    super(message)
    this.name = "RuntimeReconciliationProblemError"
    this.status = status
    this.code = code
  }
}

export function isStepUpRequiredRuntimeReconciliationProblem(value: unknown): boolean {
  return value instanceof RuntimeReconciliationProblemError &&
    value.status === 403 &&
    value.code === "step_up_required"
}

async function readProblemCode(response: Response): Promise<string | null> {
  try {
    const body = (await response.json()) as { status?: unknown; error?: unknown }

    if (typeof body.status === "string") {
      return body.status
    }

    return typeof body.error === "string" ? body.error : null
  } catch {
    return null
  }
}

async function throwRuntimeReconciliationProblem(
  response: Response,
  operation: string,
): Promise<never> {
  const code = await readProblemCode(response)

  throw new RuntimeReconciliationProblemError(
    `${operation} failed with status ${response.status}${code ? `: ${code}` : ""}`,
    response.status,
    code,
  )
}

export async function deleteSelectedOrphanedNpmProxyHosts(
  input: RuntimeReconciliationNpmProxyHostCleanupRequest,
): Promise<RuntimeReconciliationNpmProxyHostCleanupResponse> {
  const response = await fetch(RUNTIME_RECONCILIATION_DELETE_NPM_HOSTS_PATH, {
    method: "POST",
    credentials: "include",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
    },
    body: JSON.stringify(input),
  })

  if (!response.ok) {
    return throwRuntimeReconciliationProblem(
      response,
      `POST ${RUNTIME_RECONCILIATION_DELETE_NPM_HOSTS_PATH}`,
    )
  }

  return (await response.json()) as RuntimeReconciliationNpmProxyHostCleanupResponse
}
