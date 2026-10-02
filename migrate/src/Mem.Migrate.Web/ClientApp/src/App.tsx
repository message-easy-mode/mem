import { ChangeEvent, FormEvent, SyntheticEvent, useEffect, useMemo, useRef, useState } from 'react'
import {
  type AccessSession,
  type HostStatus,
  type SourceAssessment,
  type SourceAssessmentEnvelope,
  type SourcePreflight,
  type SourceStack,
  type SourceWorkflow,
  type SourceWorkflowOverview,
  login,
  logout,
  readHostStatus,
  readLatestAssessment,
  readPreflight,
  readSession,
  readWorkflowOverview,
  runAssessment,
  selectSourceStack,
  subscribeToSessionExpiry,
} from './api'
import { MigrationPackageWorkspace } from './MigrationPackageWorkspace'
import { MigrationTimestamp } from './MigrationTimestamp'
import { migrationUtcIso } from './migration-time'
import { workflowActivity } from './workflow-presentation'
import './styles.css'

type LoadState = 'loading' | 'ready' | 'failed'
type WorkspaceSelection =
  | { kind: 'new' }
  | { kind: 'workflow'; workflowId: string }
  | null

function LogoMark() {
  return (
    <div className="logo-mark" aria-hidden="true">
      <span>M</span>
    </div>
  )
}

function humanize(value: string) {
  return value
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replaceAll('-', ' ')
    .replaceAll('_', ' ')
    .replace(/^./, (character) => character.toUpperCase())
}

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

function formatSessionMinutes(totalMinutes: number) {
  if (!Number.isFinite(totalMinutes) || totalMinutes <= 0) return 'unknown'
  const minutes = Math.round(totalMinutes)
  const hours = Math.floor(minutes / 60)
  const remainder = minutes % 60
  if (hours === 0) return `${remainder}m`
  if (remainder === 0) return `${hours}h`
  return `${hours}h ${remainder}m`
}

function workflowTitle(workflow: SourceWorkflow) {
  return workflowActivity(workflow).label
}

function AccessScreen({
  onAuthenticated,
  sessionExpired,
}: {
  onAuthenticated: (session: AccessSession) => void
  sessionExpired: boolean
}) {
  const [accessCode, setAccessCode] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSubmitting(true)
    setError(null)
    try {
      const session = await login(accessCode)
      setAccessCode('')
      onAuthenticated(session)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'The access code was not accepted.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <main className="access-layout">
      <section className="access-card" aria-labelledby="access-title">
        <div className="brand-lockup">
          <LogoMark />
          <div>
            <p className="eyebrow">MEM Migrate</p>
            <h1 id="access-title">Source Assistant</h1>
          </div>
        </div>

        <p className="lead">
          A temporary management surface for preparing an encrypted migration package on the old MEM server.
        </p>

        {sessionExpired ? (
          <div className="security-note session-expired-note" role="status">
            <strong>Session expired</strong>
            <span>Sign in again to continue. Your durable migration workflow and completed source work are preserved.</span>
          </div>
        ) : null}

        <div className="security-note">
          <strong>Privileged local tool</strong>
          <span>Use the access code printed in the terminal that started this process.</span>
        </div>

        <form onSubmit={submit} className="access-form">
          <label htmlFor="access-code">Access code</label>
          <input
            id="access-code"
            name="access-code"
            value={accessCode}
            onChange={(event: ChangeEvent<HTMLInputElement>) => setAccessCode(event.target.value)}
            autoComplete="one-time-code"
            autoCapitalize="characters"
            spellCheck={false}
            placeholder="XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX"
            maxLength={40}
            required
          />
          {error ? <p className="error-message" role="alert">{error}</p> : null}
          <button type="submit" disabled={submitting || accessCode.trim().length === 0}>
            {submitting ? 'Signing in…' : 'Open Source Assistant'}
          </button>
        </form>

        <p className="access-footnote">
          The safe default is loopback access through an SSH tunnel. Public internet exposure is unsupported.
        </p>
      </section>
    </main>
  )
}

