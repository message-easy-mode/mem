import type {
  RuntimeStackFederationApplyRequest,
  RuntimeStackFederationApplyResult,
  RuntimeStackFederationPolicyRequest,
  RuntimeStackFederationReview,
  RuntimeStackFederationState,
} from "./federation.types"

const STACKS_BASE = "/internal/host-agent/runtime-stacks"

type FederationHostAgentProblem = Readonly<{
  error?: string
  status?: string
  code?: string
  detail?: string
}>

export class FederationHostAgentProblemError extends Error {
  readonly problem?: FederationHostAgentProblem

  constructor(message: string, problem?: FederationHostAgentProblem) {
    super(message)
    this.name = "FederationHostAgentProblemError"
    this.problem = problem
  }
}

export function getFederationProblemCode(value: unknown): string | null {
  return value instanceof FederationHostAgentProblemError
    ? value.problem?.code ?? value.problem?.error ?? value.problem?.status ?? null
    : null
}

export function getFederationProblemDetail(value: unknown): string | null {
  return value instanceof FederationHostAgentProblemError
    ? value.problem?.detail ?? null
    : null
}

export function isStepUpRequiredFederationProblem(value: unknown): boolean {
  return getFederationProblemCode(value) === "step_up_required"
}

export async function inspectRuntimeStackFederation(
  slugOrId: string,
): Promise<RuntimeStackFederationState> {
  return federationRequest<RuntimeStackFederationState>(
    "GET",
    federationPath(slugOrId),
  )
}

export async function reviewRuntimeStackFederation(
  slugOrId: string,
  request: RuntimeStackFederationPolicyRequest,
): Promise<RuntimeStackFederationReview> {
  return federationRequest<RuntimeStackFederationReview>(
    "POST",
    `${federationPath(slugOrId)}/review`,
    request,
  )
}

export async function applyRuntimeStackFederation(
  slugOrId: string,
  request: RuntimeStackFederationApplyRequest,
): Promise<RuntimeStackFederationApplyResult> {
  const path = `${federationPath(slugOrId)}/apply`
  const response = await fetch(path, {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  })

  const contentType = response.headers.get("content-type") ?? ""
  const text = await response.text().catch(() => "")
  const parsed = parseJsonObject(text)

  // Candidate rejection and truthful failed-operation outcomes are returned
  // with non-2xx HTTP statuses. They are still application results rather than
  // transport errors and must remain visible to the operator.
  if (parsed && isFederationApplyResult(parsed)) {
    return parsed
  }

  if (!response.ok) {
    throw new FederationHostAgentProblemError(
      `POST ${path} failed with status ${response.status}${text ? `: ${text}` : ""}`,
      parseProblemObject(parsed),
    )
  }

  if (!contentType.includes("application/json")) {
    throw new Error(
      `POST ${path} returned non-JSON content (${contentType}). Response started with: ${text.slice(0, 160)}`,
    )
  }

  throw new Error(`POST ${path} returned an invalid federation apply result.`)
}

async function federationRequest<T>(
  method: "GET" | "POST",
  path: string,
  body?: unknown,
): Promise<T> {
  const response = await fetch(path, {
    method,
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  const contentType = response.headers.get("content-type") ?? ""
  const text = await response.text().catch(() => "")
  const parsed = parseJsonObject(text)

  if (!response.ok) {
    throw new FederationHostAgentProblemError(
      `${method} ${path} failed with status ${response.status}${text ? `: ${text}` : ""}`,
      parseProblemObject(parsed),
    )
  }

  if (!contentType.includes("application/json")) {
    throw new Error(
      `${method} ${path} returned non-JSON content (${contentType}). Response started with: ${text.slice(0, 160)}`,
    )
  }

  if (!parsed) {
    throw new Error(`${method} ${path} returned an invalid JSON object.`)
  }

  return parsed as T
}

function federationPath(slugOrId: string) {
  return `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/federation`
}

function parseJsonObject(text: string): Record<string, unknown> | undefined {
  if (!text) return undefined

  try {
    const parsed: unknown = JSON.parse(text)
    return typeof parsed === "object" && parsed !== null && !Array.isArray(parsed)
      ? parsed as Record<string, unknown>
      : undefined
  } catch {
    return undefined
  }
}

function parseProblemObject(
  record: Record<string, unknown> | undefined,
): FederationHostAgentProblem | undefined {
  if (!record) return undefined

  const problem: FederationHostAgentProblem = {
    error: typeof record.error === "string" ? record.error : undefined,
    status: typeof record.status === "string" ? record.status : undefined,
    code: typeof record.code === "string" ? record.code : undefined,
    detail: typeof record.detail === "string" ? record.detail : undefined,
  }

  return problem.error || problem.status || problem.code || problem.detail
    ? problem
    : undefined
}

function isFederationApplyResult(
  record: Record<string, unknown>,
): record is RuntimeStackFederationApplyResult {
  return typeof record.source === "string" &&
    typeof record.status === "string" &&
    typeof record.operationId === "string" &&
    typeof record.previousMode === "string" &&
    typeof record.requestedMode === "string" &&
    typeof record.observedMode === "string" &&
    typeof record.rollbackAttempted === "boolean" &&
    Array.isArray(record.checks) &&
    typeof record.detail === "string"
}
