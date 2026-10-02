import { getJson, postJson, putJson } from "@/lib/api"
import type {
  DiagnosticsAttentionResponse,
  DiagnosticsDockerEvidenceResponse,
  DiagnosticsEventPageResponse,
  DiagnosticsIncidentDetailResponse,
  DiagnosticsIncidentPageResponse,
  DiagnosticsLoggingHealthResponse,
  DiagnosticsOverviewResponse,
  DiagnosticsPipelineSelfTestResponse,
  DiagnosticsPortainerOverviewResponse,
  DiagnosticsSeqBootstrapExecuteRequest,
  DiagnosticsSeqBootstrapExecuteResponse,
  DiagnosticsSeqBootstrapOperationResponse,
  DiagnosticsSeqBootstrapOverviewResponse,
  DiagnosticsSeqBootstrapReviewRequest,
  DiagnosticsSeqBootstrapReviewResponse,
  DiagnosticsSeqConnectionResponse,
  DiagnosticsSeqDeliveryVerificationResponse,
  DiagnosticsSeqOperationResponse,
  DiagnosticsSeqOverviewResponse,
  DiagnosticsSeqSetupReviewResponse,
  DiagnosticsSeqUiAuthorityUpdateResponse,
  DiagnosticsQuery,
  DiagnosticsSupportReport,
} from "./diagnostics.types"

export function getDiagnosticsOverview(): Promise<DiagnosticsOverviewResponse> {
  return getJson<DiagnosticsOverviewResponse>("/api/operator/diagnostics/overview")
}

export function getDiagnosticsAttention(): Promise<DiagnosticsAttentionResponse> {
  return getJson<DiagnosticsAttentionResponse>(
    "/api/operator/diagnostics/attention?limit=5",
  )
}

export function getDiagnosticIncident(
  incidentId: string,
): Promise<DiagnosticsIncidentDetailResponse> {
  return getJson<DiagnosticsIncidentDetailResponse>(
    `/api/operator/diagnostics/incidents/${encodeURIComponent(incidentId)}`,
  )
}

export function acknowledgeDiagnosticIncident(
  incidentId: string,
): Promise<DiagnosticsIncidentDetailResponse> {
  return postJson<Record<string, never>, DiagnosticsIncidentDetailResponse>(
    `/api/operator/diagnostics/incidents/${encodeURIComponent(incidentId)}/acknowledge`,
    {},
  )
}

export function snoozeDiagnosticIncident(
  incidentId: string,
  snoozedUntilUtc: string,
): Promise<DiagnosticsIncidentDetailResponse> {
  return postJson<{ snoozedUntilUtc: string }, DiagnosticsIncidentDetailResponse>(
    `/api/operator/diagnostics/incidents/${encodeURIComponent(incidentId)}/snooze`,
    { snoozedUntilUtc },
  )
}

export function resolveDiagnosticIncident(
  incidentId: string,
  resolutionCode: string,
): Promise<DiagnosticsIncidentDetailResponse> {
  return postJson<{ resolutionCode: string }, DiagnosticsIncidentDetailResponse>(
    `/api/operator/diagnostics/incidents/${encodeURIComponent(incidentId)}/resolve`,
    { resolutionCode },
  )
}

export function reopenDiagnosticIncident(
  incidentId: string,
): Promise<DiagnosticsIncidentDetailResponse> {
  return postJson<Record<string, never>, DiagnosticsIncidentDetailResponse>(
    `/api/operator/diagnostics/incidents/${encodeURIComponent(incidentId)}/reopen`,
    {},
  )
}

export function listDiagnosticIncidents(
  query: DiagnosticsQuery = {},
): Promise<DiagnosticsIncidentPageResponse> {
  return getJson<DiagnosticsIncidentPageResponse>(
    `/api/operator/diagnostics/incidents${buildQuery(query)}`,
  )
}

export function listDiagnosticEvents(
  query: DiagnosticsQuery = {},
): Promise<DiagnosticsEventPageResponse> {
  return getJson<DiagnosticsEventPageResponse>(
    `/api/operator/diagnostics/events${buildQuery(query)}`,
  )
}


export function getDiagnosticDockerEvidence(
  incidentId: string,
): Promise<DiagnosticsDockerEvidenceResponse> {
  return getJson<DiagnosticsDockerEvidenceResponse>(
    `/api/operator/diagnostics/incidents/${encodeURIComponent(incidentId)}/docker-evidence`,
  )
}

export function refreshDiagnosticDockerEvidence(
  incidentId: string,
): Promise<DiagnosticsDockerEvidenceResponse> {
  return postJson<Record<string, never>, DiagnosticsDockerEvidenceResponse>(
    `/api/operator/diagnostics/incidents/${encodeURIComponent(incidentId)}/docker-evidence/refresh`,
    {},
  )
}

export function getDiagnosticsLoggingHealth(): Promise<DiagnosticsLoggingHealthResponse> {
  return getJson<DiagnosticsLoggingHealthResponse>(
    "/api/operator/diagnostics/logging-health",
  )
}

export function getDiagnosticsSeqOverview(): Promise<DiagnosticsSeqOverviewResponse> {
  return getJson<DiagnosticsSeqOverviewResponse>("/api/operator/diagnostics/seq")
}

