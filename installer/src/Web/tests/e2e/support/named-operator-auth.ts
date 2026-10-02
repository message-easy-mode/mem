import { createHmac, randomBytes } from "node:crypto"
import { expect, type Page } from "@playwright/test"

type PasswordCredentials = Readonly<{
  username: string
  password: string
}>

export type NamedOperatorCredentials = PasswordCredentials & Readonly<{
  totpSecret: string
}>

const base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"

function decodeBase32(value: string): Uint8Array {
  const normalized = value
    .replace(/\s+/g, "")
    .replace(/=+$/g, "")
    .toUpperCase()

  if (!normalized) {
    throw new Error("e2e_totp_secret_missing")
  }

  const bytes: number[] = []
  let buffer = 0
  let bits = 0

  for (const character of normalized) {
    const digit = base32Alphabet.indexOf(character)

    if (digit < 0) {
      throw new Error("e2e_totp_secret_invalid")
    }

    buffer = (buffer << 5) | digit
    bits += 5

    while (bits >= 8) {
      bits -= 8
      bytes.push((buffer >>> bits) & 0xff)
    }
  }

  return Uint8Array.from(bytes)
}

function createTotp(secret: string, nowMilliseconds = Date.now()): string {
  const counter = BigInt(Math.floor(nowMilliseconds / 30_000))
  const counterBytes = new Uint8Array(8)
  let remaining = counter

  for (let index = counterBytes.length - 1; index >= 0; index -= 1) {
    counterBytes[index] = Number(remaining & 0xffn)
    remaining >>= 8n
  }

  const digest = createHmac("sha1", decodeBase32(secret))
    .update(counterBytes)
    .digest()

  const offset = digest.at(-1)! & 0x0f
  const binary =
    ((digest[offset]! & 0x7f) << 24) |
    (digest[offset + 1]! << 16) |
    (digest[offset + 2]! << 8) |
    digest[offset + 3]!

  return String(binary % 1_000_000).padStart(6, "0")
}

async function fillCurrentTotp(page: Page, selector: string, secret: string) {
  const secondsRemaining = 30 - (Math.floor(Date.now() / 1_000) % 30)

  // Avoid submitting a code near its rotation boundary. This keeps the
  // browser proof independent of minor local scheduling delays.
  if (secondsRemaining <= 5) {
    await page.waitForTimeout((secondsRemaining + 1) * 1_000)
  }

  await page.locator(selector).fill(createTotp(secret))
}

function createDisposableFirstOwner(): PasswordCredentials {
  const runId = randomBytes(6).toString("hex")

  return {
    username: `e2e-owner-${runId}`,
    password: `MemE2e!${runId}Aa1`,
  }
}

async function forceEnglishBrowserUi(page: Page) {
  // MEM follows the browser's preferred language unless an operator preference
  // is stored. E2E labels intentionally use English, so set the product's own
  // persisted language preference before the first route loads.
  await page.addInitScript(() => {
    window.localStorage.setItem("mem.ui-language", "en")
  })
}

function requiredEnvironmentValue(name: string): string {
  const value = process.env[name]?.trim()

  if (!value) {
    throw new Error(`Missing required environment value: ${name}`)
  }

  return value
}

/**
 * The caller must point this only at a new, disposable no-owner database.
 *
 * The browser completes the real first-owner UI, including TOTP verification
 * and the recovery-code acknowledgement. It never reads, logs, saves, or
 * returns the recovery codes.
 */
