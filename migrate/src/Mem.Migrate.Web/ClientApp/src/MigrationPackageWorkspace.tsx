import { ChangeEvent, useEffect, useMemo, useRef, useState } from 'react'
import {
  type SecureIntakeRequestInput,
  type SourceCaptureOption,
  type SourceWorkflow,
  cancelWorkflowOperation,
  deleteWorkflowPackage,
  importMigrationRequest,
  readWorkflow,
  readWorkflowCaptures,
  selectWorkflowCapture,
  startWorkflowCapture,
  startWorkflowPackage,
  workflowPackageDownloadUrl,
  workflowPackageReportUrl,
} from './api'

import { MigrationTimestamp } from './MigrationTimestamp'
import { migrationDisplayTimeZone, migrationUtcIso } from './migration-time'
import { selectedWorkflowCaptureId, workflowActivity, workflowStep } from './workflow-presentation'

type Props = {
  csrfToken: string
  selectedSourceStackId: string
  sourceStackName: string
  mode: 'new' | 'workflow'
  workflowId?: string
  onWorkflowChanged?: (workflow: SourceWorkflow) => void
  onStartNew?: () => void
  onClose?: () => void
}

type StepState = 'complete' | 'current' | 'upcoming'

const steps = [
  { number: 1, label: 'Import request' },
  { number: 2, label: 'Confirm readiness' },
  { number: 3, label: 'Create capture' },
  { number: 4, label: 'Create package' },
  { number: 5, label: 'Download package' },
]

function formatBytes(value: number) {
  if (!Number.isFinite(value) || value <= 0) return '0 B'
  const units = ['B', 'KiB', 'MiB', 'GiB', 'TiB']
  let size = value
  let index = 0
  while (size >= 1024 && index < units.length - 1) {
    size /= 1024
    index += 1
  }
  return `${size.toFixed(size >= 10 || index === 0 ? 0 : 1)} ${units[index]}`
}

function humanize(value: string) {
  return value
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replaceAll('-', ' ')
    .replaceAll('_', ' ')
    .replace(/^./, (character) => character.toUpperCase())
}

function parseRequestJson(text: string): SecureIntakeRequestInput {
  const parsed = JSON.parse(text) as Partial<SecureIntakeRequestInput>
  return {
    schema: String(parsed.schema ?? ''),
    schemaVersion: Number(parsed.schemaVersion ?? 0),
    intakeId: String(parsed.intakeId ?? ''),
    packageRevisionId: parsed.packageRevisionId == null ? null : String(parsed.packageRevisionId),
    requestKind: String(parsed.requestKind ?? ''),
    ageRecipient: String(parsed.ageRecipient ?? ''),
    recipientFingerprint: String(parsed.recipientFingerprint ?? ''),
    expiresAtUtc: String(parsed.expiresAtUtc ?? ''),
    targetControlPlaneVersion: String(parsed.targetControlPlaneVersion ?? ''),
    sourceStackId: parsed.sourceStackId == null ? null : String(parsed.sourceStackId),
  }
}

function stepState(stepNumber: number, currentStep: number): StepState {
  if (stepNumber < currentStep) return 'complete'
  if (stepNumber === currentStep) return 'current'
  return 'upcoming'
}

function MigrationWorkflowStepper({ currentStep }: { currentStep: number }) {
  return (
    <ol className="migration-stepper" aria-label={`Migration workflow: step ${currentStep} of 5`}>
      {steps.map((step) => {
        const state = stepState(step.number, currentStep)
        return (
          <li className={state} key={step.number} aria-current={state === 'current' ? 'step' : undefined}>
            <span className="step-marker">{state === 'complete' ? '✓' : step.number}</span>
            <span className="step-label">{step.label}</span>
          </li>
        )
      })}
    </ol>
  )
}