export function updateDiagnosticsSeqUiAuthority(
  url: string | null,
): Promise<DiagnosticsSeqUiAuthorityUpdateResponse> {
  return putJson<{ url: string | null }, DiagnosticsSeqUiAuthorityUpdateResponse>(
    "/api/operator/diagnostics/seq/ui-authority",
    { url },
  )
}

export function getDiagnosticsSeqBootstrapOverview(): Promise<DiagnosticsSeqBootstrapOverviewResponse> {
  return getJson<DiagnosticsSeqBootstrapOverviewResponse>(
    "/api/operator/diagnostics/seq/bootstrap",
  )
}

export function reviewDiagnosticsSeqBootstrap(
  request: DiagnosticsSeqBootstrapReviewRequest,
): Promise<DiagnosticsSeqBootstrapReviewResponse> {
  return postJson<DiagnosticsSeqBootstrapReviewRequest, DiagnosticsSeqBootstrapReviewResponse>(
    "/api/operator/diagnostics/seq/bootstrap/review",
    request,
  )
}

export function executeDiagnosticsSeqBootstrap(
  request: DiagnosticsSeqBootstrapExecuteRequest,
): Promise<DiagnosticsSeqBootstrapExecuteResponse> {
  return postJson<DiagnosticsSeqBootstrapExecuteRequest, DiagnosticsSeqBootstrapExecuteResponse>(
    "/api/operator/diagnostics/seq/bootstrap/execute",
    request,
  )
}

export function getDiagnosticsSeqBootstrapOperation(
  operationId: string,
): Promise<DiagnosticsSeqBootstrapOperationResponse> {
  return getJson<DiagnosticsSeqBootstrapOperationResponse>(
    `/api/operator/diagnostics/seq/bootstrap/operations/${encodeURIComponent(operationId)}`,
  )
}

export function connectDiagnosticsSeq(
  administratorPassword: string,
): Promise<DiagnosticsSeqConnectionResponse> {
  return postJson<{ administratorPassword: string }, DiagnosticsSeqConnectionResponse>(
    "/api/operator/diagnostics/seq/connect",
    { administratorPassword },
  )
}

export function getDiagnosticsPortainerOverview(): Promise<DiagnosticsPortainerOverviewResponse> {
  return getJson<DiagnosticsPortainerOverviewResponse>(
    "/api/operator/diagnostics/portainer",
  )
}

export function reviewDiagnosticsSeqSetup(): Promise<DiagnosticsSeqSetupReviewResponse> {
  return postJson<Record<string, never>, DiagnosticsSeqSetupReviewResponse>(
    "/api/operator/diagnostics/seq/setup/review",
    {},
  )
}

export type DiagnosticsSeqRuntimeAction =
  | "deploy"
  | "start"
  | "stop"
  | "restart"
  | "remove"

export function runDiagnosticsSeqRuntimeAction(
  action: DiagnosticsSeqRuntimeAction,
): Promise<DiagnosticsSeqOperationResponse> {
  return postJson<Record<string, never>, DiagnosticsSeqOperationResponse>(
    `/api/operator/diagnostics/seq/runtime/${action}`,
    {},
  )
}

export function changeDiagnosticsSeqDelivery(
  enabled: boolean,
): Promise<DiagnosticsSeqOperationResponse> {
  return postJson<{ enabled: boolean }, DiagnosticsSeqOperationResponse>(
    "/api/operator/diagnostics/seq/delivery",
    { enabled },
  )
}

export function verifyDiagnosticsSeqDelivery(): Promise<DiagnosticsSeqDeliveryVerificationResponse> {
  return postJson<Record<string, never>, DiagnosticsSeqDeliveryVerificationResponse>(
    "/api/operator/diagnostics/seq/delivery/verify",
    {},
  )
}

export function checkDiagnosticsSeqHealth(): Promise<DiagnosticsSeqOperationResponse> {
  return postJson<Record<string, never>, DiagnosticsSeqOperationResponse>(
    "/api/operator/diagnostics/seq/health-check",
    {},
  )
}

export function runDiagnosticsPipelineSelfTest(): Promise<DiagnosticsPipelineSelfTestResponse> {
  return postJson<Record<string, never>, DiagnosticsPipelineSelfTestResponse>(
    "/api/operator/diagnostics/self-test",
    {},
  )
}

export function generateDiagnosticSupportReport(
  incidentId: string,
  includeDockerEvidence = false,
): Promise<DiagnosticsSupportReport> {
  return postJson<
    { incidentId: string; includeDockerEvidence: boolean },
    DiagnosticsSupportReport
  >("/api/operator/diagnostics/support-report", {
    incidentId,
    includeDockerEvidence,
  })
}

function buildQuery(query: DiagnosticsQuery): string {
  const params = new URLSearchParams()

  if (query.severity) params.set("severity", query.severity)
  if (query.feature) params.set("feature", query.feature)
  if (query.search) params.set("search", query.search)
  if (query.incidentId) params.set("incidentId", query.incidentId)
  if (query.lifecycle) params.set("lifecycle", query.lifecycle)
  if (query.cursor) params.set("cursor", query.cursor)
  if (query.pageSize !== undefined) params.set("pageSize", String(query.pageSize))

  const value = params.toString()
  return value ? `?${value}` : ""
}