function CheckList({ preflight }: { preflight: SourcePreflight }) {
  return (
    <div className="check-list">
      {preflight.checks.map((check) => (
        <div className="check-row" key={check.code}>
          <span className={`check-dot ${check.status}`} aria-hidden="true" />
          <div>
            <strong>{check.summary}</strong>
            <small>{check.detail ?? humanize(check.status)}</small>
          </div>
        </div>
      ))}
    </div>
  )
}

function StackCards({
  assessment,
  selectedSourceStackId,
  selectingStackId,
  onChoose,
}: {
  assessment: SourceAssessment
  selectedSourceStackId: string | null
  selectingStackId: string | null
  onChoose: (sourceStackId: string) => void
}) {
  if (assessment.stacks.length === 0) {
    return <p className="empty-state">No stack records were detected in the supported source database.</p>
  }

  return (
    <div className="stack-grid">
      {assessment.stacks.map((stack) => {
        const selected = stack.sourceStackId === selectedSourceStackId
        return (
          <article className={`stack-card ${selected ? 'selected' : ''}`} key={stack.sourceStackId}>
            <div className="stack-card-heading">
              <div>
                <span className="stack-slug">{stack.slug}</span>
                <h3>{stack.name}</h3>
              </div>
              {selected ? <span className="selected-badge">Selected</span> : null}
            </div>
            <dl>
              <div><dt>Matrix server</dt><dd>{stack.matrixServerName ?? stack.matrixPublicHost ?? 'Not detected'}</dd></div>
              <div><dt>Element</dt><dd>{stack.elementPublicHost ?? 'Not detected'}</dd></div>
              <div><dt>Media</dt><dd>{formatBytes(stack.mediaBytes)}</dd></div>
              <div><dt>Source files</dt><dd>{stack.sourceFilesReady ? 'Ready' : 'Review required'}</dd></div>
            </dl>
            <div className="stack-checks" aria-label={`${stack.name} source file checks`}>
              <span className={stack.homeserverConfigurationReady ? 'ok' : 'missing'}>Configuration</span>
              <span className={stack.matrixDatabaseReady ? 'ok' : 'missing'}>Database</span>
              <span className={stack.signingKeyReady ? 'ok' : 'missing'}>Signing key</span>
              <span className={stack.mediaStoreReady ? 'ok' : 'missing'}>Media</span>
            </div>
            <button
              className={selected ? 'secondary-button' : 'primary-button'}
              type="button"
              disabled={selected || selectingStackId !== null}
              onClick={() => onChoose(stack.sourceStackId)}
            >
              {selected ? 'Selected' : selectingStackId === stack.sourceStackId ? 'Selecting…' : 'Select stack'}
            </button>
          </article>
        )
      })}
    </div>
  )
}