export async function bootstrapDisposableFirstOwner(
  page: Page,
  setupToken: string,
): Promise<NamedOperatorCredentials> {
  await forceEnglishBrowserUi(page)

  const owner = createDisposableFirstOwner()

  await page.goto("/dashboard")
  await expect(page).toHaveURL(/\/bootstrap$/, { timeout: 10_000 })
  // CardTitle currently renders a styled div rather than a semantic heading.
  // The bootstrap-code input is stable, locale-independent, and proves the
  // intended first-owner screen is ready for interaction.
  await expect(page.locator("#bootstrap-token")).toBeVisible()

  await page.locator("#bootstrap-token").fill(setupToken)
  await page.getByRole("button", { name: "Verify setup code" }).click()

  await expect(page.locator("#bootstrap-username")).toBeVisible()
  await page.locator("#bootstrap-username").fill(owner.username)
  await page.locator("#bootstrap-password").fill(owner.password)
  await page.locator("#bootstrap-password-confirmation").fill(owner.password)
  await page.getByRole("button", { name: "Prepare authenticator setup" }).click()

  await expect(page.locator("#bootstrap-totp")).toBeVisible()
  const secretElement = page.locator("dd > code")
  await expect(secretElement).toHaveCount(1)

  const totpSecret = (await secretElement.innerText()).trim()

  if (!totpSecret) {
    throw new Error("e2e_bootstrap_totp_secret_missing")
  }

  await fillCurrentTotp(page, "#bootstrap-totp", totpSecret)
  await page.getByRole("button", { name: "Verify authenticator" }).click()

  // Do not inspect or serialize the recovery-code content. The acknowledgement
  // is valid only because this test requires a disposable database that is
  // destroyed after the browser smoke run.
  await expect(page.getByLabel("One-time recovery codes")).toBeVisible()
  await page
    .getByLabel("I have stored these recovery codes in a secure place.")
    .check()
  await page.getByRole("button", { name: "Finish and open MEM" }).click()

  // First-owner completion returns through the canonical root startup resolver.
  // The E2E identity/database state is disposable, but the local lane still
  // observes the developer host's real Docker daemon. Existing MEM-managed
  // resources may therefore select a bounded repair/attention setup card rather
  // than the fresh-install card. Prove the server-owned setup destination and
  // that the setup-start page resolved beyond its loading/error shell without
  // inventing a specific Docker-derived setup mode.
  await expect(page).toHaveURL(/\/setup\/start$/, { timeout: 10_000 })
  await expect(page.locator("h1")).toHaveCount(1)
  await expect(page.locator("h1")).toBeVisible()

  return {
    ...owner,
    totpSecret,
  }
}

export async function signOutNamedOperator(page: Page) {
  await page.getByRole("button", { name: "Open account menu" }).click()
  await page.getByRole("menuitem", { name: "Sign out" }).click()
  await expect(page).toHaveURL(/\/login$/, { timeout: 10_000 })
}

export async function signInNamedOperator(
  page: Page,
  credentials: NamedOperatorCredentials,
) {
  await forceEnglishBrowserUi(page)

  await page.goto("/login")
  await expect(page.locator("#login-username")).toBeVisible()

  await page.locator("#login-username").fill(credentials.username)
  await page.locator("#login-password").fill(credentials.password)
  await page.getByRole("button", { name: "Continue" }).click()

  await expect(page.locator("#login-totp")).toBeVisible()
  await fillCurrentTotp(page, "#login-totp", credentials.totpSecret)
  await page.getByRole("button", { name: "Verify and sign in" }).click()

  // Login returns to the canonical root route when no explicit protected
  // return target was supplied. Installed systems resolve to the dashboard;
  // fresh disposable E2E systems resolve to first-time setup. Both outcomes
  // are server-owned lifecycle decisions and are valid authenticated landings.
  await expect(page).toHaveURL(/\/(?:dashboard|setup\/start)$/, { timeout: 10_000 })
}

/**
 * Verifies the current named operator again with password plus live TOTP.
 * The helper intentionally has no recovery-code branch: recovery codes may
 * complete sign-in but cannot create a high-risk step-up grant.
 */
export async function completeOpenOperatorStepUp(
  page: Page,
  credentials: NamedOperatorCredentials,
) {
  await expect(page.getByRole("dialog", { name: "Verify your identity" })).toBeVisible()
  await page.locator("#step-up-password").fill(credentials.password)
  await fillCurrentTotp(page, "#step-up-totp", credentials.totpSecret)
  await page.getByRole("button", { name: "Verify identity" }).click()
}

export async function verifyCurrentOperatorStepUp(
  page: Page,
  credentials: NamedOperatorCredentials,
) {
  await page.getByRole("button", { name: "Open account menu" }).click()
  await page.getByRole("menuitem", { name: "Verify identity" }).click()

  await completeOpenOperatorStepUp(page, credentials)
  await expect(page.getByText("Identity verified", { exact: true })).toBeVisible()
  await expect(page.locator("#step-up-password")).toHaveCount(0)
  await expect(page.locator("#step-up-totp")).toHaveCount(0)
  await page.getByRole("button", { name: "Close" }).click()
}

export function readNamedOperatorCredentialsFromEnvironment(): NamedOperatorCredentials {
  return {
    username: requiredEnvironmentValue("MEM_E2E_OPERATOR_USERNAME"),
    password: requiredEnvironmentValue("MEM_E2E_OPERATOR_PASSWORD"),
    totpSecret: requiredEnvironmentValue("MEM_E2E_OPERATOR_TOTP_SECRET"),
  }
}

/**
 * Opens the real Home dashboard through the current browser origin and proves
 * that its authenticated overview request completed through the same Vite or
 * installed-bundle proxy path used by an operator. The assertion deliberately
 * avoids topology-specific health text: a disposable development database may
 * have no installed platform containers, while a deployed test host may.
 */
