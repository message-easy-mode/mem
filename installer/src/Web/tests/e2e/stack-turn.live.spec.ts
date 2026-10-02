import { expect, test, type Page, type Response } from "@playwright/test"

import {
  readNamedOperatorCredentialsFromEnvironment,
  signInNamedOperator,
  signOutNamedOperator,
} from "./support/named-operator-auth"

type TurnState = "connected" | "not-connected" | "external" | "drift" | "unknown"
type DesiredFinalState = "connected" | "not-connected"

type TurnInspection = Readonly<{
  state: TurnState
  management: "mem-managed" | "external-observed" | "none" | "unknown"
  platform: Readonly<{
    readiness: string
    publicHost: string
    turnUris: string[]
  }> | null
  liveConfiguration: Readonly<{
    turnUris: string[]
    sharedSecretMatchesPlatform: boolean | null
  }> | null
  persistedMetadata: Readonly<{
    recorded: boolean
    configured: boolean | null
  }>
}>

type CoturnCheck = Readonly<{
  readiness: string
  checks: ReadonlyArray<Readonly<{
    key: string
    status: "passed" | "failed" | "warning" | "not-run"
  }>>
  allocation: Readonly<{
    status: "passed" | "failed" | "warning" | "not-run"
  }>
}>

type BackupResponse = Readonly<{
  backupId: string
}>

const stackSlug = requiredEnvironmentValue("MEM_E2E_TURN_STACK_SLUG")
const baselineBackupId = requiredEnvironmentValue("MEM_E2E_TURN_BASELINE_BACKUP_ID")
const desiredFinalState = parseDesiredFinalState(
  requiredEnvironmentValue("MEM_E2E_TURN_FINAL_STATE"),
)

if (
  requiredEnvironmentValue("MEM_E2E_TURN_MUTATION_ACK") !==
  "I_UNDERSTAND_TURN_PROOF_MUTATES_STACK"
) {
  throw new Error("turn_live_mutation_ack_invalid")
}

test.describe("live stack TURN journey", () => {
  test("proves platform health, connect, backup, disconnect, and the requested final state", async ({ page }) => {
    test.setTimeout(8 * 60_000)

    const operator = readNamedOperatorCredentialsFromEnvironment()
    await signInNamedOperator(page, operator)

    const platformCheck = await provePlatformTurn(page)
    const initial = await openStackTurn(page)
    assertSafeInitialState(initial)

    const connectOperationId = await ensureConnected(page, initial)
    const connected = await waitForTurnState(page, "connected")
    assertConnectedState(connected)

    const connectedBackupId = await createConnectedBackup(page)
    await proveCatalogEntry(page, connectedBackupId)
    await proveCatalogEntry(page, baselineBackupId)

    await openStackTurn(page)
    const disconnectOperationId = await disconnectStack(page)
    const disconnected = await waitForTurnState(page, "not-connected")
    assertDisconnectedState(disconnected)

    let finalConnectOperationId: string | null = null
    if (desiredFinalState === "connected") {
      finalConnectOperationId = await ensureConnected(page, disconnected)
      const finalConnected = await waitForTurnState(page, "connected")
      assertConnectedState(finalConnected)
    } else {
      await page.reload()
      const finalDisconnected = await waitForTurnState(page, "not-connected")
      assertDisconnectedState(finalDisconnected)
    }

    console.log(JSON.stringify({
      proof: "STACK-TURN-LIVE-01",
      stackSlug,
      platformReadiness: platformCheck.readiness,
      initialState: initial.state,
      connectOperationId,
      connectedBackupId,
      disconnectOperationId,
      finalConnectOperationId,
      finalState: desiredFinalState,
      baselineBackupId,
    }))

    await signOutNamedOperator(page)
  })
})

async function provePlatformTurn(page: Page): Promise<CoturnCheck> {
  const statusResponse = page.waitForResponse(
    (response) =>
      isResponse(response, "GET", "/internal/host-agent/platform/coturn") &&
      response.status() === 200,
    { timeout: 30_000 },
  )

  await page.goto("/services/coturn")
  await expect(page.getByRole("heading", { name: "Coturn TURN server" })).toBeVisible()

  const runtime = await readJsonResponse<{
    readiness: string
    secretStorage?: string
  }>(await statusResponse)

  expect(runtime.readiness).toBe("ready")
  expect(JSON.stringify(runtime)).not.toContain("coturn-secret.json")

  const checkResponse = page.waitForResponse(
    (response) =>
      isResponse(response, "POST", "/internal/host-agent/platform/coturn/check"),
    { timeout: 60_000 },
  )

  await page.getByRole("button", { name: "Run TURN check" }).click()
  const response = await checkResponse
  expect(response.status()).toBe(200)
  const check = await readJsonResponse<CoturnCheck>(response)

  const requiredChecks = [
    "container",
    "approved-image",
    "configuration",
    "shared-secret",
    "listener-ports",
    "relay-ports",
    "platform-domain",
    "host-dns",
  ]

  expect(check.readiness).toBe("ready")
  for (const key of requiredChecks) {
    expect(check.checks.find((item) => item.key === key)?.status, key).toBe("passed")
  }
  expect(check.allocation.status).toBe("passed")

  await expect(page.getByText("Local UDP allocation probe", { exact: true })).toBeVisible()
  await expect(page.getByText("Coturn accepted short-lived credentials and completed the local allocation probe.", { exact: true })).toBeVisible()

  const logsResponse = page.waitForResponse(
    (candidate) =>
      candidate.request().method() === "GET" &&
      candidate.url().includes("/internal/host-agent/platform/coturn/logs?tail=") &&
      candidate.status() === 200,
    { timeout: 30_000 },
  )

  await page.getByRole("button", { name: "View recent logs" }).click()
  const logs = await readJsonResponse<{ content: string }>(await logsResponse)
  expect(logs.content).not.toMatch(/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F\uFFFD]/u)
  expect(logs.content).not.toContain("coturn-secret.json")

  return check
}

