import type {
  CreateChatStackRuntimeAcceptedResponse,
  CreateChatStackRuntimeCommand,
  CreateChatStackRuntimeOperationResponse,
  CreateRuntimeStackRequest,
  CreateRuntimeStackUserRequest,
  ImportMatrixAdminAuthorityRequest,
  DeactivateRuntimeStackUserRequest,
  ReactivateRuntimeStackUserRequest,
  RuntimeStackBackupResponse,
  RuntimeStackDestroyAcceptedResponse,
  RuntimeStackDestroyRequest,
  RuntimeStackDoctorResponse,
  RuntimeStackDoctorHistoryResponse,
  RuntimeStackLatestDoctorResponse,
  RuntimeStackInspectResponse,
  RuntimeStackIdentityUpdateRequest,
  RuntimeStackListRequest,
  RuntimeStackListResponse,
  RuntimeStackImagePolicyResponse,
  RuntimeStackOperationsResponse,
  MatrixAdminAuthorityStatusResponse,
  ResetRuntimeStackUserPasswordRequest,
  RuntimeStackStorageResponse,
  RuntimeStackTurnConnectRequest,
  RuntimeStackTurnConnectResponse,
  RuntimeStackTurnConnectReviewResponse,
  RuntimeStackTurnDisconnectRequest,
  RuntimeStackTurnDisconnectResponse,
  RuntimeStackTurnDisconnectReviewResponse,
  RuntimeStackTurnInspectionResponse,
  RuntimeStackUserLifecycleResponse,
  RuntimeStackUserPasswordResetResponse,
  RuntimeStackUserResponse,
  RuntimeStackUsersResponse,
} from "./stacks.types"
const STACKS_BASE = "/internal/host-agent/runtime-stacks"
const LOCAL_BACKUPS_BASE = "/internal/host-agent/backups/artifacts/local-backups"
const CREATE_COMMAND_PATH = "/internal/host-agent/commands/create-chat-stack-runtime"
const RUNTIME_IMAGE_POLICY_PATH = "/internal/host-agent/runtime-images/policy"
const RUNTIME_OPERATION_PATH = "/internal/host-agent/operations"
const DOCTOR_BROWSER_REQUEST_TIMEOUT_MS = 130_000

type RuntimeStackHostAgentProblem = Readonly<{
  error?: string
  status?: string
  code?: string
  detail?: string
}>

class RuntimeStackHostAgentProblemError extends Error {
  readonly problem?: RuntimeStackHostAgentProblem

  constructor(message: string, problem?: RuntimeStackHostAgentProblem) {
    super(message)
    this.name = "RuntimeStackHostAgentProblemError"
    this.problem = problem
  }
}

/**
 * Runtime stack destruction is a server-side high-risk action. Keep the
 * machine-readable Host Agent error code available to the stack page so it
 * can open the existing password-plus-current-TOTP step-up flow only when the
 * server requires it.
 */
export function isStepUpRequiredRuntimeStackProblem(value: unknown): boolean {
  return value instanceof RuntimeStackHostAgentProblemError &&
    value.problem?.error === "step_up_required"
}


export function isRuntimeStackNotFoundProblem(value: unknown): boolean {
  return value instanceof RuntimeStackHostAgentProblemError &&
    value.problem?.status === "not_found"
}


export function getRuntimeStackProblemDetail(value: unknown): string | null {
  return value instanceof RuntimeStackHostAgentProblemError
    ? value.problem?.detail ?? null
    : null
}

export function getRuntimeStackProblemCode(value: unknown): string | null {
  return value instanceof RuntimeStackHostAgentProblemError
    ? value.problem?.code ?? value.problem?.error ?? null
    : null
}


