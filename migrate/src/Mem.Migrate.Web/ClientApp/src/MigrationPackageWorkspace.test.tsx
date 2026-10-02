import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { MigrationPackageWorkspace } from './MigrationPackageWorkspace'
import type { SourceCaptureOption, SourceWorkflow } from './api'

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

const stackId = '11111111-1111-1111-1111-111111111111'
const request = {
  schema: 'mem-secure-intake-request',
  schemaVersion: 1,
  intakeId: 'mig_20260730_preview',
  packageRevisionId: null,
  requestKind: 'preview',
  ageRecipient: 'age1r5cmtjs7qft4w44jqh0y4w5w23jlcccztfrlqq8x3r2g8r60vqkqnkvu6f',
  recipientFingerprint: '5686-DAB3-3EB4-58D0',
  expiresAtUtc: '2026-08-01T00:00:00Z',
  targetControlPlaneVersion: '0.2.0',
  sourceStackId: stackId,
}

const workflow = {
  schemaVersion: 1,
  workflowId: 'source-20260730-000000Z-11111111111111111111111111111111',
  status: 'Pending',
  stage: 'RequestValidated',
  assessmentId: 'assessment-01',
  selectedSourceStackId: stackId,
  request: {
    intakeId: request.intakeId,
    packageRevisionId: null,
    requestKind: 'preview',
    recipientFingerprint: request.recipientFingerprint,
    expiresAtUtc: request.expiresAtUtc,
    targetControlPlaneVersion: '0.2.0',
  },
  encryptionReadinessAcknowledgedAtUtc: '2026-07-30T00:00:00Z',
  captureId: null,
  package: null,
  createdAtUtc: '2026-07-30T00:00:00Z',
  updatedAtUtc: '2026-07-30T00:00:00Z',
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

function readyWorkflow(localState = 'Available') {
  return {
    ...workflow,
    status: 'Completed',
    stage: localState === 'Deleted' ? 'PackageDeleted' : 'ReadyForDownload',
    captureId: 'capture-01',
    package: {
      fileName: 'mem-migration-20260730-010000Z.memmigration.zip.age',
      sizeBytes: 8192,
      sha256: 'abc123',
      completedAtUtc: '2026-07-30T01:00:00Z',
      captureKind: 'preview',
      sourceFrozen: false,
      rehearsalOnly: true,
      stackCount: 1,
      localState,
      packageFileAvailable: localState === 'Available',
      reportAvailable: localState === 'Available',
    },
    actions: {
      canCreatePreviewCapture: false,
      canSelectCapture: false,
      canCreatePackage: false,
      canCancel: false,
      canDownloadPackage: localState === 'Available',
      canDownloadReport: localState === 'Available',
      canDeletePackage: localState === 'Available',
      blockedReason: null,
    },
  }
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

describe('Guided migration package workspace', () => {
  it('opens a clean Step 1 and advances to readiness after request JSON is supplied', async () => {
    vi.stubGlobal('fetch', vi.fn())
    const user = userEvent.setup()

    render(
      <MigrationPackageWorkspace
        csrfToken="csrf-test"
        selectedSourceStackId={stackId}
        sourceStackName="Testing"
        mode="new"
      />,
    )

    expect(screen.getByLabelText('Migration workflow: step 1 of 5')).toBeInTheDocument()
    await user.click(screen.getByText('Paste request JSON instead'))
    fireEvent.change(screen.getByLabelText('Request JSON'), { target: { value: JSON.stringify(request) } })

    expect(screen.getByLabelText('Migration workflow: step 2 of 5')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Validate and use request' })).toBeDisabled()
  })

  it('requires encryption readiness and validates the request through the existing server contract', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(jsonResponse({ workflow, captures: [] }))
    vi.stubGlobal('fetch', fetchMock)

    const user = userEvent.setup()
    render(
      <MigrationPackageWorkspace
        csrfToken="csrf-test"
        selectedSourceStackId={stackId}
        sourceStackName="Testing"
        mode="new"
      />,
    )

    await user.click(screen.getByText('Paste request JSON instead'))
    fireEvent.change(screen.getByLabelText('Request JSON'), { target: { value: JSON.stringify(request) } })
    await user.click(screen.getByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: 'Validate and use request' }))

    expect(await screen.findByText(request.recipientFingerprint)).toBeInTheDocument()
    expect(screen.getByLabelText('Migration workflow: step 3 of 5')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith('/api/intake-requests/validate', expect.objectContaining({
      method: 'POST',
      headers: expect.objectContaining({ 'X-MEM-CSRF': 'csrf-test' }),
    }))
  })

  it('continues the exact durable workflow instead of reading the global latest row', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(workflow))
      .mockResolvedValueOnce(jsonResponse([]))
    vi.stubGlobal('fetch', fetchMock)

    render(
      <MigrationPackageWorkspace
        csrfToken="csrf-test"
        selectedSourceStackId={stackId}
        sourceStackName="Testing"
        mode="workflow"
        workflowId={workflow.workflowId}
      />,
    )

    expect(await screen.findByText(request.recipientFingerprint)).toBeInTheDocument()
    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      `/api/workflows/${encodeURIComponent(workflow.workflowId)}`,
      expect.objectContaining({ credentials: 'same-origin' }),
    )
    expect(fetchMock).not.toHaveBeenCalledWith('/api/workflows/latest', expect.anything())
  })

  it('maps a ready package to Step 5 without claiming that it was downloaded', async () => {
    const completed = readyWorkflow()
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(completed))
      .mockResolvedValueOnce(jsonResponse([]))
    vi.stubGlobal('fetch', fetchMock)

    render(
      <MigrationPackageWorkspace
        csrfToken="csrf-test"
        selectedSourceStackId={stackId}
        sourceStackName="Testing"
        mode="workflow"
        workflowId={completed.workflowId}
      />,
    )

    await screen.findByRole('link', { name: 'Download encrypted package' })
    expect(screen.getByLabelText('Migration workflow: step 5 of 5')).toBeInTheDocument()
    expect(screen.getByText('Package ready to download')).toBeInTheDocument()
    expect(screen.queryByText(/^Downloaded$/)).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Download encrypted package' })).toHaveAttribute(
      'href',
      `/api/workflows/${encodeURIComponent(completed.workflowId)}/package/download`,
    )
    expect(screen.queryByText(/\/var\/lib\/mem-migrate/)).not.toBeInTheDocument()
  })

  it('requires explicit confirmation before deleting local package artifacts and directs deleted work to a fresh migration', async () => {
    const completed = readyWorkflow()
    const deleted = readyWorkflow('Deleted')
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(completed))
      .mockResolvedValueOnce(jsonResponse([]))
      .mockResolvedValueOnce(jsonResponse(deleted))
    vi.stubGlobal('fetch', fetchMock)
    const onStartNew = vi.fn()

    const user = userEvent.setup()
    render(
      <MigrationPackageWorkspace
        csrfToken="csrf-test"
        selectedSourceStackId={stackId}
        sourceStackName="Testing"
        mode="workflow"
        workflowId={completed.workflowId}
        onStartNew={onStartNew}
      />,
    )

    await user.click(await screen.findByRole('button', { name: 'Delete local encrypted package' }))
    expect(screen.getByText(/does not delete the plaintext source capture/i)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Delete package and reports' }))

    expect(await screen.findByText('Local package deleted')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Start a new migration' }))
    expect(onStartNew).toHaveBeenCalledOnce()
    await waitFor(() => expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      `/api/workflows/${encodeURIComponent(completed.workflowId)}/package`,
      expect.objectContaining({ method: 'DELETE' }),
    ))
  })
})


