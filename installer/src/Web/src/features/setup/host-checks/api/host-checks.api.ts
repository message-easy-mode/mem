// src/features/setup/host-checks/api/host-checks.api.ts

import { getJson, postJson } from "@/lib/api"

import type { HostCheckRunResponse } from "./host-checks.types"

export function startHostCheckRun() {
  return postJson<void, HostCheckRunResponse>("/api/setup/host-checks/runs")
}

export function getCurrentHostCheckRun() {
  return getJson<HostCheckRunResponse | null>(
    "/api/setup/host-checks/runs/current",
  )
}

export function getHostCheckRun(runId: string) {
  return getJson<HostCheckRunResponse>(
    `/api/setup/host-checks/runs/${runId}`,
  )
}