async function openStackTurn(page: Page): Promise<TurnInspection> {
  const turnResponse = page.waitForResponse(
    (response) =>
      isResponse(
        response,
        "GET",
        `/internal/host-agent/runtime-stacks/${encodeURIComponent(stackSlug)}/turn`,
      ) && response.status() === 200,
    { timeout: 30_000 },
  )

  await page.goto(`/stacks/${encodeURIComponent(stackSlug)}/services`)
  await expect(page.getByText("Voice / video TURN", { exact: true })).toBeVisible()

  const turn = await readJsonResponse<TurnInspection>(await turnResponse)
  expect(turn.platform?.readiness).toBe("ready")
  return turn
}

async function inspectTurn(page: Page): Promise<TurnInspection> {
  return page.evaluate(async (slug) => {
    const response = await fetch(
      `/internal/host-agent/runtime-stacks/${encodeURIComponent(slug)}/turn`,
      {
        method: "GET",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
      },
    )

    if (!response.ok) {
      throw new Error(`turn_inspection_failed_${response.status}`)
    }

    return await response.json() as TurnInspection
  }, stackSlug)
}

async function waitForTurnState(
  page: Page,
  expectedState: "connected" | "not-connected",
): Promise<TurnInspection> {
  await expect.poll(
    async () => (await inspectTurn(page)).state,
    { timeout: 120_000, intervals: [1_000, 2_000, 5_000] },
  ).toBe(expectedState)

  await page.reload()
  await expect(page.getByText("Voice / video TURN", { exact: true })).toBeVisible()
  await expect(page.getByText(
    expectedState === "connected" ? "Connected" : "Not connected",
    { exact: true },
  ).first()).toBeVisible()

  return inspectTurn(page)
}

async function ensureConnected(
  page: Page,
  current: TurnInspection,
): Promise<string | null> {
  if (current.state === "connected") {
    assertConnectedState(current)
    return null
  }

  if (!canSafelyConnect(current)) {
    throw new Error(`turn_live_connect_not_safe_from_${current.state}_${current.management}`)
  }

  const reviewResponse = page.waitForResponse(
    (response) =>
      isResponse(
        response,
        "POST",
        `/internal/host-agent/runtime-stacks/${encodeURIComponent(stackSlug)}/turn/connect/review`,
      ),
    { timeout: 30_000 },
  )

  await page.getByRole("button", { name: "Connect to platform TURN" }).click()
  expect((await reviewResponse).status()).toBe(200)

  const dialog = page.getByRole("dialog", { name: "Connect stack to platform TURN" })
  await expect(dialog).toBeVisible()

  const applyResponse = page.waitForResponse(
    (response) =>
      isResponse(
        response,
        "POST",
        `/internal/host-agent/runtime-stacks/${encodeURIComponent(stackSlug)}/turn/connect`,
      ),
    { timeout: 120_000 },
  )

  await dialog.getByRole("button", { name: "Connect to platform TURN" }).click()
  const response = await applyResponse
  expect(response.status()).toBe(200)

  const result = await readJsonResponse<{
    status: string
    operationId: string
  }>(response)
  expect(result.status).toBe("succeeded")
  return result.operationId
}

