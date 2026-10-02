export type AccessSession = {
  schemaVersion: number
  authenticated: boolean
  csrfToken: string | null
  expiresAtUtc: string | null
}

export type HostStatus = {
  schemaVersion: number
  status: string
  listener: string
  loopbackOnly: boolean
  remoteAccessAcknowledged: boolean
  sessionIdleMinutes: number
  sessionAbsoluteMinutes: number
  sourceOperationsAvailable: boolean
}

export type PreflightCheck = {
  code: string
  status: string
  summary: string
  detail: string | null
}

export type SourcePreflight = {
  schemaVersion: number
  status: string
  canRunAssessment: boolean
  checks: PreflightCheck[]
}

export type SourceFinding = {
  code: string
  severity: string
  message: string
  remediation: string | null
}

export type SourceStack = {
  sourceStackId: string
  slug: string
  name: string
  matrixServerName: string | null
  matrixPublicHost: string | null
  elementPublicHost: string | null
  homeserverConfigurationReady: boolean
  matrixDatabaseReady: boolean
  signingKeyReady: boolean
  mediaStoreReady: boolean
  mediaBytes: number
  blockers: number
  warnings: number
  sourceFilesReady: boolean
}

export type SourceAssessment = {
  schemaVersion: number
  assessmentId: string
  completedAtUtc: string
  classification: string
  recommendation: string
  canProceedToCapture: boolean
  sourceFingerprint: string
  host: {
    operatingSystem: string
    architecture: string
    isLinux: boolean
  }
  runtime: {
    dockerAvailable: boolean
    dockerVersion: string | null
    systemConfigReachable: boolean
    productName: string | null
    productVersion: string | null
    databaseProbeAttempted: boolean
  }
  counts: {
    dockerContainers: number
    databaseCandidates: number
    exactSupportedDatabases: number
    stackFileSets: number
    stacks: number
    blockers: number
    warnings: number
  }
  stacks: SourceStack[]
  findings: SourceFinding[]
}

export type SourceAssessmentEnvelope = {
  schemaVersion: number
  available: boolean
  running: boolean
  selectedSourceStackId: string | null
  assessment: SourceAssessment | null
}


type SessionExpiredListener = () => void
const sessionExpiredListeners = new Set<SessionExpiredListener>()

export function subscribeToSessionExpiry(listener: SessionExpiredListener): () => void {
  sessionExpiredListeners.add(listener)
  return () => sessionExpiredListeners.delete(listener)
}

function notifySessionExpired() {
  for (const listener of sessionExpiredListeners) {
    listener()
  }
}

type ProblemDetails = {
  title?: string
  detail?: string
}

async function parseResponse<T>(response: Response): Promise<T> {
  if (response.ok) {
    return (await response.json()) as T
  }

  let problem: ProblemDetails | undefined
  try {
    problem = (await response.json()) as ProblemDetails
  } catch {
    // Preserve a safe generic failure when the response is not JSON.
  }

  throw new Error(problem?.detail ?? problem?.title ?? `Request failed with status ${response.status}.`)
}

async function parseProtectedResponse<T>(response: Response): Promise<T> {
  if (response.status === 401) {
    notifySessionExpired()
  }

  return parseResponse<T>(response)
}

export async function readSession(): Promise<AccessSession> {
  return parseResponse<AccessSession>(await fetch('/api/access/session', {
    credentials: 'same-origin',
    headers: { Accept: 'application/json' },
  }))
}

export async function login(accessCode: string): Promise<AccessSession> {
  return parseResponse<AccessSession>(await fetch('/api/access/login', {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ accessCode }),
  }))
}

export async function logout(csrfToken: string): Promise<void> {
  const response = await fetch('/api/access/logout', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'X-MEM-CSRF': csrfToken },
  })

  if (response.status === 401) {
    notifySessionExpired()
    return
  }

  if (!response.ok) {
    throw new Error('The Source Assistant could not end the current session.')
  }
}

export async function readHostStatus(): Promise<HostStatus> {
  return parseProtectedResponse<HostStatus>(await fetch('/api/host/status', {
    credentials: 'same-origin',
    headers: { Accept: 'application/json' },
  }))
}