const captureOne: SourceCaptureOption = {
  captureId: 'capture-01', sourceStackSlug: 'testing', matrixServerName: 'matrix.example.test',
  completedAtUtc: '2026-09-21T04:44:49.1234567Z', archiveBytes: 8192,
  archiveSha256: 'archive-sha-01', sourceFingerprint: 'source-fingerprint-01',
  captureKind: 'preview', eligibleForRequest: true, selected: true,
}
const captureTwo: SourceCaptureOption = {
  ...captureOne, captureId: 'capture-02', completedAtUtc: '2026-09-20T17:34:12Z', selected: false,
}
function selectedWorkflow(): SourceWorkflow {
  return { ...workflow, stage: 'CaptureSelected', captureId: captureOne.captureId,
    actions: { ...workflow.actions, canCreatePackage: true } }
}
function sourceFixture(initial: SourceWorkflow, options: SourceCaptureOption[] = [captureOne, captureTwo]) {
  const state = { workflow: initial, captures: options, rejectSelection: false, failCaptureRead: false }
  const base = `/api/workflows/${encodeURIComponent(initial.workflowId)}`
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    const method = init?.method ?? 'GET'
    if (method === 'GET' && url === base) return jsonResponse(state.workflow)
    if (method === 'GET' && url === `${base}/captures`) {
      return state.failCaptureRead ? jsonResponse({ detail: 'Capture metadata unavailable.' }, 503) : jsonResponse(state.captures)
    }
    if (method === 'POST' && url.endsWith('/select')) {
      if (state.rejectSelection) return jsonResponse({ detail: 'The capture is no longer eligible.' }, 409)
      const captureId = decodeURIComponent(url.slice(`${base}/captures/`.length, -'/select'.length))
      state.workflow = { ...state.workflow, captureId, stage: 'CaptureSelected',
        actions: { ...state.workflow.actions, canCreatePackage: true } }
      return jsonResponse(state.workflow)
    }
    if (method === 'POST' && url === `${base}/capture`) {
      state.workflow = { ...state.workflow, captureId: 'fresh-capture', status: 'Running', stage: 'Capturing',
        actions: { ...state.workflow.actions, canCreatePackage: false, canSelectCapture: false,
          canCreatePreviewCapture: false, canCancel: true } }
      return jsonResponse(state.workflow)
    }
    if (method === 'POST' && url === `${base}/package`) {
      state.workflow = { ...state.workflow, status: 'Running', stage: 'Packaging',
        actions: { ...state.workflow.actions, canCreatePackage: false, canSelectCapture: false,
          canCreatePreviewCapture: false, canCancel: true } }
      return jsonResponse(state.workflow)
    }
    throw new Error(`Unexpected request: ${method} ${url}`)
  })
  vi.stubGlobal('fetch', fetchMock)
  const props = { csrfToken: 'csrf-test', selectedSourceStackId: stackId, sourceStackName: 'Testing',
    mode: 'workflow' as const, workflowId: initial.workflowId }
  const view = render(<MigrationPackageWorkspace {...props} />)
  return { state, fetchMock, props, view, base }
}
function mutations(mock: ReturnType<typeof sourceFixture>['fetchMock']) {
  return mock.mock.calls.filter(([, init]) => init?.method === 'POST' || init?.method === 'DELETE')
}

