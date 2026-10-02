// src/features/setup/start/api/platform-status-api.ts

import { getJson, postJson } from "@/lib/api"

import type { PlatformStatusResponse } from "./platform-status"

export type FirstTimeSetupAuthority = {
  id: string
  status: string
}

export function getPlatformStatus() {
  return getJson<PlatformStatusResponse>("/api/setup/start/status")
}

export function beginPlatformSetup() {
  return postJson<void, FirstTimeSetupAuthority>("/api/setup/start/begin")
}