function SourceServerSummary({
  stack,
  assessment,
  overview,
  overviewLoading,
  onChangeStack,
  onViewAssessment,
  onViewLatestPrevious,
}: {
  stack: SourceStack
  assessment: SourceAssessment
  overview: SourceWorkflowOverview | null
  overviewLoading: boolean
  onChangeStack: () => void
  onViewAssessment: () => void
  onViewLatestPrevious: () => void
}) {
  const latestPrevious = overview?.previousWorkflows[0] ?? null
  const current = overview?.currentWorkflow ?? null

  return (
    <section className="source-ready-card" aria-labelledby="source-ready-title">
      <div className="source-ready-heading">
        <div className="ready-icon" aria-hidden="true">✓</div>
        <div>
          <p className="eyebrow">Source server ready</p>
          <h2 id="source-ready-title">{stack.name}</h2>
          <p>The selected MEM 0.1.0 stack is ready for migration package preparation.</p>
        </div>
        <span className="ready-badge">Ready</span>
      </div>

      <dl className="source-ready-grid">
        <div><dt>Selected stack</dt><dd>{stack.slug}</dd></div>
        <div><dt>Matrix server</dt><dd>{stack.matrixServerName ?? stack.matrixPublicHost ?? 'Not detected'}</dd></div>
        <div><dt>Element</dt><dd>{stack.elementPublicHost ?? 'Not detected'}</dd></div>
        <div><dt>Media size</dt><dd>{formatBytes(stack.mediaBytes)}</dd></div>
        <div><dt>Source state</dt><dd>{humanize(assessment.classification)}</dd></div>
        <div>
          <dt>{current ? 'Current migration' : 'Latest previous migration'}</dt>
          <dd>
            {overviewLoading
              ? 'Loading…'
              : current
                ? workflowTitle(current)
                : latestPrevious
                  ? workflowTitle(latestPrevious)
                  : 'No previous migration'}
          </dd>
        </div>
      </dl>

      <div className="source-ready-actions">
        <button type="button" className="secondary-button" onClick={onChangeStack}>Change stack</button>
        <button type="button" className="text-button" onClick={onViewAssessment}>View assessment summary</button>
        {latestPrevious ? (
          <button type="button" className="text-button" onClick={onViewLatestPrevious}>View previous migration</button>
        ) : null}
      </div>
    </section>
  )
}

function MigrationEntryPanel({
  overview,
  loading,
  onStartNew,
  onContinue,
  currentWorkflowOpen,
}: {
  overview: SourceWorkflowOverview | null
  loading: boolean
  onStartNew: () => void
  onContinue: () => void
  currentWorkflowOpen: boolean
}) {
  const current = overview?.currentWorkflow ?? null
  const operationRunning = overview?.operationRunning === true
  const canStart = overview?.canStartNewWorkflow === true

  return (
    <section className={`migration-entry-card ${operationRunning ? 'running' : ''}`} aria-labelledby="migration-entry-title">
      <div>
        <p className="eyebrow">Migration package</p>
        <h2 id="migration-entry-title">
          {operationRunning ? 'A migration operation is running' : currentWorkflowOpen ? 'Migration workspace open' : 'Ready to prepare a migration package'}
        </h2>
        <p>
          {currentWorkflowOpen
            ? 'Continue in the workspace below. Closing it returns to this overview without deleting recorded work.'
            : operationRunning
            ? 'Continue the active workflow to see durable progress. Closing or refreshing the browser does not stop the source operation.'
            : 'Start with a fresh Migration Request from the new MEM server, or continue the current resumable workflow.'}
        </p>
      </div>

      <div className="migration-entry-actions">
        {current && !currentWorkflowOpen ? (
          <button
            type="button"
            className={operationRunning ? 'primary-button large-action' : 'secondary-button large-action'}
            onClick={onContinue}
            disabled={loading}
          >
            Continue current migration
          </button>
        ) : null}
        <button
          type="button"
          className={!operationRunning ? 'primary-button large-action' : 'secondary-button large-action'}
          onClick={onStartNew}
          disabled={loading || !canStart}
        >
          Start a new migration
        </button>
      </div>

      {!canStart && overview?.startNewBlockedReason ? (
        <p className="entry-blocked-note">{overview.startNewBlockedReason}</p>
      ) : null}
    </section>
  )
}