export async function readPreflight(): Promise<SourcePreflight> {
  return parseProtectedResponse<SourcePreflight>(await fetch('/api/preflight', {
    credentials: 'same-origin',
    headers: { Accept: 'application/json' },
  }))
}

export async function readLatestAssessment(): Promise<SourceAssessmentEnvelope> {
  return parseProtectedResponse<SourceAssessmentEnvelope>(await fetch('/api/assessments/latest', {
    credentials: 'same-origin',
    headers: { Accept: 'application/json' },
  }))
}

export async function runAssessment(csrfToken: string): Promise<SourceAssessmentEnvelope> {
  return parseProtectedResponse<SourceAssessmentEnvelope>(await fetch('/api/assessments', {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-MEM-CSRF': csrfToken,
    },
  }))
}

export async function selectSourceStack(
  sourceStackId: string,
  csrfToken: string,
): Promise<SourceAssessmentEnvelope> {
  return parseProtectedResponse<SourceAssessmentEnvelope>(await fetch(
    `/api/source-stacks/${encodeURIComponent(sourceStackId)}/select`,
    {
      method: 'POST',
      credentials: 'same-origin',
      headers: {
        Accept: 'application/json',
        'X-MEM-CSRF': csrfToken,
      },
    },
  ))
}

export type SecureIntakeRequestInput = {
  schema: string
  schemaVersion: number
  intakeId: string
  packageRevisionId: string | null
  requestKind: string
  ageRecipient: string
  recipientFingerprint: string
  expiresAtUtc: string
  targetControlPlaneVersion: string
  sourceStackId?: string | null
}

export type SourceWorkflowRequest = {
  intakeId: string
  packageRevisionId: string | null
  requestKind: string
  recipientFingerprint: string
  expiresAtUtc: string
  targetControlPlaneVersion: string
}

export type SourcePackage = {
  fileName: string
  sizeBytes: number
  sha256: string
  completedAtUtc: string
  captureKind: string
  sourceFrozen: boolean
  rehearsalOnly: boolean
  stackCount: number
  localState: string
  packageFileAvailable: boolean
  reportAvailable: boolean
}

export type SourceWorkflow = {
  schemaVersion: number
  workflowId: string
  status: string
  stage: string
  assessmentId: string
  selectedSourceStackId: string
  request: SourceWorkflowRequest
  encryptionReadinessAcknowledgedAtUtc: string
  captureId: string | null
  package: SourcePackage | null
  createdAtUtc: string
  updatedAtUtc: string
  completedAtUtc: string | null
  failureCode: string | null
  failureSummary: string | null
  actions: {
    canCreatePreviewCapture: boolean
    canSelectCapture: boolean
    canCreatePackage: boolean
    canCancel: boolean
    canDownloadPackage: boolean
    canDownloadReport: boolean
    canDeletePackage: boolean
    blockedReason: string | null
  }
}


export type SourceWorkflowOverview = {
  schemaVersion: number
  sourceStackId: string
  operationRunning: boolean
  canStartNewWorkflow: boolean
  startNewBlockedReason: string | null
  currentWorkflow: SourceWorkflow | null
  previousWorkflows: SourceWorkflow[]
}

export type SourceWorkflowEnvelope = {
  schemaVersion: number
  available: boolean
  running: boolean
  workflow: SourceWorkflow | null
}

export type SourceCaptureOption = {
  captureId: string
  sourceStackSlug: string
  matrixServerName: string
  completedAtUtc: string
  archiveBytes: number
  archiveSha256: string
  sourceFingerprint: string
  captureKind: string
  eligibleForRequest: boolean
  selected: boolean
}

export type SourceWorkflowImportResult = {
  workflow: SourceWorkflow
  captures: SourceCaptureOption[]
}


export async function readWorkflowOverview(
  sourceStackId: string,
  limit = 10,
): Promise<SourceWorkflowOverview> {
  const query = new URLSearchParams({
    sourceStackId,
    limit: String(limit),
  })
  return parseProtectedResponse<SourceWorkflowOverview>(await fetch(`/api/workflows/overview?${query.toString()}`, {
    credentials: 'same-origin',
    headers: { Accept: 'application/json' },
  }))
}

