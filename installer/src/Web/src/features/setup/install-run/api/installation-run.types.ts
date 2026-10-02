export type InstallationProgressPhaseSnapshot = {
  code: string
  status: string
  safeSummary: string
  startedAtUtc: string | null
  completedAtUtc: string | null
}

export type InstallationProgressSnapshot = {
  schemaVersion: number
  installationId: string
  stepId: string
  stepSequence: number
  stepName: string
  attemptNumber: number
  stepStatus: string
  phaseCode: string | null
  phaseStatus: string | null
  safeSummary: string
  stepStartedAtUtc: string
  lastActivityAtUtc: string
  phases: InstallationProgressPhaseSnapshot[]
}

export type InstallationResponse = {
  id: string
  status: string
  configJson: string | null
  frozenConfigJson: string | null
  lastError: string | null
  createdAtUtc: string
  updatedAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
}

export type InstallationStepExecutionResponse = {
  id: string
  stepName: string
  sequence: number
  status: string
  message: string | null
  errorMessage: string | null
  attemptCount: number
  startedAtUtc: string | null
  completedAtUtc: string | null
  humanActionPrompt?: string | null
  progress?: InstallationProgressSnapshot | null
}

export type RunInstallationResponse = {
  installationId: string
  accepted: boolean
  status: string
  message: string
}