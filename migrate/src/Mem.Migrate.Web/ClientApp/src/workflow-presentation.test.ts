import { describe, expect, it } from 'vitest'
import type { SourceWorkflow } from './api'
import { selectedWorkflowCaptureId, workflowActivity, workflowStep } from './workflow-presentation'

function workflow(overrides: Partial<SourceWorkflow> = {}): SourceWorkflow {
  return {
    schemaVersion: 1, workflowId: 'workflow-01', status: 'Pending', stage: 'RequestValidated',
    assessmentId: 'assessment-01', selectedSourceStackId: 'source-01',
    request: { intakeId: 'intake-01', packageRevisionId: null, requestKind: 'preview',
      recipientFingerprint: 'fingerprint', expiresAtUtc: '2026-09-23T04:44:49Z', targetControlPlaneVersion: '0.2.0' },
    encryptionReadinessAcknowledgedAtUtc: '2026-09-22T04:40:00Z', captureId: null, package: null,
    createdAtUtc: '2026-09-22T04:44:49Z', updatedAtUtc: '2026-09-22T04:44:49Z', completedAtUtc: null,
    failureCode: null, failureSummary: null,
    actions: { canCreatePreviewCapture: true, canSelectCapture: true, canCreatePackage: false,
      canCancel: false, canDownloadPackage: false, canDownloadReport: false, canDeletePackage: false, blockedReason: null },
    ...overrides,
  }
}

function packageWorkflow(localState = 'Available'): SourceWorkflow {
  const initial = workflow()
  return workflow({ status: 'Completed', stage: localState === 'Deleted' ? 'PackageDeleted' : 'ReadyForDownload',
    captureId: 'capture-01', package: { fileName: 'capture.zip.age', sizeBytes: 8192, sha256: 'recorded-sha',
      completedAtUtc: '2026-09-22T05:00:00Z', captureKind: 'preview', sourceFrozen: false,
      rehearsalOnly: true, stackCount: 1, localState, packageFileAvailable: localState === 'Available',
      reportAvailable: localState === 'Available' },
    actions: { ...initial.actions, canCreatePreviewCapture: false, canSelectCapture: false,
      canDownloadPackage: localState === 'Available', canDownloadReport: localState === 'Available',
      canDeletePackage: localState === 'Available' },
  })
}

describe('Source workflow presentation without action reconstruction', () => {
  it.each([[false, 1], [true, 2]] as const)('shows the unsubmitted request step (%s)', (hasText, expected) => {
    expect(workflowStep(null, hasText)).toBe(expected)
    expect(selectedWorkflowCaptureId(null)).toBeNull()
  })

  it.each([
    ['RequestValidated', 'Pending', null, 3, null],
    ['Capturing', 'Running', 'reserved-capture-id', 3, null],
    ['Capturing', 'Failed', null, 3, null],
    ['CaptureSelected', 'Pending', 'capture-01', 4, 'capture-01'],
    ['CaptureReady', 'Pending', 'capture-01', 4, 'capture-01'],
    ['captureready', 'pending', 'capture-01', 4, 'capture-01'],
    ['CaptureReady', 'Pending', null, 3, null],
    ['Packaging', 'Running', 'capture-01', 4, 'capture-01'],
    ['Packaging', 'Failed', 'capture-01', 4, 'capture-01'],
    ['RecoveryRequired', 'Failed', 'reserved-capture-id', 3, null],
    ['UnknownStage', 'Pending', 'unproven-id', 3, null],
  ] as const)('maps %s / %s to a location without inferring a completed capture', (stage, status, captureId, step, selected) => {
    const value = workflow({ stage, status, captureId })
    expect(workflowStep(value, false)).toBe(step)
    expect(selectedWorkflowCaptureId(value)).toBe(selected)
  })

  it.each(['Available', 'Missing', 'Incomplete', 'Deleted'])('keeps the recorded package at the download/review step: %s', (state) => {
    const value = packageWorkflow(state)
    expect(workflowStep(value, false)).toBe(5)
    expect(selectedWorkflowCaptureId(value)).toBe('capture-01')
    expect(workflowActivity(value).label).not.toBe('Downloaded')
  })

  it.each([
    ['Capturing', 'Running', 'Creating source capture'],
    ['Packaging', 'Running', 'Encrypting selected capture'],
    ['UnknownStage', 'Running', 'Source operation running'],
    ['Packaging', 'Failed', 'Workflow needs review'],
    ['RecoveryRequired', 'Pending', 'Workflow needs review'],
    ['Capturing', 'Cancelled', 'Operation cancelled'],
    ['RequestValidated', 'Pending', 'Choose a source capture'],
    ['UnknownStage', 'Pending', 'Review workflow details'],
    ['CaptureReady', 'UnexpectedStatus', 'Review workflow details'],
  ])('labels %s / %s without claiming success or completion', (stage, status, expected) => {
    const value = workflow({ stage, status, captureId: 'retained-or-reserved-id' })
    expect(workflowActivity(value).label).toBe(expected)
  })

  it.each(['CaptureReady', 'CaptureSelected'])('makes %s package readiness agree with the actual action', (stage) => {
    const initial = workflow({ stage, captureId: 'capture-01' })
    const ready = { ...initial, actions: { ...initial.actions, canCreatePackage: true } }
    expect(workflowActivity(ready).label).toBe('Ready to create package')
    expect(workflowActivity(initial).label).toBe('Capture selected')
    expect(ready.actions.canCreatePackage).toBe(true)
    expect(initial.actions.canCreatePackage).toBe(false)
  })

  it('retains an explicit blocker rather than claiming the recorded capture is ready for packaging', () => {
    const initial = workflow({ stage: 'CaptureReady', captureId: 'capture-01' })
    const paused = { ...initial, actions: { ...initial.actions, blockedReason: 'The target request expired.' } }
    expect(workflowActivity(paused)).toEqual({ label: 'Preparation paused', detail: 'The target request expired.' })
  })

  it.each([
    ['Available', 'Encrypted package ready'],
    ['Missing', 'Local package needs review'],
    ['Deleted', 'Package removed locally'],
  ])('distinguishes recorded package state %s', (state, expected) => {
    expect(workflowActivity(packageWorkflow(state)).label).toBe(expected)
  })

  it('does not claim an available package can be downloaded when the server withholds that action', () => {
    const value = packageWorkflow()
    value.actions.canDownloadPackage = false
    expect(workflowActivity(value).label).toBe('Local package needs review')
  })

  it('does not mask a failed operation with retained package success evidence', () => {
    expect(workflowActivity({ ...packageWorkflow(), status: 'Failed' }).label).toBe('Workflow needs review')
  })

  it('does not mutate server permissions, capture choice, request, or raw stage/status while presenting them', () => {
    for (const value of [workflow(), workflow({ stage: 'CaptureReady', captureId: 'capture-01' }), packageWorkflow(), packageWorkflow('Deleted')]) {
      const original = JSON.stringify(value)
      workflowStep(value, true)
      workflowActivity(value)
      selectedWorkflowCaptureId(value)
      expect(JSON.stringify(value)).toBe(original)
    }
  })
})
