export type PrivateNetworkExceptionAction = "add" | "remove"

export type PrivateNetworkFederationStackState = {
  runtimeStackId: string
  slug: string
  matrixServerName: string
  federationMode: string
  federationConfigurationState: string
  configurationState: string
  currentExceptions: string[]
  matrixContainerRunning: boolean
  latestOperationId: string | null
  latestOperationStatus: string | null
  latestOperationStep: string | null
  problemCode: string | null
  detail: string | null
}

export type PrivateNetworkFederationInventory = {
  source: string
  status: string
  stacks: PrivateNetworkFederationStackState[]
}

export type PrivateNetworkFederationReviewRequest = {
  address: string
  action: PrivateNetworkExceptionAction
}

export type PrivateNetworkFederationReview = {
  source: string
  status: string
  runtimeStackId: string
  slug: string
  matrixServerName: string
  action: PrivateNetworkExceptionAction
  canonicalAddress: string
  canonicalCidr: string
  currentExceptions: string[]
  proposedExceptions: string[]
  restartRequired: boolean
  noChange: boolean
  reviewHash: string
  confirmationText: string
}

export type PrivateNetworkFederationApplyRequest = PrivateNetworkFederationReviewRequest & {
  reviewHash: string
  idempotencyKey: string
}

export type PrivateNetworkFederationApplyResult = {
  source: string
  status: "running" | "succeeded" | "candidate_rejected" | "rolled_back" | "failed"
  operationId: string
  runtimeStackId: string
  slug: string
  action: PrivateNetworkExceptionAction
  canonicalCidr: string
  observedExceptions: string[]
  rollbackAttempted: boolean
  rollbackSucceeded: boolean | null
  errorCode: string | null
  detail: string
}

type Problem = {
  error?: string
  status?: string
  code?: string
  detail?: string
}

export class PrivateNetworkFederationProblemError extends Error {
  readonly problem?: Problem

  constructor(message: string, problem?: Problem) {
    super(message)
    this.name = "PrivateNetworkFederationProblemError"
    this.problem = problem
  }
}

const BASE = "/internal/host-agent/security/private-network-federation"

export function privateNetworkProblemCode(value: unknown): string | null {
  return value instanceof PrivateNetworkFederationProblemError
    ? value.problem?.code ?? value.problem?.error ?? value.problem?.status ?? null
    : null
}

export function privateNetworkProblemDetail(value: unknown): string | null {
  return value instanceof PrivateNetworkFederationProblemError
    ? value.problem?.detail ?? null
    : null
}

export function isPrivateNetworkStepUpRequired(value: unknown): boolean {
  return privateNetworkProblemCode(value) === "step_up_required"
}

export function getPrivateNetworkFederationInventory(): Promise<PrivateNetworkFederationInventory> {
  return request<PrivateNetworkFederationInventory>("GET", BASE)
}

export function reviewPrivateNetworkFederationException(
  slugOrId: string,
  input: PrivateNetworkFederationReviewRequest,
): Promise<PrivateNetworkFederationReview> {
  return request<PrivateNetworkFederationReview>(
    "POST",
    `${BASE}/${encodeURIComponent(slugOrId)}/review`,
    input,
  )
}

export async function applyPrivateNetworkFederationException(
  slugOrId: string,
  input: PrivateNetworkFederationApplyRequest,
): Promise<PrivateNetworkFederationApplyResult> {
  const path = `${BASE}/${encodeURIComponent(slugOrId)}/apply`
  const response = await fetch(path, {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(input),
  })
  const text = await response.text().catch(() => "")
  const parsed = parseObject(text)

  if (parsed && isApplyResult(parsed)) {
    return parsed
  }

  if (!response.ok) {
    throw new PrivateNetworkFederationProblemError(
      `POST ${path} failed with status ${response.status}${text ? `: ${text}` : ""}`,
      parseProblem(parsed),
    )
  }

  throw new PrivateNetworkFederationProblemError(
    `POST ${path} returned an invalid apply result`,
    { code: "private_network_invalid_response" },
  )
}

async function request<T>(method: "GET" | "POST", path: string, body?: unknown): Promise<T> {
  const response = await fetch(path, {
    method,
    credentials: "include",
    cache: "no-store",
    headers: { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const text = await response.text().catch(() => "")
  const parsed = parseObject(text)
  if (!response.ok) {
    throw new PrivateNetworkFederationProblemError(
      `${method} ${path} failed with status ${response.status}${text ? `: ${text}` : ""}`,
      parseProblem(parsed),
    )
  }
  if (!parsed) {
    throw new PrivateNetworkFederationProblemError(
      `${method} ${path} returned invalid JSON`,
      { code: "private_network_invalid_response" },
    )
  }
  return parsed as T
}

function parseObject(text: string): Record<string, unknown> | undefined {
  if (!text) return undefined
  try {
    const value: unknown = JSON.parse(text)
    return typeof value === "object" && value !== null && !Array.isArray(value)
      ? value as Record<string, unknown>
      : undefined
  } catch {
    return undefined
  }
}

function parseProblem(record: Record<string, unknown> | undefined): Problem | undefined {
  if (!record) return undefined
  const problem: Problem = {
    error: typeof record.error === "string" ? record.error : undefined,
    status: typeof record.status === "string" ? record.status : undefined,
    code: typeof record.code === "string" ? record.code : undefined,
    detail: typeof record.detail === "string" ? record.detail : undefined,
  }
  return problem.error || problem.status || problem.code || problem.detail ? problem : undefined
}

function isApplyResult(record: Record<string, unknown>): record is PrivateNetworkFederationApplyResult {
  return typeof record.source === "string" &&
    typeof record.status === "string" &&
    typeof record.operationId === "string" &&
    typeof record.runtimeStackId === "string" &&
    typeof record.slug === "string" &&
    typeof record.action === "string" &&
    typeof record.canonicalCidr === "string" &&
    Array.isArray(record.observedExceptions) &&
    typeof record.rollbackAttempted === "boolean" &&
    typeof record.detail === "string"
}