async function parseJsonResponse<T>(
  response: Response,
  method: string,
  path: string,
): Promise<T> {
  const contentType = response.headers.get("content-type") ?? ""

  if (!response.ok) {
    const text = await response.text().catch(() => "")
    const problem = parseRuntimeStackHostAgentProblem(text)
    throw new RuntimeStackHostAgentProblemError(
      problem?.detail?.trim() || `MEM request failed with status ${response.status}.`,
      problem,
    )
  }

  if (!contentType.includes("application/json")) {
    const text = await response.text().catch(() => "")
    throw new Error(
      `${method} ${path} returned non-JSON content (${contentType}). Response started with: ${text.slice(0, 160)}`,
    )
  }

  return (await response.json()) as T
}

async function controlPlaneGet<T>(path: string): Promise<T> {
  const response = await fetch(path, {
    method: "GET",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
  })

  return parseJsonResponse<T>(response, "GET", path)
}

async function controlPlanePost<TRequest, TResponse>(
  path: string,
  body?: TRequest,
  signal?: AbortSignal,
): Promise<TResponse> {
  const response = await fetch(path, {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  })

  return parseJsonResponse<TResponse>(response, "POST", path)
}

async function controlPlanePut<TRequest, TResponse>(
  path: string,
  body: TRequest,
): Promise<TResponse> {
  const response = await fetch(path, {
    method: "PUT",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
  })

  return parseJsonResponse<TResponse>(response, "PUT", path)
}

function parseRuntimeStackHostAgentProblem(
  text: string,
): RuntimeStackHostAgentProblem | undefined {
  if (!text) {
    return undefined
  }

  try {
    const parsed: unknown = JSON.parse(text)

    if (
      typeof parsed !== "object" ||
      parsed === null ||
      Array.isArray(parsed)
    ) {
      return undefined
    }

    const record = parsed as Record<string, unknown>
    const problem: RuntimeStackHostAgentProblem = {
      error: typeof record.error === "string" ? record.error : undefined,
      status: typeof record.status === "string" ? record.status : undefined,
      code: typeof record.code === "string" ? record.code : undefined,
      detail: typeof record.detail === "string" ? record.detail : undefined,
    }

    return problem.error || problem.status || problem.code || problem.detail
      ? problem
      : undefined
  } catch {
    return undefined
  }
}

function newId() {
  return crypto.randomUUID()
}

function normalizeSlug(value: string) {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9-]+/g, "-")
    .replace(/-{2,}/g, "-")
    .replace(/^-+|-+$/g, "")
}

export function listRuntimeStacks(request?: RuntimeStackListRequest) {
  const query = new URLSearchParams()

  if (request?.page !== undefined) query.set("page", String(request.page))
  if (request?.pageSize !== undefined) query.set("pageSize", String(request.pageSize))
  if (request?.search) query.set("search", request.search)
  if (request?.status && request.status !== "all") query.set("status", request.status)
  if (request?.category) query.set("category", request.category)
  if (request?.sortBy) query.set("sortBy", request.sortBy)
  if (request?.sortDirection) query.set("sortDirection", request.sortDirection)

  const queryString = query.toString()
  const suffix = queryString ? `?${queryString}` : ""
  return controlPlaneGet<RuntimeStackListResponse>(`${STACKS_BASE}${suffix}`)
}

export function inspectRuntimeStack(slugOrId: string) {
  return controlPlaneGet<RuntimeStackInspectResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}`,
  )
}

export function updateRuntimeStackIdentity(
  slugOrId: string,
  request: RuntimeStackIdentityUpdateRequest,
) {
  return controlPlanePut<RuntimeStackIdentityUpdateRequest, RuntimeStackInspectResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/identity`,
    request,
  )
}

export async function uploadRuntimeStackLogo(slugOrId: string, file: File) {
  const path = `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/logo`
  const form = new FormData()
  form.append("file", file, file.name)

  const response = await fetch(path, {
    method: "PUT",
    credentials: "include",
    body: form,
  })

  return parseJsonResponse<RuntimeStackInspectResponse>(response, "PUT", path)
}

export async function removeRuntimeStackLogo(slugOrId: string) {
  const path = `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/logo`
  const response = await fetch(path, {
    method: "DELETE",
    credentials: "include",
  })

  return parseJsonResponse<RuntimeStackInspectResponse>(response, "DELETE", path)
}

