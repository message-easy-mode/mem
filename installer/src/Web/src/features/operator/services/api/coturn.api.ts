
export type CoturnInstallRequest = {
  externalIp?: string | null
}

export type CoturnInstallAcceptedResponse = {
  operationId: string
  status: string
  pollUrl: string
  reusedExistingOperation: boolean
}

export type CoturnInstallOperationResponse = {
  operationId: string
  runtimeStackId: string | null
  status: string
  currentStep: string | null
  requestedAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  lastError: string | null
  terminal: boolean
  succeeded: boolean
}

export type CoturnMaintenanceAction = "restart-verify" | "repair"

export type CoturnMaintenanceRequest = {
  action: CoturnMaintenanceAction
  externalIp?: string | null
  idempotencyKey?: string | null
}

export type CoturnMaintenanceAcceptedResponse = {
  operationId: string
  action: CoturnMaintenanceAction
  status: string
  pollUrl: string
  reusedExistingOperation: boolean
}

export type CoturnMaintenanceOperationResponse = CoturnInstallOperationResponse

export type CoturnMaintenanceActiveOperation = CoturnMaintenanceOperationResponse & {
  action: CoturnMaintenanceAction
}

export type CoturnMaintenanceActiveResponse = {
  source: string
  active: boolean
  operation: CoturnMaintenanceActiveOperation | null
}

export type CoturnStartupSupervisionStatus =
  | "disabled"
  | "waiting"
  | "deferred"
  | "not-installed"
  | "verified"
  | "recovering"
  | "recovered"
  | "repair-required"
  | "conflict"
  | "cooldown"
  | "failed"

export type CoturnStartupSupervisionResponse = {
  source: string
  status: CoturnStartupSupervisionStatus
  runtimeMode: string
  enabled: boolean
  decision: string
  mutationPerformed: boolean
  automaticRestartAttempted: boolean
  cooldownActive: boolean
  cooldownUntilUtc: string | null
  operationId: string | null
  observedAtUtc: string
  detail: string | null
}

export type CoturnEnsureRequest = {
  externalIp?: string | null
  forceRecreate?: boolean
  publishRelayPorts?: boolean
}

export type CoturnDockerRuntimeEvidence = {
  restartPolicy: string
  expectedRestartPolicy: string
  restartPolicyMatches: boolean
  restartCount: number
  restarting: boolean
  paused: boolean
  exitCode: number
  oomKilled: boolean
  dead: boolean
  stateErrorPresent: boolean
  healthStatus: string | null
  startedAtUtc: string | null
  finishedAtUtc: string | null
  networkMode: string | null
  expectedNetwork: string
  networkModeMatches: boolean
  attachedNetworks: string[]
  expectedNetworkAliases: string[]
  observedExpectedNetworkAliases: string[]
  expectedNetworkAttached: boolean
  networkAliasesMatch: boolean
  configMountPresent: boolean
  configMountReadOnly: boolean
  configMountSourceMatches: boolean
  configMountDestinationMatches: boolean
  commandMatches: boolean
  startupUserMatches: boolean
}

export type CoturnRuntimeResponse = {
  source: string
  status: string
  containerState: string
  readiness: string
  serviceKey: string
  containerName: string
  image: string
  approvedImageReference: string
  resolvedImageId: string | null
  imageApproved: boolean
  containerExists: boolean
  running: boolean
  ownershipVerified: boolean
  containerId: string | null
  dockerState: string | null
  operatorStatus: string
  runtimeExact: boolean
  configurationPresent: boolean
  configurationExact: boolean
  dockerRuntime: CoturnDockerRuntimeEvidence | null
  runtimeDrift: string[]
  protectedEvidenceAccess: "available" | "restricted" | "unavailable"
  realm: string
  publicHost: string
  turnPort: number
  relayMinPort: number
  relayMaxPort: number
  turnUris: string[]
  secretPresent: boolean
  secretSource: string
  secretStorage: string
  secretFilePermissionsApplied: boolean
  expectedBaseDomain: string
  configuredBaseDomain: string | null
  domainDriftDetected: boolean
  recreated: boolean
  externalIp: string | null
  relayPortsPublished: boolean
  securityPolicyApplied: boolean
  securityPolicyVersion: string
  publishedPorts: string[]
  requiredProductionFirewallPorts: string[]
  warnings: string[]
  detail: string | null
}

export type CoturnCheckStatus = "passed" | "failed" | "warning" | "not-run"

export type CoturnCheckItem = {
  key: string
  status: CoturnCheckStatus
  summary: string
  detail: string | null
  code?: string | null
}

export type CoturnAllocationProbeResponse = {
  status: CoturnCheckStatus
  transport: string
  summary: string
  logTail: string | null
  relayAddressEvidence?: {
    complete: boolean
    relays: { address: string; port: number }[]
  } | null
}