export async function openDashboardOverview(page: Page) {
  const overviewResponse = page.waitForResponse((response) =>
    response.request().method() === "GET" &&
    response.url().includes("/api/operator/dashboard/overview") &&
    response.status() === 200,
  )

  await page.goto("/dashboard")
  await expect(page).toHaveURL(/\/dashboard$/, { timeout: 10_000 })
  await expect(page.getByRole("heading", { name: "Home", exact: true })).toBeVisible()

  const response = await overviewResponse
  expect(response.request().headers()["x-mem-agent-secret"]).toBeUndefined()
  expect(response.headers()["cache-control"]).toContain("no-store")

  const overview: unknown = await response.json()
  expect(overview).toMatchObject({ source: "control-plane" })

  await expect(page.getByRole("button", { name: "Refresh" })).toBeVisible()
  await expect(page.getByRole("link", { name: "Open services", exact: true })).toHaveCount(0)
  await expect(
    page.getByRole("link", { name: "Coturn TURN server", exact: true }),
  ).toHaveAttribute("href", "/services/coturn")
  await expect(
    page.getByRole("main").getByText("Chat servers", { exact: true }),
  ).toBeVisible()
  await expect(page.getByRole("region", { name: "Recent activity" })).toBeVisible()
  await expect(page.getByRole("region", { name: "Host status" })).toBeVisible()
  await expect(page.getByText("Could not load the Home dashboard")).toHaveCount(0)
}

export async function openBackupCatalog(page: Page) {
  const catalogResponse = page.waitForResponse((response) =>
    response.request().method() === "GET" &&
    response.url().includes("/internal/host-agent/backups/catalog") &&
    response.status() === 200,
  )

  await page.goto("/backups")
  await expect(page).toHaveURL(/\/backups$/, { timeout: 10_000 })
  await expect(
    page.getByRole("heading", { name: "Backup Catalog" }),
  ).toBeVisible()

  const response = await catalogResponse
  expect(response.request().headers()["x-mem-agent-secret"]).toBeUndefined()
  await expect(
    page.getByText("Could not load the Backup Catalog"),
  ).toHaveCount(0)
  await expect(page.getByText("Catalog entries", { exact: true })).toBeVisible()
}

export async function inspectFirstCatalogEntryWhenAvailable(page: Page) {
  const detailLinks = page.getByRole("link", { name: "Details", exact: true })
  const emptyState = page.getByText("No backups in the catalog", { exact: true })

  await expect(async () => {
    const hasDetail = (await detailLinks.count()) > 0
    const isEmpty = await emptyState.isVisible()

    expect(hasDetail || isEmpty).toBe(true)
  }).toPass({ timeout: 10_000 })

  if (await detailLinks.count() === 0) {
    await expect(emptyState).toBeVisible()
    return
  }

  await detailLinks.first().click()
  await expect(page).toHaveURL(/\/backups\/catalog\/[^/]+$/, { timeout: 10_000 })
  await expect(page.getByText("Backup identity", { exact: true })).toBeVisible()
}

/**
 * Opens the MEM-native Diagnostics overview and incident workspace through the
 * same browser origin used by an operator. The helper proves no-store API
 * delivery without assuming that a deployed host already has an incident.
 */
export async function openDiagnosticsWorkspace(page: Page) {
  const overviewResponse = page.waitForResponse((response) =>
    response.request().method() === "GET" &&
    response.url().includes("/api/operator/diagnostics/overview") &&
    response.status() === 200,
  )

  await page.goto("/diagnostics")
  await expect(page).toHaveURL(/\/diagnostics$/, { timeout: 10_000 })
  await expect(page.getByRole("heading", { name: "Diagnostics", exact: true })).toBeVisible()

  const response = await overviewResponse
  expect(response.headers()["cache-control"]).toContain("no-store")
  await expect(page.getByText("Diagnostics health", { exact: true })).toBeVisible()
  await expect(page.getByRole("region", { name: "Operator diagnostics" })).toBeVisible()

  await page.getByRole("link", { name: "Open incidents", exact: true }).click()
  await expect(page).toHaveURL(/\/diagnostics\/logs(?:\?|$)/, { timeout: 10_000 })
  await expect(
    page.getByRole("heading", { name: "Incidents and technical events", exact: true }),
  ).toBeVisible()
  await expect(page.getByRole("button", { name: "Needs attention" })).toBeVisible()
  await expect(page.getByRole("button", { name: "Logging health" })).toBeVisible()
}
