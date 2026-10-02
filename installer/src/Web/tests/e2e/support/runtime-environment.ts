import { chmodSync, mkdirSync, readFileSync, writeFileSync } from "node:fs"
import path from "node:path"
import { expect, type APIRequestContext } from "@playwright/test"

import type { NamedOperatorCredentials } from "./named-operator-auth"

export type RuntimePreflight = Readonly<{
  schemaVersion: number
  productDisplayName: string
  runtimeMode: string
  controlPlaneInstanceId: string
  apiProcessInstanceId: string
  uiDeliveryMode: string
  version: string
  commit: string | null
  validationState: string
  showDevelopmentBanner: boolean
}>

type PersistedAuthState = Readonly<{
  credentials: NamedOperatorCredentials
  runtime: RuntimePreflight
}>

function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) throw new Error(`Missing required environment value: ${name}`)
  return value
}

function boundedAuthFile(): string {
  const candidate = path.resolve(required("MEM_E2E_AUTH_FILE"))
  const repoRoot = path.resolve(process.cwd(), "../../..")
  const allowedRoot = path.join(repoRoot, "dev", ".state", "e2e") + path.sep
  if (!candidate.startsWith(allowedRoot)) {
    throw new Error(`E2E auth file must remain under ${allowedRoot}`)
  }
  return candidate
}

export async function assertRuntimePreflight(request: APIRequestContext): Promise<RuntimePreflight> {
  const response = await request.get("/health/runtime")
  expect(response.status()).toBe(200)
  expect(response.headers()["cache-control"]).toContain("no-store")
  const runtime = await response.json() as RuntimePreflight
  const expectedMode = required("MEM_E2E_EXPECTED_RUNTIME_MODE")
  const expectedUi = required("MEM_E2E_EXPECTED_UI_DELIVERY_MODE")

  if (runtime.runtimeMode !== expectedMode || runtime.uiDeliveryMode !== expectedUi || runtime.validationState !== "valid") {
    throw new Error(
      `E2E runtime mismatch: expected ${expectedMode}/${expectedUi}; ` +
      `received ${runtime.runtimeMode}/${runtime.uiDeliveryMode}; ` +
      `validation=${runtime.validationState}; ` +
      `controlPlaneInstance=${runtime.controlPlaneInstanceId}; ` +
      `apiProcessInstance=${runtime.apiProcessInstanceId}`,
    )
  }
  return runtime
}

export function persistE2eAuthState(credentials: NamedOperatorCredentials, runtime: RuntimePreflight) {
  const file = boundedAuthFile()
  mkdirSync(path.dirname(file), { recursive: true, mode: 0o700 })
  chmodSync(path.dirname(file), 0o700)
  writeFileSync(file, `${JSON.stringify({ credentials, runtime } satisfies PersistedAuthState)}\n`, {
    encoding: "utf8",
    mode: 0o600,
  })
  chmodSync(file, 0o600)
}

export function readE2eAuthState(): PersistedAuthState {
  return JSON.parse(readFileSync(boundedAuthFile(), "utf8")) as PersistedAuthState
}
