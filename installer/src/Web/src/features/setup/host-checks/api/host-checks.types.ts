// src/features/setup/host-checks/api/host-checks.types.ts

export type HostCheckRunStatus =
  | "Pending"
  | "Running"
  | "Succeeded"
  | "SucceededWithWarnings"
  | "Failed"

export type HostCheckStatus =
  | "Pass"
  | "Warning"
  | "Fail"
  | "Skipped"
  | "Unavailable"
  | "Unknown"

export type HostCheckGroupKey =
  | "host"
  | "docker"
  | "network"
  | "ports"
  | "storage"
  | "existing-installation"

export type DiagnosticEvidence = {
  kind: "Command" | "Docker" | "Docker API" | "File" | "Network" | "Runtime" | "System"
  label: string
  value: string
  sensitive?: boolean
}

export type HostCheckResult = {
  key: string
  title: string
  status: HostCheckStatus
  blocking: boolean
  summary: string
  details?: string | null
  whyItMatters: string
  recommendedAction?: string | null
  evidence?: DiagnosticEvidence[]
}

export type HostCheckGroup = {
  key: HostCheckGroupKey
  title: string
  description: string
  checks: HostCheckResult[]
}

export type HostCheckRunResponse = {
  id: string
  status: HostCheckRunStatus
  startedAtUtc: string | null
  completedAtUtc: string | null
  summary: {
    passed: number
    warnings: number
    failed: number
    skipped: number
    unavailable: number
    unknown: number
  }
  groups: HostCheckGroup[]
}

export function getHostCheckStatusLabel(status: HostCheckStatus) {
  switch (status) {
    case "Pass":
      return "Pass"
    case "Warning":
      return "Warning"
    case "Fail":
      return "Fail"
    case "Skipped":
      return "Skipped"
    case "Unavailable":
      return "Unavailable"
    case "Unknown":
      return "Unknown"
  }
}

export function getHostCheckStatusClassName(status: HostCheckStatus) {
  switch (status) {
    case "Pass":
      return "border-emerald-500/30 bg-emerald-500/10 text-emerald-300"
    case "Warning":
      return "border-amber-500/30 bg-amber-500/10 text-amber-300"
    case "Fail":
      return "border-red-500/30 bg-red-500/10 text-red-300"
    case "Skipped":
      return "border-border bg-muted text-muted-foreground"
    case "Unavailable":
      return "border-sky-500/30 bg-sky-500/10 text-sky-300"
    case "Unknown":
      return "border-border bg-muted text-muted-foreground"
  }
}
