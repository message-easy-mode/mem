export type VerificationReportResponse = {
  status: VerificationStatus
  message: string
  checkedAtUtc: string
  checks: VerificationCheckResult[]
}

export type VerificationCheckResult = {
  key: string
  title: string
  description: string
  status: VerificationStatus
  message: string
  evidence: VerificationEvidence[]
}

export type VerificationEvidence = {
  key: string
  value: string
  sensitive: boolean
  status: string | null
}

export type VerificationStatus =
  | "Succeeded"
  | "Warning"
  | "Failed"
  | "Pending"
  | "Skipped"