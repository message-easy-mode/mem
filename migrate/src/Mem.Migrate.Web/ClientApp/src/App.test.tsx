import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

const authenticatedSession = {
  schemaVersion: 1,
  authenticated: true,
  csrfToken: 'csrf-test',
  expiresAtUtc: '2026-07-30T10:00:00Z',
}

const hostStatus = {
  schemaVersion: 1,
  status: 'source-assessment-ready',
  listener: 'http://127.0.0.1:7391',
  loopbackOnly: true,
  remoteAccessAcknowledged: false,
  sessionIdleMinutes: 240,
  sessionAbsoluteMinutes: 1440,
  sourceOperationsAvailable: true,
}

const preflight = {
  schemaVersion: 1,
  status: 'ready-with-warnings',
  canRunAssessment: true,
  checks: [
    { code: 'operating-system', status: 'ready', summary: 'A supported Linux source host was detected.', detail: null },
    { code: 'source-assessment', status: 'not-run', summary: 'No source assessment has been recorded yet.', detail: null },
  ],
}

const stackId = '11111111-1111-1111-1111-111111111111'
const secondStackId = '22222222-2222-2222-2222-222222222222'

const sourceAssessment = {
  schemaVersion: 1,
  assessmentId: 'assessment-01',
  completedAtUtc: '2026-07-30T09:00:00Z',
  classification: 'ConfirmedSupportedV010',
  recommendation: 'NewServerRecommended',
  canProceedToCapture: true,
  sourceFingerprint: 'sha256:source-fingerprint',
  host: { operatingSystem: 'Ubuntu 24.04', architecture: 'x64', isLinux: true },
  runtime: {
    dockerAvailable: true,
    dockerVersion: '28.0.0',
    systemConfigReachable: true,
    productName: 'MatrixEasyMode',
    productVersion: '0.1.0',
    databaseProbeAttempted: true,
  },
  counts: {
    dockerContainers: 5,
    databaseCandidates: 1,
    exactSupportedDatabases: 1,
    stackFileSets: 1,
    stacks: 1,
    blockers: 0,
    warnings: 0,
  },
  stacks: [
    {
      sourceStackId: stackId,
      slug: 'testing',
      name: 'Testing',
      matrixServerName: 'matrix.example.test',
      matrixPublicHost: 'matrix.example.test',
      elementPublicHost: 'chat.example.test',
      homeserverConfigurationReady: true,
      matrixDatabaseReady: true,
      signingKeyReady: true,
      mediaStoreReady: true,
      mediaBytes: 4096,
      blockers: 0,
      warnings: 0,
      sourceFilesReady: true,
    },
  ],
  findings: [],
}

const currentWorkflow = {
  schemaVersion: 1,
  workflowId: 'source-current',
  status: 'Pending',
  stage: 'RequestValidated',
  assessmentId: 'assessment-01',
  selectedSourceStackId: stackId,
  request: {
    intakeId: 'mig_current',
    packageRevisionId: null,
    requestKind: 'preview',
    recipientFingerprint: 'CURRENT-FINGERPRINT',
    expiresAtUtc: '2026-08-01T00:00:00Z',
    targetControlPlaneVersion: '0.2.0',
  },
  encryptionReadinessAcknowledgedAtUtc: '2026-07-30T00:00:00Z',
  captureId: null,
  package: null,
  createdAtUtc: '2026-07-30T00:00:00Z',
  updatedAtUtc: '2026-07-30T01:00:00Z',
  completedAtUtc: null,
  failureCode: null,
  failureSummary: null,
  actions: {
    canCreatePreviewCapture: true,
    canSelectCapture: true,
    canCreatePackage: false,
    canCancel: false,
    canDownloadPackage: false,
    canDownloadReport: false,
    canDeletePackage: false,
    blockedReason: null,
  },
}

const previousWorkflow = {
  ...currentWorkflow,
  workflowId: 'source-previous',
  status: 'Completed',
  stage: 'ReadyForDownload',
  request: { ...currentWorkflow.request, intakeId: 'mig_previous' },
  completedAtUtc: '2026-07-29T02:00:00Z',
  updatedAtUtc: '2026-07-29T02:00:00Z',
  package: {
    fileName: 'migration.zip.age',
    sizeBytes: 8192,
    sha256: 'abc123',
    completedAtUtc: '2026-07-29T02:00:00Z',
    captureKind: 'preview',
    sourceFrozen: false,
    rehearsalOnly: true,
    stackCount: 1,
    localState: 'Available',
    packageFileAvailable: true,
    reportAvailable: true,
  },
  actions: {
    ...currentWorkflow.actions,
    canCreatePreviewCapture: false,
    canSelectCapture: false,
    canDownloadPackage: true,
    canDownloadReport: true,
    canDeletePackage: true,
  },
}

