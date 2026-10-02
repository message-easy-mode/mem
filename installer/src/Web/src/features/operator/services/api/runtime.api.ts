// src/features/operator/services/api/runtime.api.ts

import { getJson, postJson } from "@/lib/api"
import type {
  DockerPingResponse,
  DockerContainersResponse,
  PlanPostgresRequest,
  RuntimePlanResponse,
  DeployPostgresRequest,
  RuntimeActionResponse,
  RuntimeServiceStatusResponse,
  PlanNpmRequest,
  RuntimeNpmPlanResponse,
  DeployNpmRequest,
  RuntimeNpmStatusResponse,
  PlanSeqRequest,
  RuntimeSeqPlanResponse,
  DeploySeqRequest,
  RuntimeSeqStatusResponse,
} from "./runtime.types"

const DOCKER_BASE = "/api/operator/services/docker"
const POSTGRES_BASE = "/api/operator/services/postgres"
const NPM_BASE = "/api/operator/services/npm"
const SEQ_BASE = "/api/operator/services/seq"

export function pingDocker() {
  return getJson<DockerPingResponse>(`${DOCKER_BASE}/ping`)
}

export function getManagedContainers() {
  return getJson<DockerContainersResponse>(`${DOCKER_BASE}/containers`)
}

export function planPostgres(request: PlanPostgresRequest) {
  return postJson<PlanPostgresRequest, RuntimePlanResponse>(
    `${POSTGRES_BASE}/plan`,
    request,
  )
}

export function deployPostgres(request: DeployPostgresRequest) {
  return postJson<DeployPostgresRequest, RuntimeActionResponse>(
    `${POSTGRES_BASE}/deploy`,
    request,
  )
}

export function inspectPostgres() {
  return getJson<RuntimeServiceStatusResponse>(`${POSTGRES_BASE}/`)
}

export function startPostgres() {
  return postJson<void, RuntimeActionResponse>(`${POSTGRES_BASE}/start`)
}

export function stopPostgres() {
  return postJson<void, RuntimeActionResponse>(`${POSTGRES_BASE}/stop`)
}

export function removePostgres() {
  return postJson<void, RuntimeActionResponse>(`${POSTGRES_BASE}/remove`)
}

export function planNpm(request: PlanNpmRequest) {
  return postJson<PlanNpmRequest, RuntimeNpmPlanResponse>(
    `${NPM_BASE}/plan`,
    request,
  )
}

export function deployNpm(request: DeployNpmRequest) {
  return postJson<DeployNpmRequest, RuntimeActionResponse>(
    `${NPM_BASE}/deploy`,
    request,
  )
}

export function inspectNpm() {
  return getJson<RuntimeNpmStatusResponse>(`${NPM_BASE}/`)
}

export function startNpm() {
  return postJson<void, RuntimeActionResponse>(`${NPM_BASE}/start`)
}

export function stopNpm() {
  return postJson<void, RuntimeActionResponse>(`${NPM_BASE}/stop`)
}

export function removeNpm() {
  return postJson<void, RuntimeActionResponse>(`${NPM_BASE}/remove`)
}

export function planSeq(request: PlanSeqRequest) {
  return postJson<PlanSeqRequest, RuntimeSeqPlanResponse>(
    `${SEQ_BASE}/plan`,
    request,
  )
}

export function deploySeq(request: DeploySeqRequest) {
  return postJson<DeploySeqRequest, RuntimeActionResponse>(
    `${SEQ_BASE}/deploy`,
    request,
  )
}

export function inspectSeq() {
  return getJson<RuntimeSeqStatusResponse>(`${SEQ_BASE}/`)
}

export function startSeq() {
  return postJson<void, RuntimeActionResponse>(`${SEQ_BASE}/start`)
}

export function stopSeq() {
  return postJson<void, RuntimeActionResponse>(`${SEQ_BASE}/stop`)
}

export function removeSeq() {
  return postJson<void, RuntimeActionResponse>(`${SEQ_BASE}/remove`)
}