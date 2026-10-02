import type { InstallationProgressSnapshot } from "./installation-run.types"

export type WorkflowStepStatus =
  | "Pending"
  | "Running"
  | "Succeeded"
  | "WaitingForUser"
  | "Failed"

export type InstallationStatus =
  | "Draft"
  | "Ready"
  | "Running"
  | "WaitingForUser"
  | "Succeeded"
  | "Failed"

export type StatusTone =
  | "muted"
  | "info"
  | "success"
  | "warning"
  | "danger"

export function getStepStatusLabel(status: WorkflowStepStatus): string {
  switch (status) {
    case "Pending":
      return "Pending"
    case "Running":
      return "In progress"
    case "Succeeded":
      return "Completed"
    case "WaitingForUser":
      return "Action required"
    case "Failed":
      return "Failed"
    default:
      return status
  }
}

export function getStepStatusTone(status: WorkflowStepStatus): StatusTone {
  switch (status) {
    case "Pending":
      return "muted"
    case "Running":
      return "info"
    case "Succeeded":
      return "success"
    case "WaitingForUser":
      return "warning"
    case "Failed":
      return "danger"
    default:
      return "muted"
  }
}

export function getInstallationStatusLabel(status: InstallationStatus): string {
  switch (status) {
    case "Draft":
      return "Draft"
    case "Ready":
      return "Ready"
    case "Running":
      return "Installing"
    case "WaitingForUser":
      return "Action required"
    case "Succeeded":
      return "Completed"
    case "Failed":
      return "Failed"
    default:
      return status
  }
}

export function getInstallationStatusTone(status: InstallationStatus): StatusTone {
  switch (status) {
    case "Draft":
      return "muted"
    case "Ready":
      return "info"
    case "Running":
      return "info"
    case "WaitingForUser":
      return "warning"
    case "Succeeded":
      return "success"
    case "Failed":
      return "danger"
    default:
      return "muted"
  }
}

export interface WorkflowStep {
  order: number
  name: string
  title: string
  kind: string
  notes?: string | null
  category?: string | null
  tags: string[]
  requiresHumanAction: boolean
  humanActionPrompt?: string | null
  isCheckpoint: boolean
  status: WorkflowStepStatus
  message?: string | null
  errorMessage?: string | null
  attemptCount: number
  startedAtUtc?: string | null
  completedAtUtc?: string | null
  progress?: InstallationProgressSnapshot | null
}

export function getCurrentWorkflowStep(
  steps: WorkflowStep[],
): WorkflowStep | null {
  const running = steps.find((x) => x.status === "Running")
  if (running) return running

  const waiting = steps.find((x) => x.status === "WaitingForUser")
  if (waiting) return waiting

  const failed = steps.find((x) => x.status === "Failed")
  if (failed) return failed

  return steps.find((x) => x.status === "Pending") ?? null
}