function PreviousMigrationsDisclosure({
  workflows,
  onOpen,
}: {
  workflows: SourceWorkflow[]
  onOpen: (workflowId: string) => void
}) {
  return (
    <details className="workspace-disclosure">
      <summary>
        <span>
          <strong>Previous migrations</strong>
          <small>{workflows.length === 0 ? 'No previous workflows for this stack' : `${workflows.length} recent workflow${workflows.length === 1 ? '' : 's'}`}</small>
        </span>
      </summary>
      <div className="disclosure-content">
        {workflows.length === 0 ? (
          <p className="empty-state">No previous migration workflow has been recorded for this stack.</p>
        ) : (
          <div className="history-list">
            {workflows.map((workflow) => (
              <article className="history-row" key={workflow.workflowId}>
                <div className="history-status" aria-hidden="true" />
                <div>
                  <strong>{workflowTitle(workflow)}</strong>
                  <span>{humanize(workflow.status)} · {humanize(workflow.stage)}</span>
                  <small>Updated <MigrationTimestamp value={workflow.updatedAtUtc} /> · Request {workflow.request.intakeId}</small>
                </div>
                <button type="button" className="secondary-button" onClick={() => onOpen(workflow.workflowId)}>
                  View details
                </button>
              </article>
            ))}
          </div>
        )}
      </div>
    </details>
  )
}

function AssessmentDetails({
  open,
  onToggle,
  assessment,
  preflight,
  selectedSourceStackId,
  selectingStackId,
  assessing,
  canRunAssessment,
  onAssess,
  onChooseStack,
}: {
  open: boolean
  onToggle: (open: boolean) => void
  assessment: SourceAssessment
  preflight: SourcePreflight
  selectedSourceStackId: string | null
  selectingStackId: string | null
  assessing: boolean
  canRunAssessment: boolean
  onAssess: () => void
  onChooseStack: (sourceStackId: string) => void
}) {
  return (
    <details
      id="source-assessment-details"
      className="workspace-disclosure"
      open={open}
      onToggle={(event: SyntheticEvent<HTMLDetailsElement>) => onToggle(event.currentTarget.open)}
    >
      <summary>
        <span>
          <strong>Source assessment details</strong>
          <small>Host readiness, stack selection, report and findings</small>
        </span>
      </summary>
      <div className="disclosure-content assessment-disclosure-content">
        <div className="disclosure-heading-actions">
          <div>
            <p className="eyebrow">Latest durable assessment</p>
            <h3>{humanize(assessment.classification)}</h3>
          </div>
          <div>
            <button className="secondary-button" type="button" onClick={onAssess} disabled={!canRunAssessment}>
              {assessing ? 'Assessing source…' : 'Run fresh assessment'}
            </button>
            <a className="secondary-button link-button" href="/api/assessments/latest/report">Download assessment report</a>
          </div>
        </div>

        <div className="assessment-metrics">
          <div><span>Capture eligibility</span><strong>{assessment.canProceedToCapture ? 'Allowed' : 'Blocked'}</strong></div>
          <div><span>Product</span><strong>{assessment.runtime.productName ?? 'Unknown'} {assessment.runtime.productVersion ?? ''}</strong></div>
          <div><span>Docker</span><strong>{assessment.runtime.dockerAvailable ? assessment.runtime.dockerVersion ?? 'Available' : 'Unavailable'}</strong></div>
          <div><span>Findings</span><strong>{assessment.counts.blockers} blockers · {assessment.counts.warnings} warnings</strong></div>
        </div>

        <div className="subsection-block">
          <h3>Host readiness</h3>
          <CheckList preflight={preflight} />
        </div>

        <div className="subsection-block">
          <h3>Select migration stack</h3>
          <StackCards
            assessment={assessment}
            selectedSourceStackId={selectedSourceStackId}
            selectingStackId={selectingStackId}
            onChoose={onChooseStack}
          />
        </div>

        <div className="scope-disclosure" aria-label="Package scope disclosure">
          <strong>Current package scope: one selected MEM 0.1.0 stack</strong>
          <p>
            Choose the stack intended for migration. New captures and encrypted packages contain only that stack's Matrix data, configuration, signing identity, media, Element configuration, and minimal stack-bound provenance.
          </p>
        </div>

        <div className="subsection-block">
          <h3>Warnings and blockers</h3>
          {assessment.findings.length === 0 ? (
            <p className="empty-state">No assessment findings were recorded.</p>
          ) : (
            <div className="finding-list">
              {assessment.findings.map((finding) => (
                <article className={`finding ${finding.severity.toLowerCase()}`} key={finding.code}>
                  <div>
                    <span>{finding.severity}</span>
                    <code>{finding.code}</code>
                  </div>
                  <p>{finding.message}</p>
                  {finding.remediation ? <small>{finding.remediation}</small> : null}
                </article>
              ))}
            </div>
          )}
        </div>
      </div>
    </details>
  )
}

