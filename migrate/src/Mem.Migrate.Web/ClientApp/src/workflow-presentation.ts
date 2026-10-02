import type { SourceWorkflow } from './api'

// Presentation only. Mutation eligibility still comes from workflow.actions and
// is rechecked on the server. A running capture already has a reserved ID; that
// does not mean the capture has completed or is selected for packaging.
export function selectedWorkflowCaptureId(workflow: SourceWorkflow | null): string | null {
  if (!workflow?.captureId) return null
  if (workflow.package) return workflow.captureId
  return ['captureselected', 'captureready', 'packaging']
    .includes(workflow.stage.toLowerCase()) ? workflow.captureId : null
}

export function workflowStep(workflow: SourceWorkflow | null, hasRequestText: boolean): number {
  if (!workflow) return hasRequestText ? 2 : 1
  const stage = workflow.stage.toLowerCase()
  if (workflow.package || stage === 'readyfordownload' || stage === 'packagedeleted') return 5
  if (stage === 'packaging' || selectedWorkflowCaptureId(workflow)) return 4
  return 3
}

export function workflowActivity(workflow: SourceWorkflow): { label: string; detail: string } {
  const status = workflow.status.toLowerCase()
  const stage = workflow.stage.toLowerCase()
  if (status === 'running') {
    return {
      label: stage === 'capturing' ? 'Creating source capture' : stage === 'packaging' ? 'Encrypting selected capture' : 'Source operation running',
      detail: 'The server owns this operation. Closing or refreshing the browser does not cancel it.',
    }
  }
  // Do not turn a retained stage or ID into a success claim after a failure.
  if (status === 'failed' || stage === 'recoveryrequired') {
    return { label: 'Workflow needs review', detail: 'Review the recorded problem and the available recovery actions below.' }
  }
  if (status === 'cancelled') {
    return { label: 'Operation cancelled', detail: 'Review retained evidence and the actions the server makes available.' }
  }
  if (status !== 'pending' && status !== 'completed') {
    return { label: 'Review workflow details', detail: 'Review the recorded state and the actions the server makes available.' }
  }
  if (workflow.package) {
    if (workflow.package.localState.toLowerCase() === 'deleted') {
      return { label: 'Package removed locally', detail: 'The source capture and workflow history remain recorded.' }
    }
    if (workflow.package.localState.toLowerCase() === 'available' && workflow.actions.canDownloadPackage) {
      return { label: 'Encrypted package ready', detail: 'Download the package, then upload it to the target MEM server.' }
    }
    return { label: 'Local package needs review', detail: 'Review the recorded package state before attempting a transfer.' }
  }
  if (workflow.actions.blockedReason) {
    return { label: 'Preparation paused', detail: workflow.actions.blockedReason }
  }
  if (selectedWorkflowCaptureId(workflow)) {
    return workflow.actions.canCreatePackage
      ? { label: 'Ready to create package', detail: 'Create the encrypted package from the selected capture.' }
      : { label: 'Capture selected', detail: 'Package creation is not currently offered by the server.' }
  }
  if (stage === 'requestvalidated') {
    return { label: 'Choose a source capture', detail: 'Create a fresh capture or explicitly select retained evidence.' }
  }
  return { label: 'Review workflow details', detail: 'Review the recorded state and the actions the server makes available.' }
}