describe('Source capture presentation closeout', () => {
  it('reopens the recorded capture as a compact summary and highlights package creation', async () => {
    const { fetchMock } = sourceFixture(selectedWorkflow())
    expect(await screen.findByLabelText('Selected capture summary')).toBeInTheDocument()
    expect(screen.getByLabelText('Migration workflow: step 4 of 5')).toBeInTheDocument()
    expect(screen.getByLabelText('Selected capture summary')).toHaveTextContent('matrix.example.test')
    expect(screen.getByText('Ready to create package')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Change capture' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('button', { name: 'Use capture' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create encrypted package' })).toBeEnabled()
    const panel = screen.getByRole('heading', { name: 'Encrypt the selected source capture for the target MEM server' }).closest('section')
    if (!(panel instanceof HTMLElement)) throw new Error('Expected the package stage panel')
    expect(panel).toHaveClass('active')
    expect(panel).not.toHaveClass('upcoming')
    expect(mutations(fetchMock)).toHaveLength(0)
  })

  it('does not select a retained capture just because the list marks it selected', async () => {
    const { fetchMock } = sourceFixture(workflow)
    expect(await screen.findByRole('button', { name: 'Create fresh capture' })).toBeEnabled()
    expect(screen.getByLabelText('Migration workflow: step 3 of 5')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Use capture' })).toHaveLength(2)
    expect(screen.queryByLabelText('Selected capture summary')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create encrypted package' })).toBeDisabled()
    expect(mutations(fetchMock)).toHaveLength(0)
  })

  it('opens and closes alternatives without changing the selected capture or submitting anything', async () => {
    const { fetchMock } = sourceFixture(selectedWorkflow())
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'Change capture' }))
    expect(screen.getByRole('button', { name: 'Keep selected capture' })).toHaveAttribute('aria-expanded', 'true')
    expect(within(screen.getByRole('article', { name: 'Capture capture-01' })).getByRole('button', { name: 'Selected' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Keep selected capture' }))
    expect(screen.queryByRole('button', { name: 'Use capture' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create encrypted package' })).toBeEnabled()
    expect(mutations(fetchMock)).toHaveLength(0)
  })

  it('selects exactly the requested alternative through the existing CSRF-protected endpoint', async () => {
    const { state, fetchMock, base } = sourceFixture(selectedWorkflow())
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'Change capture' }))
    await user.click(within(screen.getByRole('article', { name: 'Capture capture-02' })).getByRole('button', { name: 'Use capture' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Change capture' })).toBeEnabled())
    expect(state.workflow.captureId).toBe('capture-02')
    await waitFor(() => expect(screen.getByLabelText('Selected capture summary')).toHaveFocus())
    expect(screen.getByLabelText('Selected capture summary')).toHaveTextContent('capture-02')
    expect(screen.queryByRole('button', { name: 'Use capture' })).not.toBeInTheDocument()
    expect(mutations(fetchMock)).toHaveLength(1)
    expect(fetchMock).toHaveBeenCalledWith(`${base}/captures/capture-02/select`, expect.objectContaining({
      method: 'POST', headers: expect.objectContaining({ 'X-MEM-CSRF': 'csrf-test' }),
    }))
  })

  it('retains the original selection and visible alternatives after a rejected change', async () => {
    const { state, fetchMock } = sourceFixture(selectedWorkflow())
    state.rejectSelection = true
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'Change capture' }))
    await user.click(within(screen.getByRole('article', { name: 'Capture capture-02' })).getByRole('button', { name: 'Use capture' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('The capture is no longer eligible.')
    expect(screen.getByLabelText('Selected capture summary')).toHaveTextContent('capture-01')
    expect(screen.getByRole('button', { name: 'Keep selected capture' })).toHaveAttribute('aria-expanded', 'true')
    expect(mutations(fetchMock)).toHaveLength(1)
  })

  it('keeps ineligible alternatives disabled and follows the server package gate', async () => {
    const paused = { ...selectedWorkflow(), actions: { ...selectedWorkflow().actions, canCreatePackage: false, blockedReason: 'The target migration request has expired.' } }
    const { fetchMock } = sourceFixture(paused, [captureOne, { ...captureTwo, eligibleForRequest: false }])
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'Change capture' }))
    expect(screen.getByRole('button', { name: 'Create encrypted package' })).toBeDisabled()
    expect(within(screen.getByRole('article', { name: 'Capture capture-02' })).getByRole('button', { name: 'Use capture' })).toBeDisabled()
    expect(screen.getByText('Preparation paused')).toBeInTheDocument()
    expect(mutations(fetchMock)).toHaveLength(0)
  })

  it('does not veto a server-permitted package when selected-capture metadata is missing', async () => {
    const { fetchMock } = sourceFixture(selectedWorkflow(), [])
    expect(await screen.findByLabelText('Selected capture summary')).toHaveTextContent('capture-01')
    expect(screen.getByText(/Capture details are unavailable/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create encrypted package' })).toBeEnabled()
    expect(screen.queryByRole('button', { name: 'Use capture' })).not.toBeInTheDocument()
    expect(mutations(fetchMock)).toHaveLength(0)
  })

  it('retains the server snapshot and metadata-read error without starting another operation', async () => {
    const fixture = sourceFixture(selectedWorkflow())
    fixture.state.failCaptureRead = true
    expect(await screen.findByRole('alert')).toHaveTextContent('Capture metadata unavailable.')
    expect(screen.getByLabelText('Selected capture summary')).toHaveTextContent('capture-01')
    expect(screen.getByRole('button', { name: 'Create encrypted package' })).toBeEnabled()
    expect(mutations(fixture.fetchMock)).toHaveLength(0)
  })

  it('reconstructs a collapsed selection after the workspace is unmounted and reopened', async () => {
    const { view, props, fetchMock } = sourceFixture(selectedWorkflow())
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'Change capture' }))
    view.unmount()
    render(<MigrationPackageWorkspace {...props} />)
    expect(await screen.findByRole('button', { name: 'Change capture' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getByLabelText('Migration workflow: step 4 of 5')).toBeInTheDocument()
    expect(mutations(fetchMock)).toHaveLength(0)
  })

  it('keeps a running capture ID distinct from a completed selected capture', async () => {
    const running = { ...workflow, captureId: 'reserved-id', stage: 'Capturing', status: 'Running',
      actions: { ...workflow.actions, canCreatePreviewCapture: false, canSelectCapture: false, canCancel: true } }
    const { fetchMock } = sourceFixture(running)
    expect(await screen.findByRole('button', { name: 'Cancel operation' })).toBeEnabled()
    expect(screen.getByLabelText('Migration workflow: step 3 of 5')).toBeInTheDocument()
    expect(screen.queryByLabelText('Selected capture summary')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create encrypted package' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Cancel operation' })).toBeEnabled()
    expect(mutations(fetchMock)).toHaveLength(0)
  })

  it('advances a fresh capture to the package action from polled server state, without another capture POST', async () => {
    const { state, fetchMock, base } = sourceFixture(workflow, [])
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'Create fresh capture' }))
    expect(await screen.findByRole('button', { name: 'Cancel operation' })).toBeEnabled()
    state.workflow = { ...selectedWorkflow(), captureId: 'fresh-capture', stage: 'CaptureReady' }
    state.captures = [{ ...captureOne, captureId: 'fresh-capture' }]
    await waitFor(() => expect(screen.getByRole('button', { name: 'Change capture' })).toBeEnabled(), { timeout: 3000 })
    expect(screen.getByLabelText('Migration workflow: step 4 of 5')).toBeInTheDocument()
    expect(screen.getByLabelText('Selected capture summary')).toHaveTextContent('fresh-capture')
    expect(screen.getByRole('button', { name: 'Create encrypted package' })).toBeEnabled()
    expect(mutations(fetchMock).map(([url]) => url)).toEqual([`${base}/capture`])
  })

  it('packages the recorded capture once and keeps download/report links server-authored', async () => {
    const { state, fetchMock, base } = sourceFixture(selectedWorkflow())
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'Create encrypted package' }))
    expect(await screen.findByRole('button', { name: 'Cancel operation' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Starting encryption…' })).toBeDisabled()
    state.workflow = readyWorkflow()
    await waitFor(() => expect(screen.getByRole('link', { name: 'Download encrypted package' })).toHaveAttribute('href', `${base}/package/download`), { timeout: 3000 })
    expect(screen.getByRole('link', { name: 'Download package report' })).toHaveAttribute('href', `${base}/package/report`)
    expect(screen.queryByText(/^Downloaded$/)).not.toBeInTheDocument()
    expect(mutations(fetchMock).map(([url]) => url)).toEqual([`${base}/package`])
  })

  it('keeps failed packaging visible even with retained capture evidence', async () => {
    const failed = { ...selectedWorkflow(), status: 'Failed', stage: 'Packaging', failureCode: 'package_failed', failureSummary: 'Encryption stopped before completion.' }
    const { fetchMock } = sourceFixture(failed)
    expect(await screen.findByRole('alert')).toHaveTextContent('Encryption stopped before completion.')
    expect(screen.getByText('Workflow needs review')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Download encrypted package' })).not.toBeInTheDocument()
    expect(screen.queryByText('Encrypted package ready')).not.toBeInTheDocument()
    expect(mutations(fetchMock)).toHaveLength(0)
  })

  it('keeps a final request limited to eligible retained evidence without a fresh preview capture', async () => {
    const final = { ...workflow, request: { ...workflow.request, requestKind: 'final' },
      actions: { ...workflow.actions, canCreatePreviewCapture: false, blockedReason: 'A final request requires an eligible frozen capture.' } }
    const { fetchMock } = sourceFixture(final, [{ ...captureOne, captureKind: 'final' }])
    expect(await screen.findByRole('button', { name: 'Use capture' })).toBeEnabled()
    expect(screen.queryByRole('button', { name: 'Create fresh capture' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create encrypted package' })).toBeDisabled()
    expect(mutations(fetchMock)).toHaveLength(0)
  })

  it('shows explicit local offsets while retaining full UTC precision and IDs in accessible details', async () => {
    sourceFixture(selectedWorkflow())
    const summary = await screen.findByLabelText('Selected capture summary')
    const timestamp = summary.querySelector('time')
    expect(timestamp).toHaveAttribute('datetime', '2026-09-21T04:44:49.1234567Z')
    expect(timestamp).toHaveAttribute('title', 'Recorded UTC: 2026-09-21T04:44:49.1234567Z')
    expect(timestamp).toHaveTextContent(/GMT/)
    const user = userEvent.setup()
    await user.click(within(summary).getByText('Selected capture details'))
    expect(within(summary).getByText('2026-09-21T04:44:49.1234567Z')).toBeVisible()
    expect(within(summary).getByText('archive-sha-01')).toBeVisible()
    expect(within(summary).getByText('source-fingerprint-01')).toBeVisible()
    await user.click(screen.getByText('Technical workflow details'))
    expect(screen.getByText(workflow.workflowId)).toBeVisible()
    expect(screen.getByText(/Times shown in your browser time zone/)).toBeInTheDocument()
  })
})
