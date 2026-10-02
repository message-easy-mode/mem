// src/features/shared/domains/api/domains.api.ts

import { delJson, getJson, postJson, putJson } from "@/lib/api"

import type {
  DomainCertificateIssueActiveResponse,
  DomainCertificateIssueLatestResponse,
  DomainCertificateIssueOperation,
  DomainCertificateIssueQueueResult,
  DomainCertificateIssueStartRequest,
  CertificateOperationResult,
  CreateOperatorDomainRequest,
  DomainActiveCertificateSelectionResult,
  DeleteOperatorDomainResponse,
  DeleteCertificateResponse,
  NpmCertificateProbeResult,
  NpmCertificateTestProxyHostRequest,
  NpmCertificateTestProxyHostResult,
  NpmReadinessResponse,
  OperatorDomainDetail,
  OperatorDomainRenewalCredentialMutationResponse,
  OperatorDomainRenewalHistoryItem,
  OperatorDomainRenewalRunResponse,
  OperatorDomainRenewalStatus,
  OperatorDomainSummary,
  StoredCertificateMetadata,
  UpdateOperatorDomainRenewalCredentialRequest,
  UpdateOperatorDomainRenewalPolicyRequest,
  UpdateOperatorDomainRequest,
} from "./domains.types"

const OPERATOR_DOMAINS_BASE = "/api/operator/domains"

export function listOperatorDomains() {
  return getJson<OperatorDomainSummary[]>(`${OPERATOR_DOMAINS_BASE}`)
}

export function getOperatorDomain(domainId: string) {
  return getJson<OperatorDomainDetail>(`${OPERATOR_DOMAINS_BASE}/${domainId}`)
}

export function createOperatorDomain(request: CreateOperatorDomainRequest) {
  return postJson<CreateOperatorDomainRequest, OperatorDomainDetail>(
    `${OPERATOR_DOMAINS_BASE}`,
    request,
  )
}

export function updateOperatorDomain(domainId: string, request: UpdateOperatorDomainRequest) {
  return putJson<UpdateOperatorDomainRequest, OperatorDomainDetail>(
    `${OPERATOR_DOMAINS_BASE}/${domainId}`,
    request,
  )
}

export function deleteOperatorDomain(domainId: string, options?: { force?: boolean }) {
  const params = new URLSearchParams({
    force: String(options?.force ?? false),
  })

  return delJson<DeleteOperatorDomainResponse>(
    `${OPERATOR_DOMAINS_BASE}/${domainId}?${params.toString()}`,
  )
}

export function setMainOperatorDomain(domainId: string) {
  return postJson<void, OperatorDomainDetail>(
    `${OPERATOR_DOMAINS_BASE}/${domainId}/set-main`,
  )
}



export function listOperatorDomainRenewalStatuses() {
  return getJson<OperatorDomainRenewalStatus[]>(`${OPERATOR_DOMAINS_BASE}/renewal`)
}

export function getOperatorDomainRenewalStatus(domainId: string) {
  return getJson<OperatorDomainRenewalStatus>(`${OPERATOR_DOMAINS_BASE}/${domainId}/renewal`)
}

export function getOperatorDomainRenewalHistory(domainId: string) {
  return getJson<OperatorDomainRenewalHistoryItem[]>(
    `${OPERATOR_DOMAINS_BASE}/${domainId}/renewal/history`,
  )
}

export function runOperatorDomainRenewal(domainId: string) {
  return postJson<void, OperatorDomainRenewalRunResponse>(
    `${OPERATOR_DOMAINS_BASE}/${domainId}/renewal/run`,
  )
}

export function updateOperatorDomainRenewalPolicy(
  domainId: string,
  request: UpdateOperatorDomainRenewalPolicyRequest,
) {
  return putJson<UpdateOperatorDomainRenewalPolicyRequest, OperatorDomainRenewalStatus>(
    `${OPERATOR_DOMAINS_BASE}/${domainId}/renewal/policy`,
    request,
  )
}