export function inspectRuntimeStackTurn(slugOrId: string) {
  return controlPlaneGet<RuntimeStackTurnInspectionResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/turn`,
  )
}

export function reviewRuntimeStackTurnConnect(slugOrId: string) {
  return controlPlanePost<void, RuntimeStackTurnConnectReviewResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/turn/connect/review`,
  )
}

export function connectRuntimeStackTurn(
  slugOrId: string,
  request: Omit<RuntimeStackTurnConnectRequest, "idempotencyKey"> & {
    idempotencyKey?: string
  },
) {
  return controlPlanePost<RuntimeStackTurnConnectRequest, RuntimeStackTurnConnectResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/turn/connect`,
    {
      ...request,
      idempotencyKey:
        request.idempotencyKey ?? `turn-connect-${slugOrId}-${newId()}`,
    },
  )
}

export function reviewRuntimeStackTurnDisconnect(slugOrId: string) {
  return controlPlanePost<void, RuntimeStackTurnDisconnectReviewResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/turn/disconnect/review`,
  )
}

export function disconnectRuntimeStackTurn(
  slugOrId: string,
  request: Omit<RuntimeStackTurnDisconnectRequest, "idempotencyKey"> & {
    idempotencyKey?: string
  },
) {
  return controlPlanePost<RuntimeStackTurnDisconnectRequest, RuntimeStackTurnDisconnectResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/turn/disconnect`,
    {
      ...request,
      idempotencyKey:
        request.idempotencyKey ?? `turn-disconnect-${slugOrId}-${newId()}`,
    },
  )
}

export function getLatestRuntimeStackDoctor(slugOrId: string) {
  return controlPlaneGet<RuntimeStackLatestDoctorResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/doctor/latest`,
  )
}

export function listRuntimeStackDoctorHistory(
  slugOrId: string,
  page = 1,
  pageSize = 10,
) {
  const query = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  })

  return controlPlaneGet<RuntimeStackDoctorHistoryResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/doctor/history?${query.toString()}`,
  )
}

export function getRuntimeStackDoctorReport(slugOrId: string, reportId: string) {
  return controlPlaneGet<RuntimeStackDoctorResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/doctor/reports/${encodeURIComponent(reportId)}`,
  )
}

export class RuntimeStackDoctorTransportTimeoutError extends Error {
  constructor() {
    super("The Doctor request did not return after the server deadline.")
    this.name = "RuntimeStackDoctorTransportTimeoutError"
  }
}

export async function doctorRuntimeStack(
  slugOrId: string,
  browserTimeoutMs = DOCTOR_BROWSER_REQUEST_TIMEOUT_MS,
) {
  const controller = new AbortController()
  const timeoutHandle = globalThis.setTimeout(() => controller.abort(), browserTimeoutMs)

  try {
    return await controlPlanePost<void, RuntimeStackDoctorResponse>(
      `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/doctor`,
      undefined,
      controller.signal,
    )
  } catch (error) {
    if (controller.signal.aborted) {
      throw new RuntimeStackDoctorTransportTimeoutError()
    }

    throw error
  } finally {
    globalThis.clearTimeout(timeoutHandle)
  }
}

export function backupRuntimeStack(slugOrId: string) {
  return controlPlanePost<void, RuntimeStackBackupResponse>(
    `${LOCAL_BACKUPS_BASE}/stacks/${encodeURIComponent(slugOrId)}`,
  )
}

export function listRuntimeStackOperations(slugOrId: string) {
  return controlPlaneGet<RuntimeStackOperationsResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/operations`,
  )
}

export function listRuntimeStackUsers(slugOrId: string) {
  return controlPlaneGet<RuntimeStackUsersResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/users`,
  )
}

