import { getJson } from "@/lib/api"
import type { VerificationReportResponse } from "@/features/setup/verification/api/verification.types"

export function getVerificationReport(installationId: string) {
  return getJson<VerificationReportResponse>(
    `/api/setup/install-runs/${installationId}/verification-report`,
  )
}