function AdvancedDiagnostics({ host, assessment }: { host: HostStatus; assessment: SourceAssessment }) {
  return (
    <details className="workspace-disclosure">
      <summary>
        <span>
          <strong>Advanced diagnostics</strong>
          <small>Listener, assessment identity and technical evidence</small>
        </span>
      </summary>
      <div className="disclosure-content diagnostic-grid">
        <div><span>Listener</span><strong>{host.listener}</strong></div>
        <div><span>Access boundary</span><strong>{host.loopbackOnly ? 'Loopback only' : 'Trusted remote bind'}</strong></div>
        <div><span>Assessment ID</span><strong>{assessment.assessmentId}</strong></div>
        <div><span>Completed</span><strong><MigrationTimestamp value={assessment.completedAtUtc} /></strong></div>
        <div><span>Completed (UTC)</span><code>{migrationUtcIso(assessment.completedAtUtc) ?? 'Not recorded'}</code></div>
        <div className="diagnostic-wide"><span>Source fingerprint</span><code>{assessment.sourceFingerprint}</code></div>
      </div>
    </details>
  )
}

function AssessmentWorkspace({ session, onSignedOut }: { session: AccessSession; onSignedOut: () => void }) {
  const [host, setHost] = useState<HostStatus | null>(null)
  const [preflight, setPreflight] = useState<SourcePreflight | null>(null)
  const [assessmentEnvelope, setAssessmentEnvelope] = useState<SourceAssessmentEnvelope | null>(null)
  const [workflowOverview, setWorkflowOverview] = useState<SourceWorkflowOverview | null>(null)
  const [loadState, setLoadState] = useState<LoadState>('loading')
  const [overviewLoading, setOverviewLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [overviewError, setOverviewError] = useState<string | null>(null)
  const [signingOut, setSigningOut] = useState(false)
  const [assessing, setAssessing] = useState(false)
  const [selectingStackId, setSelectingStackId] = useState<string | null>(null)
  const [workspaceSelection, setWorkspaceSelection] = useState<WorkspaceSelection>(null)
  const [confirmStartNew, setConfirmStartNew] = useState(false)
  const [openedWorkflowId, setOpenedWorkflowId] = useState<string | null>(null)
  const [assessmentDetailsOpen, setAssessmentDetailsOpen] = useState(false)
  const autoSelectAttemptedAssessmentId = useRef<string | null>(null)

  const assessment = assessmentEnvelope?.assessment ?? null
  const selectedSourceStackId = assessmentEnvelope?.selectedSourceStackId ?? null
  const selectedStack = useMemo(
    () => assessment?.stacks.find((stack) => stack.sourceStackId === selectedSourceStackId) ?? null,
    [assessment, selectedSourceStackId],
  )

  useEffect(() => {
    let active = true
    Promise.all([readHostStatus(), readPreflight(), readLatestAssessment()])
      .then(([hostResult, preflightResult, assessmentResult]) => {
        if (!active) return
        setHost(hostResult)
        setPreflight(preflightResult)
        setAssessmentEnvelope(assessmentResult)
        setLoadState('ready')
      })
      .catch((caught) => {
        if (!active) return
        setError(caught instanceof Error ? caught.message : 'The Source Assistant status could not be loaded.')
        setLoadState('failed')
      })

    return () => { active = false }
  }, [])

  useEffect(() => {
    if (!assessment || selectedSourceStackId || selectingStackId || !session.csrfToken) return
    if (autoSelectAttemptedAssessmentId.current === assessment.assessmentId) return

    const readyStacks = assessment.stacks.filter((stack) => stack.sourceFilesReady)
    if (!assessment.canProceedToCapture || readyStacks.length !== 1) return

    autoSelectAttemptedAssessmentId.current = assessment.assessmentId
    void chooseStack(readyStacks[0].sourceStackId)
  }, [assessment, selectedSourceStackId, selectingStackId, session.csrfToken])

  useEffect(() => {
    setWorkspaceSelection(null)
    setOpenedWorkflowId(null)
    setConfirmStartNew(false)
    setWorkflowOverview(null)
    setOverviewError(null)
    if (!selectedSourceStackId) return
    void refreshWorkflowOverview(selectedSourceStackId)
  }, [selectedSourceStackId])

  async function refreshWorkflowOverview(sourceStackId = selectedSourceStackId) {
    if (!sourceStackId) return
    setOverviewLoading(true)
    setOverviewError(null)
    try {
      setWorkflowOverview(await readWorkflowOverview(sourceStackId))
    } catch (caught) {
      setOverviewError(caught instanceof Error ? caught.message : 'Migration workflow history could not be loaded.')
    } finally {
      setOverviewLoading(false)
    }
  }

  async function signOut() {
    if (!session.csrfToken) return
    setSigningOut(true)
    setError(null)
    try {
      await logout(session.csrfToken)
      onSignedOut()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'The current session could not be ended.')
      setSigningOut(false)
    }
  }

  async function assessSource() {
    if (!session.csrfToken) return
    setAssessing(true)
    setError(null)
    try {
      const result = await runAssessment(session.csrfToken)
      autoSelectAttemptedAssessmentId.current = null
      setAssessmentEnvelope(result)
      setPreflight(await readPreflight())
      setAssessmentDetailsOpen(false)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'The source assessment failed.')
    } finally {
      setAssessing(false)
    }
  }

  async function chooseStack(sourceStackId: string) {
    if (!session.csrfToken) return
    setSelectingStackId(sourceStackId)
    setError(null)
    try {
      setAssessmentEnvelope(await selectSourceStack(sourceStackId, session.csrfToken))
      setAssessmentDetailsOpen(false)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'The source stack could not be selected.')
    } finally {
      setSelectingStackId(null)
    }
  }

  function requestStartNew() {
    if (!workflowOverview?.canStartNewWorkflow) return
    if (workflowOverview.currentWorkflow) {
      setConfirmStartNew(true)
      return
    }
    setOpenedWorkflowId(null)
    setWorkspaceSelection({ kind: 'new' })
  }

  function confirmNewWorkflow() {
    setConfirmStartNew(false)
    setOpenedWorkflowId(null)
    setWorkspaceSelection({ kind: 'new' })
  }

  function openWorkflow(workflowId: string) {
    setConfirmStartNew(false)
    setOpenedWorkflowId(workflowId)
    setWorkspaceSelection({ kind: 'workflow', workflowId })
  }

  const canRunAssessment = preflight?.canRunAssessment === true && !assessing
  const statusLabel = assessment
    ? assessment.canProceedToCapture ? 'Source supported' : 'Review required'
    : preflight?.status === 'blocked' ? 'Preflight blocked' : 'Not assessed'

  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="brand-lockup compact">
          <LogoMark />
          <div>
            <p className="eyebrow">MEM Migrate</p>
            <p className="product-name">Source Assistant</p>
          </div>
        </div>
        <div className="header-actions">
          {host ? (
            <span className="session-policy-note" title="Source Assistant session timeout policy">
              Session: {formatSessionMinutes(host.sessionIdleMinutes)} idle · {formatSessionMinutes(host.sessionAbsoluteMinutes)} maximum
            </span>
          ) : null}
          <a className="header-help-link" href="#before-you-begin">Help</a>
          <button className="secondary-button" type="button" onClick={signOut} disabled={signingOut}>
            {signingOut ? 'Signing out…' : 'Sign out'}
          </button>
        </div>
      </header>

      <main className="workspace">
        <div className="page-heading compact-heading">
          <div>
            <h1>Prepare the source migration package</h1>
            <p>Move from source readiness to an encrypted package through one guided workspace.</p>
          </div>
          <div className="source-status-cluster">
            <span className={`status-pill ${assessment && !assessment.canProceedToCapture ? 'attention' : ''}`}>{statusLabel}</span>
            <small>{assessment ? <>Last refreshed <MigrationTimestamp value={assessment.completedAtUtc} /></> : humanize(preflight?.status ?? 'loading')}</small>
          </div>
        </div>

        {loadState === 'loading' ? <div className="panel">Loading protected source status…</div> : null}
        {error ? <div className="panel error-panel" role="alert">{error}</div> : null}

        {loadState === 'ready' && host && preflight && assessmentEnvelope ? (
          <>
            {!assessment ? (
              <>
                <section className="first-assessment-card">
                  <div>
                    <p className="eyebrow">Read-only source inspection</p>
                    <h2>Run the first source assessment</h2>
                    <p>
                      MEM Migrate will inspect Docker, the MEM 0.1.0 application database and Matrix stack files. It does not stop containers or change the source service.
                    </p>
                  </div>
                  <button type="button" className="primary-button large-action" onClick={assessSource} disabled={!canRunAssessment}>
                    {assessing ? 'Assessing source…' : 'Run source assessment'}
                  </button>
                </section>

                <details className="workspace-disclosure">
                  <summary>
                    <span><strong>Host readiness</strong><small>Protected preflight checks</small></span>
                  </summary>
                  <div className="disclosure-content"><CheckList preflight={preflight} /></div>
                </details>
              </>
            ) : (
              <>
                {!selectedStack ? (
                  <section className="stack-choice-panel">
                    <div>
                      <p className="eyebrow">Choose the source stack</p>
                      <h2>Select the stack to migrate</h2>
                      <p>More than one stack may be present. Select the intended stack before starting a migration.</p>
                    </div>
                    <StackCards
                      assessment={assessment}
                      selectedSourceStackId={selectedSourceStackId}
                      selectingStackId={selectingStackId}
                      onChoose={chooseStack}
                    />
                  </section>
                ) : (
                  <>
                    <SourceServerSummary
                      stack={selectedStack}
                      assessment={assessment}
                      overview={workflowOverview}
                      overviewLoading={overviewLoading}
                      onChangeStack={() => setAssessmentDetailsOpen(true)}
                      onViewAssessment={() => setAssessmentDetailsOpen(true)}
                      onViewLatestPrevious={() => {
                        const latest = workflowOverview?.previousWorkflows[0]
                        if (latest) openWorkflow(latest.workflowId)
                      }}
                    />

                    {overviewError ? <div className="panel error-panel" role="alert">{overviewError}</div> : null}

                    <MigrationEntryPanel
                      overview={workflowOverview}
                      loading={overviewLoading}
                      currentWorkflowOpen={workspaceSelection !== null && openedWorkflowId !== null && openedWorkflowId === workflowOverview?.currentWorkflow?.workflowId}
                      onStartNew={requestStartNew}
                      onContinue={() => {
                        const current = workflowOverview?.currentWorkflow
                        if (current) openWorkflow(current.workflowId)
                      }}
                    />

                    {confirmStartNew && workflowOverview?.currentWorkflow ? (
                      <section className="start-new-confirmation" role="alert">
                        <div>
                          <strong>Start a separate migration workflow?</strong>
                          <p>
                            The current workflow will not be deleted or changed. It will remain available in migration history for this source stack.
                          </p>
                        </div>
                        <div>
                          <button type="button" className="secondary-button" onClick={() => setConfirmStartNew(false)}>Keep current workflow</button>
                          <button type="button" className="primary-button" onClick={confirmNewWorkflow}>Start new migration</button>
                        </div>
                      </section>
                    ) : null}

                    {workspaceSelection && session.csrfToken ? (
                      <MigrationPackageWorkspace
                        key={workspaceSelection.kind === 'new' ? `new-${selectedStack.sourceStackId}` : workspaceSelection.workflowId}
                        csrfToken={session.csrfToken}
                        selectedSourceStackId={selectedStack.sourceStackId}
                        sourceStackName={selectedStack.name}
                        mode={workspaceSelection.kind}
                        workflowId={workspaceSelection.kind === 'workflow' ? workspaceSelection.workflowId : undefined}
                        onWorkflowChanged={(updated) => {
                          setOpenedWorkflowId(updated.workflowId)
                          void refreshWorkflowOverview(selectedStack.sourceStackId)
                        }}
                        onStartNew={requestStartNew}
                        onClose={() => { setWorkspaceSelection(null); setOpenedWorkflowId(null) }}
                      />
                    ) : null}

                    <PreviousMigrationsDisclosure
                      workflows={workflowOverview?.previousWorkflows ?? []}
                      onOpen={openWorkflow}
                    />
                  </>
                )}

                <AssessmentDetails
                  open={assessmentDetailsOpen}
                  onToggle={setAssessmentDetailsOpen}
                  assessment={assessment}
                  preflight={preflight}
                  selectedSourceStackId={selectedSourceStackId}
                  selectingStackId={selectingStackId}
                  assessing={assessing}
                  canRunAssessment={canRunAssessment}
                  onAssess={assessSource}
                  onChooseStack={chooseStack}
                />

                <AdvancedDiagnostics host={host} assessment={assessment} />

                <section id="before-you-begin" className="before-you-begin">
                  <div className="shield-icon" aria-hidden="true">◇</div>
                  <div>
                    <strong>Before you begin</strong>
                    <p>
                      The migration package is encrypted for the new MEM server. The private decryption identity never enters this Source Assistant. Ask users to preserve their trusted Element sessions and encryption recovery options before capture.
                    </p>
                  </div>
                </section>
              </>
            )}
          </>
        ) : null}
      </main>

      <footer className="app-footer">
        <span>MEM Migrate v{__MEM_MIGRATE_VERSION__}</span>
        <span>Source Assistant</span>
      </footer>
    </div>
  )
}