function assessmentEnvelope(selectedSourceStackId: string | null = null, assessment = sourceAssessment) {
  return {
    schemaVersion: 1,
    available: true,
    running: false,
    selectedSourceStackId,
    assessment,
  }
}

function overview(overrides: Record<string, unknown> = {}) {
  return {
    schemaVersion: 1,
    sourceStackId: stackId,
    operationRunning: false,
    canStartNewWorkflow: true,
    startNewBlockedReason: null,
    currentWorkflow: null,
    previousWorkflows: [],
    ...overrides,
  }
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('Source Assistant guided workspace', () => {
  it('requires the startup access code and presents assessment as the first action', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ schemaVersion: 1, authenticated: false, csrfToken: null, expiresAtUtc: null }))
      .mockResolvedValueOnce(jsonResponse(authenticatedSession))
      .mockResolvedValueOnce(jsonResponse(hostStatus))
      .mockResolvedValueOnce(jsonResponse(preflight))
      .mockResolvedValueOnce(jsonResponse({ schemaVersion: 1, available: false, running: false, selectedSourceStackId: null, assessment: null }))
    vi.stubGlobal('fetch', fetchMock)

    const user = userEvent.setup()
    render(<App />)

    await user.type(await screen.findByLabelText('Access code'), 'ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2345')
    await user.click(screen.getByRole('button', { name: 'Open Source Assistant' }))

    expect(await screen.findByRole('heading', { name: 'Prepare the source migration package' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Run source assessment' })).toBeInTheDocument()
    expect(screen.getByText('Session: 4h idle · 24h maximum')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Start a new migration' })).not.toBeInTheDocument()
  })

  it('shows the effective server-owned session policy in the header', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(authenticatedSession))
      .mockResolvedValueOnce(jsonResponse({
        ...hostStatus,
        sessionIdleMinutes: 90,
        sessionAbsoluteMinutes: 360,
      }))
      .mockResolvedValueOnce(jsonResponse(preflight))
      .mockResolvedValueOnce(jsonResponse({ schemaVersion: 1, available: false, running: false, selectedSourceStackId: null, assessment: null }))
    vi.stubGlobal('fetch', fetchMock)

    render(<App />)

    expect(await screen.findByText('Session: 1h 30m idle · 6h maximum')).toBeInTheDocument()
  })

  it('automatically selects the only ready stack and exposes Start a new migration above diagnostics', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(authenticatedSession))
      .mockResolvedValueOnce(jsonResponse(hostStatus))
      .mockResolvedValueOnce(jsonResponse(preflight))
      .mockResolvedValueOnce(jsonResponse(assessmentEnvelope()))
      .mockResolvedValueOnce(jsonResponse(assessmentEnvelope(stackId)))
      .mockResolvedValueOnce(jsonResponse(overview()))
    vi.stubGlobal('fetch', fetchMock)

    render(<App />)

    const sourceSummaryLabel = await screen.findByText('Source server ready')
    const sourceSummary = sourceSummaryLabel.closest('section') as HTMLElement
    expect(within(sourceSummary).getByRole('heading', { name: 'Testing' })).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Start a new migration' })).toBeEnabled()
    expect(within(sourceSummary).getByText('matrix.example.test')).toBeInTheDocument()
    expect(screen.getByText('No previous migration')).toBeInTheDocument()
    expect(screen.getByText('Source assessment details')).toBeInTheDocument()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      `/api/source-stacks/${stackId}/select`,
      expect.objectContaining({ method: 'POST' }),
    ))
  })

  it('requires explicit stack selection when multiple ready stacks exist', async () => {
    const multipleAssessment = {
      ...sourceAssessment,
      counts: { ...sourceAssessment.counts, stacks: 2, stackFileSets: 2 },
      stacks: [
        ...sourceAssessment.stacks,
        { ...sourceAssessment.stacks[0], sourceStackId: secondStackId, slug: 'club', name: 'Club' },
      ],
    }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(authenticatedSession))
      .mockResolvedValueOnce(jsonResponse(hostStatus))
      .mockResolvedValueOnce(jsonResponse(preflight))
      .mockResolvedValueOnce(jsonResponse(assessmentEnvelope(null, multipleAssessment)))
    vi.stubGlobal('fetch', fetchMock)

    render(<App />)

    const stackChoiceHeading = await screen.findByRole('heading', { name: 'Select the stack to migrate' })
    const stackChoicePanel = stackChoiceHeading.closest('section') as HTMLElement
    expect(within(stackChoicePanel).getAllByRole('button', { name: 'Select stack' })).toHaveLength(2)
    expect(screen.getByText('Current package scope: one selected MEM 0.1.0 stack')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Start a new migration' })).not.toBeInTheDocument()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(4))
  })

  it('keeps completed work in previous migrations and opens a clean new workflow', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(authenticatedSession))
      .mockResolvedValueOnce(jsonResponse(hostStatus))
      .mockResolvedValueOnce(jsonResponse(preflight))
      .mockResolvedValueOnce(jsonResponse(assessmentEnvelope(stackId)))
      .mockResolvedValueOnce(jsonResponse(overview({ previousWorkflows: [previousWorkflow] })))
    vi.stubGlobal('fetch', fetchMock)

    const user = userEvent.setup()
    render(<App />)

    await user.click(await screen.findByRole('button', { name: 'Start a new migration' }))

    expect(await screen.findByRole('heading', { name: 'Prepare a new migration package' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Load the Migration Request from the new MEM server' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: previousWorkflow.package.fileName })).not.toBeInTheDocument()

    const history = screen.getByText('Previous migrations').closest('details')
    expect(history).not.toHaveAttribute('open')
  })

  it('confirms a separate new workflow when resumable work exists', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(authenticatedSession))
      .mockResolvedValueOnce(jsonResponse(hostStatus))
      .mockResolvedValueOnce(jsonResponse(preflight))
      .mockResolvedValueOnce(jsonResponse(assessmentEnvelope(stackId)))
      .mockResolvedValueOnce(jsonResponse(overview({ currentWorkflow })))
    vi.stubGlobal('fetch', fetchMock)

    const user = userEvent.setup()
    render(<App />)

    expect(await screen.findByRole('button', { name: 'Continue current migration' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Start a new migration' }))

    const confirmation = await screen.findByRole('alert')
    expect(within(confirmation).getByText(/will not be deleted or changed/i)).toBeInTheDocument()
    await user.click(within(confirmation).getByRole('button', { name: 'Start new migration' }))

    expect(await screen.findByRole('heading', { name: 'Prepare a new migration package' })).toBeInTheDocument()
  })

  it('blocks a new workflow while a source operation is running', async () => {
    const runningWorkflow = {
      ...currentWorkflow,
      status: 'Running',
      stage: 'Capturing',
      actions: { ...currentWorkflow.actions, canCreatePreviewCapture: false, canCancel: true },
    }
    const blockedReason = 'A source capture or packaging operation is already running. Continue that workflow before starting another migration.'
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(authenticatedSession))
      .mockResolvedValueOnce(jsonResponse(hostStatus))
      .mockResolvedValueOnce(jsonResponse(preflight))
      .mockResolvedValueOnce(jsonResponse(assessmentEnvelope(stackId)))
      .mockResolvedValueOnce(jsonResponse(overview({
        operationRunning: true,
        canStartNewWorkflow: false,
        startNewBlockedReason: blockedReason,
        currentWorkflow: runningWorkflow,
      })))
    vi.stubGlobal('fetch', fetchMock)

    render(<App />)

    expect(await screen.findByRole('button', { name: 'Continue current migration' })).toHaveClass('primary-button')
    expect(screen.getByRole('button', { name: 'Start a new migration' })).toBeDisabled()
    expect(screen.getByText(blockedReason)).toBeInTheDocument()
  })

  it('returns to sign-in when a protected session expires and restores durable workflow context after re-authentication', async () => {
    let protectedSessionExpired = true
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input)

      if (url === '/api/access/session') {
        return jsonResponse(authenticatedSession)
      }

      if (url === '/api/host/status' && protectedSessionExpired) {
        protectedSessionExpired = false
        return jsonResponse({
          type: 'about:blank',
          title: 'Authentication required',
          status: 401,
          detail: 'Sign in with the access code printed by the Source Assistant process.',
        }, 401)
      }

      if (url === '/api/access/login') {
        return jsonResponse(authenticatedSession)
      }

      if (url === '/api/host/status') {
        return jsonResponse(hostStatus)
      }

      if (url === '/api/preflight') {
        return jsonResponse(preflight)
      }

      if (url === '/api/assessments/latest') {
        return jsonResponse(assessmentEnvelope(stackId))
      }

      if (url.startsWith('/api/workflows/overview?')) {
        return jsonResponse(overview({ currentWorkflow }))
      }

      throw new Error(`Unexpected fetch: ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)

    const user = userEvent.setup()
    render(<App />)

    expect(await screen.findByText('Session expired')).toBeInTheDocument()
    expect(screen.getByText(/durable migration workflow.*preserved/i)).toBeInTheDocument()
    expect(screen.queryByText('Sign in with the access code printed by the Source Assistant process.')).not.toBeInTheDocument()

    await user.type(screen.getByLabelText('Access code'), 'ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2345')
    await user.click(screen.getByRole('button', { name: 'Open Source Assistant' }))

    expect(await screen.findByRole('button', { name: 'Continue current migration' })).toBeInTheDocument()
    expect(screen.getByText('Source server ready')).toBeInTheDocument()
  })

})


function entryFixture() {
  const state = { current: currentWorkflow }
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url === '/api/access/session') return jsonResponse(authenticatedSession)
    if (url === '/api/host/status') return jsonResponse(hostStatus)
    if (url === '/api/preflight') return jsonResponse(preflight)
    if (url === '/api/assessments/latest') return jsonResponse(assessmentEnvelope(stackId))
    if (url.startsWith('/api/workflows/overview?')) return jsonResponse(overview({ currentWorkflow: state.current, previousWorkflows: [previousWorkflow] }))
    if (url === '/api/intake-requests/validate' && init?.method === 'POST') {
      state.current = { ...currentWorkflow, workflowId: 'source-newly-imported' }
      return jsonResponse({ workflow: state.current, captures: [] })
    }
    if (url === `/api/workflows/${state.current.workflowId}`) return jsonResponse(state.current)
    if (url === `/api/workflows/${previousWorkflow.workflowId}`) return jsonResponse(previousWorkflow)
    if (url.endsWith('/captures')) return jsonResponse([])
    throw new Error(`Unexpected fetch: ${url}`)
  })
  vi.stubGlobal('fetch', fetchMock)
  render(<App />)
  return { state, fetchMock }
}

describe('Source overview presentation closeout', () => {
  it('hides the redundant continuation only while that exact current workflow is open', async () => {
    const { fetchMock } = entryFixture()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'Continue current migration' }))
    expect(await screen.findByText('CURRENT-FINGERPRINT')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Continue current migration' })).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Migration workspace open' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Close workspace' }))
    expect(screen.getByRole('button', { name: 'Continue current migration' })).toBeEnabled()
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0)
  })

  it('keeps continuation available when the operator is viewing a different historical workflow', async () => {
    entryFixture()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'View previous migration' }))
    expect(await screen.findByRole('heading', { name: previousWorkflow.package.fileName })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Continue current migration' })).toBeEnabled()
  })

  it('tracks the accepted new workflow without remounting it or hiding another resumable workflow first', async () => {
    const { fetchMock } = entryFixture()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'Start a new migration' }))
    await user.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Start new migration' }))
    expect(screen.getByRole('button', { name: 'Continue current migration' })).toBeEnabled()
    await user.click(screen.getByText('Paste request JSON instead'))
    fireEvent.change(screen.getByLabelText('Request JSON'), { target: { value: JSON.stringify({
      schema: 'mem-secure-intake-request', schemaVersion: 1, intakeId: 'mig_new', requestKind: 'preview',
      ageRecipient: 'age1-public-recipient', recipientFingerprint: 'fingerprint',
      expiresAtUtc: '2026-09-22T04:44:49Z', targetControlPlaneVersion: '0.2.0', sourceStackId: stackId,
    }) } })
    await user.click(screen.getByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: 'Validate and use request' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Continue current migration' })).not.toBeInTheDocument())
    expect(screen.getByLabelText('Migration workflow: step 3 of 5')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create fresh capture' })).toBeEnabled()
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1)
    expect(fetchMock).not.toHaveBeenCalledWith('/api/workflows/source-newly-imported', expect.anything())
    expect(fetchMock).not.toHaveBeenCalledWith('/api/workflows/latest', expect.anything())
  })

  it('retains the source assessment UTC evidence and explicitly zoned local history time', async () => {
    entryFixture()
    const user = userEvent.setup()
    await screen.findByRole('button', { name: 'Continue current migration' })
    await user.click(screen.getByText('Advanced diagnostics'))
    expect(screen.getByText('2026-07-30T09:00:00.000Z')).toBeVisible()
    await user.click(screen.getByText('Previous migrations'))
    const history = screen.getByText('Previous migrations').closest('details')
    if (!(history instanceof HTMLElement)) throw new Error('Expected the history disclosure')
    expect(history.querySelector('time')).toHaveAttribute('datetime', '2026-07-29T02:00:00.000Z')
    expect(history.querySelector('time')).toHaveTextContent(/GMT/)
  })
})