export function updateOperatorDomainRenewalCredential(
  domainId: string,
  request: UpdateOperatorDomainRenewalCredentialRequest,
) {
  return putJson<
    UpdateOperatorDomainRenewalCredentialRequest,
    OperatorDomainRenewalCredentialMutationResponse
  >(`${OPERATOR_DOMAINS_BASE}/${domainId}/renewal/credential`, request)
}

export function removeOperatorDomainRenewalCredential(domainId: string) {
  return delJson<OperatorDomainRenewalCredentialMutationResponse>(
    `${OPERATOR_DOMAINS_BASE}/${domainId}/renewal/credential`,
  )
}

export function getNpmIngressStatus() {
  return getJson<NpmReadinessResponse>(`${OPERATOR_DOMAINS_BASE}/ingress/npm/status`)
}


export function listOperatorDomainCertificates(domainId: string) {
  return getJson<StoredCertificateMetadata[]>(
    `${OPERATOR_DOMAINS_BASE}/${domainId}/certificates`,
  )
}

export function getOperatorDomainCertificate(domainId: string, certificateId: string) {
  return getJson<StoredCertificateMetadata>(
    `${OPERATOR_DOMAINS_BASE}/${domainId}/certificates/${certificateId}`,
  )
}

export function deleteOperatorDomainCertificate(domainId: string, certificateId: string) {
  return delJson<DeleteCertificateResponse>(
    `${OPERATOR_DOMAINS_BASE}/${domainId}/certificates/${certificateId}`,
  )
}

export async function getLatestOperatorDomainCertificateIssuance(domainId: string) {
  const response = await getJson<DomainCertificateIssueLatestResponse>(
    `${OPERATOR_DOMAINS_BASE}/${domainId}/certificates/issuance/latest`,
  )
  return response.operation
}

export async function listActiveOperatorDomainCertificateIssuances() {
  const response = await getJson<DomainCertificateIssueActiveResponse>(
    `${OPERATOR_DOMAINS_BASE}/certificates/issuance/active`,
  )
  return response.operations
}

export function getOperatorDomainCertificateIssuance(
  domainId: string,
  operationId: string,
) {
  return getJson<DomainCertificateIssueOperation>(
    `${OPERATOR_DOMAINS_BASE}/${domainId}/certificates/issuance/${operationId}`,
  )
}

export function startOperatorDomainCertificateIssuance(
  domainId: string,
  request: DomainCertificateIssueStartRequest,
) {
  return postJson<DomainCertificateIssueStartRequest, DomainCertificateIssueQueueResult>(
    `${OPERATOR_DOMAINS_BASE}/${domainId}/certificates/issuance`,
    request,
  )
}

export function setActiveOperatorDomainCertificate(
  domainId: string,
  certificateId: string,
) {
  return postJson<void, DomainActiveCertificateSelectionResult>(
    `${OPERATOR_DOMAINS_BASE}/${domainId}/certificates/${certificateId}/set-active`,
  )
}

export function listCertificates() {
  return getJson<StoredCertificateMetadata[]>(`${OPERATOR_DOMAINS_BASE}/certificates`)
}

export function getCertificate(certificateId: string) {
  return getJson<StoredCertificateMetadata>(
    `${OPERATOR_DOMAINS_BASE}/certificates/${certificateId}`,
  )
}

export function validateCertificate(certificateId: string) {
  return postJson<void, CertificateOperationResult>(
    `${OPERATOR_DOMAINS_BASE}/certificates/${certificateId}/validate`,
  )
}

export function probeNpmCertificate(certificateId: string) {
  return postJson<void, NpmCertificateProbeResult>(
    `${OPERATOR_DOMAINS_BASE}/certificates/${certificateId}/npm/probe`,
  )
}

export function importCertificateToNpm(certificateId: string) {
  return postJson<void, NpmCertificateProbeResult>(
    `${OPERATOR_DOMAINS_BASE}/certificates/${certificateId}/npm/import`,
  )
}

export function testNpmProxyHost(
  certificateId: string,
  request: NpmCertificateTestProxyHostRequest,
) {
  return postJson<
    NpmCertificateTestProxyHostRequest,
    NpmCertificateTestProxyHostResult
  >(
    `${OPERATOR_DOMAINS_BASE}/certificates/${certificateId}/npm/test-proxy-host`,
    request,
  )
}