export function synchronizeRuntimeStackUsers(slugOrId: string) {
  return controlPlanePost<void, RuntimeStackUsersResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/users/synchronize`,
  )
}

export function createRuntimeStackFirstAdmin(
  slugOrId: string,
  request: CreateRuntimeStackUserRequest,
) {
  return controlPlanePost<CreateRuntimeStackUserRequest, RuntimeStackUserResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/users/first-admin`,
    request,
  )
}

export function createRuntimeStackUser(
  slugOrId: string,
  request: CreateRuntimeStackUserRequest,
) {
  return controlPlanePost<CreateRuntimeStackUserRequest, RuntimeStackUserResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/users`,
    request,
  )
}


export function setRuntimeStackMatrixAdminAuthority(
  slugOrId: string,
  request: ImportMatrixAdminAuthorityRequest,
) {
  return controlPlanePost<
    ImportMatrixAdminAuthorityRequest,
    MatrixAdminAuthorityStatusResponse
  >(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/users/admin-authority`,
    request,
  )
}

export function resetRuntimeStackUserPassword(
  slugOrId: string,
  userId: string,
  request: ResetRuntimeStackUserPasswordRequest,
) {
  return controlPlanePost<
    ResetRuntimeStackUserPasswordRequest,
    RuntimeStackUserPasswordResetResponse
  >(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/users/${encodeURIComponent(userId)}/password`,
    request,
  )
}

export function deactivateRuntimeStackUser(
  slugOrId: string,
  userId: string,
  request: DeactivateRuntimeStackUserRequest,
) {
  return controlPlanePost<DeactivateRuntimeStackUserRequest, RuntimeStackUserLifecycleResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/users/${encodeURIComponent(userId)}/deactivate`,
    request,
  )
}

export function reactivateRuntimeStackUser(
  slugOrId: string,
  userId: string,
  request: ReactivateRuntimeStackUserRequest,
) {
  return controlPlanePost<ReactivateRuntimeStackUserRequest, RuntimeStackUserLifecycleResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/users/${encodeURIComponent(userId)}/reactivate`,
    request,
  )
}

export function getRuntimeStackImagePolicy() {
  return controlPlaneGet<RuntimeStackImagePolicyResponse>(RUNTIME_IMAGE_POLICY_PATH)
}

export function createRuntimeStack(request: CreateRuntimeStackRequest) {
  const stackSlug = normalizeSlug(request.slug)

  const command: CreateChatStackRuntimeCommand = {
    stackId: newId(),
    matrixInstanceId: newId(),
    elementInstanceId: newId(),
    stackSlug,
    displayName: request.displayName?.trim() || stackSlug,
    category: request.category?.trim() || null,
    requestedDomainId: request.requestedDomainId ?? null,
    idempotencyKey: `create-stack-${stackSlug}-${Date.now()}`,
  }

  return controlPlanePost<
    CreateChatStackRuntimeCommand,
    CreateChatStackRuntimeAcceptedResponse
  >(CREATE_COMMAND_PATH, command)
}

export function getCreateRuntimeStackOperation(operationId: string) {
  return controlPlaneGet<CreateChatStackRuntimeOperationResponse>(
    `${RUNTIME_OPERATION_PATH}/${encodeURIComponent(operationId)}`,
  )
}

export function inspectRuntimeStackStorage(slugOrId: string) {
  return controlPlaneGet<RuntimeStackStorageResponse>(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/storage`,
  )
}

export function destroyRuntimeStack(
  slugOrId: string,
  request: RuntimeStackDestroyRequest,
) {
  return controlPlanePost<
    RuntimeStackDestroyRequest,
    RuntimeStackDestroyAcceptedResponse
  >(
    `${STACKS_BASE}/${encodeURIComponent(slugOrId)}/destroy`,
    request,
  )
}

export function getDestroyRuntimeStackOperation(operationId: string) {
  return controlPlaneGet<CreateChatStackRuntimeOperationResponse>(
    `${RUNTIME_OPERATION_PATH}/${encodeURIComponent(operationId)}`,
  )
}

export { normalizeSlug }