export type CoturnCheckResponse = {
  source: string
  status: CoturnCheckStatus
  checkedAtUtc: string
  freshUntilUtc: string
  containerState: string
  readiness: string
  publicHost: string
  runtimeContainerId: string | null
  runtimeStartedAtUtc: string | null
  runtimeRestartCount: number | null
  checks: CoturnCheckItem[]
  allocation: CoturnAllocationProbeResponse
  warnings: string[]
  detail: string | null
  evidencePersisted: boolean
  incidentId: string | null
}

export type CoturnCheckFreshness =
  | "fresh"
  | "stale"
  | "runtime-changed"
  | "not-checked"
  | "unavailable"

export type CoturnLatestCheckResponse = {
  source: string
  freshness: CoturnCheckFreshness
  fresh: boolean
  freshForSeconds: number
  observedAtUtc: string
  checkedAtUtc: string | null
  freshUntilUtc: string | null
  incidentId: string | null
  result: CoturnCheckResponse | null
  warnings: string[]
  detail: string | null
}

export type CoturnLogsResponse = {
  source: string
  status: string
  retrievedAtUtc: string
  containerName: string
  requestedTail: number
  returnedLines: number
  truncated: boolean
  content: string
  warnings: string[]
}

const COTURN_BASE = "/internal/host-agent/platform/coturn"

type ErrorEnvelope = {
  error?: string
  detail?: string
}

export class CoturnApiError extends Error {
  readonly code: string

  constructor(code: string, message: string) {
    super(message)
    this.name = "CoturnApiError"
    this.code = code
  }
}

async function parseJsonResponse<T>(
  response: Response,
  method: string,
  path: string,
): Promise<T> {
  const contentType = response.headers.get("content-type") ?? ""

  if (!response.ok) {
    if (contentType.includes("application/json")) {
      const body = (await response.json().catch(() => null)) as ErrorEnvelope | null
      const detail = body?.detail?.trim()
      const error = body?.error?.trim()
      throw new CoturnApiError(
        error || "coturn_request_failed",
        detail || error || `${method} ${path} failed with status ${response.status}`,
      )
    }

    const text = await response.text().catch(() => "")
    throw new Error(
      `${method} ${path} failed with status ${response.status}${text ? `: ${text}` : ""}`,
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
): Promise<TResponse> {
  const response = await fetch(path, {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  return parseJsonResponse<TResponse>(response, "POST", path)
}

export function installCoturn(request: CoturnInstallRequest = {}) {
  return controlPlanePost<CoturnInstallRequest, CoturnInstallAcceptedResponse>(
    `${COTURN_BASE}/install`,
    request,
  )
}

export function getCoturnInstallOperation(operationId: string) {
  return controlPlaneGet<CoturnInstallOperationResponse>(
    `/internal/host-agent/operations/${encodeURIComponent(operationId)}`,
  )
}

export function maintainCoturn(request: CoturnMaintenanceRequest) {
  return controlPlanePost<CoturnMaintenanceRequest, CoturnMaintenanceAcceptedResponse>(
    `${COTURN_BASE}/maintenance`,
    request,
  )
}

export function getCoturnMaintenanceOperation(operationId: string) {
  return controlPlaneGet<CoturnMaintenanceOperationResponse>(
    `/internal/host-agent/operations/${encodeURIComponent(operationId)}`,
  )
}

export function getActiveCoturnMaintenance() {
  return controlPlaneGet<CoturnMaintenanceActiveResponse>(
    `${COTURN_BASE}/maintenance/active`,
  )
}

export function getCoturnStartupSupervision() {
  return controlPlaneGet<CoturnStartupSupervisionResponse>(
    `${COTURN_BASE}/startup-supervision`,
  )
}

export function isCoturnStepUpRequired(error: unknown) {
  return error instanceof CoturnApiError && error.code === "step_up_required"
}

export function inspectCoturn() {
  return controlPlaneGet<CoturnRuntimeResponse>(COTURN_BASE)
}

export function ensureCoturn(request: CoturnEnsureRequest = {}) {
  return controlPlanePost<CoturnEnsureRequest, CoturnRuntimeResponse>(
    `${COTURN_BASE}/ensure`,
    request,
  )
}

export function checkCoturn() {
  return controlPlanePost<undefined, CoturnCheckResponse>(
    `${COTURN_BASE}/check`,
  )
}

export function getLatestCoturnCheck() {
  return controlPlaneGet<CoturnLatestCheckResponse>(
    `${COTURN_BASE}/check/latest`,
  )
}

export function getCoturnLogs(tail = 100) {
  const safeTail = Math.max(20, Math.min(200, Math.trunc(tail)))
  return controlPlaneGet<CoturnLogsResponse>(
    `${COTURN_BASE}/logs?tail=${safeTail}`,
  )
}