export default function App() {
  const [session, setSession] = useState<AccessSession | null>(null)
  const [loading, setLoading] = useState(true)
  const [reauthenticationRequired, setReauthenticationRequired] = useState(
    () => new URLSearchParams(window.location.search).get('session') === 'expired',
  )

  useEffect(() => {
    return subscribeToSessionExpiry(() => {
      setSession(null)
      setReauthenticationRequired(true)
    })
  }, [])

  useEffect(() => {
    let active = true
    readSession()
      .then((result) => {
        if (active) setSession(result.authenticated ? result : null)
      })
      .catch(() => {
        if (active) setSession(null)
      })
      .finally(() => {
        if (active) setLoading(false)
      })

    return () => { active = false }
  }, [])

  function acceptSession(authenticatedSession: AccessSession) {
    setSession(authenticatedSession)
    setReauthenticationRequired(false)
    if (new URLSearchParams(window.location.search).get('session') === 'expired') {
      window.history.replaceState(null, '', window.location.pathname)
    }
  }

  function signedOut() {
    setSession(null)
    setReauthenticationRequired(false)
  }

  if (loading) {
    return <main className="loading-screen" aria-label="Loading Source Assistant">Loading Source Assistant…</main>
  }

  if (!session) {
    return (
      <AccessScreen
        onAuthenticated={acceptSession}
        sessionExpired={reauthenticationRequired}
      />
    )
  }

  return <AssessmentWorkspace session={session} onSignedOut={signedOut} />
}