export async function readWorkflow(workflowId: string): Promise<SourceWorkflow> {
  return parseProtectedResponse<SourceWorkflow>(await fetch(
    `/api/workflows/${encodeURIComponent(workflowId)}`,
    {
      credentials: 'same-origin',
      headers: { Accept: 'application/json' },
    },
  ))
}

export async function readLatestWorkflow(): Promise<SourceWorkflowEnvelope> {
  return parseProtectedResponse<SourceWorkflowEnvelope>(await fetch('/api/workflows/latest', {
    credentials: 'same-origin',
    headers: { Accept: 'application/json' },
  }))
}

export async function importMigrationRequest(
  request: SecureIntakeRequestInput,
  encryptionReadinessAcknowledged: boolean,
  csrfToken: string,
): Promise<SourceWorkflowImportResult> {
  return parseProtectedResponse<SourceWorkflowImportResult>(await fetch('/api/intake-requests/validate', {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
      'X-MEM-CSRF': csrfToken,
    },
    body: JSON.stringify({ request, encryptionReadinessAcknowledged }),
  }))
}

export async function readWorkflowCaptures(workflowId: string): Promise<SourceCaptureOption[]> {
  return parseProtectedResponse<SourceCaptureOption[]>(await fetch(
    `/api/workflows/${encodeURIComponent(workflowId)}/captures`,
    {
      credentials: 'same-origin',
      headers: { Accept: 'application/json' },
    },
  ))
}

export async function selectWorkflowCapture(
  workflowId: string,
  captureId: string,
  csrfToken: string,
): Promise<SourceWorkflow> {
  return parseProtectedResponse<SourceWorkflow>(await fetch(
    `/api/workflows/${encodeURIComponent(workflowId)}/captures/${encodeURIComponent(captureId)}/select`,
    {
      method: 'POST',
      credentials: 'same-origin',
      headers: {
        Accept: 'application/json',
        'X-MEM-CSRF': csrfToken,
      },
    },
  ))
}

export async function startWorkflowCapture(
  workflowId: string,
  csrfToken: string,
): Promise<SourceWorkflow> {
  return parseProtectedResponse<SourceWorkflow>(await fetch(
    `/api/workflows/${encodeURIComponent(workflowId)}/capture`,
    {
      method: 'POST',
      credentials: 'same-origin',
      headers: {
        Accept: 'application/json',
        'X-MEM-CSRF': csrfToken,
      },
    },
  ))
}

export async function startWorkflowPackage(
  workflowId: string,
  csrfToken: string,
): Promise<SourceWorkflow> {
  return parseProtectedResponse<SourceWorkflow>(await fetch(
    `/api/workflows/${encodeURIComponent(workflowId)}/package`,
    {
      method: 'POST',
      credentials: 'same-origin',
      headers: {
        Accept: 'application/json',
        'X-MEM-CSRF': csrfToken,
      },
    },
  ))
}

export async function cancelWorkflowOperation(
  workflowId: string,
  csrfToken: string,
): Promise<SourceWorkflow> {
  return parseProtectedResponse<SourceWorkflow>(await fetch(
    `/api/workflows/${encodeURIComponent(workflowId)}/cancel`,
    {
      method: 'POST',
      credentials: 'same-origin',
      headers: {
        Accept: 'application/json',
        'X-MEM-CSRF': csrfToken,
      },
    },
  ))
}


export function workflowPackageDownloadUrl(workflowId: string): string {
  return `/api/workflows/${encodeURIComponent(workflowId)}/package/download`
}

export function workflowPackageReportUrl(workflowId: string): string {
  return `/api/workflows/${encodeURIComponent(workflowId)}/package/report`
}

export async function deleteWorkflowPackage(
  workflowId: string,
  csrfToken: string,
): Promise<SourceWorkflow> {
  return parseProtectedResponse<SourceWorkflow>(await fetch(
    `/api/workflows/${encodeURIComponent(workflowId)}/package`,
    {
      method: 'DELETE',
      credentials: 'same-origin',
      headers: {
        Accept: 'application/json',
        'X-MEM-CSRF': csrfToken,
      },
    },
  ))
}
