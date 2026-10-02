// src/features/setup/install-run/api/installation-run.api.ts

import { getJson, postJson } from "@/lib/api"

import type {
  InstallationResponse,
  InstallationStepExecutionResponse,
  RunInstallationResponse,
} from "./installation-run.types"

export function getInstallation(id: string) {
  return getJson<InstallationResponse>(`/api/setup/install-plans/${id}`)
}

export function runInstallation(id: string) {
  return postJson<void, RunInstallationResponse>(
    `/api/setup/install-runs/${id}/run`,
  )
}

export function getInstallationSteps(id: string) {
  return getJson<InstallationStepExecutionResponse[]>(
    `/api/setup/install-runs/${id}/steps`,
  )
}