async function disconnectStack(page: Page): Promise<string> {
  const current = await inspectTurn(page)
  assertConnectedState(current)

  const reviewResponse = page.waitForResponse(
    (response) =>
      isResponse(
        response,
        "POST",
        `/internal/host-agent/runtime-stacks/${encodeURIComponent(stackSlug)}/turn/disconnect/review`,
      ),
    { timeout: 30_000 },
  )

  await page.getByRole("button", { name: "Disconnect from platform TURN" }).click()
  expect((await reviewResponse).status()).toBe(200)

  const dialog = page.getByRole("dialog", { name: "Disconnect stack from platform TURN" })
  await expect(dialog).toBeVisible()
  await expect(dialog.getByText("Yes", { exact: true })).toBeVisible()

  const applyResponse = page.waitForResponse(
    (response) =>
      isResponse(
        response,
        "POST",
        `/internal/host-agent/runtime-stacks/${encodeURIComponent(stackSlug)}/turn/disconnect`,
      ),
    { timeout: 120_000 },
  )

  await dialog.getByRole("button", { name: "Disconnect from platform TURN" }).click()
  const response = await applyResponse
  expect(response.status()).toBe(200)

  const result = await readJsonResponse<{
    status: string
    operationId: string
  }>(response)
  expect(result.status).toBe("succeeded")
  return result.operationId
}

async function createConnectedBackup(page: Page): Promise<string> {
  const current = await inspectTurn(page)
  assertConnectedState(current)

  const backupResponse = page.waitForResponse(
    (response) =>
      isResponse(
        response,
        "POST",
        `/internal/host-agent/backups/artifacts/local-backups/stacks/${encodeURIComponent(stackSlug)}`,
      ),
    { timeout: 120_000 },
  )

  await page.getByRole("button", { name: "Create backup" }).click()
  const response = await backupResponse
  expect(response.status()).toBe(200)
  const backup = await readJsonResponse<BackupResponse>(response)

  expect(backup.backupId).toMatch(/^\d{8}-\d{6}Z$/)
  await expect(page.getByText("Backup created", { exact: true })).toBeVisible()
  return backup.backupId
}

async function proveCatalogEntry(page: Page, backupId: string) {
  const catalogResponse = page.waitForResponse(
    (response) =>
      response.request().method() === "GET" &&
      response.url().includes("/internal/host-agent/backups/catalog") &&
      response.status() === 200,
    { timeout: 30_000 },
  )

  await page.goto("/backups")
  await expect(page.getByRole("heading", { name: "Backup Catalog" })).toBeVisible()
  await catalogResponse

  const search = page.getByRole("textbox", { name: "Search catalog" })
  await search.fill(backupId)
  await expect(page.getByText(backupId, { exact: true })).toBeVisible()
}

function assertSafeInitialState(turn: TurnInspection) {
  expect(turn.platform?.readiness).toBe("ready")
  if (turn.state === "external" || turn.state === "unknown") {
    throw new Error(`turn_live_unsafe_initial_state_${turn.state}`)
  }
  if (turn.state === "drift" && !canSafelyConnect(turn)) {
    throw new Error("turn_live_unreconcilable_drift")
  }
}

function assertConnectedState(turn: TurnInspection) {
  expect(turn.state).toBe("connected")
  expect(turn.management).toBe("mem-managed")
  expect(turn.platform?.readiness).toBe("ready")
  expect(turn.persistedMetadata.recorded).toBe(true)
  expect(turn.persistedMetadata.configured).toBe(true)
  expect(turn.liveConfiguration?.sharedSecretMatchesPlatform).toBe(true)
  expect(sameUris(
    turn.liveConfiguration?.turnUris ?? [],
    turn.platform?.turnUris ?? [],
  )).toBe(true)
}

function assertDisconnectedState(turn: TurnInspection) {
  expect(turn.state).toBe("not-connected")
  expect(turn.management).toBe("none")
  expect(turn.platform?.readiness).toBe("ready")
  expect(turn.persistedMetadata.recorded).toBe(true)
  expect(turn.persistedMetadata.configured).toBe(false)
  expect(turn.liveConfiguration?.turnUris ?? []).toHaveLength(0)
}

function canSafelyConnect(turn: TurnInspection) {
  if (turn.platform?.readiness !== "ready") return false
  if (turn.state === "not-connected") return true

  return turn.state === "drift" &&
    turn.management === "mem-managed" &&
    turn.persistedMetadata.recorded === false &&
    turn.liveConfiguration?.sharedSecretMatchesPlatform === true &&
    sameUris(turn.liveConfiguration.turnUris, turn.platform.turnUris)
}

function sameUris(left: string[], right: string[]) {
  const normalize = (value: string) => value.trim().toLowerCase()
  return left.map(normalize).sort().join("\n") === right.map(normalize).sort().join("\n")
}

function isResponse(response: Response, method: string, path: string) {
  return response.request().method() === method &&
    new URL(response.url()).pathname === path
}

async function readJsonResponse<T>(response: Response): Promise<T> {
  const contentType = response.headers()["content-type"] ?? ""
  expect(contentType).toContain("application/json")
  return await response.json() as T
}

function parseDesiredFinalState(value: string): DesiredFinalState {
  if (value === "connected" || value === "not-connected") return value
  throw new Error("turn_live_final_state_invalid")
}

function requiredEnvironmentValue(name: string) {
  const value = process.env[name]?.trim()
  if (!value) throw new Error(`Missing required environment value: ${name}`)
  return value
}
