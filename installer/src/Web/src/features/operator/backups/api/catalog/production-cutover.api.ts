import { controlPlaneGet, controlPlanePost } from "../transport/host-agent"
import type {
  CatalogCutoverConfirmationRequest,
  CatalogCutoverConfirmationResponse,
  CatalogCutoverExecutionRequest,
  CatalogCutoverExecutionResponse,
  CatalogCutoverPreviewResponse,
  CatalogPreCutoverReadinessResponse,
  CatalogProductionCandidateResponse,
  CatalogPostCutoverProjectionResponse,
} from "./backup-catalog.types"

const base = (catalogEntryId: string) =>
  `/internal/host-agent/backups/catalog/${encodeURIComponent(catalogEntryId)}/production-restore`

export function createCatalogProductionCandidate(catalogEntryId: string) {
  return controlPlanePost<Record<string, never>, CatalogProductionCandidateResponse>(
    `${base(catalogEntryId)}/candidates/recreate-private`,
    {},
  )
}

export function createCatalogCutoverPreview(catalogEntryId: string, candidateId: string) {
  return controlPlanePost<{ candidateId: string }, CatalogCutoverPreviewResponse>(
    `${base(catalogEntryId)}/cutover-preview`,
    { candidateId },
  )
}

export function confirmCatalogCutover(
  catalogEntryId: string,
  request: CatalogCutoverConfirmationRequest,
) {
  return controlPlanePost<CatalogCutoverConfirmationRequest, CatalogCutoverConfirmationResponse>(
    `${base(catalogEntryId)}/cutover-confirmations/evaluate`,
    request,
  )
}

export function getCatalogPreCutoverReadiness(
  catalogEntryId: string,
  candidateId: string,
  previewId: string,
  confirmationId: string,
) {
  const query = new URLSearchParams({ candidateId, previewId, confirmationId })
  return controlPlaneGet<CatalogPreCutoverReadinessResponse>(
    `${base(catalogEntryId)}/pre-cutover-readiness?${query.toString()}`,
  )
}

export function executeCatalogCutover(
  catalogEntryId: string,
  request: CatalogCutoverExecutionRequest,
) {
  return controlPlanePost<CatalogCutoverExecutionRequest, CatalogCutoverExecutionResponse>(
    `${base(catalogEntryId)}/cutover-executions/execute`,
    request,
  )
}


export function getCatalogPostCutoverProjection(catalogEntryId: string) {
  return controlPlaneGet<CatalogPostCutoverProjectionResponse>(
    `${base(catalogEntryId)}/post-cutover`,
  )
}