export function MigrationPackageWorkspace({
  csrfToken,
  selectedSourceStackId,
  sourceStackName,
  mode,
  workflowId,
  onWorkflowChanged,
  onStartNew,
  onClose,
}: Props) {
  const [requestText, setRequestText] = useState('')
  const [requestFileName, setRequestFileName] = useState<string | null>(null)
  const [readinessAcknowledged, setReadinessAcknowledged] = useState(false)
  const [workflow, setWorkflow] = useState<SourceWorkflow | null>(null)
  const [captures, setCaptures] = useState<SourceCaptureOption[]>([])
  const [loadingWorkflow, setLoadingWorkflow] = useState(mode === 'workflow')
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [confirmDeletePackage, setConfirmDeletePackage] = useState(false)
  const [choosingCapture, setChoosingCapture] = useState(false)
  const selectedSummaryRef = useRef<HTMLDivElement>(null)
  const focusAcceptedSelection = useRef(false)

  const selectedCaptureId = selectedWorkflowCaptureId(workflow)
  const selectedCapture = useMemo(
    () => captures.find((capture) => capture.captureId === selectedCaptureId) ?? null,
    [captures, selectedCaptureId],
  )
  const currentStep = workflowStep(workflow, requestText.trim().length > 0)
  const activity = workflow ? workflowActivity(workflow) : null
  const showCaptureChoices = !selectedCaptureId || choosingCapture

  // This is only an open/closed presentation choice; never select a capture here.
  useEffect(() => { setChoosingCapture(false) }, [selectedCaptureId])

  // A successful explicit choice hides its button; return keyboard focus to the
  // recorded selection rather than leaving it in the collapsed alternatives.
  useEffect(() => {
    if (focusAcceptedSelection.current && busy === null) {
      focusAcceptedSelection.current = false
      selectedSummaryRef.current?.focus()
    }
  }, [busy, selectedCaptureId])

  useEffect(() => {
    let active = true
    setRequestText('')
    setRequestFileName(null)
    setReadinessAcknowledged(false)
    setWorkflow(null)
    setCaptures([])
    setError(null)
    setConfirmDeletePackage(false)
    setChoosingCapture(false)
    focusAcceptedSelection.current = false

    if (mode !== 'workflow' || !workflowId) {
      setLoadingWorkflow(false)
      return () => { active = false }
    }

    setLoadingWorkflow(true)
    readWorkflow(workflowId)
      .then(async (loadedWorkflow) => {
        if (!active) return
        setWorkflow(loadedWorkflow)
        setReadinessAcknowledged(true)
        setCaptures(await readWorkflowCaptures(loadedWorkflow.workflowId))
      })
      .catch((caught) => {
        if (!active) return
        setError(caught instanceof Error ? caught.message : 'The migration workflow could not be loaded.')
      })
      .finally(() => {
        if (active) setLoadingWorkflow(false)
      })

    return () => { active = false }
  }, [mode, workflowId, selectedSourceStackId])

  useEffect(() => {
    if (!workflow || workflow.status.toLowerCase() !== 'running') return

    const timer = window.setInterval(() => {
      readWorkflow(workflow.workflowId)
        .then(async (updated) => {
          setWorkflow(updated)
          onWorkflowChanged?.(updated)
          if (updated.status.toLowerCase() !== 'running') {
            setBusy(null)
            setCaptures(await readWorkflowCaptures(updated.workflowId))
          }
        })
        .catch((caught) => setError(caught instanceof Error ? caught.message : 'Workflow status could not be refreshed.'))
    }, 1500)

    return () => window.clearInterval(timer)
  }, [workflow?.workflowId, workflow?.status, onWorkflowChanged])

  function publishWorkflow(updated: SourceWorkflow) {
    setWorkflow(updated)
    onWorkflowChanged?.(updated)
  }

  async function chooseRequestFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (!file) return
    setError(null)
    try {
      setRequestText(await file.text())
      setRequestFileName(file.name)
    } catch {
      setError('The migration request file could not be read.')
    } finally {
      event.target.value = ''
    }
  }

  async function validateRequest() {
    setBusy('request')
    setError(null)
    try {
      const request = parseRequestJson(requestText)
      const result = await importMigrationRequest(request, readinessAcknowledged, csrfToken)
      publishWorkflow(result.workflow)
      setCaptures(result.captures)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'The target migration request could not be validated.')
    } finally {
      setBusy(null)
    }
  }

  async function createCapture() {
    if (!workflow) return
    setBusy('capture')
    setError(null)
    try {
      publishWorkflow(await startWorkflowCapture(workflow.workflowId, csrfToken))
    } catch (caught) {
      setBusy(null)
      setError(caught instanceof Error ? caught.message : 'The source capture could not be started.')
    }
  }

  async function chooseCapture(captureId: string) {
    if (!workflow) return
    setBusy(`select-${captureId}`)
    setError(null)
    try {
      const updated = await selectWorkflowCapture(workflow.workflowId, captureId, csrfToken)
      focusAcceptedSelection.current = true
      publishWorkflow(updated)
      setChoosingCapture(false)
      setCaptures(await readWorkflowCaptures(workflow.workflowId))
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'The capture could not be selected.')
    } finally {
      setBusy(null)
    }
  }

  async function createPackage() {
    if (!workflow) return
    setBusy('package')
    setError(null)
    try {
      publishWorkflow(await startWorkflowPackage(workflow.workflowId, csrfToken))
    } catch (caught) {
      setBusy(null)
      setError(caught instanceof Error ? caught.message : 'The encrypted package could not be started.')
    }
  }

  async function cancelOperation() {
    if (!workflow) return
    setBusy('cancel')
    setError(null)
    try {
      publishWorkflow(await cancelWorkflowOperation(workflow.workflowId, csrfToken))
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'The active operation could not be cancelled.')
    } finally {
      setBusy(null)
    }
  }

  async function deleteLocalPackage() {
    if (!workflow) return
    setBusy('delete-package')
    setError(null)
    try {
      publishWorkflow(await deleteWorkflowPackage(workflow.workflowId, csrfToken))
      setConfirmDeletePackage(false)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'The local encrypted package could not be deleted.')
    } finally {
      setBusy(null)
    }
  }

  return (
    <section className="migration-package-workspace" aria-labelledby="migration-package-title">
      <div className="workspace-panel-heading">
        <div>
          <p className="eyebrow">{mode === 'new' ? 'New migration' : 'Migration workflow'}</p>
          <h2 id="migration-package-title">
            {mode === 'new' ? 'Prepare a new migration package' : 'Continue migration package preparation'}
          </h2>
          <p>
            Source stack: <strong>{sourceStackName}</strong>. The workflow remains durable if this browser is refreshed or closed.
          </p>
        </div>
        {onClose ? <button type="button" className="secondary-button" onClick={onClose}>Close workspace</button> : null}
      </div>

      <MigrationWorkflowStepper currentStep={currentStep} />

      {loadingWorkflow ? <div className="workflow-loading">Loading the selected migration workflow…</div> : null}
      {error ? <div className="error-panel inline-error" role="alert">{error}</div> : null}

      {!loadingWorkflow && !workflow ? (
        <div className="new-workflow-steps">
          <section className={`guided-step-panel ${currentStep === 1 ? 'active' : 'complete'}`} aria-labelledby="import-request-title">
            <div className="guided-step-number">1</div>
            <div className="guided-step-content">
              <p className="eyebrow">Import request</p>
              <h3 id="import-request-title">Load the Migration Request from the new MEM server</h3>
              <p>
                Download the JSON request from the target MEM Control Plane, then upload it here. The request contains public encryption material only.
              </p>

              <label className="file-button prominent-file-button">
                Upload migration request
                <input type="file" accept="application/json,.json" onChange={chooseRequestFile} />
              </label>

              {requestFileName ? (
                <div className="request-loaded-state" role="status">
                  <span aria-hidden="true">✓</span>
                  <div><strong>Request loaded</strong><small>{requestFileName}</small></div>
                </div>
              ) : null}

              <details className="paste-request-disclosure">
                <summary>Paste request JSON instead</summary>
                <div>
                  <label htmlFor="migration-request-json">Request JSON</label>
                  <textarea
                    id="migration-request-json"
                    value={requestText}
                    onChange={(event: ChangeEvent<HTMLTextAreaElement>) => {
                      setRequestText(event.target.value)
                      setRequestFileName(null)
                    }}
                    spellCheck={false}
                    placeholder={'{\n  "schema": "mem-secure-intake-request",\n  "schemaVersion": 1,\n  ...\n}'}
                    rows={12}
                  />
                </div>
              </details>
            </div>
          </section>

          <section className={`guided-step-panel ${currentStep === 2 ? 'active' : 'upcoming'}`} aria-labelledby="readiness-title">
            <div className="guided-step-number">2</div>
            <div className="guided-step-content">
              <p className="eyebrow">Confirm readiness</p>
              <h3 id="readiness-title">Protect access to encrypted message history</h3>
              <p>
                Ask users not to sign out of existing Element sessions. They should verify another device, confirm Secure Backup and its recovery secret work, or export room keys. A password reset cannot recreate lost historical message keys.
              </p>
              <label className="acknowledgement-row">
                <input
                  type="checkbox"
                  checked={readinessAcknowledged}
                  onChange={(event: ChangeEvent<HTMLInputElement>) => setReadinessAcknowledged(event.target.checked)}
                  disabled={requestText.trim().length === 0}
                />
                <span>I have communicated this encryption-recovery requirement to the affected users.</span>
              </label>
              <button
                type="button"
                className="primary-button"
                onClick={validateRequest}
                disabled={!readinessAcknowledged || requestText.trim().length === 0 || busy !== null}
              >
                {busy === 'request' ? 'Validating request…' : 'Validate and use request'}
              </button>
            </div>
          </section>

          <aside className="request-trust-note">
            <div>
              <span>Selected source stack</span>
              <strong>{sourceStackName}</strong>
              <code>{selectedSourceStackId}</code>
            </div>
            <p>The target private age identity is never transferred to this server or shown in the browser.</p>
          </aside>
        </div>
      ) : null}

      {!loadingWorkflow && workflow ? (
        <>
          <div className="workflow-summary-grid">
            <div><span>Request</span><strong>{workflow.request.requestKind}</strong><small>{workflow.request.intakeId}</small></div>
            <div><span>Recipient fingerprint</span><strong>{workflow.request.recipientFingerprint}</strong><small>Recalculated and accepted</small></div>
            <div><span>Current activity</span><strong>{activity?.label}</strong><small>{activity?.detail}</small></div>
            <div><span>Request expires</span><strong><MigrationTimestamp value={workflow.request.expiresAtUtc} /></strong><small>Target request validity</small></div>
          </div>

          <p className="workflow-time-zone">Times shown in your browser time zone: <strong>{migrationDisplayTimeZone()}</strong>. Recorded UTC times are available in technical workflow details.</p>

          {workflow.failureSummary ? (
            <div className="workflow-failure" role="alert">
              <strong>{workflow.failureCode ? humanize(workflow.failureCode) : 'Operation failed'}</strong>
              <p>{workflow.failureSummary}</p>
            </div>
          ) : null}

          {workflow.status.toLowerCase() === 'running' ? (
            <div className="operation-progress">
              <div>
                <span className="operation-spinner" aria-hidden="true" />
                <div>
                  <strong>{activity?.label}</strong>
                  <p>The server owns this operation. Closing or refreshing the browser does not cancel it.</p>
                </div>
              </div>
              {workflow.actions.canCancel ? (
                <button type="button" className="danger-outline" onClick={cancelOperation} disabled={busy === 'cancel'}>
                  {busy === 'cancel' ? 'Requesting cancellation…' : 'Cancel operation'}
                </button>
              ) : null}
            </div>
          ) : null}

          {!workflow.package ? (
            <>
              <section className={`guided-step-panel workflow-stage-panel ${currentStep === 3 ? 'active' : 'complete'}`} aria-labelledby="capture-title">
                <div className="guided-step-number">3</div>
                <div className="guided-step-content">
                  <div className="capture-section-heading">
                    <div>
                      <p className="eyebrow">Source capture</p>
                      <h3 id="capture-title">{selectedCaptureId ? 'Selected source capture' : 'Choose a source capture'}</h3>
                      <p>Capture is read-only. A retained capture contains data from its recorded capture time, not later changes.</p>
                    </div>
                    {selectedCaptureId && (workflow.actions.canSelectCapture || workflow.actions.canCreatePreviewCapture) ? (
                      <button
                        type="button"
                        className="secondary-button"
                        aria-expanded={choosingCapture}
                        aria-controls="capture-choices"
                        disabled={busy !== null}
                        onClick={() => setChoosingCapture(!choosingCapture)}
                      >
                        {choosingCapture ? 'Keep selected capture' : 'Change capture'}
                      </button>
                    ) : null}
                  </div>

                  {selectedCaptureId ? (
                    <div className="selected-capture-summary" aria-label="Selected capture summary" ref={selectedSummaryRef} tabIndex={-1}>
                      {selectedCapture ? (
                        <>
                          <strong>{selectedCapture.sourceStackSlug} · {selectedCapture.matrixServerName}</strong>
                          <p><MigrationTimestamp value={selectedCapture.completedAtUtc} /> · {formatBytes(selectedCapture.archiveBytes)} · {humanize(selectedCapture.captureKind)}</p>
                        </>
                      ) : (
                        <>
                          <strong>{sourceStackName}</strong>
                          <p>Capture details are unavailable. The server-recorded selection is retained; package availability still comes from the server.</p>
                        </>
                      )}
                      <details className="capture-technical-details">
                        <summary>Selected capture details</summary>
                        <dl>
                          <div><dt>Capture ID</dt><dd><code>{selectedCaptureId}</code></dd></div>
                          {selectedCapture ? (
                            <>
                              <div><dt>Captured at (UTC)</dt><dd><code>{migrationUtcIso(selectedCapture.completedAtUtc) ?? 'Not recorded'}</code></dd></div>
                              <div><dt>Archive SHA-256</dt><dd><code>{selectedCapture.archiveSha256}</code></dd></div>
                              <div><dt>Source fingerprint</dt><dd><code>{selectedCapture.sourceFingerprint}</code></dd></div>
                            </>
                          ) : null}
                        </dl>
                      </details>
                    </div>
                  ) : null}

                  <div id="capture-choices" hidden={!showCaptureChoices}>
                    <div className="capture-choice-actions">
                      {workflow.actions.canCreatePreviewCapture ? (
                        <button type="button" className="primary-button" onClick={createCapture} disabled={busy !== null}>
                          {busy === 'capture' ? 'Starting capture…' : 'Create fresh capture'}
                        </button>
                      ) : null}
                      <p>{selectedCaptureId ? 'The current selection stays in place until the server accepts another capture.' : 'Choose a fresh capture or use one of the retained captures below. Nothing is selected automatically.'}</p>
                    </div>
                    <h4>Retained captures</h4>
                    {captures.length === 0 ? (
                      <p className="empty-state">No eligible retained plaintext capture is available yet.</p>
                    ) : (
                      <div className="capture-list">
                        {captures.map((capture) => {
                          const selected = capture.captureId === selectedCaptureId
                          return (
                            <article className={`capture-option ${selected ? 'selected' : ''}`} key={capture.captureId} aria-label={`Capture ${capture.captureId}`}>
                              <div>
                                <span>{capture.captureKind}</span>
                                <strong>{capture.sourceStackSlug} · {capture.matrixServerName}</strong>
                                <small><MigrationTimestamp value={capture.completedAtUtc} /> · {formatBytes(capture.archiveBytes)}</small>
                                <details className="capture-technical-details">
                                  <summary>Capture details</summary>
                                  <dl>
                                    <div><dt>Capture ID</dt><dd><code>{capture.captureId}</code></dd></div>
                                    <div><dt>Captured at (UTC)</dt><dd><code>{migrationUtcIso(capture.completedAtUtc) ?? 'Not recorded'}</code></dd></div>
                                    <div><dt>Archive SHA-256</dt><dd><code>{capture.archiveSha256}</code></dd></div>
                                  </dl>
                                </details>
                              </div>
                              <button
                                type="button"
                                className={selected ? 'secondary-button' : 'primary-button'}
                                disabled={selected || !capture.eligibleForRequest || busy !== null || !workflow.actions.canSelectCapture}
                                onClick={() => chooseCapture(capture.captureId)}
                              >
                                {selected ? 'Selected' : busy === `select-${capture.captureId}` ? 'Selecting…' : 'Use capture'}
                              </button>
                            </article>
                          )
                        })}
                      </div>
                    )}
                  </div>
                </div>
              </section>

              <section className={`guided-step-panel workflow-stage-panel ${currentStep === 4 ? 'active' : currentStep > 4 ? 'complete' : 'upcoming'}`} aria-labelledby="package-title">
                <div className="guided-step-number">4</div>
                <div className="guided-step-content">
                  <p className="eyebrow">Create package</p>
                  <h3 id="package-title">Encrypt the selected source capture for the target MEM server</h3>
                  <div className="package-action-row">
                    <div>
                      <span>{selectedCaptureId ? 'Package source' : 'Before packaging'}</span>
                      <strong>{selectedCaptureId ? selectedCapture?.sourceStackSlug ?? sourceStackName : workflow.stage.toLowerCase() === 'recoveryrequired' ? 'Review the recorded capture and recovery evidence' : 'Select or create a capture first'}</strong>
                      {selectedCapture ? <small>Captured <MigrationTimestamp value={selectedCapture.completedAtUtc} /></small> : null}
                    </div>
                    <button
                      type="button"
                      className="primary-button"
                      onClick={createPackage}
                      disabled={!workflow.actions.canCreatePackage || busy !== null}
                    >
                      {busy === 'package' ? 'Starting encryption…' : 'Create encrypted package'}
                    </button>
                  </div>
                  {workflow.actions.blockedReason ? <p className="operation-note">{workflow.actions.blockedReason}</p> : null}
                </div>
              </section>
            </>
          ) : (
            <section className="guided-step-panel workflow-stage-panel active" aria-labelledby="download-title">
              <div className="guided-step-number">5</div>
              <div className="guided-step-content">
                <div className={`package-ready-card package-state-${workflow.package.localState.toLowerCase()}`}>
                  <div className="package-ready-content">
                    <p className="eyebrow">
                      {workflow.package.localState === 'Available'
                        ? 'Package ready to download'
                        : workflow.package.localState === 'Deleted'
                          ? 'Local package deleted'
                          : 'Package requires review'}
                    </p>
                    <h3 id="download-title">{workflow.package.fileName}</h3>
                    <p>
                      {workflow.package.localState === 'Available'
                        ? 'Download the encrypted package, verify its SHA-256 after transfer, then upload it to the target MEM Migration Workspace.'
                        : workflow.package.localState === 'Deleted'
                          ? 'The local encrypted package and package reports were deleted. The source capture, assessment history and migration journal remain available.'
                          : 'The durable package evidence remains, but the local encrypted package is incomplete, missing or unsafe to serve. Review the source artifacts before continuing.'}
                    </p>

                    {workflow.actions.canDownloadPackage || workflow.actions.canDownloadReport || workflow.actions.canDeletePackage ? (
                      <div className="package-download-actions">
                        {workflow.actions.canDownloadPackage ? (
                          <a className="primary-button action-link" href={workflowPackageDownloadUrl(workflow.workflowId)}>
                            Download encrypted package
                          </a>
                        ) : null}
                        {workflow.actions.canDownloadReport ? (
                          <a className="secondary-button action-link" href={workflowPackageReportUrl(workflow.workflowId)}>
                            Download package report
                          </a>
                        ) : null}
                        {workflow.actions.canDeletePackage ? (
                          <button type="button" className="danger-outline" onClick={() => setConfirmDeletePackage(true)} disabled={busy !== null}>
                            Delete local encrypted package
                          </button>
                        ) : null}
                      </div>
                    ) : null}

                    {workflow.package.localState === 'Deleted' && onStartNew ? (
                      <div className="deleted-package-next-step">
                        <strong>A fresh target request is required before creating another package.</strong>
                        <button type="button" className="primary-button" onClick={onStartNew}>Start a new migration</button>
                      </div>
                    ) : null}

                    {confirmDeletePackage ? (
                      <div className="package-delete-confirmation" role="alert">
                        <strong>Delete the local encrypted package?</strong>
                        <p>
                          This deletes the encrypted <code>.zip.age</code> file and its local package reports. It does not delete the plaintext source capture, source assessment, migration journal, Matrix data or the old MEM server.
                        </p>
                        <div>
                          <button type="button" className="secondary-button" onClick={() => setConfirmDeletePackage(false)} disabled={busy === 'delete-package'}>
                            Keep package
                          </button>
                          <button type="button" className="danger-button" onClick={deleteLocalPackage} disabled={busy === 'delete-package'}>
                            {busy === 'delete-package' ? 'Deleting…' : 'Delete package and reports'}
                          </button>
                        </div>
                      </div>
                    ) : null}
                  </div>
                  <dl>
                    <div><dt>Local state</dt><dd>{workflow.package.localState}</dd></div>
                    <div><dt>Size</dt><dd>{formatBytes(workflow.package.sizeBytes)}</dd></div>
                    <div><dt>SHA-256</dt><dd><code>{workflow.package.sha256}</code></dd></div>
                    <div><dt>Capture</dt><dd>{workflow.package.captureKind}</dd></div>
                    <div><dt>Stacks in archive</dt><dd>{workflow.package.stackCount}</dd></div>
                  </dl>
                </div>
              </div>
            </section>
          )}
          <details className="technical-workflow-details">
            <summary>Technical workflow details</summary>
            <dl>
              <div><dt>Workflow ID</dt><dd><code>{workflow.workflowId}</code></dd></div>
              <div><dt>Recorded stage / status</dt><dd>{workflow.stage} / {workflow.status}</dd></div>
              <div><dt>Recorded capture ID</dt><dd><code>{workflow.captureId ?? 'Not recorded'}</code></dd></div>
              <div><dt>Created (UTC)</dt><dd><code>{migrationUtcIso(workflow.createdAtUtc) ?? 'Not recorded'}</code></dd></div>
              <div><dt>Updated (UTC)</dt><dd><code>{migrationUtcIso(workflow.updatedAtUtc) ?? 'Not recorded'}</code></dd></div>
              <div><dt>Request expires (UTC)</dt><dd><code>{migrationUtcIso(workflow.request.expiresAtUtc) ?? 'Not recorded'}</code></dd></div>
              <div><dt>Encryption readiness acknowledged (UTC)</dt><dd><code>{migrationUtcIso(workflow.encryptionReadinessAcknowledgedAtUtc) ?? 'Not recorded'}</code></dd></div>
              <div><dt>Workflow completed (UTC)</dt><dd><code>{migrationUtcIso(workflow.completedAtUtc) ?? 'Not recorded'}</code></dd></div>
              {workflow.package ? <div><dt>Package completed (UTC)</dt><dd><code>{migrationUtcIso(workflow.package.completedAtUtc) ?? 'Not recorded'}</code></dd></div> : null}
            </dl>
          </details>
        </>
      ) : null}
    </section>
  )
}
